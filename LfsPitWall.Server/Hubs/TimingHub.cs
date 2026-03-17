using LfsPitWall.Server.Helpers;
using LfsPitWall.Server.Models;
using LfsPitWall.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace LfsPitWall.Server.Hubs;

/// <summary>
/// SignalR hub for real-time live timing updates.
/// Sends initial session state on connect; periodic updates are handled by TimingBroadcaster.
/// </summary>
public class TimingHub : Hub
{
    private readonly RaceSession _raceSession;
    private readonly DriverProfileService _driverProfileService;
    private readonly TvOverlayDirector _tvOverlayDirector;
    private readonly ILogger<TimingHub> _logger;

    public TimingHub(RaceSession raceSession, DriverProfileService driverProfileService, TvOverlayDirector tvOverlayDirector, ILogger<TimingHub> logger)
    {
        _raceSession = raceSession;
        _driverProfileService = driverProfileService;
        _tvOverlayDirector = tvOverlayDirector;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);

        // Send current state to the newly connected client immediately
        var sessionData = SessionDataBuilder.Build(_raceSession, _driverProfileService);
        await Clients.Caller.SendAsync("ReceiveSessionUpdate", sessionData);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        _tvOverlayDirector.UnregisterSubscriber(Context.ConnectionId);

        if (exception != null)
            _logger.LogError(exception, "Client disconnection error");

        await base.OnDisconnectedAsync(exception);
    }

    public async Task RequestFullUpdate()
    {
        _logger.LogDebug("Client {ConnectionId} requested full update", Context.ConnectionId);
        var sessionData = SessionDataBuilder.Build(_raceSession, _driverProfileService);
        await Clients.Caller.SendAsync("ReceiveSessionUpdate", sessionData);
    }

    public async Task JoinTvOverlay()
    {
        _tvOverlayDirector.RegisterSubscriber(Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, TvOverlayDirector.HubGroupName);
        await Clients.Caller.SendAsync("ReceiveTvOverlayUpdate", _tvOverlayDirector.BuildSnapshot(_raceSession));
    }

    public Task<DriverProfileSnapshot> GetDriverProfile(byte playerId)
    {
        var driverIdentity = _raceSession.GetDriverSnapshot(playerId, driver => new
        {
            driver.PlayerId,
            driver.Username,
            driver.NameHtml,
            driver.CarName
        });

        if (driverIdentity == null)
        {
            return Task.FromResult(new DriverProfileSnapshot
            {
                PlayerId = playerId,
                UnavailableReason = "Driver is no longer present in the current session."
            });
        }

        return Task.FromResult(_driverProfileService.GetDriverProfile(
            driverIdentity.PlayerId,
            driverIdentity.Username,
            driverIdentity.NameHtml,
            driverIdentity.CarName));
    }

    public Task<object> GetDriverLapHistory(byte playerId)
    {
        var lapHistory = _raceSession.GetDriverSnapshot(playerId, driver => (object)new
        {
            playerId = driver.PlayerId,
            laps = driver.LapHistory
                .OrderByDescending(lap => lap.LapNumber)
                .Select(lap => new
                {
                    lapNumber = lap.LapNumber,
                    lapTimeMs = lap.LapTimeMs,
                    isValid = lap.IsValid
                })
                .ToList()
        });

        return Task.FromResult(lapHistory ?? new
        {
            playerId,
            laps = Array.Empty<object>()
        } as object);
    }
}
