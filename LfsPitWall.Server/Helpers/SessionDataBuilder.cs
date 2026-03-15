using LfsPitWall.Server.Models;
using LfsPitWall.Server.Services;

namespace LfsPitWall.Server.Helpers;

/// <summary>
/// Builds the session data projection shared by TimingHub and TimingBroadcaster.
/// Single source of truth for the JSON shape sent to frontend clients.
/// </summary>
public static class SessionDataBuilder
{
    private static readonly TimeSpan MapTelemetryFreshnessWindow = TimeSpan.FromSeconds(4);

    public static object Build(RaceSession session, DriverProfileService? driverProfileService = null)
    {
        var snapshotTimeUtc = DateTime.UtcNow;
        var (authorNameHtml, authorUsername, bestLapNumber) = session.GetSessionBestLapInfo();
        var sessionBestLap = session.SessionBestLap;
        var sessionBestSectorInfos = session.GetSessionBestSectorInfos();
        var trackMap = session.GetTrackMapSnapshot();
        var orderedDrivers = session.GetDriversForStandings().ToList();
        var chatMessages = session.GetChatMessages();
        var estimatedRemainingTimeMs = session.GetEstimatedRemainingTimeMs();
        var sessionTopSpeedDriver = orderedDrivers
            .Where(driver => driver.TopSpeedKmh > 0)
            .OrderByDescending(driver => driver.TopSpeedKmh)
            .FirstOrDefault();
        
        return new
        {
            trackName = session.TrackName,
            hostName = session.HostName,
            hostNameHtml = session.HostNameHtml,
            sessionType = session.GetSessionTypeString(),
            weatherType = session.GetWeatherTypeString(),
            windType = session.GetWindTypeString(),
            raceInProgress = session.RaceInProgress,
            sessionTimeMs = session.SessionTimeMs,
            estimatedRemainingTimeMs,
            estimatedRemainingReferenceSessionMs = estimatedRemainingTimeMs.HasValue ? (uint?)session.SessionTimeMs : null,
            maxRaceLaps = session.MaxRaceLaps,
            qualifyingMins = session.QualifyingMins,
            activeSectorCount = session.ActiveSectorCount,
            trackMap = new
            {
                revision = trackMap.Revision,
                minX = trackMap.MinX,
                maxX = trackMap.MaxX,
                minY = trackMap.MinY,
                maxY = trackMap.MaxY,
                points = trackMap.Points.Select(point => new
                {
                    node = point.Node,
                    x = point.X,
                    y = point.Y
                }).ToList()
            },
            chatRevision = session.ChatRevision,
            chatMessages = chatMessages.Select(message => new
            {
                kind = message.Kind,
                messageText = message.MessageText,
                messageHtml = LfsColorConverter.ConvertToHtml(message.MessageLfsText),
                receivedAtUtc = message.ReceivedAtUtc.ToString("O")
            }).ToList(),
            players = orderedDrivers.Select((d, index) =>
            {
                var driverProfileSummary = driverProfileService?.GetDriverSummary(d.Username) ?? DriverProfileSummary.Empty;
                var personalBestLap = d.PersonalBestLap;
                var currentLap = d.LapHistory.Count > 0 ? d.LapHistory[^1] : null;
                uint? gapToPreviousMs = session.SessionType == 2 && index > 0
                    ? GetGapToPreviousMs(d, orderedDrivers[index - 1])
                    : null;
                var hasFreshWorldPosition = d.HasFreshWorldPosition(snapshotTimeUtc, MapTelemetryFreshnessWindow);

                return new
                {
                    playerId = d.PlayerId,
                    name = d.Name,
                    username = d.Username,
                    mapLabelHtml = d.NameHtml,
                    nameHtml = string.IsNullOrEmpty(d.Username)
                        ? d.NameHtml
                        : $"{d.NameHtml} <span style=\"color:#AAAAAA\">({System.Net.WebUtility.HtmlEncode(d.Username)})</span>",
                    countryName = driverProfileSummary.CountryName,
                    countryCode = driverProfileSummary.CountryCode,
                    driverProfilePending = driverProfileSummary.IsRefreshQueued,
                    carName = d.CarName,
                    driverColor = d.DriverColor,
                    currentRacePosition = d.CurrentRacePosition,
                    lapsCompleted = d.LapsCompleted,
                    personalBestLapMs = personalBestLap?.LapTimeMs ?? 0,
                    lastLapNumber = currentLap?.LapNumber ?? 0,
                    lastLapTimeMs = currentLap?.LapTimeMs ?? 0,
                    topSpeedKmh = Math.Round(d.TopSpeedKmh, 1),
                    gapToPreviousMs,
                    personalBestSectors = d.PersonalBestSectors,
                    tyreTypes = d.TyreTypes.Select(t => (int)t).ToArray(),
                    hasWorldPosition = hasFreshWorldPosition,
                    mapX = hasFreshWorldPosition ? d.WorldX : (int?)null,
                    mapY = hasFreshWorldPosition ? d.WorldY : (int?)null,
                    heading = hasFreshWorldPosition ? d.CurrentHeading : (ushort?)null,
                    pitStops = d.PitStops,
                    pitStatus = d.GetPitStatus(),
                    pitLaneTimeMs = d.GetDisplayedPitLaneTimeMs(snapshotTimeUtc),
                    lastPitStopTimeMs = d.LastPitStopTimeMs,
                    currentSectorProgress = d.GetCurrentSectorProgress().ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.TimeMs
                    )
                };
            }).ToList(),
            sessionBestLapMs = sessionBestLap?.LapTimeMs ?? 0,
            sessionBestLapAuthorName = authorNameHtml,
            sessionBestLapAuthorUsername = authorUsername,
            sessionBestLapNumber = bestLapNumber,
            sessionTopSpeedKmh = sessionTopSpeedDriver != null ? Math.Round(sessionTopSpeedDriver.TopSpeedKmh, 1) : 0,
            sessionTopSpeedAuthorName = sessionTopSpeedDriver?.NameHtml ?? "",
            sessionTopSpeedAuthorUsername = sessionTopSpeedDriver?.Username ?? "",
            sessionBestSectors = session.SessionBestSectors,
            sessionBestSectorInfos = sessionBestSectorInfos.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    timeMs = kvp.Value.TimeMs,
                    authorNameHtml = kvp.Value.AuthorNameHtml,
                    authorUsername = kvp.Value.AuthorUsername
                }),
            packetType = "SESSION_UPDATE",
            updatedAt = DateTime.UtcNow.ToString("O")
        };
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
}
