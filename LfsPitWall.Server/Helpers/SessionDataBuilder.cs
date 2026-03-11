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
        
        return new
        {
            trackName = session.TrackName,
            sessionType = session.GetSessionTypeString(),
            weatherType = session.GetWeatherTypeString(),
            raceInProgress = session.RaceInProgress,
            sessionTimeMs = session.SessionTimeMs,
            maxRaceLaps = session.MaxRaceLaps,
            qualifyingMins = session.QualifyingMins,
            players = session.GetDriversSortedByBestLap().Select(d => new
            {
                playerId = d.PlayerId,
                name = d.Name,
                nameHtml = string.IsNullOrEmpty(d.Username)
                    ? d.NameHtml
                    : $"{d.NameHtml} <span style=\"color:#AAAAAA\">({System.Net.WebUtility.HtmlEncode(d.Username)})</span>",
                carName = d.CarName,
                driverColor = d.DriverColor,
                lapsCompleted = d.LapsCompleted,
                personalBestLapMs = d.PersonalBestLap?.LapTimeMs ?? 0,
                lastElapsedTimeMs = d.LastElapsedTimeMs,
                currentLapNumber = d.LapHistory.Count > 0 ? d.LapHistory.Last().LapNumber : 0,
                currentLapTimeMs = d.LapHistory.Count > 0 ? d.LapHistory.Last().LapTimeMs : 0,
                personalBestSectors = d.PersonalBestSectors,
                fuelPercent = d.FuelPercent,
                pitStops = d.PitStops,
                currentSectorProgress = d.GetCurrentSectorProgress().ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.TimeMs
                )
            }).ToList(),
            sessionBestLapMs = session.SessionBestLap?.LapTimeMs ?? 0,
            sessionBestLapAuthorName = authorNameHtml,
            sessionBestLapAuthorUsername = authorUsername,
            sessionBestLapNumber = bestLapNumber,
            sessionBestSectors = session.SessionBestSectors,
            packetType = "SESSION_UPDATE",
            updatedAt = DateTime.UtcNow.ToString("O")
        };
    }
}
