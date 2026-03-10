using LfsPitWall.Server.Helpers;
using LfsPitWall.Server.Hubs;
using LfsPitWall.Server.Models;
using Microsoft.AspNetCore.SignalR;

namespace LfsPitWall.Server.Services;

/// <summary>
/// Background service that broadcasts timing updates to connected SignalR clients.
/// Matches the LFS NLP packet frequency (200ms).
/// </summary>
public class TimingBroadcaster : BackgroundService
{
    private readonly IHubContext<TimingHub> _hubContext;
    private readonly RaceSession _raceSession;
    private readonly ILogger<TimingBroadcaster> _logger;

    private const int BroadcastIntervalMs = 200;

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
                try
                {
                    var sessionData = SessionDataBuilder.Build(_raceSession);
                    await _hubContext.Clients.All.SendAsync("ReceiveSessionUpdate", sessionData, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting session state");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Timing broadcaster stopped");
        }
    }
}
