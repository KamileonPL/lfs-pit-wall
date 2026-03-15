using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LfsPitWall.Server.Models;
using LfsPitWall.Server.Models.Archive;
using Microsoft.Extensions.Options;

namespace LfsPitWall.Server.Services;

public sealed class SessionArchiveWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly HashSet<string> _archivedSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly RaceSession _raceSession;
    private readonly ArchiveOptions _options;
    private readonly ILogger<SessionArchiveWriter> _logger;
    private readonly string _rootPath;
    private readonly string _appVersion;

    public SessionArchiveWriter(RaceSession raceSession, IOptions<ArchiveOptions> options, IHostEnvironment hostEnvironment, ILogger<SessionArchiveWriter> logger)
    {
        _raceSession = raceSession;
        _options = options.Value;
        _logger = logger;
        _rootPath = _options.GetNormalizedRootPath(hostEnvironment.ContentRootPath);
        _appVersion = ResolveApplicationVersion();
    }

    public void ArchiveCurrentSessionIfNeeded(string trigger)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var snapshot = _raceSession.CreateArchiveSnapshot();
        if (snapshot.SessionTypeId == 0 || !snapshot.HasAnyMeaningfulData())
        {
            return;
        }

        lock (_sync)
        {
            if (_archivedSessionIds.Contains(snapshot.SessionId))
            {
                return;
            }

            Directory.CreateDirectory(_rootPath);

            var archivedAtUtc = DateTime.UtcNow;
            var wroteAnyFile = false;

            if (_options.WriteOfficialResults && snapshot.OfficialResults.Count > 0)
            {
                var officialResultsDocument = BuildOfficialResultsDocument(snapshot, trigger, archivedAtUtc);
                WriteArchiveDocument(
                    Path.Combine(_rootPath, "official-results", archivedAtUtc.ToString("yyyy"), archivedAtUtc.ToString("MM")),
                    BuildArchiveFileName(snapshot, "results", officialResultsDocument.SchemaVersion),
                    officialResultsDocument);
                wroteAnyFile = true;
            }

            if (_options.WriteSessionDump)
            {
                var sessionDumpDocument = BuildSessionDumpDocument(snapshot, trigger, archivedAtUtc);
                WriteArchiveDocument(
                    Path.Combine(_rootPath, "session-dumps", archivedAtUtc.ToString("yyyy"), archivedAtUtc.ToString("MM")),
                    BuildArchiveFileName(snapshot, "dump", sessionDumpDocument.SchemaVersion),
                    sessionDumpDocument);
                wroteAnyFile = true;
            }

            if (wroteAnyFile)
            {
                _archivedSessionIds.Add(snapshot.SessionId);
                _logger.LogInformation(
                    "Archived session {SessionId} ({SessionType} on {Track}) to {ArchiveRoot}",
                    snapshot.SessionId,
                    snapshot.SessionType,
                    snapshot.TrackName,
                    _rootPath);
            }
        }
    }

    private OfficialResultsArchiveFile BuildOfficialResultsDocument(SessionArchiveSnapshot snapshot, string trigger, DateTime archivedAtUtc)
    {
        return new OfficialResultsArchiveFile
        {
            SourceAppVersion = _appVersion,
            Trigger = trigger,
            ArchivedAtUtc = archivedAtUtc,
            Session = BuildSessionMetadata(snapshot),
            Results = snapshot.OfficialResults
                .OrderBy(result => result.FinishPosition ?? int.MaxValue)
                .ThenBy(result => result.Username)
                .ToList()
        };
    }

    private SessionDumpArchiveFile BuildSessionDumpDocument(SessionArchiveSnapshot snapshot, string trigger, DateTime archivedAtUtc)
    {
        return new SessionDumpArchiveFile
        {
            SourceAppVersion = _appVersion,
            Trigger = trigger,
            ArchivedAtUtc = archivedAtUtc,
            Session = BuildSessionMetadata(snapshot),
            SessionBestLap = snapshot.SessionBestLap,
            SessionBestSectors = snapshot.SessionBestSectors,
            OfficialResults = snapshot.OfficialResults,
            Drivers = snapshot.Drivers
        };
    }

    private static ArchiveSessionMetadata BuildSessionMetadata(SessionArchiveSnapshot snapshot)
    {
        return new ArchiveSessionMetadata
        {
            SessionId = snapshot.SessionId,
            SessionType = snapshot.SessionType,
            SessionTypeId = snapshot.SessionTypeId,
            TrackName = snapshot.TrackName,
            HostName = snapshot.HostName,
            WeatherType = snapshot.WeatherType,
            WeatherTypeId = snapshot.WeatherTypeId,
            WindType = snapshot.WindType,
            WindTypeId = snapshot.WindTypeId,
            RaceFlag = snapshot.RaceFlag,
            RaceInProgress = snapshot.RaceInProgress,
            SessionTimeMs = snapshot.SessionTimeMs,
            MaxRaceLaps = snapshot.MaxRaceLaps,
            QualifyingMins = snapshot.QualifyingMins,
            ActiveSectorCount = snapshot.ActiveSectorCount,
            SessionStartedAtUtc = snapshot.SessionStartedAtUtc
        };
    }

    private void WriteArchiveDocument<TDocument>(string directoryPath, string fileName, TDocument document)
    {
        Directory.CreateDirectory(directoryPath);

        var finalPath = Path.Combine(directoryPath, fileName);
        var tempPath = Path.Combine(directoryPath, $".{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(document, JsonOptions);

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, finalPath, true);
    }

    private static string BuildArchiveFileName(SessionArchiveSnapshot snapshot, string suffix, int schemaVersion)
    {
        var startedAt = snapshot.SessionStartedAtUtc == default ? snapshot.CapturedAtUtc : snapshot.SessionStartedAtUtc;
        var timestamp = startedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        var track = SanitizeFileToken(snapshot.TrackName, "unknown-track");
        var sessionType = SanitizeFileToken(snapshot.SessionType, "unknown-session").ToLowerInvariant();
        var shortSessionId = snapshot.SessionId.Length > 8 ? snapshot.SessionId[..8] : snapshot.SessionId;
        return $"{timestamp}_{track}_{sessionType}_{shortSessionId}.{suffix}.v{schemaVersion}.json";
    }

    private static string SanitizeFileToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var sanitized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string ResolveApplicationVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "0.0.0"
            : informationalVersion.Split('+')[0];
    }
}