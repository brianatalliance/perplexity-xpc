using PerplexityXPC.RemoteGateway.Configuration;
using PerplexityXPC.RemoteGateway.Middleware;
using PerplexityXPC.RemoteGateway.Routes;
using PerplexityXPC.RemoteGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "PERPLEXITYXPC_REMOTE_");

builder.Services.Configure<RemoteConfig>(
    builder.Configuration.GetSection("RemoteGateway"));

var remoteConfig = builder.Configuration
    .GetSection("RemoteGateway")
    .Get<RemoteConfig>() ?? new RemoteConfig();

// -----------------------------------------------------------------------
// Kestrel - listen only on loopback so Cloudflare Tunnel is the only
// external entry point.
// -----------------------------------------------------------------------
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(47778);
});

// -----------------------------------------------------------------------
// Services
// -----------------------------------------------------------------------
builder.Services.AddSingleton<RemoteConfig>(_ => remoteConfig);
builder.Services.AddSingleton<CommandExecutor>();
builder.Services.AddSingleton<SystemMonitor>();
builder.Services.AddSingleton<FileManager>();
builder.Services.AddHttpClient<BrokerProxy>();

// -----------------------------------------------------------------------
// CORS - Cloudflare handles actual authentication; allow all origins
// here so the tunnel can forward requests freely.
// -----------------------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

var app = builder.Build();

// -----------------------------------------------------------------------
// Middleware pipeline
// -----------------------------------------------------------------------
app.UseCors();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<ApiTokenAuthMiddleware>();

// -----------------------------------------------------------------------
// Routes
// -----------------------------------------------------------------------
RemoteApiRoutes.MapRoutes(app);

app.Run();
