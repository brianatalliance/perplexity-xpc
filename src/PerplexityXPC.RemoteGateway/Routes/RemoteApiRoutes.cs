using System.Diagnostics;
using System.Text.Json;
using PerplexityXPC.RemoteGateway.Configuration;
using PerplexityXPC.RemoteGateway.Services;

namespace PerplexityXPC.RemoteGateway.Routes;

/// <summary>
/// Registers all minimal-API route handlers for the RemoteGateway.
///
/// Route map:
/// <list type="bullet">
///   <item>GET  /health                          - Liveness probe (no auth)</item>
///   <item>POST /api/query                       - Proxy query to broker</item>
///   <item>GET  /api/status                      - Broker status</item>
///   <item>POST /api/execute                     - Run PowerShell command</item>
///   <item>POST /api/execute/module              - Run PerplexityXPC module function</item>
///   <item>GET  /api/system                      - Full system info</item>
///   <item>GET  /api/system/processes            - Top processes</item>
///   <item>GET  /api/system/services             - Service list</item>
///   <item>GET  /api/system/events               - Event log entries</item>
///   <item>GET  /api/system/disks                - Disk space</item>
///   <item>GET  /api/files                       - Directory listing</item>
///   <item>GET  /api/files/read                  - File content</item>
///   <item>GET  /api/files/exists                - File existence check</item>
/// </list>
/// </summary>
public static class RemoteApiRoutes
{
    private const string GatewayVersion = "1.2.0";

    /// <summary>
    /// Maps all gateway routes onto <paramref name="app"/>.
    /// Call this from Program.cs after middleware registration.
    /// </summary>
    public static void MapRoutes(WebApplication app)
    {
        MapHealthRoutes(app);
        MapQueryRoutes(app);
        MapExecuteRoutes(app);
        MapSystemRoutes(app);
        MapFileRoutes(app);
    }

    // -----------------------------------------------------------------------
    // Health
    // -----------------------------------------------------------------------

    private static void MapHealthRoutes(WebApplication app)
    {
        /// <summary>Liveness probe - no authentication required.</summary>
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            version = GatewayVersion,
            hostname = Environment.MachineName,
            timestamp = DateTime.UtcNow
        }));
    }

    // -----------------------------------------------------------------------
    // Perplexity Proxy
    // -----------------------------------------------------------------------

    private static void MapQueryRoutes(WebApplication app)
    {
        /// <summary>
        /// POST /api/query
        /// Proxies a natural-language query to the local broker.
        /// Body: { "query": "...", "model": "sonar" }
        /// </summary>
        app.MapPost("/api/query", async (
            QueryRequest body,
            BrokerProxy broker) =>
        {
            if (string.IsNullOrWhiteSpace(body.Query))
                return Results.BadRequest(new { error = "query field is required." });

            var sw = Stopwatch.StartNew();
            string result = await broker.QueryAsync(body.Query, body.Model);
            sw.Stop();

            // The broker returns raw JSON - parse and re-emit to include timing.
            try
            {
                using var doc = JsonDocument.Parse(result);
                return Results.Ok(new
                {
                    data = doc.RootElement,
                    durationMs = sw.ElapsedMilliseconds
                });
            }
            catch
            {
                return Results.Ok(new
                {
                    data = result,
                    durationMs = sw.ElapsedMilliseconds
                });
            }
        });

        /// <summary>
        /// GET /api/status
        /// Returns the broker's own status object.
        /// </summary>
        app.MapGet("/api/status", async (BrokerProxy broker) =>
        {
            var sw = Stopwatch.StartNew();
            object status = await broker.GetStatusAsync();
            sw.Stop();
            return Results.Ok(new { data = status, durationMs = sw.ElapsedMilliseconds });
        });

        /// <summary>
        /// GET /api/mcp/servers
        /// Lists MCP servers registered in the broker.
        /// </summary>
        app.MapGet("/api/mcp/servers", async (BrokerProxy broker) =>
        {
            var sw = Stopwatch.StartNew();
            object servers = await broker.ListMcpServersAsync();
            sw.Stop();
            return Results.Ok(new { data = servers, durationMs = sw.ElapsedMilliseconds });
        });
    }

    // -----------------------------------------------------------------------
    // PowerShell execution
    // -----------------------------------------------------------------------

    private static void MapExecuteRoutes(WebApplication app)
    {
        /// <summary>
        /// POST /api/execute
        /// Executes an arbitrary PowerShell command subject to allow/block lists.
        /// Body: { "command": "Get-Service PerplexityXPC", "timeout": 30 }
        /// </summary>
        app.MapPost("/api/execute", async (
            ExecuteRequest body,
            CommandExecutor executor,
            RemoteConfig config) =>
        {
            if (!config.EnablePowerShellExecution)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(body.Command))
                return Results.BadRequest(new { error = "command field is required." });

            int timeout = body.Timeout > 0
                ? Math.Min(body.Timeout, config.MaxCommandTimeoutSeconds)
                : config.MaxCommandTimeoutSeconds;

            var result = await executor.ExecuteAsync(body.Command, timeout, CancellationToken.None);

            return Results.Ok(new
            {
                output = result.Output,
                errors = result.Errors,
                exitCode = result.ExitCode,
                durationMs = result.DurationMs,
                command = result.Command
            });
        });

        /// <summary>
        /// POST /api/execute/module
        /// Builds and executes a PerplexityXPC module command from a function
        /// name and parameter dictionary.
        /// Body: { "function": "Invoke-PerplexityEventAnalysis", "parameters": { "GroupBySource": true } }
        /// </summary>
        app.MapPost("/api/execute/module", async (
            ModuleExecuteRequest body,
            CommandExecutor executor,
            RemoteConfig config) =>
        {
            if (!config.EnablePowerShellExecution)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(body.Function))
                return Results.BadRequest(new { error = "function field is required." });

            string command = BuildModuleCommand(body.Function, body.Parameters);

            int timeout = body.Timeout > 0
                ? Math.Min(body.Timeout, config.MaxCommandTimeoutSeconds)
                : config.MaxCommandTimeoutSeconds;

            var result = await executor.ExecuteAsync(command, timeout, CancellationToken.None);

            return Results.Ok(new
            {
                output = result.Output,
                errors = result.Errors,
                exitCode = result.ExitCode,
                durationMs = result.DurationMs,
                command = result.Command
            });
        });
    }

    // -----------------------------------------------------------------------
    // System monitoring
    // -----------------------------------------------------------------------

    private static void MapSystemRoutes(WebApplication app)
    {
        /// <summary>GET /api/system - Full system snapshot.</summary>
        app.MapGet("/api/system", async (SystemMonitor monitor) =>
        {
            var sw = Stopwatch.StartNew();
            var info = await monitor.GetSystemInfoAsync();
            sw.Stop();
            return Results.Ok(new { data = info, durationMs = sw.ElapsedMilliseconds });
        });

        /// <summary>GET /api/system/processes?top=10 - Top N processes by memory.</summary>
        app.MapGet("/api/system/processes", async (
            SystemMonitor monitor,
            int top = 10) =>
        {
            var sw = Stopwatch.StartNew();
            var processes = await monitor.GetProcessesAsync(top);
            sw.Stop();
            return Results.Ok(new
            {
                data = processes,
                count = processes.Count,
                durationMs = sw.ElapsedMilliseconds
            });
        });

        /// <summary>GET /api/system/services?filter=running - Windows services.</summary>
        app.MapGet("/api/system/services", async (
            SystemMonitor monitor,
            string? filter = null) =>
        {
            var sw = Stopwatch.StartNew();
            var services = await monitor.GetServicesAsync(filter);
            sw.Stop();
            return Results.Ok(new
            {
                data = services,
                count = services.Count,
                durationMs = sw.ElapsedMilliseconds
            });
        });

        /// <summary>GET /api/system/events?log=System&max=20 - Event log entries.</summary>
        app.MapGet("/api/system/events", async (
            SystemMonitor monitor,
            string log = "System",
            int max = 20) =>
        {
            var sw = Stopwatch.StartNew();
            var events = await monitor.GetEventLogsAsync(log, max);
            sw.Stop();
            return Results.Ok(new
            {
                data = events,
                log,
                count = events.Count,
                durationMs = sw.ElapsedMilliseconds
            });
        });

        /// <summary>GET /api/system/disks - Fixed disk space.</summary>
        app.MapGet("/api/system/disks", async (SystemMonitor monitor) =>
        {
            var sw = Stopwatch.StartNew();
            var disks = await monitor.GetDiskSpaceAsync();
            sw.Stop();
            return Results.Ok(new
            {
                data = disks,
                count = disks.Count,
                durationMs = sw.ElapsedMilliseconds
            });
        });
    }

    // -----------------------------------------------------------------------
    // File operations
    // -----------------------------------------------------------------------

    private static void MapFileRoutes(WebApplication app)
    {
        /// <summary>GET /api/files?path=C:\Reports - Directory listing.</summary>
        app.MapGet("/api/files", async (
            FileManager files,
            RemoteConfig config,
            string path) =>
        {
            if (!config.EnableFileOperations)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(path))
                return Results.BadRequest(new { error = "path query parameter is required." });

            try
            {
                var sw = Stopwatch.StartNew();
                var listing = await files.ListDirectoryAsync(path);
                sw.Stop();
                return Results.Ok(new { data = listing, durationMs = sw.ElapsedMilliseconds });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        /// <summary>GET /api/files/read?path=C:\Reports\daily.md&lines=100 - File content.</summary>
        app.MapGet("/api/files/read", async (
            FileManager files,
            RemoteConfig config,
            string path,
            int lines = 100) =>
        {
            if (!config.EnableFileOperations)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(path))
                return Results.BadRequest(new { error = "path query parameter is required." });

            try
            {
                var sw = Stopwatch.StartNew();
                var content = await files.ReadFileAsync(path, lines);
                sw.Stop();
                return Results.Ok(new { data = content, durationMs = sw.ElapsedMilliseconds });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        /// <summary>GET /api/files/exists?path=C:\Reports\daily.md - Existence check.</summary>
        app.MapGet("/api/files/exists", async (
            FileManager files,
            RemoteConfig config,
            string path) =>
        {
            if (!config.EnableFileOperations)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(path))
                return Results.BadRequest(new { error = "path query parameter is required." });

            try
            {
                bool exists = await files.FileExistsAsync(path);
                return Results.Ok(new { path, exists });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a PowerShell command string from a function name and a parameter
    /// dictionary of name-to-value pairs.
    /// </summary>
    private static string BuildModuleCommand(
        string function,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Import-Module PerplexityXPC -ErrorAction SilentlyContinue; ");
        sb.Append(function);

        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                sb.Append(' ');
                sb.Append('-');
                sb.Append(name);
                sb.Append(' ');
                sb.Append(FormatPsValue(value));
            }
        }

        return sb.ToString();
    }

    private static string FormatPsValue(object? value) => value switch
    {
        null => "$null",
        bool b => b ? "$true" : "$false",
        string s => $"'{s.Replace("'", "''")}'",
        int or long or double or float => value.ToString()!,
        _ => $"'{value}'"
    };
}

// -----------------------------------------------------------------------
// Request body records
// -----------------------------------------------------------------------

/// <summary>Body for POST /api/query.</summary>
internal sealed record QueryRequest(string Query, string? Model = null);

/// <summary>Body for POST /api/execute.</summary>
internal sealed record ExecuteRequest(string Command, int Timeout = 0);

/// <summary>Body for POST /api/execute/module.</summary>
internal sealed record ModuleExecuteRequest(
    string Function,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    int Timeout = 0);
