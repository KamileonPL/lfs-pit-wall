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
    private readonly RaceSession _raceSession;
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

    public InSimService(ILogger<InSimService> logger, IConfiguration configuration, RaceSession raceSession)
    {
        _logger = logger;
        _raceSession = raceSession;
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

        // Request player/connection/result info to get complete picture
        await SendInfoRequestsAsync();

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
                    var fullPacket = new byte[packetSize * 4];
                    Array.Copy(header, fullPacket, 4);
                    Array.Copy(remaining, 0, fullPacket, 4, remainingSize);

                    // Route to specific packet handler based on packetType
                    await HandlePacketAsync((InSimPacketType)packetType, fullPacket, cancellationToken);
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
    /// Send information requests to LFS to get player data, connections, results, and session info.
    /// Should be called on connection and periodically to keep data in sync.
    /// </summary>
    private async Task SendInfoRequestsAsync()
    {
        try
        {
            if (_stream == null || !_stream.CanWrite)
                return;

            byte reqId = (byte)DateTime.Now.Ticks;

            // Request: Session State (track, weather, race status, etc)
            await SendPacketAsync(new IS_TINY 
            { 
                Size = 1, 
                Type = (byte)InSimPacketType.ISP_TINY, 
                ReqI = reqId, 
                SubT = (byte)TinyPacketType.TINY_SST 
            }, CancellationToken.None);

            // Request: New Connections
            await SendPacketAsync(new IS_TINY 
            { 
                Size = 1, 
                Type = (byte)InSimPacketType.ISP_TINY, 
                ReqI = reqId, 
                SubT = (byte)TinyPacketType.TINY_NCN 
            }, CancellationToken.None);

            // Request: New Players (including those already in race)
            await SendPacketAsync(new IS_TINY 
            { 
                Size = 1, 
                Type = (byte)InSimPacketType.ISP_TINY, 
                ReqI = reqId, 
                SubT = (byte)TinyPacketType.TINY_NPL 
            }, CancellationToken.None);

            // Request: Race Start Info (track layout, laps, quali time, etc)
            await SendPacketAsync(new IS_TINY 
            { 
                Size = 1, 
                Type = (byte)InSimPacketType.ISP_TINY, 
                ReqI = reqId, 
                SubT = (byte)TinyPacketType.TINY_RST 
            }, CancellationToken.None);

            // Request: Results (qualify/race results)
            await SendPacketAsync(new IS_TINY 
            { 
                Size = 1, 
                Type = (byte)InSimPacketType.ISP_TINY, 
                ReqI = reqId, 
                SubT = (byte)TinyPacketType.TINY_RES 
            }, CancellationToken.None);

            _logger.LogDebug("📤 Sent info requests: SST, NCN, NPL, RST, RES");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send info requests");
        }
    }

    /// <summary>
    /// Routes packets to appropriate handler methods based on packet type
    /// </summary>
    private async Task HandlePacketAsync(InSimPacketType packetType, byte[] packetData, CancellationToken cancellationToken)
    {
        try
        {
            switch (packetType)
            {
                case InSimPacketType.ISP_STA:
                    HandleSessionState(BytesToStruct<IS_STA>(packetData));
                    break;

                case InSimPacketType.ISP_MCI:
                    HandleMultiCarInfo(BytesToStruct<IS_MCI>(packetData));
                    break;

                case InSimPacketType.ISP_NPL:
                    HandleNewPlayer(BytesToStruct<IS_NPL>(packetData));
                    break;

                case InSimPacketType.ISP_NCN:
                    HandleNewConnection(BytesToStruct<IS_NCN>(packetData));
                    break;

                case InSimPacketType.ISP_CNL:
                    HandleConnectionLeave(BytesToStruct<IS_CNL>(packetData));
                    break;

                case InSimPacketType.ISP_PLL:
                    HandlePlayerLeave(BytesToStruct<IS_PLL>(packetData));
                    break;

                case InSimPacketType.ISP_LAP:
                    HandleLapTime(BytesToStruct<IS_LAP>(packetData));
                    break;

                case InSimPacketType.ISP_SPX:
                    HandleSectorTime(BytesToStruct<IS_SPX>(packetData));
                    break;

                case InSimPacketType.ISP_RST:
                    HandleRaceStart(BytesToStruct<IS_RST>(packetData));
                    break;

                case InSimPacketType.ISP_FIN: // Finished race
                case InSimPacketType.ISP_PIT: // Pit stop start
                case InSimPacketType.ISP_PSF: // Pit stop finish  
                case InSimPacketType.ISP_PEN: // Penalty
                case InSimPacketType.ISP_NLP: // Node and lap packet (37) - Ignore for now
                case InSimPacketType.ISP_CCH: // Camera changed
                case InSimPacketType.ISP_UCO: // Unknown - try to skip
                    _logger.LogDebug("Packet {PacketType} (suppressed)", packetType);
                    break;

                default:
                    _logger.LogInformation("📦 OTHER Packet: {PacketType}", packetType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling packet type {PacketType}", packetType);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles IS_MCI (Multi Car Info) packets - identifies all active cars
    /// </summary>
    private void HandleMultiCarInfo(IS_MCI packet)
    {
        for (int i = 0; i < packet.NumCars && i < packet.Cars.Length; i++)
        {
            var car = packet.Cars[i];
            if (car.PLID > 0 && !_raceSession.Players.ContainsKey(car.PLID))
            {
                // Found a car that's not in our session yet - create placeholder
                var placeholder = new Driver
                {
                    PlayerId = car.PLID,
                    Name = $"Unknown Driver #{car.PLID}",
                    CarName = "???",
                    SkinName = "",
                    FuelPercent = 0,  // Will be updated by IS_NPL or IS_LAP
                    TyreTypes = new[] { (byte)0, (byte)0, (byte)0, (byte)0 }
                };

                _raceSession.AddOrUpdateDriver(placeholder);
                _logger.LogDebug("🚗 Found car from MCI: ID {PLID}", car.PLID);
            }
        }
    }

    /// <summary>
    /// Handles IS_STA (Session State) packets - reports current race/session status
    /// </summary>
    private void HandleSessionState(IS_STA packet)
    {
        // Extract track name (6 chars)
        var trackName = System.Text.Encoding.ASCII.GetString(packet.Track).TrimEnd('\0').Trim();
        if (!string.IsNullOrEmpty(trackName))
            _raceSession.TrackName = trackName;
        else
            _raceSession.TrackName = "Unknown";

        _raceSession.WeatherType = packet.Weather;
        _raceSession.RaceInProgress = packet.RaceInProg == 1;
        
        // Store qualifying minutes from IS_STA
        _raceSession.QualifyingMins = packet.QualMins;

        // Determine session type from packet.RaceInProg: 0=no race, 1=race, 2=qualifying
        // We map to: 0=practice/idle, 1=qualifying, 2=race
        if (packet.RaceInProg == 2)
            _raceSession.SessionType = 1; // Qualifying
        else if (packet.RaceInProg == 1)
            _raceSession.SessionType = 2; // Race
        else
            _raceSession.SessionType = 0; // Practice/Idle

        _logger.LogInformation(
            "📊 SESSION: {Track} | Race: {RaceLaps}L / Qual: {QualMins}min | Players: {NumP}/{NumConns} | Weather: {Weather} | Status: {Status}",
            trackName,
            packet.RaceLaps, packet.QualMins,
            packet.NumP, packet.NumConns,
            packet.Weather,
            packet.RaceInProg switch { 0 => "Idle", 1 => "Racing", 2 => "Qualifying", _ => "Unknown" });
    }

    /// <summary>
    /// Handles IS_NPL (New Player) packets - updates or creates driver with real name data
    /// </summary>
    private void HandleNewPlayer(IS_NPL packet)
    {
        // Use Latin1 encoding for car names to preserve all byte values (128-255)
        // as ASCII doesn't handle high bytes well for mod cars
        var encoding = System.Text.Encoding.GetEncoding("iso-8859-1"); // Latin1
        
        var playerName = System.Text.Encoding.ASCII.GetString(packet.PName).TrimEnd('\0').Trim();
        
        // Format player name: extract group prefix if present (e.g., "SRP Kamileon" → "/SRP/ Kamileon")
        var formattedName = FormatPlayerName(playerName);
        
        // Parse CName: for old cars it's a string (XRG, XFG, RB4, etc)
        // For mods it's a little-endian 3-byte ID that should be displayed as hex (e.g., 38A066)
        string carName = ParseCarName(packet.CName);
        
        var skinName = encoding.GetString(packet.SName).TrimEnd('\0').Trim();
        
        // Get LFS username if available (from IS_NCN mapping)
        var username = _raceSession.GetUsername(packet.UCID) ?? "";

        // Get existing driver if it exists, else create new
        var driver = _raceSession.GetDriver(packet.PLID);
        if (driver != null)
        {
            // Update existing driver with real data from IS_NPL - PRESERVE lap history and pit stops
            bool wasUnknown = driver.Name.StartsWith("Unknown Driver") || driver.Name.StartsWith("Driver #");
            driver.Name = formattedName;
            driver.NameHtml = LfsColorConverter.ConvertToHtml(playerName);  // Use original for color conversion
            driver.CarName = carName;
            driver.SkinName = skinName;
            driver.Username = username;
            driver.TyreTypes = new[] { packet.Tyres0, packet.Tyres1, packet.Tyres2, packet.Tyres3 };
            driver.FuelPercent = packet.Fuel == 255 ? null : packet.Fuel;
            
            if (wasUnknown)
            {
                _logger.LogInformation(
                    "✅ NAME RESOLVED: {PlayerName} ({UserName}) (ID: {PLID}) in {CarName}",
                    LfsColorConverter.RemoveColorCodes(formattedName), username, packet.PLID, carName);
            }
        }
        else
        {
            // Create new driver
            driver = new Driver
            {
                PlayerId = packet.PLID,
                Name = formattedName,
                NameHtml = LfsColorConverter.ConvertToHtml(playerName),  // Use original for color conversion
                CarName = carName,
                SkinName = skinName,
                Username = username,
                TyreTypes = new[] { packet.Tyres0, packet.Tyres1, packet.Tyres2, packet.Tyres3 },
                FuelPercent = packet.Fuel == 255 ? null : packet.Fuel
            };
            _raceSession.AddOrUpdateDriver(driver);
            _logger.LogInformation(
                "🏎️ New Player: {PlayerName} ({UserName}) (ID: {PLID}) in {CarName}",
                LfsColorConverter.RemoveColorCodes(formattedName), username, packet.PLID, carName);
        }
    }

    /// <summary>
    /// Handles IS_PLL (Player Leave) packets
    /// </summary>
    private void HandlePlayerLeave(IS_PLL packet)
    {
        var driver = _raceSession.GetDriver(packet.PLID);
        if (driver != null)
        {
            _logger.LogInformation(
                "👋 Player left: {PlayerName} (ID: {PLID})",
                driver.Name, packet.PLID);
        }

        _raceSession.RemoveDriver(packet.PLID);
    }

    /// <summary>
    /// Handles IS_LAP (Lap Time) packets - reports completed lap
    /// </summary>
    private void HandleLapTime(IS_LAP packet)
    {
        var driver = _raceSession.GetDriver(packet.PLID);
        
        // Create placeholder driver if not found
        if (driver == null)
        {
            driver = new Driver
            {
                PlayerId = packet.PLID,
                Name = $"Driver #{packet.PLID}",
                CarName = "???",
                SkinName = "",
                FuelPercent = 0,
                TyreTypes = new[] { (byte)0, (byte)0, (byte)0, (byte)0 }
            };
            _raceSession.AddOrUpdateDriver(driver);
            _logger.LogDebug("Auto-created placeholder driver for ID: {PLID}", packet.PLID);
            
            // Request player info
            _ = SendInfoRequestsAsync();
        }
        else if (driver.Name.StartsWith("Unknown Driver") || driver.Name.StartsWith("Driver #"))
        {
            // Driver still has placeholder name
            _logger.LogDebug("Lap from unknown driver ID {PLID}, requesting info", packet.PLID);
            _ = SendInfoRequestsAsync();
        }

        // Calculate fuel: if 255 it's disabled (server has /showfuel no), otherwise fuel_percent = Fuel200 / 2
        driver.FuelPercent = packet.Fuel200 == 255 ? null : (byte?)(packet.Fuel200 / 2);
        
        // Update session elapsed time from the lap packet
        _raceSession.SessionTimeMs = packet.ETime;

        var lapData = new LapData
        {
            LapNumber = packet.LapsDone,
            LapTimeMs = packet.LTime,
            ElapsedTimeMs = packet.ETime,
            IsValid = true,
            PitStops = packet.NumStops,
            PenaltyMs = packet.Penalty,
            RecordedAt = DateTime.UtcNow
        };

        driver.AddLap(lapData);
        driver.LapsCompleted = packet.LapsDone;
        
        // Update last elapsed time for gap calculation (when driver crossed finish line)
        driver.LastElapsedTimeMs = packet.ETime;
        driver.LastLapNumber = packet.LapsDone;
        
        // Helper: format ms to M:SS.mmm
        string FormatMs(uint ms) =>
            ms == 0 ? "-" : 
            $"{ms / 60000}:{(ms % 60000) / 1000:D2}.{ms % 1000:D3}";

        _logger.LogDebug(
            "🏁 {PlayerName} - Lap {LapNum}: LTime={LTimeRaw}ms ({LTimeFormatted}) | Best={BestTime} | Fuel: {Fuel} | Stops: {Stops}",
            LfsColorConverter.RemoveColorCodes(driver.Name),
            packet.LapsDone,
            packet.LTime,
            FormatMs(packet.LTime),
            FormatMs(driver.PersonalBestLap?.LapTimeMs ?? 0),
            driver.FuelPercent.HasValue ? $"{driver.FuelPercent}%" : "N/A",
            packet.NumStops);

        // Log personal/session bests
        if (driver.PersonalBestLap?.LapNumber == packet.LapsDone)
        {
            _logger.LogInformation(
                "🏁 PERSONAL BEST: {PlayerName} - {LapTime}ms",
                LfsColorConverter.RemoveColorCodes(driver.Name), packet.LTime);
        }

        // Track session best lap - when current lap equals session best, store author and lap number
        if (_raceSession.SessionBestLap?.LapTimeMs == packet.LTime && 
            _raceSession.SessionBestLap.LapTimeMs == packet.LTime)
        {
            _raceSession.SessionBestLapAuthorPLID = packet.PLID;
            _raceSession.SessionBestLapNumber = packet.LapsDone;
            
            _logger.LogInformation(
                "🌟 SESSION BEST: {PlayerName} - {LapTime}ms [Lap {LapNum}]",
                LfsColorConverter.RemoveColorCodes(driver.Name), packet.LTime, packet.LapsDone);
        }
    }

    /// <summary>
    /// Handles IS_SPX (Sector Split) packets - reports sector times during lap
    /// </summary>
    private void HandleSectorTime(IS_SPX packet)
    {
        var driver = _raceSession.GetDriver(packet.PLID);
        
        // Create placeholder if not found
        if (driver == null)
        {
            driver = new Driver
            {
                PlayerId = packet.PLID,
                Name = $"Driver #{packet.PLID}",
                CarName = "???",
                SkinName = "",
                FuelPercent = null,
                TyreTypes = new[] { (byte)0, (byte)0, (byte)0, (byte)0 }
            };
            _raceSession.AddOrUpdateDriver(driver);
            _logger.LogDebug("Auto-created placeholder driver for SPX: {PLID}", packet.PLID);
        }

        // Calculate fuel: if 255 it's disabled (server has /showfuel no), otherwise fuel_percent = Fuel200 / 2
        driver.FuelPercent = packet.Fuel200 == 255 ? null : (byte?)(packet.Fuel200 / 2);

        // Update sector time
        driver.UpdateSectorTime(packet.Split, packet.STime);

        _logger.LogDebug(
            "🎯 Sector {Sector} | {PlayerName}: {SplitTime}ms (Elapsed: {ElapsedTime}ms) | Fuel: {Fuel} | Stops: {Stops}",
            packet.Split,
            LfsColorConverter.RemoveColorCodes(driver.Name),
            packet.STime,
            packet.ETime,
            driver.FuelPercent.HasValue ? $"{driver.FuelPercent}%" : "N/A",
            packet.NumStops);
    }

    /// <summary>
    /// Handles IS_NCN (New Connection) packets - player connecting via LFS client
    /// </summary>
    private void HandleNewConnection(IS_NCN packet)
    {
        var userName = System.Text.Encoding.ASCII.GetString(packet.UName).TrimEnd('\0');
        var nickName = System.Text.Encoding.ASCII.GetString(packet.PName).TrimEnd('\0');
        
        // Store username mapping from UCID to LFS username
        // This is used to display usernames in driver listing
        _raceSession.SetUsername(packet.UCID, userName);
        
        _logger.LogDebug(
            "🔗 New Connection: {UserName} ({NickName}) - UCID: {UCID}, Total: {Total}",
            userName, nickName, packet.UCID, packet.Total);
    }

    /// <summary>
    /// Handles IS_CNL (Connection Leave) packets - player disconnecting from LFS
    /// </summary>
    private void HandleConnectionLeave(IS_CNL packet)
    {
        _logger.LogDebug(
            "🔌 Connection Leave: UCID {UCID} - Total: {Total}",
            packet.UCID, packet.Total);
    }

    /// <summary>
    /// Handles IS_RES (Result) packets - race results
    /// </summary>
    private void HandleResult(IS_RES packet)
    {
        _logger.LogDebug(
            "📊 Result: PLID {PLID} - Mode {Mode}",
            packet.PLID, packet.Mode);
    }

    /// <summary>
    /// Handles IS_RST (Race Start) packets - reports race/qualifying configuration
    /// </summary>
    private void HandleRaceStart(IS_RST packet)
    {
        var trackName = System.Text.Encoding.ASCII.GetString(packet.Track).TrimEnd('\0').Trim();
        _raceSession.TrackName = string.IsNullOrEmpty(trackName) ? "Unknown" : trackName;
        _raceSession.WeatherType = packet.Weather;
        
        // Store race parameters from IS_RST packet
        _raceSession.MaxRaceLaps = packet.RaceLaps;
        _raceSession.QualifyingMins = packet.QualMins;

        _logger.LogInformation(
            "🏁 RACE START INFO: {Track} | Race: {RaceLaps}L / Quali: {QualMins}min | Players: {NumP} | Timing: {Timing} | Wind: {Wind}",
            trackName,
            packet.RaceLaps,
            packet.QualMins,
            packet.NumP,
            packet.Timing,
            packet.Wind switch { 0 => "Off", 1 => "Weak", 2 => "Strong", _ => "Unknown" });
    }

    /// <summary>
    /// Formats player name: extracts group prefix and wraps in slashes
    /// Example: "SRP Kamileon" → "/SRP/ Kamileon"
    /// Names with brackets or slashes are kept as-is: "[FM]TJ" → "[FM]TJ"
    /// </summary>
    private string FormatPlayerName(string name)
    {
        _logger.LogDebug("🏷️ FormatPlayerName input: '{Name}'", name);
        
        if (string.IsNullOrWhiteSpace(name))
            return name;

        // If already has special prefix characters, keep as-is
        if (name.StartsWith("[") || name.StartsWith("/") || name.StartsWith("<"))
        {
            _logger.LogDebug("🏷️ -> Skipping (has prefix chars): '{Name}'", name);
            return name;
        }

        // Try to extract group prefix (word before first space)
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var prefix = parts[0];
            
            // Check if prefix is a valid group name (uppercase letters/digits only, reasonable length)
            bool isValidGroupName = !string.IsNullOrEmpty(prefix) && 
                                   prefix.Length <= 10 && 
                                   prefix.All(c => char.IsLetterOrDigit(c));

            if (isValidGroupName)
            {
                // Extract remaining name, replace multiple spaces with single space
                string rest = string.Join(" ", parts.Skip(1));
                string formatted = $"/{prefix}/ {rest}";
                _logger.LogDebug("🏷️ Formatted: '{OldName}' → '{NewName}'", name, formatted);
                return formatted;
            }
            else
            {
                _logger.LogDebug("🏷️ -> Not valid group: '{Prefix}' (len={Len}, isAlphaNum={IsAlphaNum})", 
                    prefix, prefix.Length, prefix.All(c => char.IsLetterOrDigit(c)));
            }
        }
        else
        {
            _logger.LogDebug("🏷️ -> Single word or empty: parts.Length={Len}", parts.Length);
        }

        return name;
    }

    /// <summary>
    /// Parses CName from IS_NPL packet. For old cars (XRG, XFG, RB4) it's ASCII string.
    /// For mods it's a little-endian 3-byte ID that should be displayed as hexadecimal.
    /// </summary>
    private string ParseCarName(byte[] cname)
    {
        // Check if this looks like a printable ASCII string (old car names)
        // Old car names are uppercase ASCII like "XRG", "XFG", "RB4", "LX4", etc.
        bool isAsciiString = true;
        for (int i = 0; i < cname.Length - 1; i++) // Skip last byte (null terminator)
        {
            byte b = cname[i];
            if (b == 0) break; // End of string
            
            // Check if it's a printable ASCII character
            if (b < 32 || b > 126)
            {
                isAsciiString = false;
                break;
            }
        }
        
        if (isAsciiString)
        {
            // Old car - decode as ASCII string
            var result = System.Text.Encoding.ASCII.GetString(cname).TrimEnd('\0').Trim();
            return string.IsNullOrEmpty(result) ? "???" : result;
        }
        else
        {
            // Mod car - parse as little-endian 3-byte ID and display as hex
            // e.g., bytes [0x66, 0xA0, 0x38, 0x00] → 0x38A066 → "38A066"
            uint modId = cname[0] | ((uint)cname[1] << 8) | ((uint)cname[2] << 16);
            return modId.ToString("X6");
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
