namespace LfsPitWall.Server.Services;

public sealed class PubstatOptions
{
    public const string SectionName = "Pubstat";

    public bool Enabled { get; set; } = true;
    public string IdentKey { get; set; } = "";
    public bool UsePremiumEndpoint { get; set; }
    public string PubstatUrl { get; set; } = "https://www.lfsworld.net/pubstat/get_stat2.php?version=1.5";
    public string CacheRootPath { get; set; } = "data/drivers";
    public int StaleAfterDays { get; set; } = 7;
    public int RequestIntervalSeconds { get; set; } = 6;

    public bool IsConfigured()
    {
        return Enabled
            && !string.IsNullOrWhiteSpace(IdentKey)
            && !string.IsNullOrWhiteSpace(PubstatUrl);
    }

    public int GetClampedStaleAfterDays() => Math.Clamp(StaleAfterDays, 1, 30);

    public int GetClampedRequestIntervalSeconds()
    {
        return UsePremiumEndpoint
            ? Math.Clamp(RequestIntervalSeconds, 1, 300)
            : Math.Clamp(RequestIntervalSeconds, 5, 300);
    }
}