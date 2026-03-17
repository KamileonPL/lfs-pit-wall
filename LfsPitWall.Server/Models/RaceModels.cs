using LfsPitWall.Server.Models.Archive;

namespace LfsPitWall.Server.Models;

/// <summary>
/// Represents a single sector's timing data
/// </summary>
public class SectorTime
{
    /// <summary>
    /// Sector number (1, 2, or 3)
    /// </summary>
    public int SectorNumber { get; set; }

    /// <summary>
    /// Time for this sector in milliseconds
    /// </summary>
    public uint TimeMs { get; set; }

    /// <summary>
    /// Whether this sector time is valid (not cut, etc.)
    /// </summary>
    public bool IsValid { get; set; } = true;
}

/// <summary>
/// Represents the best sector in the session together with its author.
/// </summary>
public class SessionBestSectorInfo
{
    public uint TimeMs { get; set; }
    public string AuthorNameHtml { get; set; } = "";
    public string AuthorUsername { get; set; } = "";
}

/// <summary>
/// Represents a single chat line received from LFS.
/// </summary>
public class ChatMessageEntry
{
    public string Kind { get; set; } = "system";
    public string MessageText { get; set; } = "";
    public string MessageLfsText { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a driver's most recent elapsed-time checkpoint in the session.
/// </summary>
public class TimingPointSnapshot
{
    public uint LapNumber { get; set; }
    public int TimingPointIndex { get; set; }
    public uint ElapsedTimeMs { get; set; }
}

/// <summary>
/// Represents an averaged world-space point for a track node.
/// </summary>
public class TrackMapNodeSample
{
    private const int MaxStoredSamples = 41;
    public const uint DeferredSortOrder = uint.MaxValue;
    private readonly List<TrackMapRawSample> _samples = new();

    public ushort Node { get; set; }
    public uint SampleCount { get; private set; }
    public uint SortOrder { get; set; }

    public void AddSample(int x, int y)
    {
        SampleCount++;

        if (_samples.Count >= MaxStoredSamples)
        {
            _samples.RemoveAt(0);
        }

        _samples.Add(new TrackMapRawSample(x, y));
    }

    public TrackMapPoint ToPoint()
    {
        if (_samples.Count == 0)
        {
            return new TrackMapPoint { Node = Node };
        }

        var orderedX = _samples.Select(sample => sample.X).OrderBy(value => value).ToList();
        var orderedY = _samples.Select(sample => sample.Y).OrderBy(value => value).ToList();
        var medianX = orderedX[orderedX.Count / 2];
        var medianY = orderedY[orderedY.Count / 2];

        return new TrackMapPoint
        {
            Node = Node,
            X = medianX,
            Y = medianY
        };
    }

    private readonly record struct TrackMapRawSample(int X, int Y);
}

/// <summary>
/// Represents a sampled point on the approximated live track map.
/// </summary>
public class TrackMapPoint
{
    public ushort Node { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// Snapshot of the currently observed track map.
/// </summary>
public class TrackMapSnapshot
{
    public uint Revision { get; set; }
    public List<TrackMapPoint> Points { get; set; } = new();
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
}

/// <summary>
/// Represents a single lap's complete timing data (lap-centric architecture)
/// </summary>
public class LapData
{
    /// <summary>
    /// Lap number (1-based)
    /// </summary>
    public uint LapNumber { get; set; }

    /// <summary>
    /// Total lap time in milliseconds
    /// </summary>
    public uint LapTimeMs { get; set; }

    /// <summary>
    /// Elapsed time from session start in milliseconds
    /// </summary>
    public uint ElapsedTimeMs { get; set; }

    /// <summary>
    /// Sector times (S1, S2, S3)
    /// </summary>
    public Dictionary<int, SectorTime> Sectors { get; set; } = new();

    /// <summary>
    /// Whether this lap is valid
    /// </summary>
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// Number of pit stops during this lap
    /// </summary>
    public uint PitStops { get; set; }

    /// <summary>
    /// Penalties in milliseconds
    /// </summary>
    public uint PenaltyMs { get; set; }

    /// <summary>
    /// Gear at end of lap
    /// </summary>
    public byte Gear { get; set; }

    /// <summary>
    /// Timestamp when lap was recorded
    /// </summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the full lap time including penalties
    /// </summary>
    public uint GetAdjustedTime() => LapTimeMs + PenaltyMs;

    /// <summary>
    /// Gets a specific sector time by sector number (1, 2, 3)
    /// </summary>
    public uint? GetSectorTime(int sectorNumber)
    {
        return Sectors.TryGetValue(sectorNumber, out var sector) ? sector.TimeMs : null;
    }
}

public enum OfficialResultKind
{
    Qualifying = 1,
    Race = 2
}

public class OfficialResult
{
    public OfficialResultKind Kind { get; set; }
    public uint TotalTimeMs { get; set; }
    public uint BestLapTimeMs { get; set; }
    public byte NumStops { get; set; }
    public ushort LapsDone { get; set; }
    public ushort Flags { get; set; }
    public byte ConfirmFlags { get; set; }
    public byte ResultNum { get; set; }
    public byte NumRes { get; set; }
    public ushort PenaltySeconds { get; set; }
    public int PositionPoints { get; set; }
    public int PolePositionBonusPoints { get; set; }
    public int FastestLapBonusPoints { get; set; }
    public int HighestClimberBonusPoints { get; set; }

    public int BonusPoints => PolePositionBonusPoints + FastestLapBonusPoints + HighestClimberBonusPoints;

    public int TotalPoints => PositionPoints + BonusPoints;

    public int? Position => ResultNum == byte.MaxValue ? null : ResultNum + 1;
}

public class OfficialResultIndexEntry
{
    public string Username { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string CarName { get; set; } = "";
    public byte? PlayerId { get; set; }
    public OfficialResult Result { get; set; } = new();
}

/// <summary>
/// Represents a driver in the current session
/// </summary>
public class Driver
{
    /// <summary>
    /// Player ID (unique identifier from InSim)
    /// </summary>
    public byte PlayerId { get; set; }

    /// <summary>
    /// Driver name (with LFS color codes like ^1, ^2, etc)
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// LFS Username (from IS_NCN, e.g., limac92)
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// Driver name as HTML with colors (LFS codes converted to span colors)
    /// </summary>
    public string NameHtml { get; set; } = "";

    /// <summary>
    /// Car name (e.g., "XFG", "LX6", etc.)
    /// </summary>
    public string CarName { get; set; } = "";

    /// <summary>
    /// Skin name
    /// </summary>
    public string SkinName { get; set; } = "";

    /// <summary>
    /// Tyre types for each wheel (FL, FR, RL, RR)
    /// </summary>
    public byte[] TyreTypes { get; set; } = new byte[4];

    /// <summary>
    /// Current fuel level (0-100%), or null if /showfuel is disabled on server
    /// </summary>
    public byte? FuelPercent { get; set; }

    /// <summary>
    /// Driver/team color (for UI highlighting, hex format like "#FF5733")
    /// </summary>
    public string DriverColor { get; set; } = "#9CA3AF";  // Neutral gray until a real team color is assigned

    /// <summary>
    /// Number of pit stops
    /// </summary>
    public uint PitStops { get; set; }

    /// <summary>
    /// LFS connection UCID associated with this driver.
    /// </summary>
    public byte ConnectionId { get; set; }

    /// <summary>
    /// Whether the driver is currently in the pit lane.
    /// </summary>
    public bool IsInPitLane { get; private set; }

    /// <summary>
    /// Whether the driver is currently stopped for pit work.
    /// </summary>
    public bool IsPitStopActive { get; private set; }

    /// <summary>
    /// Last pit lane fact reported by LFS.
    /// </summary>
    public PitLaneFact? LastPitLaneFact { get; private set; }

    /// <summary>
    /// Last completed pit stop duration in milliseconds.
    /// </summary>
    public uint? LastPitStopTimeMs { get; private set; }

    /// <summary>
    /// Timestamp when the driver most recently entered the pit lane.
    /// </summary>
    public DateTime? PitLaneEnteredAtUtc { get; private set; }

    /// <summary>
    /// Last completed total pit lane traversal duration in milliseconds.
    /// </summary>
    public uint? LastPitLaneTimeMs { get; private set; }

    /// <summary>
    /// Fuel added during the latest pit stop, if LFS reports it.
    /// </summary>
    public byte? LastPitStopFuelAddPercent { get; private set; }

    /// <summary>
    /// Tyres changed during the latest pit stop.
    /// </summary>
    public byte[] LastPitTyresChanged { get; private set; } = new byte[4];

    /// <summary>
    /// Number of laps completed
    /// </summary>
    public uint LapsCompleted { get; set; }

    /// <summary>
    /// Last elapsed-time checkpoint recorded for this driver.
    /// </summary>
    public TimingPointSnapshot? LastTimingPoint { get; private set; }

    /// <summary>
    /// Current race position from NLP/MCI packets. 0 means unknown.
    /// </summary>
    public byte CurrentRacePosition { get; set; }

    /// <summary>
    /// Current track node from NLP/MCI packets.
    /// </summary>
    public ushort CurrentTrackNode { get; set; }

    /// <summary>
    /// Current lap from NLP/MCI packets.
    /// </summary>
    public ushort CurrentTrackLap { get; set; }

    /// <summary>
    /// Whether the driver currently has a valid world-space position from MCI.
    /// </summary>
    public bool HasWorldPosition { get; private set; }

    /// <summary>
    /// Driver world-space X coordinate from MCI.
    /// </summary>
    public int WorldX { get; private set; }

    /// <summary>
    /// Driver world-space Y coordinate from MCI.
    /// </summary>
    public int WorldY { get; private set; }

    /// <summary>
    /// Driver heading from MCI.
    /// </summary>
    public ushort CurrentHeading { get; private set; }

    /// <summary>
    /// Driver's highest speed reached in the current session, in km/h.
    /// </summary>
    public double TopSpeedKmh { get; private set; }

    /// <summary>
    /// Timestamp of the most recent live telemetry update used by the map.
    /// </summary>
    public DateTime? LastLiveTelemetryAtUtc { get; private set; }

    /// <summary>
    /// Lap history (lap-centric architecture - all laps stored here)
    /// </summary>
    public List<LapData> LapHistory { get; set; } = new();

    /// <summary>
    /// Driver's personal best lap
    /// </summary>
    public LapData? PersonalBestLap { get; private set; }

    /// <summary>
    /// Driver's personal best time for each sector
    /// </summary>
    public Dictionary<int, uint> PersonalBestSectors { get; set; } = new();

    /// <summary>
    /// Official result confirmed by LFS for the current session, when available.
    /// </summary>
    public OfficialResult? OfficialResult { get; private set; }

    /// <summary>
    /// Sector times recorded for the current in-progress lap.
    /// </summary>
    public Dictionary<int, SectorTime> CurrentLapSectors { get; } = new();

    /// <summary>
    /// Cumulative split times recorded for the current in-progress lap.
    /// </summary>
    public Dictionary<int, uint> CurrentLapSplitTimes { get; } = new();

    /// <summary>
    /// Recent elapsed times keyed by lap number and timing-point index.
    /// </summary>
    public Dictionary<uint, Dictionary<int, uint>> TimingPointElapsedTimes { get; } = new();

    /// <summary>
    /// Gets the current lap's sector times that have been recorded so far
    /// </summary>
    public Dictionary<int, SectorTime> GetCurrentSectorProgress()
    {
        return CurrentLapSectors.ToDictionary(
            kvp => kvp.Key,
            kvp => new SectorTime
            {
                SectorNumber = kvp.Value.SectorNumber,
                TimeMs = kvp.Value.TimeMs,
                IsValid = kvp.Value.IsValid
            });
    }

    /// <summary>
    /// Updates an in-progress lap sector from a cumulative split time.
    /// </summary>
    public void UpdateSectorTime(int sectorNumber, uint splitTimeMs)
    {
        if (sectorNumber < 1 || sectorNumber > 3)
            return;

        uint previousSplitTime = 0;
        if (sectorNumber > 1)
        {
            CurrentLapSplitTimes.TryGetValue(sectorNumber - 1, out previousSplitTime);
        }

        uint sectorTimeMs = splitTimeMs >= previousSplitTime
            ? splitTimeMs - previousSplitTime
            : splitTimeMs;

        CurrentLapSplitTimes[sectorNumber] = splitTimeMs;

        var sectorTime = new SectorTime
        {
            SectorNumber = sectorNumber,
            TimeMs = sectorTimeMs,
            IsValid = true
        };

        CurrentLapSectors[sectorNumber] = sectorTime;
    }

    /// <summary>
    /// Stores an elapsed time at a specific timing point for later gap comparisons.
    /// </summary>
    public void RecordTimingPoint(uint lapNumber, int timingPointIndex, uint elapsedTimeMs)
    {
        if (lapNumber == 0 || timingPointIndex <= 0 || elapsedTimeMs == 0)
        {
            return;
        }

        if (!TimingPointElapsedTimes.TryGetValue(lapNumber, out var lapTimingPoints))
        {
            lapTimingPoints = new Dictionary<int, uint>();
            TimingPointElapsedTimes[lapNumber] = lapTimingPoints;
        }

        lapTimingPoints[timingPointIndex] = elapsedTimeMs;
        LastTimingPoint = new TimingPointSnapshot
        {
            LapNumber = lapNumber,
            TimingPointIndex = timingPointIndex,
            ElapsedTimeMs = elapsedTimeMs
        };

        PruneTimingPointHistory(lapNumber);
    }

    public bool TryGetTimingPointElapsedTime(uint lapNumber, int timingPointIndex, out uint elapsedTimeMs)
    {
        elapsedTimeMs = 0;

        return TimingPointElapsedTimes.TryGetValue(lapNumber, out var lapTimingPoints)
            && lapTimingPoints.TryGetValue(timingPointIndex, out elapsedTimeMs);
    }

    /// <summary>
    /// Updates the driver's session top speed from raw MCI speed units.
    /// </summary>
    public void UpdateTopSpeed(ushort rawSpeed)
    {
        if (rawSpeed == 0)
        {
            return;
        }

        var speedKmh = rawSpeed * 360.0 / 32768.0;
        if (speedKmh > TopSpeedKmh)
        {
            TopSpeedKmh = speedKmh;
        }
    }

    public void UpdateLiveTelemetry(ushort trackNode, ushort trackLap, byte racePosition, int worldX, int worldY, ushort heading, ushort rawSpeed)
    {
        CurrentTrackNode = trackNode;
        CurrentTrackLap = trackLap;
        CurrentRacePosition = racePosition;
        HasWorldPosition = true;
        WorldX = worldX;
        WorldY = worldY;
        CurrentHeading = heading;
        LastLiveTelemetryAtUtc = DateTime.UtcNow;
        UpdateTopSpeed(rawSpeed);
    }

    public bool HasFreshWorldPosition(DateTime snapshotTimeUtc, TimeSpan maxTelemetryAge)
    {
        if (!HasWorldPosition || !LastLiveTelemetryAtUtc.HasValue)
        {
            return false;
        }

        return snapshotTimeUtc - LastLiveTelemetryAtUtc.Value <= maxTelemetryAge;
    }

    public bool ShouldContributeToTrackMap(bool allowRelaxedSampling = false)
    {
        return HasWorldPosition
            && (allowRelaxedSampling || (!IsInPitLane && !IsPitStopActive));
    }

    public void UpdatePitStops(uint pitStops)
    {
        PitStops = pitStops;
    }

    public void SetOfficialResult(OfficialResult officialResult)
    {
        OfficialResult = officialResult;
    }

    public void UpdatePitLaneState(PitLaneFact fact, DateTime occurredAtUtc)
    {
        LastPitLaneFact = fact;

        if (fact == PitLaneFact.Exit)
        {
            if (PitLaneEnteredAtUtc.HasValue && occurredAtUtc >= PitLaneEnteredAtUtc.Value)
            {
                LastPitLaneTimeMs = (uint)(occurredAtUtc - PitLaneEnteredAtUtc.Value).TotalMilliseconds;
            }

            PitLaneEnteredAtUtc = null;
            IsInPitLane = false;
            IsPitStopActive = false;
            return;
        }

        if (!PitLaneEnteredAtUtc.HasValue)
        {
            PitLaneEnteredAtUtc = occurredAtUtc;
        }

        IsInPitLane = true;
    }

    public void StartPitStop(uint pitStops, byte fuelAdd, byte[] tyresChanged)
    {
        PitStops = pitStops;
        IsInPitLane = true;
        IsPitStopActive = true;
        LastPitStopFuelAddPercent = fuelAdd == 255 ? null : fuelAdd;
        LastPitTyresChanged = tyresChanged.ToArray();
    }

    public void FinishPitStop(uint stopTimeMs)
    {
        IsPitStopActive = false;
        LastPitStopTimeMs = stopTimeMs;
    }

    public uint? GetDisplayedPitLaneTimeMs(DateTime nowUtc)
    {
        if (PitLaneEnteredAtUtc.HasValue && nowUtc >= PitLaneEnteredAtUtc.Value)
        {
            return (uint)(nowUtc - PitLaneEnteredAtUtc.Value).TotalMilliseconds;
        }

        return LastPitLaneTimeMs;
    }

    public string GetPitStatus()
    {
        if (IsPitStopActive)
        {
            return "service";
        }

        return LastPitLaneFact switch
        {
            PitLaneFact.Enter => "lane",
            PitLaneFact.NoPurpose => "no-purpose",
            PitLaneFact.DriveThrough => "drive-through",
            PitLaneFact.StopGo => "stop-go",
            PitLaneFact.Exit => "track",
            _ => IsInPitLane ? "lane" : "track"
        };
    }

    /// <summary>
    /// Adds a completed lap to the history and finalizes sector data for that lap.
    /// </summary>
    public void AddLap(LapData lap, int activeSectorCount)
    {
        FinalizeCurrentLapSectors(lap, activeSectorCount);
        LapHistory.Add(lap);

        if (!lap.IsValid)
        {
            ClearCurrentLapProgress();
            return;
        }

        if (PersonalBestLap == null || lap.GetAdjustedTime() < PersonalBestLap.GetAdjustedTime())
        {
            PersonalBestLap = lap;
        }

        foreach (var sector in lap.Sectors.Values)
        {
            if (!PersonalBestSectors.TryGetValue(sector.SectorNumber, out var bestSectorTime) || sector.TimeMs < bestSectorTime)
            {
                PersonalBestSectors[sector.SectorNumber] = sector.TimeMs;
            }
        }

        ClearCurrentLapProgress();
    }

    private void FinalizeCurrentLapSectors(LapData lap, int activeSectorCount)
    {
        foreach (var sector in CurrentLapSectors.Values)
        {
            if (activeSectorCount > 0 && sector.SectorNumber > activeSectorCount)
            {
                continue;
            }

            lap.Sectors[sector.SectorNumber] = new SectorTime
            {
                SectorNumber = sector.SectorNumber,
                TimeMs = sector.TimeMs,
                IsValid = sector.IsValid
            };
        }

        if (activeSectorCount <= 0)
        {
            return;
        }

        var finalSectorNumber = activeSectorCount;
        var previousCheckpointNumber = finalSectorNumber - 1;
        uint previousSplitTime = 0;
        var hasRequiredPreviousCheckpoint = previousCheckpointNumber == 0;

        if (previousCheckpointNumber > 0)
        {
            hasRequiredPreviousCheckpoint = CurrentLapSplitTimes.TryGetValue(previousCheckpointNumber, out previousSplitTime);
        }

        if (!hasRequiredPreviousCheckpoint || lap.Sectors.ContainsKey(finalSectorNumber) || lap.LapTimeMs < previousSplitTime)
        {
            return;
        }

        lap.Sectors[finalSectorNumber] = new SectorTime
        {
            SectorNumber = finalSectorNumber,
            TimeMs = lap.LapTimeMs - previousSplitTime,
            IsValid = lap.IsValid
        };
    }

    private void ClearCurrentLapProgress()
    {
        CurrentLapSectors.Clear();
        CurrentLapSplitTimes.Clear();
    }

    private void PruneTimingPointHistory(uint currentLapNumber)
    {
        var minimumLapToKeep = currentLapNumber > 2 ? currentLapNumber - 2 : 1;
        var lapsToRemove = TimingPointElapsedTimes.Keys
            .Where(lapNumber => lapNumber < minimumLapToKeep)
            .ToList();

        foreach (var lapNumber in lapsToRemove)
        {
            TimingPointElapsedTimes.Remove(lapNumber);
        }
    }

    public Driver CreateArchiveCopy()
    {
        var copy = new Driver
        {
            PlayerId = PlayerId,
            Name = Name,
            Username = Username,
            NameHtml = NameHtml,
            CarName = CarName,
            SkinName = SkinName,
            TyreTypes = TyreTypes.ToArray(),
            FuelPercent = FuelPercent,
            DriverColor = DriverColor,
            PitStops = PitStops,
            ConnectionId = ConnectionId,
            IsInPitLane = IsInPitLane,
            IsPitStopActive = IsPitStopActive,
            LastPitLaneFact = LastPitLaneFact,
            LastPitStopTimeMs = LastPitStopTimeMs,
            PitLaneEnteredAtUtc = PitLaneEnteredAtUtc,
            LastPitLaneTimeMs = LastPitLaneTimeMs,
            LastPitStopFuelAddPercent = LastPitStopFuelAddPercent,
            LastPitTyresChanged = LastPitTyresChanged.ToArray(),
            LapsCompleted = LapsCompleted,
            LastTimingPoint = LastTimingPoint == null
                ? null
                : new TimingPointSnapshot
                {
                    LapNumber = LastTimingPoint.LapNumber,
                    TimingPointIndex = LastTimingPoint.TimingPointIndex,
                    ElapsedTimeMs = LastTimingPoint.ElapsedTimeMs
                },
            CurrentRacePosition = CurrentRacePosition,
            CurrentTrackNode = CurrentTrackNode,
            CurrentTrackLap = CurrentTrackLap,
            HasWorldPosition = HasWorldPosition,
            WorldX = WorldX,
            WorldY = WorldY,
            CurrentHeading = CurrentHeading,
            TopSpeedKmh = TopSpeedKmh,
            LastLiveTelemetryAtUtc = LastLiveTelemetryAtUtc,
            LapHistory = LapHistory.Select(CloneLapData).ToList(),
            PersonalBestLap = PersonalBestLap == null ? null : CloneLapData(PersonalBestLap),
            PersonalBestSectors = PersonalBestSectors.ToDictionary(pair => pair.Key, pair => pair.Value),
            OfficialResult = OfficialResult == null ? null : CloneOfficialResult(OfficialResult)
        };

        foreach (var pair in CurrentLapSectors)
        {
            copy.CurrentLapSectors[pair.Key] = CloneSectorTime(pair.Value);
        }

        foreach (var pair in CurrentLapSplitTimes)
        {
            copy.CurrentLapSplitTimes[pair.Key] = pair.Value;
        }

        foreach (var lapTimingPoints in TimingPointElapsedTimes)
        {
            copy.TimingPointElapsedTimes[lapTimingPoints.Key] = lapTimingPoints.Value
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        return copy;
    }

    public void MergeArchivedState(Driver archived)
    {
        if (archived == null)
        {
            return;
        }

        if (IsPlaceholderDriverName(Name) && !string.IsNullOrWhiteSpace(archived.Name))
        {
            Name = archived.Name;
            NameHtml = archived.NameHtml;
        }

        if (string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(archived.Username))
        {
            Username = archived.Username;
        }

        if ((string.IsNullOrWhiteSpace(CarName) || CarName == "???") && !string.IsNullOrWhiteSpace(archived.CarName))
        {
            CarName = archived.CarName;
        }

        if (string.IsNullOrWhiteSpace(SkinName) && !string.IsNullOrWhiteSpace(archived.SkinName))
        {
            SkinName = archived.SkinName;
        }

        if ((TyreTypes == null || TyreTypes.Length == 0 || TyreTypes.All(tyre => tyre == 0))
            && archived.TyreTypes.Length > 0)
        {
            TyreTypes = archived.TyreTypes.ToArray();
        }

        if (!FuelPercent.HasValue && archived.FuelPercent.HasValue)
        {
            FuelPercent = archived.FuelPercent;
        }

        if (DriverColor == "#9CA3AF" && archived.DriverColor != "#9CA3AF")
        {
            DriverColor = archived.DriverColor;
        }

        PitStops = Math.Max(PitStops, archived.PitStops);
        LapsCompleted = Math.Max(LapsCompleted, archived.LapsCompleted);
        TopSpeedKmh = Math.Max(TopSpeedKmh, archived.TopSpeedKmh);

        if (!HasWorldPosition && archived.HasWorldPosition)
        {
            HasWorldPosition = true;
            WorldX = archived.WorldX;
            WorldY = archived.WorldY;
            CurrentHeading = archived.CurrentHeading;
            LastLiveTelemetryAtUtc = archived.LastLiveTelemetryAtUtc;
        }

        if (LastTimingPoint == null || IsTimingPointLater(archived.LastTimingPoint, LastTimingPoint))
        {
            LastTimingPoint = archived.LastTimingPoint == null
                ? LastTimingPoint
                : new TimingPointSnapshot
                {
                    LapNumber = archived.LastTimingPoint.LapNumber,
                    TimingPointIndex = archived.LastTimingPoint.TimingPointIndex,
                    ElapsedTimeMs = archived.LastTimingPoint.ElapsedTimeMs
                };
        }

        if (!IsInPitLane && archived.IsInPitLane)
        {
            IsInPitLane = true;
            LastPitLaneFact = archived.LastPitLaneFact;
            PitLaneEnteredAtUtc = archived.PitLaneEnteredAtUtc;
        }

        if (!IsPitStopActive && archived.IsPitStopActive)
        {
            IsPitStopActive = true;
        }

        LastPitStopTimeMs = ChooseLatestValue(LastPitStopTimeMs, archived.LastPitStopTimeMs);
        LastPitLaneTimeMs = ChooseLatestValue(LastPitLaneTimeMs, archived.LastPitLaneTimeMs);
        LastPitStopFuelAddPercent ??= archived.LastPitStopFuelAddPercent;

        if ((LastPitTyresChanged == null || LastPitTyresChanged.All(tyre => tyre == 0))
            && archived.LastPitTyresChanged.Length > 0)
        {
            LastPitTyresChanged = archived.LastPitTyresChanged.ToArray();
        }

        if (OfficialResult == null && archived.OfficialResult != null)
        {
            OfficialResult = CloneOfficialResult(archived.OfficialResult);
        }

        foreach (var pair in archived.CurrentLapSectors)
        {
            if (!CurrentLapSectors.ContainsKey(pair.Key))
            {
                CurrentLapSectors[pair.Key] = CloneSectorTime(pair.Value);
            }
        }

        foreach (var pair in archived.CurrentLapSplitTimes)
        {
            CurrentLapSplitTimes.TryAdd(pair.Key, pair.Value);
        }

        foreach (var lapTimingPoints in archived.TimingPointElapsedTimes)
        {
            if (!TimingPointElapsedTimes.TryGetValue(lapTimingPoints.Key, out var existingTimingPoints))
            {
                TimingPointElapsedTimes[lapTimingPoints.Key] = lapTimingPoints.Value
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                continue;
            }

            foreach (var timingPoint in lapTimingPoints.Value)
            {
                existingTimingPoints[timingPoint.Key] = timingPoint.Value;
            }
        }

        LapHistory = LapHistory
            .Concat(archived.LapHistory.Select(CloneLapData))
            .GroupBy(lap => new { lap.LapNumber, lap.ElapsedTimeMs })
            .Select(group => group.Aggregate(ChoosePreferredLap))
            .OrderBy(lap => lap.LapNumber)
            .ThenBy(lap => lap.ElapsedTimeMs)
            .ToList();

        RecalculatePersonalBestData();
    }

    private void RecalculatePersonalBestData()
    {
        PersonalBestLap = null;
        PersonalBestSectors = new Dictionary<int, uint>();

        foreach (var lap in LapHistory.Where(lap => lap.IsValid))
        {
            if (PersonalBestLap == null || lap.GetAdjustedTime() < PersonalBestLap.GetAdjustedTime())
            {
                PersonalBestLap = CloneLapData(lap);
            }

            foreach (var sector in lap.Sectors.Values)
            {
                if (!PersonalBestSectors.TryGetValue(sector.SectorNumber, out var bestSectorTime) || sector.TimeMs < bestSectorTime)
                {
                    PersonalBestSectors[sector.SectorNumber] = sector.TimeMs;
                }
            }
        }
    }

    private static uint? ChooseLatestValue(uint? currentValue, uint? archivedValue)
    {
        if (!archivedValue.HasValue)
        {
            return currentValue;
        }

        if (!currentValue.HasValue)
        {
            return archivedValue;
        }

        return Math.Max(currentValue.Value, archivedValue.Value);
    }

    private static bool IsTimingPointLater(TimingPointSnapshot? candidate, TimingPointSnapshot? baseline)
    {
        if (candidate == null)
        {
            return false;
        }

        if (baseline == null)
        {
            return true;
        }

        if (candidate.LapNumber != baseline.LapNumber)
        {
            return candidate.LapNumber > baseline.LapNumber;
        }

        if (candidate.TimingPointIndex != baseline.TimingPointIndex)
        {
            return candidate.TimingPointIndex > baseline.TimingPointIndex;
        }

        return candidate.ElapsedTimeMs > baseline.ElapsedTimeMs;
    }

    private static LapData ChoosePreferredLap(LapData current, LapData candidate)
    {
        if (candidate.IsValid != current.IsValid)
        {
            return candidate.IsValid ? candidate : current;
        }

        if (candidate.Sectors.Count != current.Sectors.Count)
        {
            return candidate.Sectors.Count > current.Sectors.Count ? candidate : current;
        }

        if (candidate.LapTimeMs != current.LapTimeMs)
        {
            return candidate.LapTimeMs > current.LapTimeMs ? candidate : current;
        }

        return candidate.RecordedAt >= current.RecordedAt ? candidate : current;
    }

    private static LapData CloneLapData(LapData lap)
    {
        return new LapData
        {
            LapNumber = lap.LapNumber,
            LapTimeMs = lap.LapTimeMs,
            ElapsedTimeMs = lap.ElapsedTimeMs,
            Sectors = lap.Sectors.ToDictionary(pair => pair.Key, pair => CloneSectorTime(pair.Value)),
            IsValid = lap.IsValid,
            PitStops = lap.PitStops,
            PenaltyMs = lap.PenaltyMs,
            Gear = lap.Gear,
            RecordedAt = lap.RecordedAt
        };
    }

    private static SectorTime CloneSectorTime(SectorTime sector)
    {
        return new SectorTime
        {
            SectorNumber = sector.SectorNumber,
            TimeMs = sector.TimeMs,
            IsValid = sector.IsValid
        };
    }

    private static OfficialResult CloneOfficialResult(OfficialResult officialResult)
    {
        return new OfficialResult
        {
            Kind = officialResult.Kind,
            TotalTimeMs = officialResult.TotalTimeMs,
            BestLapTimeMs = officialResult.BestLapTimeMs,
            NumStops = officialResult.NumStops,
            LapsDone = officialResult.LapsDone,
            Flags = officialResult.Flags,
            ConfirmFlags = officialResult.ConfirmFlags,
            ResultNum = officialResult.ResultNum,
            NumRes = officialResult.NumRes,
            PenaltySeconds = officialResult.PenaltySeconds,
            PositionPoints = officialResult.PositionPoints,
            PolePositionBonusPoints = officialResult.PolePositionBonusPoints,
            FastestLapBonusPoints = officialResult.FastestLapBonusPoints,
            HighestClimberBonusPoints = officialResult.HighestClimberBonusPoints
        };
    }

    private static bool IsPlaceholderDriverName(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            || name.StartsWith("Unknown Driver", StringComparison.Ordinal)
            || name.StartsWith("Driver #", StringComparison.Ordinal);
    }
}

/// <summary>
/// Represents the overall race session
/// Singleton pattern - one instance per application lifetime
/// </summary>
public class RaceSession
{
    private readonly object _playersLock = new object(); // Thread-safe access
    private readonly Dictionary<byte, string> _usernames = new(); // UCID -> UName mapping
    private readonly List<ChatMessageEntry> _chatMessages = new();
    private readonly Dictionary<int, TrackMapNodeSample> _trackMapNodes = new();
    private readonly Dictionary<string, OfficialResultIndexEntry> _officialResultsByUsername = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Driver> _departedDriversByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<byte, int> _raceStartPositionsByPlayerId = new();
    private readonly Dictionary<string, int> _raceStartPositionsByUsername = new(StringComparer.OrdinalIgnoreCase);
    private uint _trackMapRevision;
    private uint _trackMapSortOrder;
    private const int MaxChatMessages = 80;

    public string SessionId { get; private set; } = CreateSessionId();
    public DateTime SessionStartedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Base track code reported by LFS, e.g. BL4X.
    /// </summary>
    public string TrackName { get; set; } = "Unknown";

    /// <summary>
    /// Optional custom layout name reported by IS_AXI.
    /// </summary>
    public string LayoutName { get; private set; } = string.Empty;

    /// <summary>
    /// Canonical display name used across live UI and archive.
    /// </summary>
    public string DisplayTrackName => BuildDisplayTrackName(TrackName, LayoutName);

    /// <summary>
    /// Multiplayer host name as plain text.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Multiplayer host name formatted with LFS color codes converted to HTML.
    /// </summary>
    public string HostNameHtml { get; set; } = string.Empty;

    /// <summary>
    /// Session type: 0=practice, 1=qualifying, 2=race
    /// </summary>
    public byte SessionType { get; set; }

    /// <summary>
    /// Weather type: 0=sunny, 1=cloudy, 2=rainy
    /// </summary>
    public byte WeatherType { get; set; }

    /// <summary>
    /// Wind strength: 0=off, 1=weak, 2=strong
    /// </summary>
    public byte WindType { get; set; }

    /// <summary>
    /// Race status flag: 0=green, 1=yellow, 2=blue, 3=red
    /// </summary>
    public byte RaceFlag { get; set; }

    /// <summary>
    /// Whether race is in progress
    /// </summary>
    public bool RaceInProgress { get; set; }

    /// <summary>
    /// Monotonic revision for the bounded chat history.
    /// </summary>
    public uint ChatRevision { get; private set; }

    /// <summary>
    /// Time elapsed in current session (ms)
    /// </summary>
    public uint SessionTimeMs { get; set; }

    /// <summary>
    /// Number of active sectors reported by the current timing configuration.
    /// </summary>
    public int ActiveSectorCount { get; set; }

    /// <summary>
    /// Maximum race laps (from IS_RST)
    /// </summary>
    public byte MaxRaceLaps { get; set; }

    /// <summary>
    /// Qualifying time in minutes (from IS_STA)
    /// </summary>
    public byte QualifyingMins { get; set; }

    /// <summary>
    /// Session best lap cached for fast reads.
    /// </summary>
    public LapData? SessionBestLap { get; private set; }

    /// <summary>
    /// Session Best Lap - author and lap metadata.
    /// </summary>
    public byte? SessionBestLapAuthorPLID { get; set; }
    public ushort? SessionBestLapNumber { get; set; }
    
    /// <summary>
    /// Cached best lap author info (used if driver leaves before session ends)
    /// </summary>
    public string SessionBestLapAuthorNameHtml { get; set; } = "";
    public string SessionBestLapAuthorUsername { get; set; } = "";

    /// <summary>
    /// Connected drivers, keyed by Player ID
    /// </summary>
    public Dictionary<byte, Driver> Players { get; set; } = new();

    /// <summary>
    /// Session's global best times for each sector
    /// </summary>
    public Dictionary<int, uint> SessionBestSectors
    {
        get
        {
            lock (_playersLock)
            {
                var result = new Dictionary<int, uint>();

                foreach (var driver in GetArchiveDriversLocked())
                {
                    foreach (var kvp in driver.PersonalBestSectors)
                    {
                        int sectorNum = kvp.Key;
                        uint sectorTime = kvp.Value;

                        if (!result.ContainsKey(sectorNum))
                            result[sectorNum] = sectorTime;
                        else if (sectorTime < result[sectorNum])
                            result[sectorNum] = sectorTime;
                    }
                }

                return result;
            }
        }
    }

    /// <summary>
    /// Session best sector times enriched with author metadata.
    /// </summary>
    public Dictionary<int, SessionBestSectorInfo> GetSessionBestSectorInfos()
    {
        lock (_playersLock)
        {
            var result = new Dictionary<int, SessionBestSectorInfo>();

            foreach (var driver in GetArchiveDriversLocked())
            {
                foreach (var kvp in driver.PersonalBestSectors)
                {
                    var sectorNumber = kvp.Key;
                    var sectorTime = kvp.Value;

                    if (!result.TryGetValue(sectorNumber, out var currentBest) || sectorTime < currentBest.TimeMs)
                    {
                        result[sectorNumber] = new SessionBestSectorInfo
                        {
                            TimeMs = sectorTime,
                            AuthorNameHtml = driver.NameHtml,
                            AuthorUsername = driver.Username
                        };
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Gets drivers sorted by best lap time (THREAD-SAFE snapshot)
    /// </summary>
    public IEnumerable<Driver> GetDriversSortedByBestLap()
    {
        lock (_playersLock)
        {
            return Players.Values
                .OrderBy(p => p.PersonalBestLap == null ? 1 : 0)
                .ThenBy(p => p.PersonalBestLap?.GetAdjustedTime() ?? uint.MaxValue)
                .ThenByDescending(p => p.LapsCompleted)
                .ThenBy(p => p.Name)
                .ToList(); // CRITICAL: ToList() creates snapshot before lock is released
        }
    }

    /// <summary>
    /// Gets drivers ordered for live standings depending on session type.
    /// Race sessions sort by on-track position, other sessions by best lap.
    /// </summary>
    public IEnumerable<Driver> GetDriversForStandings()
    {
        lock (_playersLock)
        {
            if (SessionType == 2)
            {
                return Players.Values
                    .OrderBy(p => p.CurrentRacePosition > 0 ? 0 : 1)
                    .ThenBy(p => p.CurrentRacePosition == 0 ? byte.MaxValue : p.CurrentRacePosition)
                    .ThenByDescending(p => p.CurrentTrackLap)
                    .ThenByDescending(p => p.CurrentTrackNode)
                    .ThenBy(p => p.Name)
                    .ToList();
            }

            return Players.Values
                .OrderBy(p => p.PersonalBestLap == null ? 1 : 0)
                .ThenBy(p => p.PersonalBestLap?.GetAdjustedTime() ?? uint.MaxValue)
                .ThenByDescending(p => p.LapsCompleted)
                .ThenBy(p => p.Name)
                .ToList();
        }
    }

    /// <summary>
    /// Gets drivers sorted by number of laps
    /// </summary>
    public IEnumerable<Driver> GetDriversSortedByLaps()
    {
        lock (_playersLock)
        {
            return Players.Values
                .OrderByDescending(p => p.LapsCompleted)
                .ThenBy(p => p.Name)
                .ToList(); // Create snapshot
        }
    }

    /// <summary>
    /// Gets session best lap metadata from the current session state.
    /// </summary>
    public (string? NameHtml, string? Username, uint? LapNumber) GetSessionBestLapInfo()
    {
        lock (_playersLock)
        {
            if (SessionBestLap != null)
            {
                return (SessionBestLapAuthorNameHtml, SessionBestLapAuthorUsername, SessionBestLapNumber);
            }

            if (!string.IsNullOrEmpty(SessionBestLapAuthorNameHtml))
            {
                return (SessionBestLapAuthorNameHtml, SessionBestLapAuthorUsername, SessionBestLapNumber);
            }

            return (null, null, null);
        }
    }

    /// <summary>
    /// Updates cached session best lap metadata.
    /// </summary>
    public bool TryUpdateSessionBestLap(Driver driver, LapData lap)
    {
        if (!lap.IsValid)
            return false;

        lock (_playersLock)
        {
            if (SessionBestLap != null && lap.GetAdjustedTime() >= SessionBestLap.GetAdjustedTime())
                return false;

            SessionBestLap = lap;
            SessionBestLapAuthorPLID = driver.PlayerId;
            SessionBestLapNumber = (ushort)lap.LapNumber;
            SessionBestLapAuthorNameHtml = driver.NameHtml;
            SessionBestLapAuthorUsername = driver.Username;
            return true;
        }
    }

    /// <summary>
    /// Refreshes cached session best author identity when richer player data arrives later.
    /// </summary>
    public void RefreshSessionBestLapAuthor(Driver driver)
    {
        lock (_playersLock)
        {
            if (SessionBestLapAuthorPLID != driver.PlayerId)
                return;

            SessionBestLapAuthorNameHtml = driver.NameHtml;
            SessionBestLapAuthorUsername = driver.Username;
        }
    }

    /// <summary>
    /// Adds or updates a driver (THREAD-SAFE)
    /// </summary>
    public void AddOrUpdateDriver(Driver driver)
    {
        lock (_playersLock)
        {
            MergeDepartedDriverInto(driver);
            Players[driver.PlayerId] = driver;

            if (TryGetOfficialResultEntry(driver.PlayerId, driver.Username, out var officialResultEntry))
            {
                officialResultEntry.PlayerId = driver.PlayerId;
                officialResultEntry.DriverName = driver.Name;
                officialResultEntry.CarName = driver.CarName;
                officialResultEntry.Username = driver.Username;
                driver.SetOfficialResult(officialResultEntry.Result);

                StoreOfficialResultEntry(officialResultEntry, driver.PlayerId, driver.Username);
            }

            if (!string.IsNullOrWhiteSpace(driver.Username)
                && _raceStartPositionsByPlayerId.TryGetValue(driver.PlayerId, out var startPosition))
            {
                _raceStartPositionsByUsername[driver.Username] = startPosition;
            }
        }
    }

    /// <summary>
    /// Removes a driver from the session (THREAD-SAFE)
    /// </summary>
    public void RemoveDriver(byte playerId)
    {
        lock (_playersLock)
        {
            if (Players.TryGetValue(playerId, out var driver))
            {
                StoreDepartedDriver(driver);
                PersistOfficialResult(driver);
                Players.Remove(playerId);
            }
        }
    }

    /// <summary>
    /// Removes all drivers belonging to a specific LFS connection (UCID).
    /// </summary>
    public void RemoveDriversByConnection(byte ucid)
    {
        lock (_playersLock)
        {
            var playerIdsToRemove = Players.Values
                .Where(driver => driver.ConnectionId == ucid)
                .Select(driver => driver.PlayerId)
                .ToList();

            foreach (var playerId in playerIdsToRemove)
            {
                if (Players.TryGetValue(playerId, out var driver))
                {
                    StoreDepartedDriver(driver);
                    PersistOfficialResult(driver);
                }

                Players.Remove(playerId);
            }
        }
    }

    public void ApplyOfficialResult(byte playerId, string username, string driverName, string carName, OfficialResult officialResult)
    {
        lock (_playersLock)
        {
            Driver? driver = null;

            if (playerId != 0)
            {
                Players.TryGetValue(playerId, out driver);
            }

            if (driver == null && !string.IsNullOrWhiteSpace(username))
            {
                driver = Players.Values.FirstOrDefault(candidate =>
                    string.Equals(candidate.Username, username, StringComparison.OrdinalIgnoreCase));
            }

            if (driver != null)
            {
                driverName = string.IsNullOrWhiteSpace(driver.Name) ? driverName : driver.Name;
                carName = string.IsNullOrWhiteSpace(driver.CarName) ? carName : driver.CarName;
                playerId = driver.PlayerId;
                driver.SetOfficialResult(officialResult);
            }

            var officialResultEntry = new OfficialResultIndexEntry
            {
                Username = username,
                DriverName = driverName,
                CarName = carName,
                PlayerId = playerId == 0 ? null : playerId,
                Result = officialResult
            };

            StoreOfficialResultEntry(officialResultEntry, playerId, username);
        }
    }

    public void UpdateRaceStartOrder(IReadOnlyList<byte> orderedPlayerIds)
    {
        lock (_playersLock)
        {
            _raceStartPositionsByPlayerId.Clear();
            _raceStartPositionsByUsername.Clear();

            for (var index = 0; index < orderedPlayerIds.Count; index++)
            {
                var playerId = orderedPlayerIds[index];
                if (playerId == 0)
                {
                    continue;
                }

                var gridPosition = index + 1;
                _raceStartPositionsByPlayerId[playerId] = gridPosition;

                if (Players.TryGetValue(playerId, out var driver) && !string.IsNullOrWhiteSpace(driver.Username))
                {
                    _raceStartPositionsByUsername[driver.Username] = gridPosition;
                }
            }
        }
    }

    public void RecalculateOfficialResultBonuses(int polePositionBonusPoints, int fastestLapBonusPoints, int highestClimberBonusPoints)
    {
        lock (_playersLock)
        {
            foreach (var officialResult in _officialResultsByUsername.Values.Select(entry => entry.Result))
            {
                officialResult.PolePositionBonusPoints = 0;
                officialResult.FastestLapBonusPoints = 0;
                officialResult.HighestClimberBonusPoints = 0;
            }

            var raceResults = _officialResultsByUsername
                .Where(pair => pair.Value.Result.Kind == OfficialResultKind.Race)
                .Select(pair => new { Entry = pair.Value, Result = pair.Value.Result })
                .ToList();

            foreach (var poleSitter in raceResults.Where(entry => ResolveRaceStartPosition(entry.Entry.Username, entry.Entry) == 1))
            {
                poleSitter.Result.PolePositionBonusPoints = polePositionBonusPoints;
            }

            var fastestLapTimeMs = raceResults
                .Where(entry => entry.Result.Position.HasValue && entry.Result.BestLapTimeMs > 0)
                .Select(entry => (uint?)entry.Result.BestLapTimeMs)
                .Min();

            if (fastestLapTimeMs.HasValue)
            {
                foreach (var entry in raceResults.Where(entry => entry.Result.Position.HasValue && entry.Result.BestLapTimeMs == fastestLapTimeMs.Value))
                {
                    entry.Result.FastestLapBonusPoints = fastestLapBonusPoints;
                }
            }

            var climberResults = raceResults
                .Select(entry => new
                {
                    entry.Result,
                    StartingPosition = ResolveRaceStartPosition(entry.Entry.Username, entry.Entry),
                    FinishPosition = entry.Result.Position
                })
                .Where(entry => entry.StartingPosition.HasValue && entry.FinishPosition.HasValue)
                .Select(entry => new
                {
                    entry.Result,
                    PlacesGained = entry.StartingPosition!.Value - entry.FinishPosition!.Value
                })
                .Where(entry => entry.PlacesGained > 0)
                .ToList();

            if (climberResults.Count == 0)
            {
                return;
            }

            var highestGain = climberResults.Max(entry => entry.PlacesGained);
            foreach (var climber in climberResults.Where(entry => entry.PlacesGained == highestGain))
            {
                climber.Result.HighestClimberBonusPoints = highestClimberBonusPoints;
            }
        }
    }

    /// <summary>
    /// Removes stored username by UCID - called when connection leaves (THREAD-SAFE)
    /// </summary>
    public void RemoveUsername(byte ucid)
    {
        lock (_playersLock)
        {
            _usernames.Remove(ucid);
        }
    }

    /// <summary>
    /// Gets a driver by player ID (THREAD-SAFE)
    /// </summary>
    public Driver? GetDriver(byte playerId)
    {
        lock (_playersLock)
        {
            return Players.TryGetValue(playerId, out var driver) ? driver : null;
        }
    }

    /// <summary>
    /// Projects driver data under the session lock and returns a snapshot-safe result.
    /// </summary>
    public T? GetDriverSnapshot<T>(byte playerId, Func<Driver, T> snapshotFactory)
    {
        lock (_playersLock)
        {
            return Players.TryGetValue(playerId, out var driver)
                ? snapshotFactory(driver)
                : default;
        }
    }

    public bool HasTrackMapNode(int key)
    {
        lock (_playersLock)
        {
            return _trackMapNodes.ContainsKey(key);
        }
    }

    public int? TryFindTrackMapClosureKey(int x, int y, int maxDistanceWorld, uint minimumAssignedPoints, uint candidatePointCount)
    {
        lock (_playersLock)
        {
            if (_trackMapSortOrder < minimumAssignedPoints)
            {
                return null;
            }

            var maxDistanceSquared = (long)maxDistanceWorld * maxDistanceWorld;
            int? bestKey = null;
            long bestDistanceSquared = long.MaxValue;

            foreach (var pair in _trackMapNodes)
            {
                var sample = pair.Value;
                if (sample.SortOrder == TrackMapNodeSample.DeferredSortOrder || sample.SortOrder > candidatePointCount)
                {
                    continue;
                }

                var point = sample.ToPoint();
                var deltaX = (long)point.X - x;
                var deltaY = (long)point.Y - y;
                var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                if (distanceSquared > maxDistanceSquared || distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestKey = pair.Key;
            }

            return bestKey;
        }
    }

    public void UpdateTrackMapNode(int key, ushort displayNode, int x, int y, bool useInsertionOrder, bool deferSortOrder = false)
    {
        lock (_playersLock)
        {
            if (!_trackMapNodes.TryGetValue(key, out var sample))
            {
                sample = new TrackMapNodeSample
                {
                    Node = displayNode,
                    SortOrder = useInsertionOrder
                        ? ++_trackMapSortOrder
                        : deferSortOrder
                            ? TrackMapNodeSample.DeferredSortOrder
                            : displayNode
                };
                _trackMapNodes[key] = sample;
            }
            else if (useInsertionOrder && sample.SortOrder == TrackMapNodeSample.DeferredSortOrder)
            {
                sample.SortOrder = ++_trackMapSortOrder;
            }

            sample.AddSample(x, y);
            _trackMapRevision++;
        }
    }

    public TrackMapSnapshot GetTrackMapSnapshot()
    {
        lock (_playersLock)
        {
            var points = _trackMapNodes.Values
                .OrderBy(sample => sample.SortOrder)
                .Select(sample => sample.ToPoint())
                .ToList();

            if (points.Count == 0)
            {
                return new TrackMapSnapshot
                {
                    Revision = _trackMapRevision
                };
            }

            return new TrackMapSnapshot
            {
                Revision = _trackMapRevision,
                Points = points,
                MinX = points.Min(point => point.X),
                MaxX = points.Max(point => point.X),
                MinY = points.Min(point => point.Y),
                MaxY = points.Max(point => point.Y)
            };
        }
    }

    public void ClearTrackMap()
    {
        lock (_playersLock)
        {
            _trackMapNodes.Clear();
            _trackMapSortOrder = 0;
            _trackMapRevision++;
        }
    }

    /// <summary>
    /// Resets the entire session
    /// </summary>
    public void Reset()
    {
        lock (_playersLock)
        {
            TrackName = "Unknown";
            LayoutName = string.Empty;
            HostName = string.Empty;
            HostNameHtml = string.Empty;
            SessionId = CreateSessionId();
            SessionStartedAtUtc = DateTime.UtcNow;
            SessionType = 0;
            WeatherType = 0;
            WindType = 0;
            RaceFlag = 0;
            RaceInProgress = false;
            SessionTimeMs = 0;
            ActiveSectorCount = 0;
            MaxRaceLaps = 0;
            QualifyingMins = 0;
            SessionBestLap = null;
            SessionBestLapAuthorPLID = null;
            SessionBestLapNumber = null;
            SessionBestLapAuthorNameHtml = string.Empty;
            SessionBestLapAuthorUsername = string.Empty;
            ChatRevision = 0;
            _usernames.Clear();
            _chatMessages.Clear();
            _trackMapNodes.Clear();
            _officialResultsByUsername.Clear();
            _departedDriversByKey.Clear();
            _raceStartPositionsByPlayerId.Clear();
            _raceStartPositionsByUsername.Clear();
            _trackMapSortOrder = 0;
            _trackMapRevision = 0;
            Players.Clear();
        }
    }

    private void PersistOfficialResult(Driver driver)
    {
        if (driver.OfficialResult == null)
        {
            return;
        }

        var officialResultEntry = new OfficialResultIndexEntry
        {
            Username = driver.Username,
            DriverName = driver.Name,
            CarName = driver.CarName,
            PlayerId = driver.PlayerId,
            Result = driver.OfficialResult
        };

        StoreOfficialResultEntry(officialResultEntry, driver.PlayerId, driver.Username);
    }

    private bool TryGetOfficialResultEntry(byte playerId, string username, out OfficialResultIndexEntry officialResultEntry)
    {
        if (!string.IsNullOrWhiteSpace(username)
            && _officialResultsByUsername.TryGetValue(username, out var officialResultEntryByUsername))
        {
            officialResultEntry = officialResultEntryByUsername;
            return true;
        }

        if (playerId != 0
            && _officialResultsByUsername.TryGetValue(CreateOfficialResultPlayerKey(playerId), out var officialResultEntryByPlayerId))
        {
            officialResultEntry = officialResultEntryByPlayerId;
            return true;
        }

        officialResultEntry = new OfficialResultIndexEntry();

        return false;
    }

    private void StoreOfficialResultEntry(OfficialResultIndexEntry officialResultEntry, byte playerId, string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            _officialResultsByUsername[username] = officialResultEntry;

            if (playerId != 0)
            {
                _officialResultsByUsername.Remove(CreateOfficialResultPlayerKey(playerId));
            }

            return;
        }

        if (playerId != 0)
        {
            _officialResultsByUsername[CreateOfficialResultPlayerKey(playerId)] = officialResultEntry;
        }
    }

    private static string CreateOfficialResultPlayerKey(byte playerId) => $"plid:{playerId}";

    private int? ResolveRaceStartPosition(string username, OfficialResultIndexEntry officialResultEntry)
    {
        if (!string.IsNullOrWhiteSpace(username)
            && _raceStartPositionsByUsername.TryGetValue(username, out var usernamePosition))
        {
            return usernamePosition;
        }

        if (officialResultEntry.PlayerId.HasValue
            && _raceStartPositionsByPlayerId.TryGetValue(officialResultEntry.PlayerId.Value, out var playerPosition))
        {
            return playerPosition;
        }

        return null;
    }

    public SessionArchiveSnapshot CreateArchiveSnapshot()
    {
        lock (_playersLock)
        {
            var archiveDrivers = GetArchiveDriversLocked();
            var sessionBestSectorInfos = BuildSessionBestSectorInfos(archiveDrivers);
            var snapshot = new SessionArchiveSnapshot
            {
                SessionId = SessionId,
                SessionStartedAtUtc = SessionStartedAtUtc,
                CapturedAtUtc = DateTime.UtcNow,
                SessionType = GetSessionTypeString(),
                SessionTypeId = SessionType,
                TrackName = DisplayTrackName,
                BaseTrackName = TrackName,
                LayoutName = LayoutName,
                HostName = HostName,
                HostNameHtml = HostNameHtml,
                WeatherType = GetWeatherTypeString(),
                WeatherTypeId = WeatherType,
                WindType = GetWindTypeString(),
                WindTypeId = WindType,
                RaceFlag = RaceFlag,
                RaceInProgress = RaceInProgress,
                SessionTimeMs = SessionTimeMs,
                MaxRaceLaps = MaxRaceLaps,
                QualifyingMins = QualifyingMins,
                ActiveSectorCount = ActiveSectorCount,
                SessionBestLap = SessionBestLap == null
                    ? null
                    : new ArchiveBestLapSnapshot
                    {
                        LapTimeMs = SessionBestLap.LapTimeMs,
                        LapNumber = SessionBestLapNumber,
                        AuthorName = SessionBestLapAuthorNameHtml,
                        AuthorUsername = SessionBestLapAuthorUsername
                    },
                SessionBestSectors = sessionBestSectorInfos
                    .Select(pair => new ArchiveBestSectorSnapshot
                    {
                        SectorNumber = pair.Key,
                        TimeMs = pair.Value.TimeMs,
                        AuthorName = pair.Value.AuthorNameHtml,
                        AuthorUsername = pair.Value.AuthorUsername
                    })
                    .OrderBy(sector => sector.SectorNumber)
                    .ToList()
            };

            snapshot.Drivers = archiveDrivers
                .OrderBy(driver => driver.CurrentRacePosition > 0 ? 0 : 1)
                .ThenBy(driver => driver.CurrentRacePosition == 0 ? byte.MaxValue : driver.CurrentRacePosition)
                .ThenBy(driver => driver.Name)
                .Select(driver => new ArchiveDriverSnapshot
                {
                    PlayerId = driver.PlayerId,
                    Name = driver.Name,
                    Username = driver.Username,
                    CarName = driver.CarName,
                    DriverColor = driver.DriverColor,
                    PitStops = driver.PitStops,
                    LapsCompleted = driver.LapsCompleted,
                    CurrentRacePosition = driver.CurrentRacePosition,
                    TopSpeedKmh = driver.TopSpeedKmh,
                    PersonalBestSectors = driver.PersonalBestSectors
                        .Select(pair => new ArchiveSectorSnapshot
                        {
                            SectorNumber = pair.Key,
                            TimeMs = pair.Value,
                            IsValid = true
                        })
                        .OrderBy(sector => sector.SectorNumber)
                        .ToList(),
                    PersonalBestLap = driver.PersonalBestLap == null ? null : CloneLap(driver.PersonalBestLap),
                    RaceStartPosition = ResolveDriverRaceStartPosition(driver),
                    LapHistory = driver.LapHistory.Select(CloneLap).ToList()
                })
                .ToList();

            snapshot.OfficialResults = _officialResultsByUsername.Values
                .Select(CreateArchiveOfficialResult)
                .Where(result => result != null)
                .Cast<ArchiveOfficialResultEntry>()
                .OrderBy(result => result.FinishPosition ?? int.MaxValue)
                .ThenBy(result => result.Username)
                .ToList();

            return snapshot;
        }
    }

    private void StoreDepartedDriver(Driver driver)
    {
        foreach (var key in GetDriverArchiveKeys(driver.PlayerId, driver.Username))
        {
            if (_departedDriversByKey.TryGetValue(key, out var existingDriver))
            {
                existingDriver.MergeArchivedState(driver);
            }
            else
            {
                _departedDriversByKey[key] = driver.CreateArchiveCopy();
            }
        }
    }

    private void MergeDepartedDriverInto(Driver driver)
    {
        var mergedSnapshot = default(Driver);

        foreach (var key in GetDriverArchiveKeys(driver.PlayerId, driver.Username))
        {
            if (!_departedDriversByKey.TryGetValue(key, out var departedDriver))
            {
                continue;
            }

            mergedSnapshot ??= departedDriver.CreateArchiveCopy();
            if (!ReferenceEquals(mergedSnapshot, departedDriver))
            {
                mergedSnapshot.MergeArchivedState(departedDriver);
            }

            _departedDriversByKey.Remove(key);
        }

        if (mergedSnapshot != null)
        {
            driver.MergeArchivedState(mergedSnapshot);
        }
    }

    private List<Driver> GetArchiveDriversLocked()
    {
        var combinedDrivers = new Dictionary<string, Driver>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in _departedDriversByKey.Values)
        {
            var key = CreateDriverArchiveKey(driver.PlayerId, driver.Username);
            if (!combinedDrivers.ContainsKey(key))
            {
                combinedDrivers[key] = driver.CreateArchiveCopy();
            }
        }

        foreach (var driver in Players.Values)
        {
            var key = CreateDriverArchiveKey(driver.PlayerId, driver.Username);
            if (combinedDrivers.TryGetValue(key, out var archivedDriver))
            {
                var mergedDriver = driver.CreateArchiveCopy();
                mergedDriver.MergeArchivedState(archivedDriver);
                combinedDrivers[key] = mergedDriver;
                continue;
            }

            combinedDrivers[key] = driver.CreateArchiveCopy();
        }

        return combinedDrivers.Values.ToList();
    }

    private static Dictionary<int, SessionBestSectorInfo> BuildSessionBestSectorInfos(IEnumerable<Driver> drivers)
    {
        var result = new Dictionary<int, SessionBestSectorInfo>();

        foreach (var driver in drivers)
        {
            foreach (var kvp in driver.PersonalBestSectors)
            {
                var sectorNumber = kvp.Key;
                var sectorTime = kvp.Value;

                if (!result.TryGetValue(sectorNumber, out var currentBest) || sectorTime < currentBest.TimeMs)
                {
                    result[sectorNumber] = new SessionBestSectorInfo
                    {
                        TimeMs = sectorTime,
                        AuthorNameHtml = driver.NameHtml,
                        AuthorUsername = driver.Username
                    };
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> GetDriverArchiveKeys(byte playerId, string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            yield return CreateUsernameArchiveKey(username);
        }

        if (playerId != 0)
        {
            yield return CreatePlayerArchiveKey(playerId);
        }
    }

    private static string CreateDriverArchiveKey(byte playerId, string username)
    {
        return !string.IsNullOrWhiteSpace(username)
            ? CreateUsernameArchiveKey(username)
            : CreatePlayerArchiveKey(playerId);
    }

    private static string CreateUsernameArchiveKey(string username) => $"user:{username.Trim()}";

    private static string CreatePlayerArchiveKey(byte playerId) => $"plid:{playerId}";

    public void SetTrackIdentity(string trackName, string? layoutName = null)
    {
        TrackName = string.IsNullOrWhiteSpace(trackName) ? "Unknown" : trackName.Trim();
        LayoutName = string.IsNullOrWhiteSpace(layoutName) ? string.Empty : layoutName.Trim();
    }

    private static string BuildDisplayTrackName(string trackName, string? layoutName)
    {
        var normalizedTrackName = string.IsNullOrWhiteSpace(trackName) ? "Unknown" : trackName.Trim();
        var normalizedLayoutName = string.IsNullOrWhiteSpace(layoutName) ? string.Empty : layoutName.Trim();

        if (string.IsNullOrEmpty(normalizedLayoutName))
        {
            return normalizedTrackName;
        }

        if (normalizedLayoutName.StartsWith(normalizedTrackName, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedLayoutName;
        }

        return $"{normalizedTrackName}_{normalizedLayoutName}";
    }

    private static ArchiveLapSnapshot CloneLap(LapData lap)
    {
        return new ArchiveLapSnapshot
        {
            LapNumber = lap.LapNumber,
            LapTimeMs = lap.LapTimeMs,
            ElapsedTimeMs = lap.ElapsedTimeMs,
            IsValid = lap.IsValid,
            PitStops = lap.PitStops,
            PenaltyMs = lap.PenaltyMs,
            Gear = lap.Gear,
            RecordedAtUtc = lap.RecordedAt,
            Sectors = lap.Sectors.Values
                .Select(sector => new ArchiveSectorSnapshot
                {
                    SectorNumber = sector.SectorNumber,
                    TimeMs = sector.TimeMs,
                    IsValid = sector.IsValid
                })
                .OrderBy(sector => sector.SectorNumber)
                .ToList()
        };
    }

    private ArchiveOfficialResultEntry? CreateArchiveOfficialResult(string username)
    {
        return _officialResultsByUsername.TryGetValue(username, out var officialResultEntry)
            ? CreateArchiveOfficialResult(officialResultEntry)
            : null;
    }

    private ArchiveOfficialResultEntry CreateArchiveOfficialResult(OfficialResultIndexEntry officialResultEntry)
    {
        return new ArchiveOfficialResultEntry
        {
            Username = officialResultEntry.Username,
            DriverName = officialResultEntry.DriverName,
            CarName = officialResultEntry.CarName,
            PlayerId = officialResultEntry.PlayerId,
            Kind = officialResultEntry.Result.Kind.ToString(),
            TotalTimeMs = officialResultEntry.Result.TotalTimeMs,
            BestLapTimeMs = officialResultEntry.Result.BestLapTimeMs,
            NumStops = officialResultEntry.Result.NumStops,
            LapsDone = officialResultEntry.Result.LapsDone,
            Flags = officialResultEntry.Result.Flags,
            ConfirmFlags = officialResultEntry.Result.ConfirmFlags,
            ResultNum = officialResultEntry.Result.ResultNum,
            NumRes = officialResultEntry.Result.NumRes,
            PenaltySeconds = officialResultEntry.Result.PenaltySeconds,
            StartPosition = ResolveRaceStartPosition(officialResultEntry.Username, officialResultEntry),
            FinishPosition = officialResultEntry.Result.Position,
            Points = new ArchiveOfficialPointsBreakdown
            {
                PositionPoints = officialResultEntry.Result.PositionPoints,
                PolePositionBonusPoints = officialResultEntry.Result.PolePositionBonusPoints,
                FastestLapBonusPoints = officialResultEntry.Result.FastestLapBonusPoints,
                HighestClimberBonusPoints = officialResultEntry.Result.HighestClimberBonusPoints,
                BonusPoints = officialResultEntry.Result.BonusPoints,
                TotalPoints = officialResultEntry.Result.TotalPoints
            }
        };
    }

    private int? ResolveDriverRaceStartPosition(Driver driver)
    {
        if (!string.IsNullOrWhiteSpace(driver.Username)
            && _raceStartPositionsByUsername.TryGetValue(driver.Username, out var usernamePosition))
        {
            return usernamePosition;
        }

        return _raceStartPositionsByPlayerId.TryGetValue(driver.PlayerId, out var playerPosition)
            ? playerPosition
            : null;
    }

    public int? GetDriverRaceStartPosition(byte playerId, string? username)
    {
        lock (_playersLock)
        {
            if (!string.IsNullOrWhiteSpace(username)
                && _raceStartPositionsByUsername.TryGetValue(username, out var usernamePosition))
            {
                return usernamePosition;
            }

            return _raceStartPositionsByPlayerId.TryGetValue(playerId, out var playerPosition)
                ? playerPosition
                : null;
        }
    }

    private static string CreateSessionId() => $"session-{Guid.NewGuid():N}";

    /// <summary>
    /// Gets session type as string
    /// </summary>
    public string GetSessionTypeString() => SessionType switch
    {
        0 => "Practice",
        1 => "Qualifying",
        2 => "Race",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets weather type as string
    /// </summary>
    public string GetWeatherTypeString() => WeatherType switch
    {
        0 => "Sunny",
        1 => "Cloudy",
        2 => "Rainy",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets wind strength as string
    /// </summary>
    public string GetWindTypeString() => WindType switch
    {
        0 => "Off",
        1 => "Weak",
        2 => "Strong",
        _ => "Unknown"
    };

    /// <summary>
    /// Estimates the remaining time for a lap-based race using the leader's recent pace.
    /// Returns null when there is not enough data or the session is not a lap race.
    /// </summary>
    public uint? GetEstimatedRemainingTimeMs()
    {
        lock (_playersLock)
        {
            if (SessionType != 2 || !RaceInProgress || MaxRaceLaps == 0 || Players.Count == 0)
            {
                return null;
            }

            var leader = Players.Values
                .OrderBy(p => p.CurrentRacePosition > 0 ? 0 : 1)
                .ThenBy(p => p.CurrentRacePosition == 0 ? byte.MaxValue : p.CurrentRacePosition)
                .ThenByDescending(p => p.CurrentTrackLap)
                .ThenByDescending(p => p.CurrentTrackNode)
                .FirstOrDefault();

            if (leader == null)
            {
                return null;
            }

            var remainingLaps = MaxRaceLaps - Math.Min((uint)MaxRaceLaps, leader.LapsCompleted);
            if (remainingLaps == 0)
            {
                return 0;
            }

            var estimatedLapTimeMs = GetEstimatedLeaderLapTimeMs(leader);
            if (estimatedLapTimeMs == null || estimatedLapTimeMs == 0)
            {
                return null;
            }

            var latestCompletedLapElapsedMs = leader.LapHistory.Count > 0
                ? leader.LapHistory[^1].ElapsedTimeMs
                : 0u;
            var currentLapElapsedMs = SessionTimeMs > latestCompletedLapElapsedMs
                ? SessionTimeMs - latestCompletedLapElapsedMs
                : 0u;
            var totalRemainingMs = (long)remainingLaps * estimatedLapTimeMs.Value - currentLapElapsedMs;

            return totalRemainingMs > 0
                ? (uint)totalRemainingMs
                : 0;
        }
    }

    private static uint? GetEstimatedLeaderLapTimeMs(Driver leader)
    {
        var recentValidLaps = leader.LapHistory
            .Where(lap => lap.IsValid && lap.LapTimeMs > 0)
            .TakeLast(3)
            .Select(lap => lap.LapTimeMs)
            .ToList();

        if (recentValidLaps.Count > 0)
        {
            return (uint)recentValidLaps.Average(static lapTimeMs => lapTimeMs);
        }

        return leader.PersonalBestLap?.LapTimeMs;
    }

    /// <summary>
    /// Store LFS username by connection UCID (from IS_NCN)
    /// </summary>
    public void SetUsername(byte ucid, string username)
    {
        lock (_playersLock)
        {
            _usernames[ucid] = username;
        }
    }

    /// <summary>
    /// Get LFS username by UCID
    /// </summary>
    public string? GetUsername(byte ucid)
    {
        lock (_playersLock)
        {
            return _usernames.TryGetValue(ucid, out var name) ? name : null;
        }
    }

    /// <summary>
    /// Clear usernames (on session reset)
    /// </summary>
    public void ClearUsernames()
    {
        lock (_playersLock)
        {
            _usernames.Clear();
        }
    }

    /// <summary>
    /// Appends a chat message to bounded session history.
    /// </summary>
    public void AddChatMessage(ChatMessageEntry message)
    {
        lock (_playersLock)
        {
            _chatMessages.Add(message);
            if (_chatMessages.Count > MaxChatMessages)
            {
                _chatMessages.RemoveAt(0);
            }

            ChatRevision++;
        }
    }

    /// <summary>
    /// Gets a snapshot of recent chat history.
    /// </summary>
    public IReadOnlyList<ChatMessageEntry> GetChatMessages()
    {
        lock (_playersLock)
        {
            return _chatMessages
                .Select(message => new ChatMessageEntry
                {
                    Kind = message.Kind,
                    MessageText = message.MessageText,
                    MessageLfsText = message.MessageLfsText,
                    ReceivedAtUtc = message.ReceivedAtUtc,
                })
                .ToList();
        }
    }
}
