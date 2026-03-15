using LfsPitWall.Server.Hubs;
using LfsPitWall.Server.Helpers;
using LfsPitWall.Server.Models;
using LfsPitWall.Server.Models.Archive;
using LfsPitWall.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<UiOptions>()
    .Bind(builder.Configuration.GetSection(UiOptions.SectionName));

var showDebugConsole = builder.Configuration.GetValue<bool?>($"{UiOptions.SectionName}:ShowDebugConsole") ?? true;
var appMetadata = AppMetadataProvider.Get(showDebugConsole);

// Add race session singleton
builder.Services.AddSingleton<RaceSession>();
builder.Services.AddSingleton<SessionArchiveWriter>();
builder.Services.AddSingleton<ArchiveBrowserService>();
builder.Services.AddSingleton<SessionLifecycleManager>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Add SignalR
builder.Services.AddSignalR();

builder.Services
    .AddOptions<TelemetryOptions>()
    .Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName));

builder.Services
    .AddOptions<PlayerOnboardingOptions>()
    .Bind(builder.Configuration.GetSection(PlayerOnboardingOptions.SectionName));

builder.Services
    .AddOptions<ChampionshipScoringOptions>()
    .Bind(builder.Configuration.GetSection(ChampionshipScoringOptions.SectionName))
    .Validate(options => options.HasValidConfiguration(), "Championship scoring configuration must define at least one non-negative finishing score and non-negative bonuses.")
    .ValidateOnStart();

builder.Services
    .AddOptions<ArchiveOptions>()
    .Bind(builder.Configuration.GetSection(ArchiveOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Archive root path must not be empty.")
    .ValidateOnStart();

builder.Services
    .AddOptions<PubstatOptions>()
    .Bind(builder.Configuration.GetSection(PubstatOptions.SectionName));

builder.Services.AddHttpClient();
builder.Services.AddSingleton<DriverProfileService>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<DriverProfileService>());

// Add InSim service
builder.Services.AddHostedService<InSimService>();

// Add timing broadcaster
builder.Services.AddHostedService<TimingBroadcaster>();

var app = builder.Build();

// Enable CORS
app.UseCors();

// Enable static files
app.UseDefaultFiles();
app.UseStaticFiles();

// Map SignalR hub
app.MapHub<TimingHub>("/hubs/timing");

app.MapGet("/api/app-meta", () => Results.Ok(appMetadata));
app.MapGet(
    "/api/archive/sessions",
    (ArchiveBrowserService archiveBrowserService, string? track, string? sessionType, string? search, int? page, int? pageSize) =>
        Results.Ok(archiveBrowserService.GetSessions(track, sessionType, search, page ?? 1, pageSize ?? 24)));
app.MapGet(
    "/api/archive/sessions/{sessionId}",
    (ArchiveBrowserService archiveBrowserService, string sessionId) =>
    {
        var session = archiveBrowserService.GetSession(sessionId);
        return session == null ? Results.NotFound() : Results.Ok(session);
    });
app.MapGet("/archive-results", () => Results.Redirect("/archive-results.html"));
app.MapGet("/setup-editor", () => Results.Redirect("/setup-editor.html"));

// Default route to index.html
app.MapFallbackToFile("index.html");

app.Run();
