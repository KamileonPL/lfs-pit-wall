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
/// Represents a driver's most recent elapsed-time checkpoint in the session.
/// </summary>
public class TimingPointSnapshot
{
    public uint LapNumber { get; set; }
    public int TimingPointIndex { get; set; }
    public uint ElapsedTimeMs { get; set; }
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
}

/// <summary>
/// Represents the overall race session
/// Singleton pattern - one instance per application lifetime
/// </summary>
public class RaceSession
{
    private readonly object _playersLock = new object(); // Thread-safe access
    private readonly Dictionary<byte, string> _usernames = new(); // UCID -> UName mapping

    /// <summary>
    /// Track name
    /// </summary>
    public string TrackName { get; set; } = "Unknown";

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

                foreach (var driver in Players.Values)
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

            foreach (var driver in Players.Values)
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
            Players[driver.PlayerId] = driver;
        }
    }

    /// <summary>
    /// Removes a driver from the session (THREAD-SAFE)
    /// </summary>
    public void RemoveDriver(byte playerId)
    {
        lock (_playersLock)
        {
            Players.Remove(playerId);
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
    /// Resets the entire session
    /// </summary>
    public void Reset()
    {
        lock (_playersLock)
        {
            TrackName = "Unknown";
            SessionType = 0;
            WeatherType = 0;
            WindType = 0;
            RaceFlag = 0;
            RaceInProgress = false;
            SessionTimeMs = 0;
            ActiveSectorCount = 0;
            SessionBestLap = null;
            SessionBestLapAuthorPLID = null;
            SessionBestLapNumber = null;
            SessionBestLapAuthorNameHtml = string.Empty;
            SessionBestLapAuthorUsername = string.Empty;
            Players.Clear();
        }
    }

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
}
