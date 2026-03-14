namespace LfsPitWall.Server.Services;

public class PlayerOnboardingOptions
{
    public const string SectionName = "PlayerOnboarding";

    public bool Enabled { get; set; } = true;
    public string PublicUrl { get; set; } = "";

    public string GetNormalizedPublicUrl()
    {
        return string.IsNullOrWhiteSpace(PublicUrl)
            ? ""
            : PublicUrl.Trim();
    }
}