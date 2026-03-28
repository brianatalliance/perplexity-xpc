using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PerplexityXPC.Service.Configuration;
using PerplexityXPC.Service.Models;

namespace PerplexityXPC.Service.Services;

/// <summary>
/// Creates and manages the named pipe server for local IPC.
/// The pipe is restricted to the current user's SID via PipeSecurity ACL.
/// Pipe path: \\.\pipe\PerplexityXPC-{Environment.UserName}
///
/// Protocol: Newline-delimited JSON messages (one request per line, one response per line).
/// </summary>
public sealed class NamedPipeServer : BackgroundService
{
    private readonly AppConfig _config;
    private readonly PerplexityApiClient _apiClient;
    private readonly McpServerManager _mcpManager;
    private readonly ILogger<NamedPipeServer> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes the named pipe server with all required service dependencies.
    /// </summary>
    public NamedPipeServer(
        IOptions<AppConfig> config,
        PerplexityApiClient apiClient,
        McpServerManager mcpManager,
        ILogger<NamedPipeServer> logger)
    {
        _config = config.Value;
        _apiClient = apiClient;
        _mcpManager = mcpManager;
        _logger = logger;
    }

    /// <summary>
    /// Continuously accepts named pipe connections and spawns a handler task for each.
    /// Runs until cancellation is requested (service shutdown).
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Named pipe server starting on pipe: {PipeName}", _config.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pipeServer = CreatePipeInstance();

                // Wait for a client to connect (non-blocking via async)
                await pipeServer.WaitForConnectionAsync(stoppingToken);

                _logger.LogDebug("Named pipe client connected.");

                // Handle on a background task so we can immediately accept the next connection
                _ = Task.Run(
                    () => HandleClientAsync(pipeServer, stoppingToken),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Named pipe server encountered an error. Restarting listener.");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("Named pipe server stopped.");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a new NamedPipeServerStream instance with security restricted to the current user.
    /// </summary>
    private NamedPipeServerStream CreatePipeInstance()
    {
        // Build ACL: only current user SID may read/write
        var security = new PipeSecurity();
        var currentUserSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot determine current user SID.");

        // Grant current user full control (read, write, create)
        security.AddAccessRule(new PipeAccessRule(
            currentUserSid,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        // Deny everyone else
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            _config.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, // Support multiple simultaneous clients
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: 65536,
            outBufferSize: 65536,
            security);
    }

    /// <summary>
    /// Handles a single client connection: reads newline-delimited JSON commands and writes responses.
    /// </summary>
    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken ct)
    {
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
            await using (var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            })
            {
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break; // Client disconnected

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var response = await DispatchCommandAsync(line, ct);
                    await writer.WriteLineAsync(response.AsMemory(), ct);
                }
            }
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        catch (IOException) { /* Client disconnected abruptly */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Named pipe client handler encountered an error.");
        }
        finally
        {
            _logger.LogDebug("Named pipe client disconnected.");
        }
    }

    /// <summary>
    /// Parses the raw JSON command line and dispatches to the appropriate handler.
    /// Returns a JSON-serialized response string.
    /// </summary>
    private async Task<string> DispatchCommandAsync(string line, CancellationToken ct)
    {
        PipeCommand? cmd = null;
        try
        {
            cmd = JsonSerializer.Deserialize<PipeCommand>(line, JsonOpts);
            if (cmd is null)
                return ErrorResponse("invalid_request", "Failed to parse command.");

            return cmd.Command switch
            {
                "query" => await HandleQueryAsync(cmd, ct),
                "stream" => await HandleStreamAsync(cmd, ct),
                "mcp-send" => await HandleMcpSendAsync(cmd, ct),
                "mcp-list" => await HandleMcpListAsync(ct),
                "status" => HandleStatus(),
                "config-get" => HandleConfigGet(),
                "config-set" => HandleConfigSet(cmd),
                _ => ErrorResponse("unknown_command", $"Unknown command: '{cmd.Command}'.")
            };
        }
        catch (JsonException ex)
        {
            return ErrorResponse("parse_error", $"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command '{Command}' failed.", cmd?.Command);
            return ErrorResponse("internal_error", ex.Message);
        }
    }

    // -------------------------------------------------------------------------
    // Command handlers
    // -------------------------------------------------------------------------

    private async Task<string> HandleQueryAsync(PipeCommand cmd, CancellationToken ct)
    {
        var request = cmd.Payload.Deserialize<ChatRequest>(JsonOpts)
            ?? throw new ArgumentException("Missing or invalid 'payload' for query command.");

        var response = await _apiClient.ChatAsync(request, ct);
        return SuccessResponse(response);
    }

    private async Task<string> HandleStreamAsync(PipeCommand cmd, CancellationToken ct)
    {
        // For streaming over a pipe we collect all chunks into a single response.
        // True streaming pipes would require a separate protocol; this provides compatibility.
        var request = cmd.Payload.Deserialize<ChatRequest>(JsonOpts)
            ?? throw new ArgumentException("Missing or invalid 'payload' for stream command.");

        var chunks = new List<string>();
        await foreach (var chunk in _apiClient.ChatStreamAsync(request, ct))
        {
            if (chunk == "[DONE]") break;
            chunks.Add(chunk);
        }

        return SuccessResponse(new { chunks, done = true });
    }

    private async Task<string> HandleMcpSendAsync(PipeCommand cmd, CancellationToken ct)
    {
        var proxy = cmd.Payload.Deserialize<McpProxyRequest>(JsonOpts)
            ?? throw new ArgumentException("Missing or invalid 'payload' for mcp-send command.");

        var result = await _mcpManager.SendRequestAsync(proxy.Server, cmd.Payload, ct);
        return SuccessResponse(result);
    }

    private async Task<string> HandleMcpListAsync(CancellationToken ct)
    {
        var servers = await _mcpManager.ListServersAsync();
        return SuccessResponse(servers);
    }

    private string HandleStatus()
    {
        var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        return SuccessResponse(new
        {
            version = "1.0.0",
            uptime = FormatUptime(uptime),
            status = "ok",
            pipe = _config.PipeName
        });
    }

    private string HandleConfigGet()
    {
        // Return non-sensitive config values only
        return SuccessResponse(new
        {
            port = _config.Port,
            pipeName = _config.PipeName,
            logLevel = _config.LogLevel,
            mcpServersPath = _config.McpServersPath,
            autoStartMcpServers = _config.AutoStartMcpServers,
            maxRetries = _config.MaxRetries,
            apiTimeoutSeconds = _config.ApiTimeoutSeconds
            // ApiKey intentionally excluded
        });
    }

    private string HandleConfigSet(PipeCommand cmd)
    {
        // Config-set is a placeholder; live config mutation requires restart in most cases.
        // Implementors can expand this to write back to appsettings.json.
        _logger.LogInformation("config-set requested via pipe (not yet fully implemented).");
        return SuccessResponse(new { accepted = true, note = "Restart service to apply config changes." });
    }

    // -------------------------------------------------------------------------
    // Response helpers
    // -------------------------------------------------------------------------

    private static string SuccessResponse<T>(T data) =>
        JsonSerializer.Serialize(new PipeResponse<T> { Ok = true, Data = data }, JsonOpts);

    private static string ErrorResponse(string code, string message) =>
        JsonSerializer.Serialize(
            new PipeResponse<object?> { Ok = false, Error = new PipeError { Code = code, Message = message } },
            JsonOpts);

    private static string FormatUptime(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s"
            : $"{ts.Minutes}m {ts.Seconds}s";

    // -------------------------------------------------------------------------
    // Protocol DTOs (internal to pipe protocol)
    // -------------------------------------------------------------------------

    private sealed class PipeCommand
    {
        public string Command { get; set; } = string.Empty;
        public JsonElement Payload { get; set; }
    }

    private sealed class PipeResponse<T>
    {
        public bool Ok { get; set; }
        public T? Data { get; set; }
        public PipeError? Error { get; set; }
    }

    private sealed class PipeError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
