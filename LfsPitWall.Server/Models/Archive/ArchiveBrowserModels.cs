namespace LfsPitWall.Server.Models.Archive;

public class ArchiveCatalogOverview
{
    public int TotalSessions { get; set; }
    public int TotalTracks { get; set; }
    public DateTime? LatestSessionStartedAtUtc { get; set; }
    public DateTime? LatestArchivedAtUtc { get; set; }
    public string LatestTrackName { get; set; } = "";
    public string LatestSessionType { get; set; } = "";
}

public class ArchiveSessionFilterOptions
{
    public List<string> Tracks { get; set; } = new();
    public List<string> SessionTypes { get; set; } = new();
}

public class ArchiveSessionListItem
{
    public string SessionId { get; set; } = "";
    public string SessionType { get; set; } = "Unknown";
    public byte SessionTypeId { get; set; }
    public string TrackName { get; set; } = "Unknown";
    public string BaseTrackName { get; set; } = "Unknown";
    public string LayoutName { get; set; } = "";
    public DateTime SessionStartedAtUtc { get; set; }
    public DateTime ArchivedAtUtc { get; set; }
    public int DriverCount { get; set; }
    public int CompletedLaps { get; set; }
    public uint SessionBestLapMs { get; set; }
    public string SessionBestLapAuthorName { get; set; } = "";
    public string WinnerName { get; set; } = "";
    public int OfficialResultsCount { get; set; }
    public int SchemaVersion { get; set; }
    public string Trigger { get; set; } = "session-transition";
}

public class ArchiveSessionCatalogResponse
{
    public ArchiveCatalogOverview Overview { get; set; } = new();
    public ArchiveSessionFilterOptions Filters { get; set; } = new();
    public List<ArchiveSessionListItem> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public class ArchiveSessionDetailResponse
{
    public ArchiveSessionListItem Summary { get; set; } = new();
    public ArchiveSessionMetadata Session { get; set; } = new();
    public ArchiveBestLapSnapshot? SessionBestLap { get; set; }
    public List<ArchiveBestSectorSnapshot> SessionBestSectors { get; set; } = new();
    public List<ArchiveOfficialResultEntry> OfficialResults { get; set; } = new();
    public List<ArchiveDriverSnapshot> Drivers { get; set; } = new();
    public List<uint> AvailableLapNumbers { get; set; } = new();
}