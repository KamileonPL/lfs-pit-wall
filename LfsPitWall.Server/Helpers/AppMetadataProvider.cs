using System.Reflection;

namespace LfsPitWall.Server.Helpers;

public sealed record AppMetadata(
    string Name,
    string Version,
    string RepositoryUrl,
    string DiscordUrl,
    string Author,
    bool IsOpenSource);

public static class AppMetadataProvider
{
    private const string DefaultRepositoryUrl = "https://github.com/KamileonPL/lfs-pit-wall";
    private const string DefaultDiscordUrl = "https://discord.gg/d68BEY6";
    private const string DefaultVersion = "0.1";
    private const string DefaultAppName = "LFS Pit Wall";
    private const string DefaultAuthor = "Kamileon";

    public static AppMetadata Get()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var normalizedVersion = string.IsNullOrWhiteSpace(informationalVersion)
            ? DefaultVersion
            : informationalVersion.Split('+')[0];

        return new AppMetadata(
            Name: DefaultAppName,
            Version: $"v{normalizedVersion}",
            RepositoryUrl: GetAssemblyMetadataValue(assembly, "RepositoryUrl") ?? DefaultRepositoryUrl,
            DiscordUrl: DefaultDiscordUrl,
            Author: DefaultAuthor,
            IsOpenSource: true);
    }

    private static string? GetAssemblyMetadataValue(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))?
            .Value;
    }
}
