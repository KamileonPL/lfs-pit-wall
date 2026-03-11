using LfsPitWall.Server.Helpers;
using LfsPitWall.Server.InSim;
using LfsPitWall.Server.Models;

namespace LfsPitWall.Server.Services;

/// <summary>
/// Background service managing the LFS InSim connection.
/// Handles initialization handshake, packet receiving, keep-alive, and packet handler registration.
/// </summary>
public class InSimService : BackgroundService
{
    private readonly ILogger<InSimService> _logger;
    private readonly RaceSession _raceSession;
    private readonly PacketDispatcher _dispatcher;
    private readonly string _host;
    private readonly int _port;
    private readonly string _adminPassword;
    private readonly string _insimName;

    private InSimConnection? _connection;
    private PeriodicTimer? _keepAliveTimer;

    private const int KeepAliveIntervalMs = 30000;

    public InSimService(ILogger<InSimService> logger, IConfiguration configuration, RaceSession raceSession)
    {
        _logger = logger;
        _raceSession = raceSession;
        _host = configuration["InSim:Host"] ?? "127.0.0.1";
        _port = int.TryParse(configuration["InSim:Port"], out var p) ? p : 29999;
        _adminPassword = configuration["InSim:AdminPassword"] ?? string.Empty;
        _insimName = configuration["InSim:Name"] ?? "LFS Pit Wall";

        _dispatcher = new PacketDispatcher(_logger);
        RegisterHandlers();
    }

    // ── Packet Handler Registration ───────────────────────

    private void RegisterHandlers()
    {
        // Core session management
        _dispatcher.Bind<IS_STA>(InSimPacketType.ISP_STA, HandleSessionState);
        _dispatcher.Bind<IS_RST>(InSimPacketType.ISP_RST, HandleRaceStart);

        // Player lifecycle
        _dispatcher.Bind<IS_NCN>(InSimPacketType.ISP_NCN, HandleNewConnection);
        _dispatcher.Bind<IS_CNL>(InSimPacketType.ISP_CNL, HandleConnectionLeave);
        _dispatcher.Bind<IS_NPL>(InSimPacketType.ISP_NPL, HandleNewPlayer);
        _dispatcher.Bind<IS_PLL>(InSimPacketType.ISP_PLL, HandlePlayerLeave);

        // Timing data
        _dispatcher.Bind<IS_LAP>(InSimPacketType.ISP_LAP, HandleLapTime);
        _dispatcher.Bind<IS_SPX>(InSimPacketType.ISP_SPX, HandleSectorTime);
        _dispatcher.Bind<IS_MCI>(InSimPacketType.ISP_MCI, HandleMultiCarInfo);
        _dispatcher.BindRaw(InSimPacketType.ISP_NLP, HandleNodeAndLap);

        // Known packet types we don't need — suppress log noise
        _dispatcher.Suppress(
            InSimPacketType.ISP_FIN,
            InSimPacketType.ISP_PIT,
            InSimPacketType.ISP_PSF,
            InSimPacketType.ISP_PEN,
            InSimPacketType.ISP_CCH,
            InSimPacketType.ISP_UCO
        );
    }

    private Driver GetOrCreatePlaceholderDriver(byte playerId, string reason)
    {
        var driver = _raceSession.GetDriver(playerId);
        if (driver != null)
            return driver;

        var driverName = $"Driver #{playerId}";
        driver = new Driver
        {
            PlayerId = playerId,
            Name = driverName,
            NameHtml = LfsColorConverter.ConvertToHtml(driverName),
            CarName = "???",
            SkinName = "",
            Username = "",
            FuelPercent = 0,
            TyreTypes = new[] { (byte)0, (byte)0, (byte)0, (byte)0 }
        };

        _raceSession.AddOrUpdateDriver(driver);
        _logger.LogDebug("Auto-created placeholder driver for {Reason}: {PLID}", reason, playerId);
        return driver;
    }

    private void HandleNodeAndLap(byte[] packet)
    {
        if (packet.Length < 4)
            return;

        var numPlayers = packet[3];
        var offset = 4;

        for (var i = 0; i < numPlayers && offset + 5 < packet.Length; i++)
        {
            var node = BitConverter.ToUInt16(packet, offset);
            var lap = BitConverter.ToUInt16(packet, offset + 2);
            var playerId = packet[offset + 4];
            var position = packet[offset + 5];
            offset += 6;

            if (playerId == 0)
                continue;

            var driver = GetOrCreatePlaceholderDriver(playerId, "NLP");
            driver.CurrentTrackNode = node;
            driver.CurrentTrackLap = lap;
            driver.CurrentRacePosition = position;
        }
    }

    // ── Service Lifecycle ─────────────────────────────────

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

    private async Task ConnectToLfsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to LFS at {Host}:{Port}", _host, _port);

        _connection = new InSimConnection();
        await _connection.ConnectAsync(_host, _port, cancellationToken);

        // Send IS_ISI initialization packet
        var isiPacket = IS_ISI.CreateDefault(_insimName, _adminPassword);
        await _connection.SendAsync(isiPacket, cancellationToken);

        // Receive and verify IS_VER response
        var verPacket = await ReceiveVersionAsync(cancellationToken);
        _logger.LogInformation(
            "✅ Connected to LFS | Version: {Version} | Product: {Product} | InSim: {InSimVer}",
            verPacket.GetVersion(), verPacket.GetProduct(), verPacket.InSimVer);

        await RequestInitialDataAsync();

        _keepAliveTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(KeepAliveIntervalMs));
    }

    private async Task<IS_VER> ReceiveVersionAsync(CancellationToken cancellationToken)
    {
        var header = await _connection!.ReadExactAsync(4, cancellationToken);

        byte packetType = header[1];
        if (packetType != (byte)InSimPacketType.ISP_VER)
            throw new InvalidOperationException($"Expected IS_VER (type 2), got type {packetType}");

        var remaining = await _connection.ReadExactAsync(16, cancellationToken);
        var fullPacket = new byte[20];
        Array.Copy(header, fullPacket, 4);
        Array.Copy(remaining, 0, fullPacket, 4, 16);

        return InSimConnection.BytesToStruct<IS_VER>(fullPacket);
    }

    // ── Packet Receive Loop ───────────────────────────────
    private async Task ListenForPacketsAsync(CancellationToken cancellationToken)
    {
        var keepAliveTask = ProcessKeepAliveAsync(cancellationToken);
        var receiveTask = ReceivePacketsLoopAsync(cancellationToken);
        await Task.WhenAny(keepAliveTask, receiveTask);
    }

    private async Task ProcessKeepAliveAsync(CancellationToken cancellationToken)
    {
        if (_keepAliveTimer == null) return;

        try
        {
            while (await _keepAliveTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (_connection?.IsConnected == true)
                    await _connection.SendAsync(IS_TINY.CreateKeepAlive(), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReceivePacketsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var header = await _connection!.ReadExactAsync(4, cancellationToken);
            byte packetType = header[1];
            byte packetSize = header[0];

            // TINY packets are exactly 4 bytes — handle inline
            if (packetType == (byte)InSimPacketType.ISP_TINY)
            {
                ProcessTinyPacket(header[3]);
                continue;
            }

            // Read remaining bytes and dispatch to registered handler
            if (packetSize > 1)
            {
                int remainingSize = (packetSize * 4) - 4;
                var remaining = await _connection.ReadExactAsync(remainingSize, cancellationToken);
                var fullPacket = new byte[packetSize * 4];
                Array.Copy(header, fullPacket, 4);
                Array.Copy(remaining, 0, fullPacket, 4, remainingSize);

                _dispatcher.Dispatch((InSimPacketType)packetType, fullPacket);
            }
        }
    }

    private void ProcessTinyPacket(byte subType)
    {
        switch ((TinyPacketType)subType)
        {
            case TinyPacketType.TINY_NONE:
                break;
            case TinyPacketType.TINY_REPLY:
                _logger.LogDebug("Ping reply from LFS");
                break;
            default:
                _logger.LogDebug("Unhandled TINY subtype: {SubType}", subType);
                break;
        }
    }

    // ── Data Requests ─────────────────────────────────────

    private async Task RequestInitialDataAsync()
    {
        try
        {
            if (_connection?.IsConnected != true) return;

            byte reqId = (byte)DateTime.Now.Ticks;

            await _connection.SendAsync(new IS_TINY
            {
                Size = 1, Type = (byte)InSimPacketType.ISP_TINY,
                ReqI = reqId, SubT = (byte)TinyPacketType.TINY_SST
            }, CancellationToken.None);

            await _connection.SendAsync(new IS_TINY
            {
                Size = 1, Type = (byte)InSimPacketType.ISP_TINY,
                ReqI = reqId, SubT = (byte)TinyPacketType.TINY_NCN
            }, CancellationToken.None);

            await _connection.SendAsync(new IS_TINY
            {
                Size = 1, Type = (byte)InSimPacketType.ISP_TINY,
                ReqI = reqId, SubT = (byte)TinyPacketType.TINY_NPL
            }, CancellationToken.None);

            await _connection.SendAsync(new IS_TINY
            {
                Size = 1, Type = (byte)InSimPacketType.ISP_TINY,
                ReqI = reqId, SubT = (byte)TinyPacketType.TINY_RST
            }, CancellationToken.None);

            await _connection.SendAsync(new IS_TINY
            {
                Size = 1, Type = (byte)InSimPacketType.ISP_TINY,
                ReqI = reqId, SubT = (byte)TinyPacketType.TINY_RES
            }, CancellationToken.None);

            _logger.LogDebug("📤 Sent info requests: SST, NCN, NPL, RST, RES");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send info requests");
        }
    }

    // ── Packet Handlers ─────────────────────────────────
    private void HandleMultiCarInfo(IS_MCI packet)
    {
        // IS_MCI is currently used only as movement telemetry.
        // Driver entries are created from authoritative player/timing packets (NPL, LAP, SPX),
        // which avoids filling the table with placeholder rows from incomplete MCI parsing.
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
        var playerName = LfsColorConverter.Decode(packet.PName);
        var formattedName = DriverNameHelper.FormatPlayerName(playerName);
        string carName = DriverNameHelper.ParseCarName(packet.CName);
        var skinName = LfsColorConverter.Decode(packet.SName);
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
            _raceSession.RefreshSessionBestLapAuthor(driver);
            
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
            _raceSession.RefreshSessionBestLapAuthor(driver);
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

        if (driver == null)
        {
            driver = GetOrCreatePlaceholderDriver(packet.PLID, "LAP");
            // Request player info
            _ = RequestInitialDataAsync();
        }
        else if (driver.Name.StartsWith("Unknown Driver") || driver.Name.StartsWith("Driver #"))
        {
            // Driver still has placeholder name
            _logger.LogDebug("Lap from unknown driver ID {PLID}, requesting info", packet.PLID);
            _ = RequestInitialDataAsync();
        }

        // Calculate fuel: if 255 it's disabled (server has /showfuel no), otherwise fuel_percent = Fuel200 / 2
        byte? fuelPercent = packet.Fuel200 == 255 ? null : (byte?)Math.Min(100, packet.Fuel200 / 2);
        driver.FuelPercent = fuelPercent;
        
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

        driver.AddLap(lapData, _raceSession.ActiveSectorCount);
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

        var isPersonalBest = ReferenceEquals(driver.PersonalBestLap, lapData);
        var isSessionBest = _raceSession.TryUpdateSessionBestLap(driver, lapData);

        if (isPersonalBest)
        {
            _logger.LogInformation(
                "🏁 PERSONAL BEST: {PlayerName} - {LapTime}ms",
                LfsColorConverter.RemoveColorCodes(driver.Name), packet.LTime);
        }

        if (isSessionBest)
        {
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
        var driver = _raceSession.GetDriver(packet.PLID) ?? GetOrCreatePlaceholderDriver(packet.PLID, "SPX");

        // Calculate fuel: if 255 it's disabled (server has /showfuel no), otherwise fuel_percent = Fuel200 / 2
        byte? fuelPercent = packet.Fuel200 == 255 ? null : (byte?)Math.Min(100, packet.Fuel200 / 2);
        driver.FuelPercent = fuelPercent;

        // Update sector time from cumulative split time
        driver.UpdateSectorTime(packet.Split, packet.STime);
        var sectorTimeMs = driver.CurrentLapSectors.TryGetValue(packet.Split, out var sector)
            ? sector.TimeMs
            : packet.STime;

        _logger.LogDebug(
            "🎯 Sector {Sector} | {PlayerName}: {SectorTime}ms (Split: {SplitTime}ms, Elapsed: {ElapsedTime}ms) | Fuel: {Fuel} | Stops: {Stops}",
            packet.Split,
            LfsColorConverter.RemoveColorCodes(driver.Name),
            sectorTimeMs,
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
        var userName = LfsColorConverter.Decode(packet.UName);
        var nickName = LfsColorConverter.Decode(packet.PName);
        
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
        
        // Clean up username mapping when connection leaves
        _raceSession.RemoveUsername(packet.UCID);
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
        var timingMode = packet.Timing & 0xC0;
        var checkpointCount = packet.Timing & 0x03;

        _raceSession.ActiveSectorCount = timingMode == 0xC0
            ? 0
            : checkpointCount + 1;

        _logger.LogInformation(
            "🏁 RACE START INFO: {Track} | Race: {RaceLaps}L / Quali: {QualMins}min | Players: {NumP} | Timing: {Timing} | Checkpoints: {CheckpointCount} | Sectors: {SectorCount} | Wind: {Wind}",
            trackName,
            packet.RaceLaps,
            packet.QualMins,
            packet.NumP,
            packet.Timing,
            checkpointCount,
            _raceSession.ActiveSectorCount,
            packet.Wind switch { 0 => "Off", 1 => "Weak", 2 => "Strong", _ => "Unknown" });
    }

    // ── Connection Cleanup ──────────────────────────────

    private async Task CloseConnectionAsync()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;

        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await CloseConnectionAsync();
        await base.StopAsync(cancellationToken);
    }
}
