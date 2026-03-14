using LfsPitWall.Server.Hubs;
using LfsPitWall.Server.Helpers;
using LfsPitWall.Server.Models;
using LfsPitWall.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<UiOptions>()
    .Bind(builder.Configuration.GetSection(UiOptions.SectionName));

var showDebugConsole = builder.Configuration.GetValue<bool?>($"{UiOptions.SectionName}:ShowDebugConsole") ?? true;
var appMetadata = AppMetadataProvider.Get(showDebugConsole);

// Add race session singleton
builder.Services.AddSingleton<RaceSession>();
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
app.MapGet("/setup-editor", () => Results.Redirect("/setup-editor.html"));

// Default route to index.html
app.MapFallbackToFile("index.html");

app.Run();
