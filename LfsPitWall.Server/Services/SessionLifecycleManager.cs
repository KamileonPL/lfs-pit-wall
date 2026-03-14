using LfsPitWall.Server.Models;
using LfsPitWall.Server.Models.Archive;

namespace LfsPitWall.Server.Services;

public sealed class SessionLifecycleManager
{
    private readonly object _sync = new();
    private readonly RaceSession _raceSession;
    private readonly SessionArchiveWriter _sessionArchiveWriter;
    private readonly ArchiveOptions _archiveOptions;
    private readonly ILogger<SessionLifecycleManager> _logger;
    private ObservedRaceSession? _activeSession;

    public SessionLifecycleManager(RaceSession raceSession, SessionArchiveWriter sessionArchiveWriter, Microsoft.Extensions.Options.IOptions<ArchiveOptions> archiveOptions, ILogger<SessionLifecycleManager> logger)
    {
        _raceSession = raceSession;
        _sessionArchiveWriter = sessionArchiveWriter;
        _archiveOptions = archiveOptions.Value;
        _logger = logger;
    }

    public bool ObserveRaceStart(string trackName, byte raceLaps, byte qualifyingMins, byte timingByte, bool isRequestReply)
    {
        var nextSession = ObservedRaceSession.Create(trackName, raceLaps, qualifyingMins, timingByte);

        lock (_sync)
        {
            if (_activeSession is null)
            {
                _activeSession = nextSession;
                return false;
            }

            if (isRequestReply && _activeSession.Value == nextSession)
            {
                return false;
            }

            _logger.LogInformation(
                "New {SessionKind} session detected on {Track}. Clearing live session state.",
                nextSession.SessionKind,
                nextSession.TrackName);

            if (_archiveOptions.Enabled && _archiveOptions.WriteOnSessionTransition)
            {
                _sessionArchiveWriter.ArchiveCurrentSessionIfNeeded("session-transition");
            }

            _raceSession.Reset();
            _activeSession = nextSession;
            return true;
        }
    }

    private readonly record struct ObservedRaceSession(
        byte SessionType,
        string SessionKind,
        string TrackName,
        byte RaceLaps,
        byte QualifyingMins,
        byte TimingByte)
    {
        public static ObservedRaceSession Create(string trackName, byte raceLaps, byte qualifyingMins, byte timingByte)
        {
            var normalizedTrackName = string.IsNullOrWhiteSpace(trackName) ? "Unknown" : trackName;
            var sessionType = raceLaps == 0 ? (byte)1 : (byte)2;

            return new ObservedRaceSession(
                sessionType,
                sessionType == 1 ? "qualifying" : "race",
                normalizedTrackName,
                raceLaps,
                qualifyingMins,
                timingByte);
        }
    }
}