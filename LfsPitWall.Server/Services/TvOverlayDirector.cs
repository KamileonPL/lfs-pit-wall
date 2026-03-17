using System.Linq;
using System.Net;
using LfsPitWall.Server.Models;

namespace LfsPitWall.Server.Services;

public sealed class TvOverlayDirector
{
    public const string HubGroupName = "tv-overlay";

    private const int MaxVisibleEntries = 15;
    private const int PinnedEntries = 3;
    private const int RotationWindowSeconds = 30;
    private const int RaceMetricCycleSeconds = 6;
    private const int QualMetricCycleSeconds = 7;
    private const int PopupLifetimeSeconds = 6;
    private const int MaxPopupCount = 2;

    private readonly object _sync = new();
    private readonly HashSet<string> _subscribers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, uint> _bestSectorTimes = new();
    private readonly List<OverlayPopupState> _activePopups = new();

    // State tracking for event-based popups
    private readonly Dictionary<byte, int> _lastDriverOrder = new();
    private readonly Dictionary<byte, bool> _lastPitStatus = new();
    private bool _lastRaceInProgress;

    private bool _isInitialized;
    private uint _bestLapTimeMs;
    private string _bestLapAuthorKey = string.Empty;
    private long _popupSequence;

    public bool HasSubscribers
    {
        get
        {
            lock (_sync)
            {
                return _subscribers.Count > 0;
            }
        }
    }

    public void RegisterSubscriber(string connectionId)
    {
        lock (_sync)
        {
            _subscribers.Add(connectionId);
        }
    }

    public void UnregisterSubscriber(string connectionId)
    {
        lock (_sync)
        {
            _subscribers.Remove(connectionId);
        }
    }

    public TvOverlaySnapshot BuildSnapshot(RaceSession session)
    {
        var orderedDrivers = session.GetDriversForStandings().ToList();
        var visibleDrivers = GetVisibleDrivers(orderedDrivers, session.SessionTimeMs).ToList();
        var metricMode = GetMetricMode(session.SessionType, session.SessionTimeMs);
        var bestSectorInfos = session.GetSessionBestSectorInfos();
        var (bestLapAuthorNameHtml, bestLapAuthorUsername, bestLapNumber) = session.GetSessionBestLapInfo();
        var leader = orderedDrivers.FirstOrDefault();
        var estimatedRemainingTimeMs = session.GetEstimatedRemainingTimeMs();

        List<TvOverlayPopup> popups;
        lock (_sync)
        {
            if (!_isInitialized)
            {
                SeedState(session, orderedDrivers, bestLapAuthorUsername, bestSectorInfos);
            }

            UpdateEventPopups(session, orderedDrivers, leader);
            UpdatePopupState(session.SessionBestLap, bestLapAuthorNameHtml, bestLapAuthorUsername, bestLapNumber, bestSectorInfos);
            PruneExpiredPopups();
            popups = _activePopups
                .OrderByDescending(popup => popup.CreatedAtUtc)
                .Take(MaxPopupCount)
                .Select(popup => new TvOverlayPopup
                {
                    Id = popup.Id,
                    Kind = popup.Kind,
                    Title = popup.Title,
                    AccentClass = popup.AccentClass,
                    SubjectHtml = popup.SubjectHtml,
                    DetailHtml = popup.DetailHtml
                })
                .ToList();
        }

        return new TvOverlaySnapshot
        {
            Theme = session.SessionType == 2 ? "race" : "qualifying",
            TrackName = session.DisplayTrackName,
            SessionType = session.GetSessionTypeString(),
            HostName = string.IsNullOrWhiteSpace(session.HostName) ? "Live for Speed" : session.HostName,
            ProgressTitle = BuildProgressTitle(session),
            ProgressValue = BuildProgressValue(session, leader),
            ProgressDetail = BuildProgressDetail(session, orderedDrivers.Count, estimatedRemainingTimeMs),
            ProgressRatio = BuildProgressRatio(session, leader),
            RotationLabel = GetMetricModeLabel(metricMode),
            StandingsWindowLabel = BuildWindowLabel(orderedDrivers.Count, visibleDrivers.Count, session.SessionTimeMs),
            Entries = BuildEntries(session, orderedDrivers, visibleDrivers, metricMode),
            Popups = popups,
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
    }

    private List<TvOverlayStandingEntry> BuildEntries(
        RaceSession session,
        List<Driver> orderedDrivers,
        List<Driver> visibleDrivers,
        OverlayMetricMode metricMode)
    {
        var cumulativeGapMs = new uint?[orderedDrivers.Count];
        cumulativeGapMs[0] = 0;

        for (var index = 1; index < orderedDrivers.Count; index++)
        {
            var gapToPreviousMs = GetGapToPreviousMs(orderedDrivers[index], orderedDrivers[index - 1]);
            cumulativeGapMs[index] = gapToPreviousMs.HasValue && cumulativeGapMs[index - 1].HasValue
                ? cumulativeGapMs[index - 1]!.Value + gapToPreviousMs.Value
                : null;
        }

        return visibleDrivers.Select(driver =>
        {
            var index = orderedDrivers.IndexOf(driver);
            var gapToPreviousMs = index > 0 ? GetGapToPreviousMs(driver, orderedDrivers[index - 1]) : 0u;
            var startPosition = session.GetDriverRaceStartPosition(driver.PlayerId, driver.Username);
            var delta = startPosition.HasValue && driver.CurrentRacePosition > 0
                ? startPosition.Value - driver.CurrentRacePosition
                : 0;

            return new TvOverlayStandingEntry
            {
                Position = driver.CurrentRacePosition > 0 ? driver.CurrentRacePosition : (byte)(index + 1),
                PlayerId = driver.PlayerId,
                NameHtml = driver.NameHtml,
                CarBadge = BuildCarBadge(driver.CarName),
                MetricText = BuildMetricText(metricMode, driver, index, orderedDrivers, cumulativeGapMs),
                MetaText = BuildMetaText(driver, delta),
                DeltaText = BuildDeltaText(delta),
                IsLeader = index == 0,
                IsInPit = !string.Equals(driver.GetPitStatus(), "Track", StringComparison.OrdinalIgnoreCase),
                IsBattling = gapToPreviousMs.HasValue && gapToPreviousMs.Value > 0 && gapToPreviousMs.Value < 1000,
                IsFocused = index < PinnedEntries
            };
        }).ToList();
    }

    private static IReadOnlyList<Driver> GetVisibleDrivers(IReadOnlyList<Driver> orderedDrivers, uint sessionTimeMs)
    {
        if (orderedDrivers.Count <= MaxVisibleEntries)
        {
            return orderedDrivers;
        }

        var pinned = orderedDrivers.Take(PinnedEntries).ToList();
        var pool = orderedDrivers.Skip(PinnedEntries).ToList();
        var rotatingSlots = MaxVisibleEntries - pinned.Count;
        var pageCount = (int)Math.Ceiling(pool.Count / (double)rotatingSlots);
        var pageIndex = pageCount <= 1 ? 0 : (int)((sessionTimeMs / 1000 / RotationWindowSeconds) % pageCount);
        var page = pool.Skip(pageIndex * rotatingSlots).Take(rotatingSlots).ToList();

        return pinned.Concat(page).ToList();
    }

    private static string BuildWindowLabel(int totalDrivers, int visibleDrivers, uint sessionTimeMs)
    {
        if (totalDrivers <= visibleDrivers)
        {
            return $"FULL FIELD • {totalDrivers}";
        }

        var rotatingSlots = MaxVisibleEntries - PinnedEntries;
        var poolSize = Math.Max(0, totalDrivers - PinnedEntries);
        var pageCount = (int)Math.Ceiling(poolSize / (double)rotatingSlots);
        var pageIndex = (int)((sessionTimeMs / 1000 / RotationWindowSeconds) % Math.Max(1, pageCount));
        return $"FIELD PAGE {pageIndex + 1}/{Math.Max(1, pageCount)} • {totalDrivers} CARS";
    }

    private static string BuildProgressTitle(RaceSession session)
    {
        return session.SessionType == 2 ? "RACE PROGRESS" : "SESSION CLOCK";
    }

    private static string BuildProgressValue(RaceSession session, Driver? leader)
    {
        if (session.SessionType == 2)
        {
            if (session.MaxRaceLaps == 0)
            {
                return $"LAP {(leader?.LapsCompleted ?? 0) + 1}";
            }

            var currentLap = leader == null
                ? 0
                : Math.Min((uint)session.MaxRaceLaps, leader.LapsCompleted >= session.MaxRaceLaps ? leader.LapsCompleted : leader.LapsCompleted + 1);
            return $"LAP {currentLap}/{session.MaxRaceLaps}";
        }

        return FormatClock(session.SessionTimeMs);
    }

    private static string BuildProgressDetail(RaceSession session, int driverCount, uint? estimatedRemainingTimeMs)
    {
        if (session.SessionType == 2)
        {
            if (estimatedRemainingTimeMs.HasValue)
            {
                return $"EST. REMAINING {FormatClock(estimatedRemainingTimeMs.Value)} • {driverCount} DRIVERS";
            }

            return $"{driverCount} DRIVERS • {session.GetWeatherTypeString().ToUpperInvariant()}";
        }

        if (session.QualifyingMins > 0)
        {
            return $"QUALIFYING {session.QualifyingMins} MIN • {driverCount} DRIVERS";
        }

        return $"{driverCount} DRIVERS • BEST LAP ORDER";
    }

    private static double BuildProgressRatio(RaceSession session, Driver? leader)
    {
        if (session.SessionType == 2)
        {
            if (session.MaxRaceLaps == 0 || leader == null)
            {
                return 0;
            }

            return Math.Clamp(leader.LapsCompleted / (double)session.MaxRaceLaps, 0, 1);
        }

        if (session.QualifyingMins <= 0)
        {
            return 0;
        }

        return Math.Clamp(session.SessionTimeMs / (session.QualifyingMins * 60000d), 0, 1);
    }

    private static readonly OverlayMetricMode[] RaceMetricRotation =
    {
        OverlayMetricMode.Gap,
        OverlayMetricMode.Gap,
        OverlayMetricMode.Gap,
        OverlayMetricMode.Delta,
        OverlayMetricMode.LastLap,
        OverlayMetricMode.BestLap,
        OverlayMetricMode.Pits
    };

    private static OverlayMetricMode GetMetricMode(byte sessionType, uint sessionTimeMs)
    {
        if (sessionType == 2)
        {
            var index = (int)((sessionTimeMs / 1000 / RaceMetricCycleSeconds) % RaceMetricRotation.Length);
            return RaceMetricRotation[index];
        }

        return ((sessionTimeMs / 1000 / QualMetricCycleSeconds) % 3) switch
        {
            0 => OverlayMetricMode.Gap,
            1 => OverlayMetricMode.LastLap,
            _ => OverlayMetricMode.BestLap
        };
    }

    private static string GetMetricModeLabel(OverlayMetricMode mode) => mode switch
    {
        OverlayMetricMode.Delta => "GRID DELTA",
        OverlayMetricMode.Pits => "PIT STOPS",
        OverlayMetricMode.LastLap => "LAST LAP",
        OverlayMetricMode.BestLap => "BEST LAP",
        _ => "INTERVAL"
    };

    private static string BuildMetricText(
        OverlayMetricMode metricMode,
        Driver driver,
        int index,
        IReadOnlyList<Driver> orderedDrivers,
        IReadOnlyList<uint?> cumulativeGapMs)
    {
        return metricMode switch
        {
            OverlayMetricMode.Delta => BuildDeltaMetric(driver, orderedDrivers, index),
            OverlayMetricMode.Pits => BuildPitMetric(driver),
            OverlayMetricMode.LastLap => driver.LapHistory.Count > 0 ? FormatLapTime(driver.LapHistory[^1].LapTimeMs) : "-",
            OverlayMetricMode.BestLap => driver.PersonalBestLap != null ? FormatLapTime(driver.PersonalBestLap.GetAdjustedTime()) : "-",
            _ => index == 0 ? "LEADER" : cumulativeGapMs[index].HasValue ? FormatGap(cumulativeGapMs[index]!.Value) : "-"
        };
    }

    private static string BuildDeltaMetric(Driver driver, IReadOnlyList<Driver> orderedDrivers, int index)
    {
        if (index == 0)
        {
            return "P1";
        }

        var gapToPreviousMs = GetGapToPreviousMs(driver, orderedDrivers[index - 1]);
        return gapToPreviousMs.HasValue ? FormatGap(gapToPreviousMs.Value) : "-";
    }

    private static string BuildMetaText(Driver driver, int delta)
    {
        var pitStatus = driver.GetPitStatus();
        var baseText = BuildCarBadge(driver.CarName);

        if (!string.Equals(pitStatus, "Track", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseText} • {pitStatus.ToUpperInvariant()}";
        }

        if (delta != 0)
        {
            return $"{baseText} • GRID {BuildDeltaText(delta)}";
        }

        return baseText;
    }

    private static string BuildDeltaText(int delta)
    {
        if (delta > 0)
        {
            return $"+{delta}";
        }

        if (delta < 0)
        {
            return delta.ToString();
        }

        return "0";
    }

    private static string BuildCarBadge(string? carName)
    {
        if (string.IsNullOrWhiteSpace(carName))
        {
            return "---";
        }

        var trimmed = carName.Trim();
        return trimmed.Length <= 4 ? trimmed.ToUpperInvariant() : trimmed[..4].ToUpperInvariant();
    }

    private static string BuildPitMetric(Driver driver)
    {
        var stops = driver.PitStops;
        var stopText = stops.ToString();

        var pitLaneTimeMs = driver.GetDisplayedPitLaneTimeMs(DateTime.UtcNow);
        if (pitLaneTimeMs.HasValue)
        {
            stopText += $" ({FormatPitStopTime(pitLaneTimeMs.Value)})";
        }

        return stopText;
    }

    private static string FormatPitStopTime(uint pitTimeMs)
    {
        // Display as seconds with one decimal (e.g. 23.4s)
        var seconds = pitTimeMs / 1000d;
        return $"{seconds:0.0}s";
    }

    private static uint? GetGapToPreviousMs(Driver driver, Driver previousDriver)
    {
        var timingPoint = driver.LastTimingPoint;
        if (timingPoint == null)
        {
            return null;
        }

        if (!previousDriver.TryGetTimingPointElapsedTime(timingPoint.LapNumber, timingPoint.TimingPointIndex, out var previousElapsedTimeMs))
        {
            return null;
        }

        return timingPoint.ElapsedTimeMs >= previousElapsedTimeMs
            ? timingPoint.ElapsedTimeMs - previousElapsedTimeMs
            : null;
    }

    private void UpdatePopupState(
        LapData? bestLap,
        string? bestLapAuthorNameHtml,
        string? bestLapAuthorUsername,
        uint? bestLapNumber,
        Dictionary<int, SessionBestSectorInfo> bestSectorInfos)
    {
        if (!_isInitialized)
        {
            // SeedState has already been called earlier in BuildSnapshot.
            return;
        }

        if (bestLap != null && bestLap.LapTimeMs > 0)
        {
            var authorKey = bestLapAuthorUsername ?? string.Empty;
            if (_bestLapTimeMs == 0 || bestLap.LapTimeMs < _bestLapTimeMs || !string.Equals(authorKey, _bestLapAuthorKey, StringComparison.Ordinal))
            {
                _bestLapTimeMs = bestLap.LapTimeMs;
                _bestLapAuthorKey = authorKey;
                EnqueuePopup(
                    "fastest-lap",
                    $"FASTEST LAP • LAP {bestLapNumber ?? 0}",
                    "lap",
                    WebUtility.HtmlEncode(FormatLapTime(bestLap.LapTimeMs)),
                    BuildPopupAuthorHtml(bestLapAuthorUsername, bestLapAuthorNameHtml));
            }
        }

        foreach (var pair in bestSectorInfos)
        {
            if (!_bestSectorTimes.TryGetValue(pair.Key, out var previousTime) || pair.Value.TimeMs < previousTime)
            {
                _bestSectorTimes[pair.Key] = pair.Value.TimeMs;
                EnqueuePopup(
                    $"best-sector-{pair.Key}",
                    $"BEST SECTOR S{pair.Key}",
                    "sector",
                    WebUtility.HtmlEncode(FormatLapTime(pair.Value.TimeMs)),
                    BuildPopupAuthorHtml(pair.Value.AuthorUsername, pair.Value.AuthorNameHtml));
            }
        }
    }

    private void UpdateEventPopups(RaceSession session, List<Driver> orderedDrivers, Driver? leader)
    {
        if (_lastRaceInProgress && !session.RaceInProgress)
        {
            // Race just finished
            var leaderNameHtml = leader?.NameHtml ?? "Unknown";
            EnqueuePopup(
                "leader-finished",
                "LEADER FINISHED",
                "finish",
                leaderNameHtml,
                "Race complete");
        }

        _lastRaceInProgress = session.RaceInProgress;

        if (session.SessionType == 2 && session.RaceInProgress)
        {
            for (var index = 0; index < orderedDrivers.Count; index++)
            {
                var driver = orderedDrivers[index];
                if (_lastDriverOrder.TryGetValue(driver.PlayerId, out var previousIndex))
                {
                    if (index < previousIndex && index < 10)
                    {
                        var gained = previousIndex - index;
                        EnqueuePopup(
                            "overtake",
                            "OVERTAKE",
                            "overtake",
                            driver.NameHtml,
                            gained == 1 ? "+1 position" : $"+{gained} positions");
                    }
                }

                _lastDriverOrder[driver.PlayerId] = index;
            }
        }

        // Pit entry / exit detection
        foreach (var driver in orderedDrivers)
        {
            var isInPit = !string.Equals(driver.GetPitStatus(), "Track", StringComparison.OrdinalIgnoreCase);
            if (_lastPitStatus.TryGetValue(driver.PlayerId, out var wasInPit))
            {
                if (!wasInPit && isInPit)
                {
                    EnqueuePopup(
                        "pit-entry",
                        "PIT ENTRY",
                        "pit",
                        driver.NameHtml,
                        "Entering pit lane");
                }
                else if (wasInPit && !isInPit)
                {
                    EnqueuePopup(
                        "pit-exit",
                        "PIT EXIT",
                        "pit",
                        driver.NameHtml,
                        "Exiting pit lane");
                }
            }

            _lastPitStatus[driver.PlayerId] = isInPit;
        }

        // Cleanup removed drivers from tracking dictionaries
        var currentIds = orderedDrivers.Select(d => d.PlayerId).ToHashSet();
        foreach (var playerId in _lastDriverOrder.Keys.Except(currentIds).ToList())
        {
            _lastDriverOrder.Remove(playerId);
            _lastPitStatus.Remove(playerId);
        }
    }

    private static string BuildPopupAuthorHtml(string? username, string? nameHtml)
    {
        if (!string.IsNullOrWhiteSpace(nameHtml))
        {
            return nameHtml;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return WebUtility.HtmlEncode(username.Trim());
        }

        return "Unknown";
    }

    private void SeedState(RaceSession session, List<Driver> orderedDrivers, string? bestLapAuthorUsername, Dictionary<int, SessionBestSectorInfo> bestSectorInfos)
    {
        _isInitialized = true;
        _bestLapTimeMs = session.SessionBestLap?.LapTimeMs ?? 0;
        _bestLapAuthorKey = bestLapAuthorUsername ?? string.Empty;
        _bestSectorTimes.Clear();

        foreach (var pair in bestSectorInfos)
        {
            _bestSectorTimes[pair.Key] = pair.Value.TimeMs;
        }

        _lastDriverOrder.Clear();
        _lastPitStatus.Clear();
        _lastRaceInProgress = session.RaceInProgress;

        for (var index = 0; index < orderedDrivers.Count; index++)
        {
            var driver = orderedDrivers[index];
            _lastDriverOrder[driver.PlayerId] = index;
            _lastPitStatus[driver.PlayerId] = !string.Equals(driver.GetPitStatus(), "Track", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void EnqueuePopup(string idPrefix, string title, string accentClass, string subjectHtml, string detailText)
    {
        _popupSequence++;
        _activePopups.Add(new OverlayPopupState
        {
            Id = $"{idPrefix}-{_popupSequence}",
            Kind = idPrefix,
            Title = title,
            AccentClass = accentClass,
            SubjectHtml = subjectHtml,
            DetailHtml = detailText,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(PopupLifetimeSeconds)
        });
    }

    private void PruneExpiredPopups()
    {
        var now = DateTime.UtcNow;
        _activePopups.RemoveAll(popup => popup.ExpiresAtUtc <= now);
        if (_activePopups.Count > MaxPopupCount)
        {
            _activePopups.RemoveRange(0, _activePopups.Count - MaxPopupCount);
        }
    }

    private static string FormatClock(uint totalMs)
    {
        var span = TimeSpan.FromMilliseconds(totalMs);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private static string FormatLapTime(uint lapTimeMs)
    {
        var minutes = lapTimeMs / 60000;
        var seconds = (lapTimeMs % 60000) / 1000;
        var milliseconds = lapTimeMs % 1000;
        return minutes > 0
            ? $"{minutes}:{seconds:00}.{milliseconds:000}"
            : $"{seconds}.{milliseconds:000}";
    }

    private static string FormatGap(uint gapMs)
    {
        return gapMs >= 60000
            ? FormatLapTime(gapMs)
            : $"+{gapMs / 1000d:0.000}";
    }

    private enum OverlayMetricMode
    {
        Gap,
        Delta,
        Pits,
        LastLap,
        BestLap
    }

    private sealed class OverlayPopupState
    {
        public string Id { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string AccentClass { get; init; } = string.Empty;
        public string SubjectHtml { get; init; } = string.Empty;
        public string DetailHtml { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }
}