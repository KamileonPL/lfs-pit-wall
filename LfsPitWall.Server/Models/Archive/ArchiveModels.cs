namespace LfsPitWall.Server.Models.Archive;

public class ArchiveOptions
{
    public const string SectionName = "Archive";

    public bool Enabled { get; set; } = true;
    public string RootPath { get; set; } = "archive";
    public bool WriteOfficialResults { get; set; } = true;
    public bool WriteSessionDump { get; set; } = true;
    public bool WriteOnSessionTransition { get; set; } = true;
    public bool WriteOnApplicationStop { get; set; } = true;

    public string GetNormalizedRootPath(string contentRootPath)
    {
        var configuredPath = string.IsNullOrWhiteSpace(RootPath) ? "archive" : RootPath.Trim();
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }
}

public class SessionArchiveSnapshot
{
    public string SessionId { get; set; } = "";
    public DateTime SessionStartedAtUtc { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string SessionType { get; set; } = "Unknown";
    public byte SessionTypeId { get; set; }
    public string TrackName { get; set; } = "Unknown";
    public string HostName { get; set; } = "";
    public string HostNameHtml { get; set; } = "";
    public string WeatherType { get; set; } = "Unknown";
    public byte WeatherTypeId { get; set; }
    public string WindType { get; set; } = "Unknown";
    public byte WindTypeId { get; set; }
    public byte RaceFlag { get; set; }
    public bool RaceInProgress { get; set; }
    public uint SessionTimeMs { get; set; }
    public byte MaxRaceLaps { get; set; }
    public byte QualifyingMins { get; set; }
    public int ActiveSectorCount { get; set; }
    public uint ChatRevision { get; set; }
    public ArchiveBestLapSnapshot? SessionBestLap { get; set; }
    public List<ArchiveBestSectorSnapshot> SessionBestSectors { get; set; } = new();
    public ArchiveTrackMapSnapshot TrackMap { get; set; } = new();
    public List<ArchiveChatMessageSnapshot> ChatMessages { get; set; } = new();
    public List<ArchiveDriverSnapshot> Drivers { get; set; } = new();
    public List<ArchiveOfficialResultEntry> OfficialResults { get; set; } = new();

    public bool HasAnyMeaningfulData()
    {
        return Drivers.Count > 0
            || OfficialResults.Count > 0
            || ChatMessages.Count > 0
            || TrackMap.Points.Count > 0
            || SessionTimeMs > 0;
    }
}

public class ArchiveSessionMetadata
{
    public string SessionId { get; set; } = "";
    public string SessionType { get; set; } = "Unknown";
    public byte SessionTypeId { get; set; }
    public string TrackName { get; set; } = "Unknown";
    public string HostName { get; set; } = "";
    public string WeatherType { get; set; } = "Unknown";
    public byte WeatherTypeId { get; set; }
    public string WindType { get; set; } = "Unknown";
    public byte WindTypeId { get; set; }
    public byte RaceFlag { get; set; }
    public bool RaceInProgress { get; set; }
    public uint SessionTimeMs { get; set; }
    public byte MaxRaceLaps { get; set; }
    public byte QualifyingMins { get; set; }
    public int ActiveSectorCount { get; set; }
    public DateTime SessionStartedAtUtc { get; set; }
}

public class ArchiveBestLapSnapshot
{
    public uint LapTimeMs { get; set; }
    public uint? LapNumber { get; set; }
    public string AuthorName { get; set; } = "";
    public string AuthorUsername { get; set; } = "";
}

public class ArchiveBestSectorSnapshot
{
    public int SectorNumber { get; set; }
    public uint TimeMs { get; set; }
    public string AuthorName { get; set; } = "";
    public string AuthorUsername { get; set; } = "";
}

public class ArchiveTrackMapSnapshot
{
    public uint Revision { get; set; }
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
    public List<ArchiveTrackMapPointSnapshot> Points { get; set; } = new();
}

public class ArchiveTrackMapPointSnapshot
{
    public ushort Node { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public class ArchiveChatMessageSnapshot
{
    public string Kind { get; set; } = "system";
    public string MessageText { get; set; } = "";
    public string MessageLfsText { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }
}

public class ArchiveDriverSnapshot
{
    public byte PlayerId { get; set; }
    public byte ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public string NameHtml { get; set; } = "";
    public string Username { get; set; } = "";
    public string CarName { get; set; } = "";
    public string SkinName { get; set; } = "";
    public string DriverColor { get; set; } = "#9CA3AF";
    public byte[] TyreTypes { get; set; } = Array.Empty<byte>();
    public byte? FuelPercent { get; set; }
    public uint PitStops { get; set; }
    public bool IsInPitLane { get; set; }
    public bool IsPitStopActive { get; set; }
    public string PitStatus { get; set; } = "track";
    public uint? LastPitStopTimeMs { get; set; }
    public uint? LastPitLaneTimeMs { get; set; }
    public byte? LastPitStopFuelAddPercent { get; set; }
    public byte[] LastPitTyresChanged { get; set; } = Array.Empty<byte>();
    public uint LapsCompleted { get; set; }
    public byte CurrentRacePosition { get; set; }
    public ushort CurrentTrackNode { get; set; }
    public ushort CurrentTrackLap { get; set; }
    public bool HasWorldPosition { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public ushort CurrentHeading { get; set; }
    public double TopSpeedKmh { get; set; }
    public List<ArchiveSectorSnapshot> PersonalBestSectors { get; set; } = new();
    public List<ArchiveSectorSnapshot> CurrentSectorProgress { get; set; } = new();
    public ArchiveLapSnapshot? PersonalBestLap { get; set; }
    public ArchiveOfficialResultEntry? OfficialResult { get; set; }
    public int? RaceStartPosition { get; set; }
    public List<ArchiveLapSnapshot> LapHistory { get; set; } = new();
}

public class ArchiveLapSnapshot
{
    public uint LapNumber { get; set; }
    public uint LapTimeMs { get; set; }
    public uint ElapsedTimeMs { get; set; }
    public bool IsValid { get; set; }
    public uint PitStops { get; set; }
    public uint PenaltyMs { get; set; }
    public byte Gear { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public List<ArchiveSectorSnapshot> Sectors { get; set; } = new();
}

public class ArchiveSectorSnapshot
{
    public int SectorNumber { get; set; }
    public uint TimeMs { get; set; }
    public bool IsValid { get; set; }
}

public class ArchiveOfficialResultEntry
{
    public string Username { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string CarName { get; set; } = "";
    public byte? PlayerId { get; set; }
    public string Kind { get; set; } = "Race";
    public uint TotalTimeMs { get; set; }
    public uint BestLapTimeMs { get; set; }
    public byte NumStops { get; set; }
    public ushort LapsDone { get; set; }
    public ushort Flags { get; set; }
    public byte ConfirmFlags { get; set; }
    public byte ResultNum { get; set; }
    public byte NumRes { get; set; }
    public ushort PenaltySeconds { get; set; }
    public int? StartPosition { get; set; }
    public int? FinishPosition { get; set; }
    public ArchiveOfficialPointsBreakdown Points { get; set; } = new();
}

public class ArchiveOfficialPointsBreakdown
{
    public int PositionPoints { get; set; }
    public int PolePositionBonusPoints { get; set; }
    public int FastestLapBonusPoints { get; set; }
    public int HighestClimberBonusPoints { get; set; }
    public int BonusPoints { get; set; }
    public int TotalPoints { get; set; }
}

public class OfficialResultsArchiveFile
{
    public string ArchiveType { get; set; } = "official-results";
    public int SchemaVersion { get; set; } = 1;
    public string SourceAppVersion { get; set; } = "";
    public string Trigger { get; set; } = "session-transition";
    public DateTime ArchivedAtUtc { get; set; }
    public ArchiveSessionMetadata Session { get; set; } = new();
    public List<ArchiveOfficialResultEntry> Results { get; set; } = new();
}

public class SessionDumpArchiveFile
{
    public string ArchiveType { get; set; } = "session-dump";
    public int SchemaVersion { get; set; } = 1;
    public string SourceAppVersion { get; set; } = "";
    public string Trigger { get; set; } = "session-transition";
    public DateTime ArchivedAtUtc { get; set; }
    public ArchiveSessionMetadata Session { get; set; } = new();
    public ArchiveBestLapSnapshot? SessionBestLap { get; set; }
    public List<ArchiveBestSectorSnapshot> SessionBestSectors { get; set; } = new();
    public ArchiveTrackMapSnapshot TrackMap { get; set; } = new();
    public List<ArchiveChatMessageSnapshot> ChatMessages { get; set; } = new();
    public List<ArchiveOfficialResultEntry> OfficialResults { get; set; } = new();
    public List<ArchiveDriverSnapshot> Drivers { get; set; } = new();
}