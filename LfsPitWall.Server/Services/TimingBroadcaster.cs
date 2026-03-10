using LfsPitWall.Server.Hubs;
using LfsPitWall.Server.Models;
using Microsoft.AspNetCore.SignalR;

namespace LfsPitWall.Server.Services;

/// <summary>
/// Background service that broadcasts timing updates to connected SignalR clients
/// Matches the LFS NLP packet frequency (200ms)
/// </summary>
public class TimingBroadcaster : BackgroundService
{
    private readonly IHubContext<TimingHub> _hubContext;
    private readonly RaceSession _raceSession;
    private readonly ILogger<TimingBroadcaster> _logger;

    private const int BroadcastIntervalMs = 200; // Match LFS NLP frequency

    public TimingBroadcaster(
        IHubContext<TimingHub> hubContext,
        RaceSession raceSession,
        ILogger<TimingBroadcaster> logger)
    {
        _hubContext = hubContext;
        _raceSession = raceSession;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timing broadcaster started");

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(BroadcastIntervalMs));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await BroadcastSessionStateAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Timing broadcaster stopped");
        }
    }

    /// <summary>
    /// Broadcasts current session state to all connected SignalR clients
    /// </summary>
    private async Task BroadcastSessionStateAsync()
    {
        try
        {
            var sessionData = new
            {
                trackName = _raceSession.TrackName,
                sessionType = _raceSession.GetSessionTypeString(),
                weatherType = _raceSession.GetWeatherTypeString(),
                raceInProgress = _raceSession.RaceInProgress,
                sessionTimeMs = _raceSession.SessionTimeMs,
                maxRaceLaps = _raceSession.MaxRaceLaps,
                qualifyingMins = _raceSession.QualifyingMins,
                players = _raceSession.GetDriversSortedByBestLap().Select(d => new
                {
                    playerId = d.PlayerId,
                    name = d.Name,
                    nameHtml = string.IsNullOrEmpty(d.Username) 
                        ? d.NameHtml 
                        : $"{d.NameHtml} <span style=\"color:#AAAAAA\">({d.Username})</span>",
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
                        kvp => kvp.Key,  // Keep as integer key for JSON serialization
                        kvp => kvp.Value.TimeMs
                    )
                }).ToList(),
                sessionBestLapMs = _raceSession.SessionBestLap?.LapTimeMs ?? 0,
                sessionBestLapAuthorName = _raceSession.SessionBestLapAuthorPLID.HasValue 
                    ? _raceSession.GetDriver(_raceSession.SessionBestLapAuthorPLID.Value)?.Name ?? "Unknown"
                    : null,
                sessionBestLapAuthorUsername = _raceSession.SessionBestLapAuthorPLID.HasValue
                    ? _raceSession.GetDriver(_raceSession.SessionBestLapAuthorPLID.Value)?.Username ?? ""
                    : null,
                sessionBestLapNumber = _raceSession.SessionBestLapNumber,
                sessionBestSectors = _raceSession.SessionBestSectors,
                packetType = "SESSION_UPDATE",
                updatedAt = DateTime.UtcNow.ToString("O")
            };

            await _hubContext.Clients.All.SendAsync("ReceiveSessionUpdate", sessionData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting session state");
        }
    }
}
