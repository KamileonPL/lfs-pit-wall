using LfsPitWall.Server.Models;

namespace LfsPitWall.Server.Helpers;

/// <summary>
/// Builds the session data projection shared by TimingHub and TimingBroadcaster.
/// Single source of truth for the JSON shape sent to frontend clients.
/// </summary>
public static class SessionDataBuilder
{
    public static object Build(RaceSession session)
    {
        var (authorNameHtml, authorUsername, bestLapNumber) = session.GetSessionBestLapInfo();
        var sessionBestLap = session.SessionBestLap;
        var sessionBestSectorInfos = session.GetSessionBestSectorInfos();
        
        return new
        {
            trackName = session.TrackName,
            sessionType = session.GetSessionTypeString(),
            weatherType = session.GetWeatherTypeString(),
            raceInProgress = session.RaceInProgress,
            sessionTimeMs = session.SessionTimeMs,
            maxRaceLaps = session.MaxRaceLaps,
            qualifyingMins = session.QualifyingMins,
            activeSectorCount = session.ActiveSectorCount,
            players = session.GetDriversForStandings().Select(d =>
            {
                var personalBestLap = d.PersonalBestLap;
                var currentLap = d.LapHistory.Count > 0 ? d.LapHistory[^1] : null;

                return new
                {
                    playerId = d.PlayerId,
                    name = d.Name,
                    nameHtml = string.IsNullOrEmpty(d.Username)
                        ? d.NameHtml
                        : $"{d.NameHtml} <span style=\"color:#AAAAAA\">({System.Net.WebUtility.HtmlEncode(d.Username)})</span>",
                    carName = d.CarName,
                    driverColor = d.DriverColor,
                    lapsCompleted = d.LapsCompleted,
                    personalBestLapMs = personalBestLap?.LapTimeMs ?? 0,
                    lastElapsedTimeMs = d.LastElapsedTimeMs,
                    lastLapNumber = currentLap?.LapNumber ?? 0,
                    lastLapTimeMs = currentLap?.LapTimeMs ?? 0,
                    personalBestSectors = d.PersonalBestSectors,
                    fuelPercent = d.FuelPercent,
                    pitStops = d.PitStops,
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
}
