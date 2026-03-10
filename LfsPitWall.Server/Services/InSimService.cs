using System.Net.Sockets;
using System.Runtime.InteropServices;
using LfsPitWall.Server.Models;

namespace LfsPitWall.Server.Services;

/// <summary>
/// InSim service - Background service managing TCP connection to LFS
/// Handles initialization handshake, packet receiving, and keep-alive logic.
/// </summary>
public class InSimService : BackgroundService
{
    private readonly ILogger<InSimService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly string _adminPassword;
    private readonly string _insimName;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private PeriodicTimer? _keepAliveTimer;

    private const int KeepAliveIntervalMs = 30000;
    private const int ConnectionTimeoutMs = 10000;
    private const int StreamTimeoutMs = 5000;

    public InSimService(ILogger<InSimService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _host = configuration["InSim:Host"] ?? "127.0.0.1";
        _port = int.TryParse(configuration["InSim:Port"], out var p) ? p : 29999;
        _adminPassword = configuration["InSim:AdminPassword"] ?? string.Empty;
        _insimName = configuration["InSim:Name"] ?? "LFS Pit Wall";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectToLfsAsync(stoppingToken);
                await ListenForPacketsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("InSim service stopped");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InSim error. Reconnecting in 5s...");
                await CloseConnectionAsync();
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Establishes TCP connection to LFS and completes initialization handshake (IS_ISI -> IS_VER)
    /// </summary>
    private async Task ConnectToLfsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to LFS at {Host}:{Port}", _host, _port);

        _client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ConnectionTimeoutMs);

        try
        {
            await _client.ConnectAsync(_host, _port, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to connect to LFS at {_host}:{_port} within {ConnectionTimeoutMs}ms");
        }

        _stream = _client.GetStream();
        _stream.ReadTimeout = StreamTimeoutMs;
        _stream.WriteTimeout = StreamTimeoutMs;

        // Send IS_ISI initialization packet
        var isiPacket = IS_ISI.CreateDefault(_insimName, _adminPassword);
        await SendPacketAsync(isiPacket, cancellationToken);

        // Receive and verify IS_VER response
        var verPacket = await ReceiveIS_VERAsync(cancellationToken);
        _logger.LogInformation(
            "✅ Connected to LFS | Version: {Version} | Product: {Product} | InSim: {InSimVer}",
            verPacket.GetVersion(), verPacket.GetProduct(), verPacket.InSimVer);

        // Start keep-alive timer
        _keepAliveTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(KeepAliveIntervalMs));
    }

    /// <summary>
    /// Receives IS_VER packet, validates header, and deserializes response
    /// </summary>
    private async Task<IS_VER> ReceiveIS_VERAsync(CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(4, cancellationToken);

        byte packetType = header[1];
        if (packetType != (byte)InSimPacketType.ISP_VER)
        {
            throw new InvalidOperationException($"Expected IS_VER (type 2), got type {packetType}");
        }

        var remaining = await ReadExactAsync(16, cancellationToken);
        var fullPacket = new byte[20];
        Array.Copy(header, fullPacket, 4);
        Array.Copy(remaining, 0, fullPacket, 4, 16);

        return BytesToStruct<IS_VER>(fullPacket);
    }

    /// <summary>
    /// Listens for packets from LFS with simultaneous keep-alive sending
    /// </summary>
    private async Task ListenForPacketsAsync(CancellationToken cancellationToken)
    {
        var keepAliveTask = ProcessKeepAliveAsync(cancellationToken);
        var receiveTask = ReceivePacketsLoopAsync(cancellationToken);

        // Exit when either task completes (error or cancellation)
        await Task.WhenAny(keepAliveTask, receiveTask);
    }

    /// <summary>
    /// Periodically sends IS_TINY keep-alive packets to LFS
    /// </summary>
    private async Task ProcessKeepAliveAsync(CancellationToken cancellationToken)
    {
        if (_keepAliveTimer == null) return;

        try
        {
            while (await _keepAliveTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (_stream?.CanWrite == true)
                {
                    await SendPacketAsync(IS_TINY.CreateKeepAlive(), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    /// <summary>
    /// Main receive loop - processes all packet types from LFS
    /// </summary>
    private async Task ReceivePacketsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var header = await ReadExactAsync(4, cancellationToken);
                byte packetType = header[1];
                byte packetSize = header[0];

                // Handle TINY packets (4 bytes) inline
                if (packetType == (byte)InSimPacketType.ISP_TINY)
                {
                    ProcessTinyPacket(header[3]);
                    continue;
                }

                // Read remaining packet bytes if necessary
                if (packetSize > 1) // Size is in units of 4, so > 1 means > 4 bytes total
                {
                    int remainingSize = (packetSize * 4) - 4;
                    var remaining = await ReadExactAsync(remainingSize, cancellationToken);
                    // TODO: Route to specific packet handler based on packetType
                }
            }
            catch (IOException)
            {
                throw; // Connection lost
            }
        }
    }

    /// <summary>
    /// Processes TINY packet subtypes
    /// </summary>
    private void ProcessTinyPacket(byte subType)
    {
        switch ((TinyPacketType)subType)
        {
            case TinyPacketType.TINY_NONE:
                // Keep-alive from LFS - no action needed
                break;
            case TinyPacketType.TINY_REPLY:
                _logger.LogDebug("Ping reply from LFS");
                break;
            default:
                _logger.LogDebug("Unhandled TINY subtype: {SubType}", subType);
                break;
        }
    }

    /// <summary>
    /// Sends a packet to LFS (any struct with [StructLayout] attribute)
    /// </summary>
    private async Task SendPacketAsync<T>(T packet, CancellationToken cancellationToken) where T : struct
    {
        var bytes = StructToBytes(packet);
        await _stream!.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads exactly the specified number of bytes from stream
    /// </summary>
    private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        int totalRead = 0;

        while (totalRead < count)
        {
            int read = await _stream!.ReadAsync(buffer, totalRead, count - totalRead, cancellationToken);
            if (read == 0)
                throw new InvalidOperationException("Connection closed by LFS");
            totalRead += read;
        }

        return buffer;
    }

    /// <summary>
    /// Serializes struct to byte array using Marshal
    /// </summary>
    private static byte[] StructToBytes<T>(T structure) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            Marshal.StructureToPtr(structure, handle.AddrOfPinnedObject(), false);
            return buffer;
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Deserializes byte array to struct using Marshal
    /// </summary>
    private static T BytesToStruct<T>(byte[] buffer) where T : struct
    {
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Closes connection and cleans up resources
    /// </summary>
    private async Task CloseConnectionAsync()
    {
        try
        {
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;

            if (_stream != null)
            {
                await _stream.FlushAsync();
                _stream.Dispose();
            }

            _client?.Dispose();
            _client = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing connection");
        }
    }

    /// <summary>
    /// Override to ensure cleanup on service stop
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await CloseConnectionAsync();
        await base.StopAsync(cancellationToken);
    }
}
