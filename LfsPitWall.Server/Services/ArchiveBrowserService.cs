using System.Text.Json;
using LfsPitWall.Server.Models.Archive;
using Microsoft.Extensions.Options;

namespace LfsPitWall.Server.Services;

public sealed class ArchiveBrowserService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private readonly string _archiveRootPath;
    private readonly ILogger<ArchiveBrowserService> _logger;
    private readonly Dictionary<string, CachedCatalogFile> _catalogFileCache = new(StringComparer.OrdinalIgnoreCase);
    private ArchiveCatalogCache? _cache;

    public ArchiveBrowserService(IOptions<ArchiveOptions> archiveOptions, IHostEnvironment hostEnvironment, ILogger<ArchiveBrowserService> logger)
    {
        _archiveRootPath = archiveOptions.Value.GetNormalizedRootPath(hostEnvironment.ContentRootPath);
        _logger = logger;
    }

    public ArchiveSessionCatalogResponse GetSessions(string? track, string? sessionType, string? search, int page, int pageSize)
    {
        var catalog = GetCatalog();
        var normalizedTrack = NormalizeFilterValue(track);
        var normalizedSessionType = NormalizeFilterValue(sessionType);
        var normalizedSearch = NormalizeFilterValue(search);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var safePage = Math.Max(1, page);

        var filteredEntries = catalog.Entries
            .Where(entry => normalizedTrack == null || string.Equals(entry.Summary.TrackName, normalizedTrack, StringComparison.OrdinalIgnoreCase))
            .Where(entry => normalizedSessionType == null || string.Equals(entry.Summary.SessionType, normalizedSessionType, StringComparison.OrdinalIgnoreCase))
            .Where(entry => normalizedSearch == null || entry.SearchText.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalCount = filteredEntries.Count;
        var pagedItems = filteredEntries
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(entry => entry.Summary)
            .ToList();

        return new ArchiveSessionCatalogResponse
        {
            Overview = catalog.Overview,
            Filters = new ArchiveSessionFilterOptions
            {
                Tracks = catalog.Tracks,
                SessionTypes = catalog.SessionTypes
            },
            Items = pagedItems,
            Page = safePage,
            PageSize = safePageSize,
            TotalCount = totalCount
        };
    }

    public ArchiveSessionDetailResponse? GetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var catalog = GetCatalog();
        if (!catalog.BySessionId.TryGetValue(sessionId, out var entry))
        {
            return null;
        }

        var dump = LoadSessionDump(entry.FilePath);
        if (dump == null)
        {
            return null;
        }

        var sortedDrivers = SortDriversForDisplay(dump);
        return new ArchiveSessionDetailResponse
        {
            Summary = entry.Summary,
            Session = dump.Session,
            SessionBestLap = dump.SessionBestLap,
            SessionBestSectors = dump.SessionBestSectors
                .OrderBy(sector => sector.SectorNumber)
                .ToList(),
            OfficialResults = dump.OfficialResults
                .OrderBy(result => result.FinishPosition ?? int.MaxValue)
                .ThenBy(result => result.ResultNum)
                .ToList(),
            Drivers = sortedDrivers,
            AvailableLapNumbers = sortedDrivers
                .SelectMany(driver => driver.LapHistory)
                .Select(lap => lap.LapNumber)
                .Distinct()
                .OrderBy(lapNumber => lapNumber)
                .ToList()
        };
    }

    private ArchiveCatalogCache GetCatalog()
    {
        lock (_sync)
        {
            if (_cache != null && DateTime.UtcNow - _cache.BuiltAtUtc <= CacheLifetime)
            {
                return _cache;
            }

            _cache = BuildCatalog();
            return _cache;
        }
    }

    private ArchiveCatalogCache BuildCatalog()
    {
        var dumpRootPath = Path.Combine(_archiveRootPath, "session-dumps");
        var entries = new List<ArchiveCatalogEntry>();

        if (!Directory.Exists(dumpRootPath))
        {
            return new ArchiveCatalogCache();
        }

        var filePaths = Directory.EnumerateFiles(dumpRootPath, "*.json", SearchOption.AllDirectories).ToList();
        PruneCatalogFileCache(filePaths);

        foreach (var filePath in filePaths)
        {
            var indexedFile = GetOrCreateIndexedCatalogFile(filePath);
            if (indexedFile == null)
            {
                continue;
            }

            entries.Add(new ArchiveCatalogEntry
            {
                FilePath = filePath,
                Summary = indexedFile.Summary,
                SearchText = indexedFile.SearchText
            });
        }

        var orderedEntries = entries
            .OrderByDescending(entry => entry.Summary.SessionStartedAtUtc)
            .ThenByDescending(entry => entry.Summary.ArchivedAtUtc)
            .ToList();

        var latestEntry = orderedEntries.FirstOrDefault();
        return new ArchiveCatalogCache
        {
            BuiltAtUtc = DateTime.UtcNow,
            Entries = orderedEntries,
            BySessionId = orderedEntries.ToDictionary(entry => entry.Summary.SessionId, StringComparer.OrdinalIgnoreCase),
            Tracks = orderedEntries
                .Select(entry => entry.Summary.TrackName)
                .Where(trackName => !string.IsNullOrWhiteSpace(trackName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(trackName => trackName)
                .ToList(),
            SessionTypes = orderedEntries
                .Select(entry => entry.Summary.SessionType)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToList(),
            Overview = new ArchiveCatalogOverview
            {
                TotalSessions = orderedEntries.Count,
                TotalTracks = orderedEntries
                    .Select(entry => entry.Summary.TrackName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                LatestSessionStartedAtUtc = latestEntry?.Summary.SessionStartedAtUtc,
                LatestArchivedAtUtc = latestEntry?.Summary.ArchivedAtUtc,
                LatestTrackName = latestEntry?.Summary.TrackName ?? "",
                LatestSessionType = latestEntry?.Summary.SessionType ?? ""
            }
        };
    }

    private SessionDumpArchiveFile? LoadSessionDump(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<SessionDumpArchiveFile>(stream, JsonOptions);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read archive session dump from {FilePath}", filePath);
            return null;
        }
    }

    private CachedCatalogFile? GetOrCreateIndexedCatalogFile(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            if (_catalogFileCache.TryGetValue(filePath, out var cachedFile)
                && cachedFile.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc
                && cachedFile.Length == fileInfo.Length)
            {
                return cachedFile;
            }

            var dump = LoadSessionDump(filePath);
            if (dump?.Session == null || string.IsNullOrWhiteSpace(dump.Session.SessionId))
            {
                _catalogFileCache.Remove(filePath);
                return null;
            }

            var summary = CreateSummary(dump);
            var indexedFile = new CachedCatalogFile
            {
                FilePath = filePath,
                LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                Length = fileInfo.Length,
                Summary = summary,
                SearchText = BuildSearchText(summary, dump)
            };

            _catalogFileCache[filePath] = indexedFile;
            return indexedFile;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to index archive session dump from {FilePath}", filePath);
            _catalogFileCache.Remove(filePath);
            return null;
        }
    }

    private void PruneCatalogFileCache(IEnumerable<string> filePaths)
    {
        var currentFiles = new HashSet<string>(filePaths, StringComparer.OrdinalIgnoreCase);
        var missingFiles = _catalogFileCache.Keys
            .Where(filePath => !currentFiles.Contains(filePath))
            .ToList();

        foreach (var missingFile in missingFiles)
        {
            _catalogFileCache.Remove(missingFile);
        }
    }

    private static ArchiveSessionListItem CreateSummary(SessionDumpArchiveFile dump)
    {
        var drivers = dump.Drivers ?? new List<ArchiveDriverSnapshot>();
        var officialResults = dump.OfficialResults ?? new List<ArchiveOfficialResultEntry>();
        return new ArchiveSessionListItem
        {
            SessionId = dump.Session.SessionId,
            SessionType = dump.Session.SessionType,
            SessionTypeId = dump.Session.SessionTypeId,
            TrackName = dump.Session.TrackName,
            BaseTrackName = string.IsNullOrWhiteSpace(dump.Session.BaseTrackName) ? dump.Session.TrackName : dump.Session.BaseTrackName,
            LayoutName = dump.Session.LayoutName ?? string.Empty,
            SessionStartedAtUtc = dump.Session.SessionStartedAtUtc,
            ArchivedAtUtc = dump.ArchivedAtUtc,
            DriverCount = drivers.Count,
            CompletedLaps = drivers.Count == 0 ? 0 : drivers.Max(driver => (int)driver.LapsCompleted),
            SessionBestLapMs = dump.SessionBestLap?.LapTimeMs ?? 0,
            SessionBestLapAuthorName = dump.SessionBestLap?.AuthorName ?? "",
            WinnerName = ResolveWinnerName(dump),
            OfficialResultsCount = officialResults.Count,
            SchemaVersion = dump.SchemaVersion,
            Trigger = dump.Trigger
        };
    }

    private static string ResolveWinnerName(SessionDumpArchiveFile dump)
    {
        var officialWinner = dump.OfficialResults?
            .Where(result => result.FinishPosition == 1)
            .OrderBy(result => result.ResultNum)
            .Select(result => string.IsNullOrWhiteSpace(result.DriverName) ? result.Username : result.DriverName)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(officialWinner))
        {
            return officialWinner;
        }

        if (dump.Session.SessionTypeId == 1)
        {
            return dump.SessionBestLap?.AuthorName ?? "";
        }

        return dump.Drivers?
            .Where(driver => driver.CurrentRacePosition > 0)
            .OrderBy(driver => driver.CurrentRacePosition)
            .Select(driver => driver.Name)
            .FirstOrDefault() ?? "";
    }

    private static List<ArchiveDriverSnapshot> SortDriversForDisplay(SessionDumpArchiveFile dump)
    {
        var drivers = dump.Drivers ?? new List<ArchiveDriverSnapshot>();

        if (dump.OfficialResults.Count > 0)
        {
            var officialResultsByPlayerId = dump.OfficialResults
                .Where(result => result.PlayerId.HasValue)
                .GroupBy(result => result.PlayerId!.Value)
                .ToDictionary(group => group.Key, group => group.OrderBy(result => result.FinishPosition ?? int.MaxValue).First());

            var officialResultsByUsername = dump.OfficialResults
                .Where(result => !string.IsNullOrWhiteSpace(result.Username))
                .GroupBy(result => result.Username, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(result => result.FinishPosition ?? int.MaxValue).First(), StringComparer.OrdinalIgnoreCase);

            return drivers
                .OrderBy(driver => TryResolveOfficialResult(driver, officialResultsByPlayerId, officialResultsByUsername, out var result)
                    ? result.FinishPosition ?? int.MaxValue
                    : int.MaxValue)
                .ThenBy(driver => driver.CurrentRacePosition > 0 ? driver.CurrentRacePosition : byte.MaxValue)
                .ThenBy(driver => driver.Name)
                .ToList();
        }

        if (dump.Session.SessionTypeId == 1)
        {
            return drivers
                .OrderBy(driver => driver.PersonalBestLap?.LapTimeMs ?? uint.MaxValue)
                .ThenBy(driver => driver.Name)
                .ToList();
        }

        return drivers
            .OrderBy(driver => driver.CurrentRacePosition > 0 ? 0 : 1)
            .ThenBy(driver => driver.CurrentRacePosition > 0 ? driver.CurrentRacePosition : byte.MaxValue)
            .ThenByDescending(driver => driver.LapsCompleted)
            .ThenBy(driver => driver.Name)
            .ToList();
    }

    private static string BuildSearchText(ArchiveSessionListItem summary, SessionDumpArchiveFile dump)
    {
        var driverNames = dump.Drivers
            .SelectMany(driver => new[] { driver.Name, driver.Username })
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(' ', new[]
        {
            summary.TrackName,
            summary.BaseTrackName,
            summary.LayoutName,
            summary.SessionType,
            summary.WinnerName,
            summary.SessionBestLapAuthorName,
            dump.Session.HostName,
            string.Join(' ', driverNames)
        });
    }

    private static string? NormalizeFilterValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryResolveOfficialResult(
        ArchiveDriverSnapshot driver,
        IReadOnlyDictionary<byte, ArchiveOfficialResultEntry> officialResultsByPlayerId,
        IReadOnlyDictionary<string, ArchiveOfficialResultEntry> officialResultsByUsername,
        out ArchiveOfficialResultEntry officialResult)
    {
        if (officialResultsByPlayerId.TryGetValue(driver.PlayerId, out var officialResultByPlayerId))
        {
            officialResult = officialResultByPlayerId;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(driver.Username)
            && officialResultsByUsername.TryGetValue(driver.Username, out var officialResultByUsername))
        {
            officialResult = officialResultByUsername;
            return true;
        }

        officialResult = new ArchiveOfficialResultEntry();
        return false;
    }

    private sealed class ArchiveCatalogCache
    {
        public DateTime BuiltAtUtc { get; init; }
        public List<ArchiveCatalogEntry> Entries { get; init; } = new();
        public Dictionary<string, ArchiveCatalogEntry> BySessionId { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Tracks { get; init; } = new();
        public List<string> SessionTypes { get; init; } = new();
        public ArchiveCatalogOverview Overview { get; init; } = new();
    }

    private sealed class ArchiveCatalogEntry
    {
        public string FilePath { get; init; } = "";
        public string SearchText { get; init; } = "";
        public ArchiveSessionListItem Summary { get; init; } = new();
    }

    private sealed class CachedCatalogFile
    {
        public string FilePath { get; init; } = "";
        public DateTime LastWriteTimeUtc { get; init; }
        public long Length { get; init; }
        public string SearchText { get; init; } = "";
        public ArchiveSessionListItem Summary { get; init; } = new();
    }
}