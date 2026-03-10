using LfsPitWall.Server.Models;
using Microsoft.AspNetCore.SignalR;

namespace LfsPitWall.Server.Hubs;

/// <summary>
/// SignalR hub for real-time live timing updates
/// </summary>
public class TimingHub : Hub
{
    private readonly RaceSession _raceSession;
    private readonly ILogger<TimingHub> _logger;

    public TimingHub(RaceSession raceSession, ILogger<TimingHub> logger)
    {
        _raceSession = raceSession;
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects to the hub
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        
        // Send current session state to the newly connected client
        await SendSessionUpdate();
        
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        
        if (exception != null)
            _logger.LogError(exception, "Client disconnection error");

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Sends the current race session state to all connected clients
    /// </summary>
    public async Task SendSessionUpdate()
    {
        var sessionData = new
        {
            trackName = _raceSession.TrackName,
            sessionType = _raceSession.GetSessionTypeString(),
            weatherType = _raceSession.GetWeatherTypeString(),
            raceInProgress = _raceSession.RaceInProgress,
            sessionTimeMs = _raceSession.SessionTimeMs,
            players = _raceSession.GetDriversSortedByBestLap().Select(d => new
            {
                playerId = d.PlayerId,
                name = d.Name,
                carName = d.CarName,
                lapsCompleted = d.LapsCompleted,
                personalBestLapTime = d.PersonalBestLap?.LapTimeMs,
                currentLapNumber = d.LapHistory.Count > 0 ? d.LapHistory.Last().LapNumber : 0,
                personalBestSectors = d.PersonalBestSectors,
                fuelPercent = d.FuelPercent,
                pitStops = d.PitStops,
                currentSectorProgress = d.GetCurrentSectorProgress().ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value.TimeMs
                )
            }).ToList(),
            sessionBestLapTime = _raceSession.SessionBestLap?.LapTimeMs,
            sessionBestSectors = _raceSession.SessionBestSectors
        };

        // Send to all connected clients
        await Clients.All.SendAsync("ReceiveSessionUpdate", sessionData);
    }

    /// <summary>
    /// Requests full session data dump from a client
    /// Used for dashboard initialization
    /// </summary>
    public async Task RequestFullUpdate()
    {
        _logger.LogDebug("Client {ConnectionId} requested full update", Context.ConnectionId);
        await SendSessionUpdate();
    }
}
