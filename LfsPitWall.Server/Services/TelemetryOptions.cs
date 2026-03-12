namespace LfsPitWall.Server.Services;

public class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public const int MinimumBroadcastIntervalMs = 50;
    public const int MaximumBroadcastIntervalMs = 1000;

    public int BroadcastIntervalMs { get; set; } = 200;

    public int GetClampedBroadcastIntervalMs()
    {
        return Math.Clamp(BroadcastIntervalMs, MinimumBroadcastIntervalMs, MaximumBroadcastIntervalMs);
    }
}