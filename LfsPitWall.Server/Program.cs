using LfsPitWall.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add InSim service
builder.Services.AddHostedService<InSimService>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
