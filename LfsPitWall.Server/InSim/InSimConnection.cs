using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LfsPitWall.Server.InSim;

/// <summary>
/// Manages low-level TCP connection to LFS InSim.
/// Handles connect, send, receive, and binary struct marshaling.
/// </summary>
public class InSimConnection : IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    private const int ConnectionTimeoutMs = 10000;
    private const int StreamTimeoutMs = 5000;

    public bool IsConnected => _client?.Connected == true && _stream?.CanWrite == true;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ConnectionTimeoutMs);

        try
        {
            await _client.ConnectAsync(host, port, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to connect to LFS at {host}:{port} within {ConnectionTimeoutMs}ms");
        }

        _stream = _client.GetStream();
        _stream.ReadTimeout = StreamTimeoutMs;
        _stream.WriteTimeout = StreamTimeoutMs;
    }

    public async Task SendAsync<T>(T packet, CancellationToken cancellationToken) where T : struct
    {
        var bytes = StructToBytes(packet);
        await _stream!.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    public async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
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

    public static byte[] StructToBytes<T>(T structure) where T : struct
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

    public static T BytesToStruct<T>(byte[] buffer) where T : struct
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

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_stream != null)
            {
                await _stream.FlushAsync();
                _stream.Dispose();
            }

            _client?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }

        _client = null;
        _stream = null;
    }
}
