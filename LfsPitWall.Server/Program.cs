using LfsPitWall.Server.Hubs;
using LfsPitWall.Server.Models;
using LfsPitWall.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add race session singleton
builder.Services.AddSingleton<RaceSession>();

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

// Default route to index.html
app.MapFallbackToFile("index.html");

app.Run();
