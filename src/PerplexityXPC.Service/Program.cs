using System.Net;
using System.Runtime.InteropServices;
using PerplexityXPC.Service.Configuration;
using PerplexityXPC.Service.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// ---------------------------------------------------------------------------
// PerplexityXPC Windows Service — Entry Point
//
// Architecture:
//   • Windows Service (sc.exe managed)
//   • Kestrel HTTP on 127.0.0.1:47777 (REST + WebSocket)
//   • Named Pipe: \\.\pipe\PerplexityXPC-{username} (restricted to current user)
//   • MCP server manager (stdio subprocesses, JSON-RPC 2.0)
// ---------------------------------------------------------------------------

// Determine log directory early (before host is built) for Serilog bootstrap
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var logDirectory = Path.Combine(localAppData, "PerplexityXPC", "logs");
Directory.CreateDirectory(logDirectory);

// ---------------------------------------------------------------------------
// Bootstrap logger (used during host initialization before DI is ready)
// ---------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        new CompactJsonFormatter(),
        path: Path.Combine(logDirectory, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 50_000_000,
        rollOnFileSizeLimit: true)
    .CreateBootstrapLogger();

try
{
    Log.Information("PerplexityXPC Service starting up.");

    var builder = WebApplication.CreateBuilder(args);

    // -----------------------------------------------------------------------
    // Windows Service support
    // Must be called before configuring Kestrel so that UseWindowsService()
    // sets the content root correctly when launched by the SCM.
    // -----------------------------------------------------------------------
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "PerplexityXPC";
    });

    // -----------------------------------------------------------------------
    // Configuration
    // Order of precedence (highest last wins):
    //   1. appsettings.json
    //   2. appsettings.{Environment}.json
    //   3. Environment variables (PERPLEXITYXPC_ prefix)
    //   4. Command-line arguments
    // -----------------------------------------------------------------------
    builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile(
            $"appsettings.{builder.Environment.EnvironmentName}.json",
            optional: true,
            reloadOnChange: true)
        .AddEnvironmentVariables(prefix: "PERPLEXITYXPC_")
        .AddCommandLine(args);

    // Also surface PERPLEXITY_API_KEY directly into the config section
    if (Environment.GetEnvironmentVariable("PERPLEXITY_API_KEY") is { } apiKey && !string.IsNullOrWhiteSpace(apiKey))
    {
        builder.Configuration["PerplexityXPC:ApiKey"] = apiKey;
    }

    // -----------------------------------------------------------------------
    // Serilog — replace the default logging pipeline
    // -----------------------------------------------------------------------
    builder.Host.UseSerilog((ctx, services, logConfig) =>
    {
        var appCfg = ctx.Configuration.GetSection(AppConfig.SectionName).Get<AppConfig>() ?? new AppConfig();
        var level = Enum.TryParse<LogEventLevel>(appCfg.LogLevel, ignoreCase: true, out var lvl)
            ? lvl
            : LogEventLevel.Information;

        logConfig
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                new CompactJsonFormatter(),
                path: Path.Combine(appCfg.LogDirectory, "service-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 50_000_000,
                rollOnFileSizeLimit: true)
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services);
    });

    // -----------------------------------------------------------------------
    // Kestrel — bind ONLY to localhost to prevent any external access
    // -----------------------------------------------------------------------
    builder.WebHost.ConfigureKestrel((ctx, options) =>
    {
        var port = ctx.Configuration.GetValue<int>("PerplexityXPC:Port", 47777);

        options.Listen(IPAddress.Loopback, port, listenOptions =>
        {
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
        });

        // Disable server header for security hygiene
        options.AddServerHeader = false;
    });

    // -----------------------------------------------------------------------
    // Dependency Injection
    // -----------------------------------------------------------------------

    // Strongly-typed config
    builder.Services.Configure<AppConfig>(
        builder.Configuration.GetSection(AppConfig.SectionName));

    // Register AppConfig as a singleton for injection into minimal API handlers
    builder.Services.AddSingleton(sp =>
        builder.Configuration.GetSection(AppConfig.SectionName).Get<AppConfig>() ?? new AppConfig());

    // Perplexity API HTTP client
    builder.Services.AddHttpClient<PerplexityApiClient>()
        .ConfigureHttpClient((sp, client) =>
        {
            // Base address and auth are set in PerplexityApiClient constructor
        });

    // Register PerplexityApiClient as singleton (holds its own HttpClient)
    builder.Services.AddSingleton<PerplexityApiClient>();

    // MCP server manager — also a hosted service (starts/stops servers with the host)
    builder.Services.AddSingleton<McpServerManager>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<McpServerManager>());

    // Named pipe server — background service
    builder.Services.AddHostedService<NamedPipeServer>();

    // ASP.NET Core services
    builder.Services.AddEndpointsApiExplorer();

    // CORS — localhost only
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost",
                    "http://127.0.0.1",
                    "http://localhost:47777",
                    "http://127.0.0.1:47777",
                    // Common browser extension / dev server ports
                    "http://localhost:3000",
                    "http://localhost:5000",
                    "http://localhost:8080")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

    // WebSockets are enabled via middleware (UseWebSockets) below.

    // -----------------------------------------------------------------------
    // Build the app
    // -----------------------------------------------------------------------
    var app = builder.Build();

    // -----------------------------------------------------------------------
    // Middleware pipeline
    // -----------------------------------------------------------------------
    app.UseWebSockets();
    app.UseCors();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    // -----------------------------------------------------------------------
    // Route registration (delegates to HttpBroker)
    // -----------------------------------------------------------------------
    HttpBroker.MapRoutes(app);

    // -----------------------------------------------------------------------
    // Windows Firewall rule (optional, requires elevated permissions)
    // Block inbound access to the port from any non-loopback interface.
    // This is belt-and-suspenders — Kestrel already only binds to 127.0.0.1.
    // -----------------------------------------------------------------------
    var finalConfig = app.Configuration.GetSection(AppConfig.SectionName).Get<AppConfig>() ?? new AppConfig();
    if (finalConfig.AddFirewallRule && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        AddFirewallRule(finalConfig.Port, app.Logger);
    }

    // -----------------------------------------------------------------------
    // Run
    // -----------------------------------------------------------------------
    Log.Information(
        "PerplexityXPC Service listening on http://127.0.0.1:{Port} | Pipe: {Pipe}",
        finalConfig.Port, finalConfig.PipeName);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PerplexityXPC Service terminated unexpectedly.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// ---------------------------------------------------------------------------
// Local functions
// ---------------------------------------------------------------------------

/// <summary>
/// Adds a Windows Firewall inbound block rule for the service port.
/// Any existing rule with the same name is removed first to avoid duplicates.
/// Silently logs and continues on failure (firewall is defense-in-depth;
/// Kestrel's loopback binding is the primary protection).
/// </summary>
static void AddFirewallRule(int port, Microsoft.Extensions.Logging.ILogger logger)
{
    const string RuleName = "PerplexityXPC-Block-Inbound";

    try
    {
        // Remove existing rule (ignore errors)
        var deleteResult = RunNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
        logger.LogDebug("Firewall rule removal: {Result}", deleteResult);

        // Add block rule for all profiles
        var addResult = RunNetsh(
            $"advfirewall firewall add rule " +
            $"name=\"{RuleName}\" " +
            $"dir=in " +
            $"action=block " +
            $"protocol=TCP " +
            $"localport={port} " +
            $"profile=any " +
            $"description=\"PerplexityXPC local service port — block external access\"");

        logger.LogInformation("Firewall rule '{Rule}' for port {Port}: {Result}", RuleName, port, addResult);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex,
            "Failed to configure Windows Firewall rule. " +
            "The service is still secure because Kestrel only binds to 127.0.0.1.");
    }
}

/// <summary>
/// Executes a netsh.exe command and returns its standard output.
/// </summary>
static string RunNetsh(string arguments)
{
    using var process = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };

    process.Start();
    var output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit(5000);
    return output;
}
