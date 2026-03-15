using LfsPitWall.Server.Helpers;
using LfsPitWall.Server.InSim;
using LfsPitWall.Server.Models;
using LfsPitWall.Server.Models.Archive;
using Microsoft.Extensions.Options;
using System.Text;

namespace LfsPitWall.Server.Services;

/// <summary>
/// Background service managing the LFS InSim connection.
/// Handles initialization handshake, packet receiving, keep-alive, and packet handler registration.
/// </summary>
public class InSimService : BackgroundService
{
    private const uint QualifyingPlaceholderTimeMs = 3600000;
    private const int CustomTrackMapBucketWorldSize = 10 * 65536;
    private const int TrackMapClosureDistanceWorldSize = 18 * 65536;
    private const uint TrackMapClosureCandidatePointCount = 10;
    private const uint TrackMapClosureMinimumAssignedPoints = 24;
    private const byte MessageSoundSilent = 0;
    private const byte MessageSoundSystem = 2;

    private readonly ILogger<InSimService> _logger;
    private readonly RaceSession _raceSession;
    private readonly SessionLifecycleManager _sessionLifecycleManager;
    private readonly PacketDispatcher _dispatcher;
    private readonly string _host;
    private readonly int _port;
    private readonly string _adminPassword;
    private readonly string _insimName;
    private readonly ChampionshipScoringOptions _championshipScoringOptions;
    private readonly SessionArchiveWriter _sessionArchiveWriter;
    private readonly ArchiveOptions _archiveOptions;
    private readonly PlayerOnboardingOptions _playerOnboardingOptions;

    private InSimConnection? _connection;
    private PeriodicTimer? _keepAliveTimer;
    private bool _useObservedSpatialTrackMapFallback;
    private int _collapsedNodeTraceCount;
    private byte? _referenceTrackMapPlayerId;
    private DateTime _lastOfficialResultsRequestUtc = DateTime.MinValue;

    private const int KeepAliveIntervalMs = 30000;
    private static readonly TimeSpan OfficialResultsRequestThrottle = TimeSpan.FromSeconds(2);

    public InSimService(ILogger<InSimService> logger, IConfiguration configuration, RaceSession raceSession, SessionLifecycleManager sessionLifecycleManager, IOptions<PlayerOnboardingOptions> playerOnboardingOptions, IOptions<ChampionshipScoringOptions> championshipScoringOptions, SessionArchiveWriter sessionArchiveWriter, IOptions<ArchiveOptions> archiveOptions)
    {
        _logger = logger;
        _raceSession = raceSession;
        _sessionLifecycleManager = sessionLifecycleManager;
        _host = configuration["InSim:Host"] ?? "127.0.0.1";
        _port = int.TryParse(configuration["InSim:Port"], out var p) ? p : 29999;
        _adminPassword = configuration["InSim:AdminPassword"] ?? string.Empty;
        _insimName = configuration["InSim:Name"] ?? "LFS Pit Wall";
        _playerOnboardingOptions = playerOnboardingOptions.Value;
        _championshipScoringOptions = championshipScoringOptions.Value;
        _sessionArchiveWriter = sessionArchiveWriter;
        _archiveOptions = archiveOptions.Value;

        _dispatcher = new PacketDispatcher(_logger);
        RegisterHandlers();
    }

    // ── Packet Handler Registration ───────────────────────

    private void RegisterHandlers()
    {
        // Core session management
        _dispatcher.Bind<IS_ISM>(InSimPacketType.ISP_ISM, HandleMultiplayerInfo);
        _dispatcher.Bind<IS_TINY>(InSimPacketType.ISP_TINY, HandleTiny);
        _dispatcher.BindRaw(InSimPacketType.ISP_MSO, HandleMessageOut);
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
        _dispatcher.Bind<IS_PIT>(InSimPacketType.ISP_PIT, HandlePitStopStart);
        _dispatcher.Bind<IS_PSF>(InSimPacketType.ISP_PSF, HandlePitStopFinish);
        _dispatcher.Bind<IS_PLA>(InSimPacketType.ISP_PLA, HandlePitLaneChange);
        _dispatcher.Bind<IS_FIN>(InSimPacketType.ISP_FIN, HandleFinish);
        _dispatcher.Bind<IS_RES>(InSimPacketType.ISP_RES, HandleResult);
        _dispatcher.Bind<IS_REO>(InSimPacketType.ISP_REO, HandleReorder);
        _dispatcher.BindRaw(InSimPacketType.ISP_MCI, HandleMultiCarInfo);

        // Known packet types we don't need — suppress log noise
        _dispatcher.Suppress(
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

        await RequestInitialDataAsync();

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
                ReqI = reqId, SubT = (byte)TinyPacketType.TINY_ISM
            }, CancellationToken.None);

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

            await _connection.SendAsync(new IS_TINY
            {
                Size = 1, Type = (byte)InSimPacketType.ISP_TINY,
                ReqI = reqId, SubT = (byte)TinyPacketType.TINY_REO
            }, CancellationToken.None);

            _logger.LogDebug("📤 Sent info requests: ISM, SST, NCN, NPL, RST, RES, REO");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send info requests");
        }
    }

    private async Task RequestOfficialResultsAsync(string reason, bool force = false)
    {
        try
        {
            if (_connection?.IsConnected != true)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (!force && nowUtc - _lastOfficialResultsRequestUtc < OfficialResultsRequestThrottle)
            {
                return;
            }

            _lastOfficialResultsRequestUtc = nowUtc;

            await _connection.SendAsync(new IS_TINY
            {
                Size = 1,
                Type = (byte)InSimPacketType.ISP_TINY,
                ReqI = (byte)nowUtc.Ticks,
                SubT = (byte)TinyPacketType.TINY_RES
            }, CancellationToken.None);

            _logger.LogDebug("📤 Requested official results ({Reason})", reason);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to request official results ({Reason})", reason);
        }
    }

    private void HandleMultiplayerInfo(IS_ISM packet)
    {
        var hostNameRaw = LfsColorConverter.Decode(packet.HName ?? Array.Empty<byte>());
        _raceSession.HostName = LfsColorConverter.RemoveColorCodes(hostNameRaw);
        _raceSession.HostNameHtml = LfsColorConverter.ConvertToHtml(hostNameRaw);

        if (!string.IsNullOrWhiteSpace(_raceSession.HostName))
        {
            _logger.LogInformation(
                "🌐 MULTIPLAYER HOST: {HostName} | Mode: {Mode}",
                _raceSession.HostName,
                packet.Host == 1 ? "Host" : "Guest");
        }
    }

    private void HandleMessageOut(byte[] packet)
    {
        const int headerSize = 8;

        if (packet.Length <= headerSize)
        {
            return;
        }

        var userType = packet[6];
        var msgBytes = new byte[packet.Length - headerSize];
        Array.Copy(packet, headerSize, msgBytes, 0, msgBytes.Length);

        var messageLfsText = LfsColorConverter.Decode(msgBytes);
        var plainMessage = LfsColorConverter.RemoveColorCodes(messageLfsText).Trim();
        if (string.IsNullOrWhiteSpace(plainMessage))
        {
            return;
        }

        _raceSession.AddChatMessage(new ChatMessageEntry
        {
            Kind = userType switch
            {
                (byte)MsoUserType.User => "user",
                (byte)MsoUserType.Prefix => "prefix",
                (byte)MsoUserType.Local => "local",
                _ => "system"
            },
            MessageText = plainMessage,
            MessageLfsText = messageLfsText,
            ReceivedAtUtc = DateTime.UtcNow
        });
    }

    // ── Packet Handlers ─────────────────────────────────
    private void HandleMultiCarInfo(byte[] packet)
    {
        const int headerSize = 4;
        const int compCarSize = 28;

        if (packet.Length < headerSize)
        {
            return;
        }

        var numCars = packet[3];
        var availableCars = Math.Min(numCars, (packet.Length - headerSize) / compCarSize);
        var allowRelaxedTrackMapSampling = ShouldUseRelaxedTrackMapSampling();
        var useSpatialTrackMapKeying = ShouldUseSpatialTrackMapKeying();
        var contributedDrivers = 0;
        var distinctNodes = new HashSet<ushort>();
        byte? leadingPlayerId = null;
        byte leadingRacePosition = byte.MaxValue;

        for (var index = 0; index < availableCars; index++)
        {
            var offset = headerSize + (index * compCarSize);
            var carBytes = new byte[compCarSize];
            Array.Copy(packet, offset, carBytes, 0, compCarSize);

            var car = InSimConnection.BytesToStruct<CompCar>(carBytes);
            if (car.PLID == 0)
            {
                continue;
            }

            var driver = _raceSession.GetDriver(car.PLID);
            if (driver == null)
            {
                continue;
            }

            driver.UpdateLiveTelemetry(car.Node, car.Lap, car.Position, car.X, car.Y, car.Heading, car.Speed);

            if (driver.ShouldContributeToTrackMap(allowRelaxedTrackMapSampling))
            {
                if (car.Position > 0 && car.Position < leadingRacePosition)
                {
                    leadingRacePosition = car.Position;
                    leadingPlayerId = car.PLID;
                }

                var trackMapKey = useSpatialTrackMapKeying
                    ? BuildCustomTrackMapKey(car.X, car.Y)
                    : car.Node;
                var displayNode = useSpatialTrackMapKeying ? (ushort)0 : car.Node;
                var isReferenceDriver = useSpatialTrackMapKeying && _referenceTrackMapPlayerId.HasValue && _referenceTrackMapPlayerId.Value == car.PLID;

                if (useSpatialTrackMapKeying && isReferenceDriver && !_raceSession.HasTrackMapNode(trackMapKey))
                {
                    trackMapKey = _raceSession.TryFindTrackMapClosureKey(
                        car.X,
                        car.Y,
                        TrackMapClosureDistanceWorldSize,
                        TrackMapClosureMinimumAssignedPoints,
                        TrackMapClosureCandidatePointCount) ?? trackMapKey;
                }

                if (useSpatialTrackMapKeying && !isReferenceDriver && !_raceSession.HasTrackMapNode(trackMapKey))
                {
                    contributedDrivers++;
                    distinctNodes.Add(car.Node);
                    continue;
                }

                _raceSession.UpdateTrackMapNode(
                    trackMapKey,
                    displayNode,
                    car.X,
                    car.Y,
                    useInsertionOrder: isReferenceDriver,
                    deferSortOrder: useSpatialTrackMapKeying);
                contributedDrivers++;
                distinctNodes.Add(car.Node);
            }
        }

        if (ShouldActivateObservedSpatialFallback(useSpatialTrackMapKeying, contributedDrivers, distinctNodes.Count))
        {
            _useObservedSpatialTrackMapFallback = true;
            _collapsedNodeTraceCount = 0;
            _referenceTrackMapPlayerId = leadingPlayerId;
            _raceSession.ClearTrackMap();
            useSpatialTrackMapKeying = true;
        }
        else if (useSpatialTrackMapKeying && contributedDrivers > 0)
        {
            if (!_referenceTrackMapPlayerId.HasValue || (leadingPlayerId.HasValue && leadingRacePosition == 1))
            {
                _referenceTrackMapPlayerId = leadingPlayerId ?? _referenceTrackMapPlayerId;
            }
        }
    }

    private bool ShouldUseSpatialTrackMapKeying()
    {
        if (_useObservedSpatialTrackMapFallback)
        {
            return true;
        }

        if (_raceSession.ActiveSectorCount == 0)
        {
            return true;
        }

        return false;
    }

    private static int BuildCustomTrackMapKey(int x, int y)
    {
        var bucketX = (int)Math.Floor((double)x / CustomTrackMapBucketWorldSize);
        var bucketY = (int)Math.Floor((double)y / CustomTrackMapBucketWorldSize);

        return HashCode.Combine(bucketX, bucketY);
    }

    private bool ShouldUseRelaxedTrackMapSampling()
    {
        if (_raceSession.ActiveSectorCount == 0)
        {
            return true;
        }

        return IsLayoutCapableTrack(_raceSession.TrackName);
    }

    private static bool IsLayoutCapableTrack(string trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            return false;
        }

        var normalizedTrackName = trackName.Trim().ToUpperInvariant();
        return normalizedTrackName.StartsWith("AU", StringComparison.Ordinal)
            || normalizedTrackName.EndsWith("X", StringComparison.Ordinal);
    }

    private bool ShouldActivateObservedSpatialFallback(bool useSpatialTrackMapKeying, int contributedDrivers, int distinctNodeCount)
    {
        if (useSpatialTrackMapKeying || contributedDrivers == 0)
        {
            _collapsedNodeTraceCount = 0;
            return false;
        }

        if (!IsLayoutCapableTrack(_raceSession.TrackName))
        {
            _collapsedNodeTraceCount = 0;
            return false;
        }

        if (distinctNodeCount > 1)
        {
            _collapsedNodeTraceCount = 0;
            return false;
        }

        _collapsedNodeTraceCount++;
        return _collapsedNodeTraceCount >= 3;
    }

    /// <summary>
    /// Handles IS_STA (Session State) packets - reports current race/session status
    /// </summary>
    private void HandleSessionState(IS_STA packet)
    {
        // Extract track name (6 chars)
        var trackName = System.Text.Encoding.ASCII.GetString(packet.Track).TrimEnd('\0').Trim();
        if (!string.IsNullOrEmpty(trackName) && !string.Equals(_raceSession.TrackName, trackName, StringComparison.OrdinalIgnoreCase))
        {
            _useObservedSpatialTrackMapFallback = false;
            _collapsedNodeTraceCount = 0;
            _referenceTrackMapPlayerId = null;
            _raceSession.ClearTrackMap();
        }

        if (!string.IsNullOrEmpty(trackName))
            _raceSession.TrackName = trackName;
        else
            _raceSession.TrackName = "Unknown";

        _raceSession.WeatherType = packet.Weather;
        _raceSession.WindType = packet.Wind;
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
            driver.ConnectionId = packet.UCID;
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
                ConnectionId = packet.UCID,
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
        driver.UpdatePitStops(packet.NumStops);
        
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

        if (ShouldIgnoreQualifyingPlaceholderLap(packet))
        {
            driver.LapsCompleted = packet.LapsDone;
            driver.CurrentLapSectors.Clear();
            driver.CurrentLapSplitTimes.Clear();

            _logger.LogDebug(
                "Ignoring qualifying placeholder lap for {PlayerName} | Lap {LapNumber} | LTime: {LapTime} | ETime: {ElapsedTime}",
                LfsColorConverter.RemoveColorCodes(driver.Name),
                packet.LapsDone,
                packet.LTime,
                packet.ETime);

            return;
        }

        driver.AddLap(lapData, _raceSession.ActiveSectorCount);
        driver.LapsCompleted = packet.LapsDone;
        driver.RecordTimingPoint(packet.LapsDone, _raceSession.ActiveSectorCount, packet.ETime);
        
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

        if (ShouldIgnoreQualifyingPlaceholderSplit(packet))
        {
            _logger.LogDebug(
                "Ignoring qualifying placeholder split for {PlayerName} | Split: {Split} | STime: {SplitTime} | ETime: {ElapsedTime}",
                LfsColorConverter.RemoveColorCodes(driver.Name),
                packet.Split,
                packet.STime,
                packet.ETime);

            return;
        }

        // Calculate fuel: if 255 it's disabled (server has /showfuel no), otherwise fuel_percent = Fuel200 / 2
        byte? fuelPercent = packet.Fuel200 == 255 ? null : (byte?)Math.Min(100, packet.Fuel200 / 2);
        driver.FuelPercent = fuelPercent;
        driver.UpdatePitStops(packet.NumStops);

        // Update sector time from cumulative split time
        driver.UpdateSectorTime(packet.Split, packet.STime);
        driver.RecordTimingPoint(driver.LapsCompleted + 1, packet.Split, packet.ETime);
        var sectorTimeMs = driver.CurrentLapSectors.TryGetValue(packet.Split, out var sector)
            ? sector.TimeMs
            : packet.STime;

        _logger.LogDebug(
            "🎯 Sector {Sector} | {PlayerName}: {SectorTime}ms (Split: {SplitTime}ms, Elapsed: {ElapsedTime}ms) | Active sectors: {ActiveSectorCount} | SessionType: {SessionType} | Fuel: {Fuel} | Stops: {Stops}",
            packet.Split,
            LfsColorConverter.RemoveColorCodes(driver.Name),
            sectorTimeMs,
            packet.STime,
            packet.ETime,
            _raceSession.ActiveSectorCount,
            _raceSession.SessionType,
            driver.FuelPercent.HasValue ? $"{driver.FuelPercent}%" : "N/A",
            packet.NumStops);
    }

    private void HandlePitStopStart(IS_PIT packet)
    {
        var driver = _raceSession.GetDriver(packet.PLID) ?? GetOrCreatePlaceholderDriver(packet.PLID, "PIT");

        driver.LapsCompleted = packet.LapsDone;
        driver.StartPitStop(
            packet.NumStops,
            packet.FuelAdd,
            new[] { packet.Tyres0, packet.Tyres1, packet.Tyres2, packet.Tyres3 });

        _logger.LogInformation(
            "🛠️ PIT STOP START: {PlayerName} | Stops: {Stops} | Fuel add: {FuelAdd}",
            LfsColorConverter.RemoveColorCodes(driver.Name),
            driver.PitStops,
            packet.FuelAdd == 255 ? "N/A" : $"{packet.FuelAdd}%");
    }

    private bool ShouldIgnoreQualifyingPlaceholderLap(IS_LAP packet)
    {
        return _raceSession.SessionType == 1
            && packet.LTime == QualifyingPlaceholderTimeMs
            && packet.ETime < QualifyingPlaceholderTimeMs;
    }

    private bool ShouldIgnoreQualifyingPlaceholderSplit(IS_SPX packet)
    {
        return _raceSession.SessionType == 1
            && packet.STime == QualifyingPlaceholderTimeMs
            && packet.ETime == QualifyingPlaceholderTimeMs;
    }

    private void HandlePitStopFinish(IS_PSF packet)
    {
        var driver = _raceSession.GetDriver(packet.PLID) ?? GetOrCreatePlaceholderDriver(packet.PLID, "PSF");
        driver.FinishPitStop(packet.STime);

        _logger.LogInformation(
            "✅ PIT STOP FINISH: {PlayerName} | Stop time: {StopTime}ms",
            LfsColorConverter.RemoveColorCodes(driver.Name),
            packet.STime);
    }

    private void HandlePitLaneChange(IS_PLA packet)
    {
        var driver = _raceSession.GetDriver(packet.PLID) ?? GetOrCreatePlaceholderDriver(packet.PLID, "PLA");
        var pitLaneFact = Enum.IsDefined(typeof(PitLaneFact), packet.Fact)
            ? (PitLaneFact)packet.Fact
            : PitLaneFact.Enter;

        driver.UpdatePitLaneState(pitLaneFact, DateTime.UtcNow);

        _logger.LogDebug(
            "🧭 PIT LANE: {PlayerName} | {PitLaneFact}",
            LfsColorConverter.RemoveColorCodes(driver.Name),
            GetPitLaneFactLabel(pitLaneFact));
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

        if (packet.ReqI == 0)
        {
            QueuePlayerOnboardingMessage(packet.UCID, userName, nickName);
        }
    }

    private void QueuePlayerOnboardingMessage(byte connectionId, string userName, string nickName)
    {
        if (!_playerOnboardingOptions.Enabled || connectionId == 0 || _connection?.IsConnected != true)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var messages = BuildPlayerOnboardingMessages().ToList();
                for (var index = 0; index < messages.Count; index++)
                {
                    await SendConnectionMessageAsync(
                        connectionId,
                        messages[index],
                        CancellationToken.None,
                        index == 0 ? MessageSoundSystem : MessageSoundSilent);
                }

                _logger.LogDebug(
                    "Sent player onboarding message to UCID {UCID} ({UserName} / {NickName})",
                    connectionId,
                    userName,
                    nickName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send player onboarding message to UCID {UCID} ({UserName} / {NickName})",
                    connectionId,
                    userName,
                    nickName);
            }
        });
    }

    private IEnumerable<string> BuildPlayerOnboardingMessages()
    {
        yield return "^3LIVE TIMING AVAILABLE ^8- ^7LFS Pit Wall by ^1Kamileon^8";
        yield return "^7Open the website for ^1live timing^7, ^1stats^7 and ^1archived results^8.";

        var publicUrl = _playerOnboardingOptions.GetNormalizedPublicUrl();
        if (!string.IsNullOrEmpty(publicUrl))
        {
            yield return $"^5Website:^8 {publicUrl}";
            yield break;
        }
    }

    private async Task SendConnectionMessageAsync(byte connectionId, string message, CancellationToken cancellationToken, byte sound)
    {
        if (_connection?.IsConnected != true || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await _connection.SendAsync(BuildConnectionMessagePacket(connectionId, message, sound), cancellationToken);
    }

    private static byte[] BuildConnectionMessagePacket(byte connectionId, string message, byte sound)
    {
        var sanitizedMessage = (message ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        var messageBytes = Encoding.ASCII.GetBytes(sanitizedMessage);
        var messageLength = Math.Min(messageBytes.Length, 127);
        var textSize = ((messageLength + 1 + 3) / 4) * 4;
        var packet = new byte[8 + textSize];

        packet[0] = (byte)(packet.Length / 4);
        packet[1] = (byte)InSimPacketType.ISP_MTC;
        packet[2] = 0;
        packet[3] = sound;
        packet[4] = connectionId;
        packet[5] = 0;
        packet[6] = 0;
        packet[7] = 0;

        Array.Copy(messageBytes, 0, packet, 8, messageLength);
        packet[8 + messageLength] = 0;

        return packet;
    }

    /// <summary>
    /// Handles IS_CNL (Connection Leave) packets - player disconnecting from LFS
    /// </summary>
    private void HandleConnectionLeave(IS_CNL packet)
    {
        _logger.LogDebug(
            "🔌 Connection Leave: UCID {UCID} - Total: {Total}",
            packet.UCID, packet.Total);

        _raceSession.RemoveDriversByConnection(packet.UCID);
        
        // Clean up username mapping when connection leaves
        _raceSession.RemoveUsername(packet.UCID);
    }

    private void HandleTiny(IS_TINY packet)
    {
        if (packet.SubT == (byte)TinyPacketType.TINY_REN || packet.SubT == (byte)TinyPacketType.TINY_CLR)
        {
            _ = RequestOfficialResultsAsync(((TinyPacketType)packet.SubT).ToString(), force: true);
        }
    }

    /// <summary>
    /// Handles IS_FIN packets so official classified results can be requested before session reset.
    /// </summary>
    private void HandleFinish(IS_FIN packet)
    {
        _logger.LogDebug(
            "🏁 Finish notification: PLID {PLID} | Laps {LapsDone} | Stops {NumStops}",
            packet.PLID,
            packet.LapsDone,
            packet.NumStops);

        _ = RequestOfficialResultsAsync("finish-notification");
    }

    /// <summary>
    /// Handles IS_RES (Result) packets - race results
    /// </summary>
    private void HandleResult(IS_RES packet)
    {
        var username = LfsColorConverter.RemoveColorCodes(LfsColorConverter.Decode(packet.UName ?? Array.Empty<byte>())).Trim();
        var driverName = DriverNameHelper.FormatPlayerName(LfsColorConverter.Decode(packet.PName ?? Array.Empty<byte>()));
        var carName = DriverNameHelper.ParseCarName(packet.CName ?? Array.Empty<byte>());
        var officialResult = new OfficialResult
        {
            Kind = _raceSession.SessionType == 1 ? OfficialResultKind.Qualifying : OfficialResultKind.Race,
            TotalTimeMs = packet.TTime,
            BestLapTimeMs = packet.BTime,
            NumStops = packet.NumStops,
            LapsDone = packet.LapsDone,
            Flags = packet.Flags,
            ConfirmFlags = packet.Confirm,
            ResultNum = packet.ResultNum,
            NumRes = packet.NumRes,
            PenaltySeconds = packet.PSeconds,
            PositionPoints = GetOfficialResultPositionPoints(packet.ResultNum)
        };

        _raceSession.ApplyOfficialResult(packet.PLID, username, driverName, carName, officialResult);
        RecalculateOfficialResultBonuses();

        _logger.LogDebug(
            "📊 Official result: PLID {PLID} | User {Username} | Kind {Kind} | Pos {Position} | Points {Points}",
            packet.PLID,
            string.IsNullOrWhiteSpace(username) ? "-" : username,
            officialResult.Kind,
            officialResult.Position?.ToString() ?? "unclassified",
            officialResult.TotalPoints);
    }

    private void HandleReorder(IS_REO packet)
    {
        if (_raceSession.SessionType != 2 || packet.NumP == 0 || packet.PLID == null)
        {
            return;
        }

        var orderedPlayerIds = packet.PLID
            .Take(packet.NumP)
            .Where(playerId => playerId != 0)
            .ToList();

        if (orderedPlayerIds.Count == 0)
        {
            return;
        }

        _raceSession.UpdateRaceStartOrder(orderedPlayerIds);
        RecalculateOfficialResultBonuses();

        _logger.LogDebug(
            "📋 Stored race start order for {DriverCount} drivers",
            orderedPlayerIds.Count);
    }

    private void RecalculateOfficialResultBonuses()
    {
        _raceSession.RecalculateOfficialResultBonuses(
            _championshipScoringOptions.Bonuses.PolePosition,
            _championshipScoringOptions.Bonuses.FastestLap,
            _championshipScoringOptions.Bonuses.HighestClimber);
    }

    private int GetOfficialResultPositionPoints(byte resultNum)
    {
        if (resultNum == byte.MaxValue)
        {
            return 0;
        }

        return _championshipScoringOptions.GetPointsForPosition(resultNum + 1);
    }

    /// <summary>
    /// Handles IS_RST (Race Start) packets - reports race/qualifying configuration
    /// </summary>
    private void HandleRaceStart(IS_RST packet)
    {
        var trackName = System.Text.Encoding.ASCII.GetString(packet.Track).TrimEnd('\0').Trim();
        var normalizedTrackName = string.IsNullOrEmpty(trackName) ? "Unknown" : trackName;
        var didResetSession = _sessionLifecycleManager.ObserveRaceStart(
            normalizedTrackName,
            packet.RaceLaps,
            packet.QualMins,
            packet.Timing,
            packet.ReqI != 0);

        if (!string.IsNullOrEmpty(trackName) && !string.Equals(_raceSession.TrackName, normalizedTrackName, StringComparison.OrdinalIgnoreCase))
        {
            _useObservedSpatialTrackMapFallback = false;
            _collapsedNodeTraceCount = 0;
            _referenceTrackMapPlayerId = null;
            _raceSession.ClearTrackMap();
        }

        _raceSession.TrackName = normalizedTrackName;
        _raceSession.WeatherType = packet.Weather;
        _raceSession.WindType = packet.Wind;
        _raceSession.SessionType = packet.RaceLaps == 0 ? (byte)1 : (byte)2;
        _raceSession.RaceInProgress = packet.RaceLaps > 0;
        
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
            _raceSession.GetWindTypeString());

        if (didResetSession)
        {
            _ = RequestInitialDataAsync();
        }
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
        if (_archiveOptions.Enabled && _archiveOptions.WriteOnApplicationStop)
        {
            _sessionArchiveWriter.ArchiveCurrentSessionIfNeeded("application-stop");
        }

        await CloseConnectionAsync();
        await base.StopAsync(cancellationToken);
    }

    private static string GetPitLaneFactLabel(PitLaneFact fact) => fact switch
    {
        PitLaneFact.Exit => "exit",
        PitLaneFact.Enter => "entry",
        PitLaneFact.NoPurpose => "no purpose",
        PitLaneFact.DriveThrough => "drive-through",
        PitLaneFact.StopGo => "stop-go",
        _ => "unknown"
    };
}
