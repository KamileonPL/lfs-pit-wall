using System.Reflection;

namespace LfsPitWall.Server.Helpers;

public sealed record AppMetadata(
    string Name,
    string Version,
    string RepositoryUrl,
    string DiscordUrl,
    string Author,
    bool IsOpenSource,
    string ProjectType,
    string DataSourceName,
    string DataSourceUrl,
    bool ShowDebugConsole);

public static class AppMetadataProvider
{
    private const string DefaultRepositoryUrl = "https://github.com/KamileonPL/lfs-pit-wall";
    private const string DefaultDiscordUrl = "https://discord.gg/d68BEY6";
    private const string DefaultVersion = "0.3";
    private const string DefaultAppName = "LFS Pit Wall";
    private const string DefaultAuthor = "Kamileon";
    private const string DefaultProjectType = "ASP.NET Core web app with an HTML/JavaScript frontend";
    private const string DefaultDataSourceName = "Live for Speed";
    private const string DefaultDataSourceUrl = "https://www.lfs.net";

    public static AppMetadata Get(bool showDebugConsole = true)
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
            IsOpenSource: true,
            ProjectType: DefaultProjectType,
            DataSourceName: DefaultDataSourceName,
            DataSourceUrl: DefaultDataSourceUrl,
            ShowDebugConsole: showDebugConsole);
    }

    private static string? GetAssemblyMetadataValue(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))?
            .Value;
    }
}
