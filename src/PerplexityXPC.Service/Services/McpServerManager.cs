using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PerplexityXPC.Service.Configuration;
using PerplexityXPC.Service.Models;

namespace PerplexityXPC.Service.Services;

/// <summary>
/// Manages MCP (Model Context Protocol) stdio server processes.
/// Spawns, communicates with, and restarts MCP servers defined in mcp-servers.json.
/// Communication is via JSON-RPC 2.0 over stdin/stdout.
/// </summary>
public sealed class McpServerManager : IHostedService, IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly ILogger<McpServerManager> _logger;

    // Per-server runtime state
    private readonly ConcurrentDictionary<string, ManagedServer> _servers = new();

    // Serialize reads from each server's stdout to avoid interleaved JSON
    // Each server has its own lock object
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _serverLocks = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes the MCP server manager with the provided configuration.
    /// </summary>
    public McpServerManager(
        IOptions<AppConfig> config,
        ILogger<McpServerManager> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // IHostedService
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the manager and auto-starts any MCP servers with auto_start=true.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("McpServerManager starting. Config path: {Path}", _config.McpServersPath);

        var configs = LoadConfigs();

        if (_config.AutoStartMcpServers)
        {
            var autoStartTasks = configs
                .Where(c => c.AutoStart)
                .Select(c => StartServerAsync(c.Name, cancellationToken));

            await Task.WhenAll(autoStartTasks);
        }
    }

    /// <summary>
    /// Stops all running MCP server processes gracefully on service shutdown.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("McpServerManager stopping. Terminating {Count} servers.", _servers.Count);

        var stopTasks = _servers.Keys.ToList()
            .Select(name => StopServerAsync(name, cancellationToken));

        await Task.WhenAll(stopTasks);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the named MCP server process. If already running, this is a no-op.
    /// </summary>
    /// <param name="name">Server name as defined in mcp-servers.json.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task StartServerAsync(string name, CancellationToken ct = default)
    {
        var configs = LoadConfigs();
        var serverConfig = configs.FirstOrDefault(c => c.Name == name);

        if (serverConfig is null)
        {
            _logger.LogWarning("MCP server '{Name}' not found in configuration.", name);
            return;
        }

        // If already running, skip
        if (_servers.TryGetValue(name, out var existing) &&
            existing.Status == McpServerStatus.Running)
        {
            _logger.LogDebug("MCP server '{Name}' is already running (PID {Pid}).", name, existing.Pid);
            return;
        }

        await LaunchServerAsync(serverConfig, ct);
    }

    /// <summary>
    /// Stops the named MCP server process.
    /// </summary>
    /// <param name="name">Server name.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task StopServerAsync(string name, CancellationToken ct = default)
    {
        if (!_servers.TryGetValue(name, out var server)) return;

        server.Status = McpServerStatus.Stopping;
        _logger.LogInformation("Stopping MCP server '{Name}' (PID {Pid}).", name, server.Pid);

        try
        {
            if (server.Process is not null && !server.Process.HasExited)
            {
                // Cancel background reader first
                server.CancellationSource?.Cancel();

                // Try graceful shutdown via stdin close, then kill
                try
                {
                    server.Process.StandardInput.Close();
                    await Task.Delay(2000, ct);
                }
                catch { /* stdin may already be closed */ }

                if (!server.Process.HasExited)
                {
                    server.Process.Kill(entireProcessTree: true);
                    await server.Process.WaitForExitAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping MCP server '{Name}'.", name);
        }
        finally
        {
            server.Process?.Dispose();
            server.Status = McpServerStatus.Stopped;
            server.Pid = null;
        }
    }

    /// <summary>
    /// Restarts the named MCP server (stop then start).
    /// </summary>
    /// <param name="name">Server name.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RestartServerAsync(string name, CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting MCP server '{Name}'.", name);
        await StopServerAsync(name, ct);
        await Task.Delay(500, ct); // Brief pause before re-launch
        await StartServerAsync(name, ct);
    }

    /// <summary>
    /// Sends a JSON-RPC 2.0 request to the named MCP server and returns the response.
    /// </summary>
    /// <param name="serverName">Target MCP server name.</param>
    /// <param name="request">The JSON-RPC request element (must contain method and optional params).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The JSON-RPC result element.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the server is not running.</exception>
    /// <exception cref="McpException">Thrown if the server returns a JSON-RPC error.</exception>
    public async Task<JsonElement> SendRequestAsync(
        string serverName,
        JsonElement request,
        CancellationToken ct = default)
    {
        if (!_servers.TryGetValue(serverName, out var server) ||
            server.Status != McpServerStatus.Running ||
            server.Process is null)
        {
            throw new InvalidOperationException(
                $"MCP server '{serverName}' is not running. Current status: " +
                $"{(_servers.TryGetValue(serverName, out var s) ? s.Status.ToString() : "Not Found")}");
        }

        var sem = _serverLocks.GetOrAdd(serverName, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);

        try
        {
            var requestId = Interlocked.Increment(ref server.NextRequestId);

            // Build JSON-RPC envelope
            var rpcRequest = new JsonRpcRequest
            {
                Id = requestId,
                Method = request.GetProperty("method").GetString() ?? string.Empty,
                Params = request.TryGetProperty("params", out var p) ? p : null
            };

            var line = JsonSerializer.Serialize(rpcRequest, JsonOpts);
            _logger.LogTrace("MCP → {Server}: {Line}", serverName, line);

            // Write to server stdin
            await server.Process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
            await server.Process.StandardInput.FlushAsync(ct);

            // Read response from stdout — each response is a single JSON line
            var responseLine = await server.Process.StandardOutput.ReadLineAsync(ct);
            _logger.LogTrace("MCP ← {Server}: {Line}", serverName, responseLine);

            if (string.IsNullOrWhiteSpace(responseLine))
                throw new InvalidOperationException($"MCP server '{serverName}' returned an empty response.");

            var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(responseLine, JsonOpts);

            if (rpcResponse is null)
                throw new InvalidOperationException("Failed to deserialize MCP server response.");

            if (rpcResponse.Error is not null)
                throw new McpException(rpcResponse.Error.Code, rpcResponse.Error.Message);

            return rpcResponse.Result ?? default;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Returns runtime status information for all configured MCP servers.
    /// </summary>
    /// <returns>A list of <see cref="McpServerInfo"/> for all servers.</returns>
    public Task<List<McpServerInfo>> ListServersAsync()
    {
        var configs = LoadConfigs();
        var infos = configs.Select(cfg =>
        {
            _servers.TryGetValue(cfg.Name, out var server);
            return BuildServerInfo(cfg.Name, server, cfg);
        }).ToList();

        return Task.FromResult(infos);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task LaunchServerAsync(McpServerConfig config, CancellationToken ct)
    {
        var managedServer = _servers.GetOrAdd(config.Name, _ => new ManagedServer());
        managedServer.Status = McpServerStatus.Starting;
        managedServer.LastError = null;

        _logger.LogInformation(
            "Launching MCP server '{Name}': {Command} {Args}",
            config.Name, config.Command, string.Join(" ", config.Args));

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = config.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = config.WorkingDirectory
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Add arguments
            foreach (var arg in config.Args)
                startInfo.ArgumentList.Add(arg);

            // Merge environment variables
            foreach (var (key, value) in config.Env)
                startInfo.Environment[key] = value;

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            // Hook stderr for diagnostics
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogDebug("[{Name}] stderr: {Data}", config.Name, e.Data);
            };

            // Handle unexpected exit for auto-restart
            var cts = new CancellationTokenSource();
            process.Exited += (_, _) => HandleUnexpectedExitAsync(config.Name, cts.Token).ConfigureAwait(false);

            process.Start();
            process.BeginErrorReadLine();

            managedServer.Process = process;
            managedServer.Pid = process.Id;
            managedServer.Status = McpServerStatus.Running;
            managedServer.StartedAt = DateTimeOffset.UtcNow;
            managedServer.CancellationSource = cts;

            _logger.LogInformation(
                "MCP server '{Name}' started (PID {Pid}).", config.Name, process.Id);
        }
        catch (Exception ex)
        {
            managedServer.Status = McpServerStatus.Error;
            managedServer.LastError = ex.Message;
            _logger.LogError(ex, "Failed to launch MCP server '{Name}'.", config.Name);
        }
    }

    private async Task HandleUnexpectedExitAsync(string name, CancellationToken ct)
    {
        if (!_servers.TryGetValue(name, out var server)) return;
        if (server.Status == McpServerStatus.Stopping || server.Status == McpServerStatus.Stopped)
            return; // Intentional stop

        _logger.LogWarning("MCP server '{Name}' exited unexpectedly.", name);
        server.Status = McpServerStatus.Error;

        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddSeconds(-_config.McpRestartWindowSeconds);

        // Track restart attempts within the window
        server.RestartTimestamps.RemoveAll(t => t < windowStart);

        if (server.RestartTimestamps.Count >= _config.McpMaxRestartAttempts)
        {
            server.LastError =
                $"Exceeded {_config.McpMaxRestartAttempts} restarts in " +
                $"{_config.McpRestartWindowSeconds}s. Auto-restart disabled.";
            _logger.LogError(
                "MCP server '{Name}' has crashed too frequently. Auto-restart disabled. " +
                "Use /mcp/servers/{name}/restart to manually restart.", name);
            return;
        }

        server.RestartTimestamps.Add(now);
        var backoffMs = (int)Math.Pow(2, server.RestartTimestamps.Count) * 1000;

        _logger.LogInformation(
            "Auto-restarting MCP server '{Name}' in {Delay}ms (attempt {Attempt}/{Max}).",
            name, backoffMs, server.RestartTimestamps.Count, _config.McpMaxRestartAttempts);

        await Task.Delay(backoffMs, ct);
        await StartServerAsync(name, ct);
    }

    private List<McpServerConfig> LoadConfigs()
    {
        if (!File.Exists(_config.McpServersPath))
        {
            _logger.LogDebug("mcp-servers.json not found at {Path}. No servers configured.", _config.McpServersPath);
            return [];
        }

        try
        {
            var json = File.ReadAllText(_config.McpServersPath);
            var file = JsonSerializer.Deserialize<McpServersFile>(json, JsonOpts);
            return file?.Servers ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load mcp-servers.json from {Path}.", _config.McpServersPath);
            return [];
        }
    }

    private static McpServerInfo BuildServerInfo(
        string name,
        ManagedServer? server,
        McpServerConfig config)
    {
        var uptime = server?.StartedAt is not null && server.Status == McpServerStatus.Running
            ? FormatUptime(DateTimeOffset.UtcNow - server.StartedAt.Value)
            : null;

        return new McpServerInfo
        {
            Name = name,
            Status = server?.Status ?? McpServerStatus.Stopped,
            Pid = server?.Pid,
            Uptime = uptime,
            LastError = server?.LastError,
            RestartAttempts = server?.RestartTimestamps.Count ?? 0,
            StartedAt = server?.StartedAt,
            Command = $"{config.Command} {string.Join(" ", config.Args)}"
        };
    }

    private static string FormatUptime(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s"
            : $"{ts.Minutes}m {ts.Seconds}s";

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var name in _servers.Keys)
            await StopServerAsync(name);
    }

    // -------------------------------------------------------------------------
    // Inner types
    // -------------------------------------------------------------------------

    private sealed class ManagedServer
    {
        public Process? Process { get; set; }
        public int? Pid { get; set; }
        public McpServerStatus Status { get; set; } = McpServerStatus.Stopped;
        public DateTimeOffset? StartedAt { get; set; }
        public string? LastError { get; set; }
        public List<DateTimeOffset> RestartTimestamps { get; } = [];
        public CancellationTokenSource? CancellationSource { get; set; }
        public long NextRequestId;
    }
}

/// <summary>
/// Exception thrown when an MCP server returns a JSON-RPC error response.
/// </summary>
public sealed class McpException : Exception
{
    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; }

    /// <summary>
    /// Initializes a new <see cref="McpException"/> with an error code and message.
    /// </summary>
    public McpException(int code, string message) : base(message)
    {
        Code = code;
    }
}
