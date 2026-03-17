namespace LfsPitWall.Server.Models;

public sealed class TvOverlaySnapshot
{
    public string PacketType { get; init; } = "TV_OVERLAY_UPDATE";
    public string Theme { get; init; } = "race";
    public string TrackName { get; init; } = "Unknown";
    public string SessionType { get; init; } = "Race";
    public string HostName { get; init; } = string.Empty;
    public string ProgressTitle { get; init; } = string.Empty;
    public string ProgressValue { get; init; } = string.Empty;
    public string ProgressDetail { get; init; } = string.Empty;
    public double ProgressRatio { get; init; }
    public string RotationLabel { get; init; } = string.Empty;
    public string StandingsWindowLabel { get; init; } = string.Empty;
    public List<TvOverlayStandingEntry> Entries { get; init; } = new();
    public List<TvOverlayPopup> Popups { get; init; } = new();
    public string UpdatedAt { get; init; } = string.Empty;
}

public sealed class TvOverlayStandingEntry
{
    public int Position { get; init; }
    public byte PlayerId { get; init; }
    public string NameHtml { get; init; } = string.Empty;
    public string CarBadge { get; init; } = string.Empty;
    public string MetricText { get; init; } = "-";
    public string MetaText { get; init; } = string.Empty;
    public string DeltaText { get; init; } = string.Empty;
    public bool IsLeader { get; init; }
    public bool IsInPit { get; init; }
    public bool IsBattling { get; init; }
    public bool IsFocused { get; init; }
}

public sealed class TvOverlayPopup
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string AccentClass { get; init; } = string.Empty;
    public string SubjectHtml { get; init; } = string.Empty;
    public string DetailHtml { get; init; } = string.Empty;
}