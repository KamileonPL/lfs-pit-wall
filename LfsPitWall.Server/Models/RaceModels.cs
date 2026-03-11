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
    /// Last elapsed time when driver finished a lap (for gap calculation)
    /// </summary>
    public uint LastElapsedTimeMs { get; set; }

    /// <summary>
    /// Last lap number completed (for gap calculation context)
    /// </summary>
    public uint LastLapNumber { get; set; }

    /// <summary>
    /// Lap history (lap-centric architecture - all laps stored here)
    /// </summary>
    public List<LapData> LapHistory { get; set; } = new();

    /// <summary>
    /// Driver's personal best lap
    /// </summary>
    public LapData? PersonalBestLap => LapHistory
        .Where(l => l.IsValid)
        .OrderBy(l => l.GetAdjustedTime())
        .FirstOrDefault();

    /// <summary>
    /// Driver's personal best time for each sector
    /// </summary>
    public Dictionary<int, uint> PersonalBestSectors { get; set; } = new();

    /// <summary>
    /// Gets the current lap's sector times that have been recorded so far
    /// </summary>
    public Dictionary<int, SectorTime> GetCurrentSectorProgress()
    {
        if (LapHistory.Count == 0)
            return new Dictionary<int, SectorTime>();

        var currentLap = LapHistory.Last();
        return currentLap.Sectors;
    }

    /// <summary>
    /// Updates a sector time for the current lap
    /// </summary>
    public void UpdateSectorTime(int sectorNumber, uint timeMs)
    {
        if (LapHistory.Count == 0)
            return;

        var currentLap = LapHistory.Last();
        var sectorTime = new SectorTime
        {
            SectorNumber = sectorNumber,
            TimeMs = timeMs,
            IsValid = true
        };

        currentLap.Sectors[sectorNumber] = sectorTime;

        // Update personal best for this sector
        if (!PersonalBestSectors.ContainsKey(sectorNumber) || 
            timeMs < PersonalBestSectors[sectorNumber])
        {
            PersonalBestSectors[sectorNumber] = timeMs;
        }
    }

    /// <summary>
    /// Adds a completed lap to the history
    /// </summary>
    public void AddLap(LapData lap)
    {
        LapHistory.Add(lap);
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
    /// Maximum race laps (from IS_RST)
    /// </summary>
    public byte MaxRaceLaps { get; set; }

    /// <summary>
    /// Qualifying time in minutes (from IS_STA)
    /// </summary>
    public byte QualifyingMins { get; set; }

    /// <summary>
    /// Session Best Lap - Author PLID and lap number
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
    /// Session's global best lap
    /// </summary>
    public LapData? SessionBestLap
    {
        get
        {
            lock (_playersLock)
            {
                var allLaps = Players.Values
                    .SelectMany(p => p.LapHistory)
                    .Where(l => l.IsValid)
                    .OrderBy(l => l.GetAdjustedTime())
                    .FirstOrDefault();
                return allLaps;
            }
        }
    }

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
            var bestLap = Players.Values
                .SelectMany(player => player.LapHistory)
                .Where(lap => lap.IsValid)
                .OrderBy(lap => lap.GetAdjustedTime())
                .FirstOrDefault();

            if (bestLap != null)
            {
                var owner = Players.Values.FirstOrDefault(player =>
                    player.LapHistory.Any(lap => ReferenceEquals(lap, bestLap)));

                if (owner != null)
                {
                    return (owner.NameHtml, owner.Username, bestLap.LapNumber);
                }
            }

            if (!string.IsNullOrEmpty(SessionBestLapAuthorNameHtml))
            {
                return (SessionBestLapAuthorNameHtml, SessionBestLapAuthorUsername, SessionBestLapNumber);
            }

            return (null, null, null);
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
            RaceFlag = 0;
            RaceInProgress = false;
            SessionTimeMs = 0;
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
