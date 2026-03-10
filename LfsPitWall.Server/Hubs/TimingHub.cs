using LfsPitWall.Server.Helpers;
using LfsPitWall.Server.Models;
using Microsoft.AspNetCore.SignalR;

namespace LfsPitWall.Server.Hubs;

/// <summary>
/// SignalR hub for real-time live timing updates.
/// Sends initial session state on connect; periodic updates are handled by TimingBroadcaster.
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

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);

        // Send current state to the newly connected client immediately
        var sessionData = SessionDataBuilder.Build(_raceSession);
        await Clients.Caller.SendAsync("ReceiveSessionUpdate", sessionData);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);

        if (exception != null)
            _logger.LogError(exception, "Client disconnection error");

        await base.OnDisconnectedAsync(exception);
    }

    public async Task RequestFullUpdate()
    {
        _logger.LogDebug("Client {ConnectionId} requested full update", Context.ConnectionId);
        var sessionData = SessionDataBuilder.Build(_raceSession);
        await Clients.Caller.SendAsync("ReceiveSessionUpdate", sessionData);
    }
}
