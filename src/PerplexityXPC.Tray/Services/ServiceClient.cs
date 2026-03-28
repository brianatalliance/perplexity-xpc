using System.IO.Pipes;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerplexityXPC.Tray.Services;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Live status of the background PerplexityXPC Windows Service.</summary>
public enum ServiceStatus { Running, Connecting, Disconnected }

/// <summary>Metadata about a configured MCP server.</summary>
public sealed class McpServerInfo
{
    public string Name      { get; init; } = "";
    public string Command   { get; init; } = "";
    public bool   IsRunning { get; init; }
}

/// <summary>Status response payload returned by GET /status.</summary>
internal sealed class StatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "stopped";

    [JsonPropertyName("mcpServers")]
    public List<McpServerInfoDto>? McpServers { get; init; }
}

internal sealed class McpServerInfoDto
{
    [JsonPropertyName("name")]    public string Name      { get; init; } = "";
    [JsonPropertyName("command")] public string Command   { get; init; } = "";
    [JsonPropertyName("running")] public bool   IsRunning { get; init; }
}

// ─── Client ───────────────────────────────────────────────────────────────────

/// <summary>
/// Communicates with the PerplexityXPC Windows Service via two channels:
/// <list type="bullet">
///   <item><description>
///     <b>HTTP</b> on <c>http://127.0.0.1:47777</c> — used for status polling
///     and streaming query responses (SSE).
///   </description></item>
///   <item><description>
///     <b>Named pipe</b> <c>\\.\pipe\PerplexityXPC-{username}</c> — used for
///     low-latency control commands (start/stop MCP server, set config, etc.).
///   </description></item>
/// </list>
/// All public methods are thread-safe and return promptly even when the service
/// is unavailable — callers receive a meaningful exception rather than a hang.
/// </summary>
public sealed class ServiceClient : IDisposable
{
    // ── HTTP ──────────────────────────────────────────────────────────────────
    private readonly HttpClient _http;
    private string _baseUrl = "http://127.0.0.1:47777";

    // ── Named pipe ────────────────────────────────────────────────────────────
    private readonly string _pipeName;

    // ── JSON options ──────────────────────────────────────────────────────────
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ServiceClient(int port = 47777)
    {
        _baseUrl  = $"http://127.0.0.1:{port}";
        _pipeName = $"PerplexityXPC-{Environment.UserName}";

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            BaseAddress = new Uri(_baseUrl + "/"),
        };
        _http.DefaultRequestHeaders.Add("User-Agent", "PerplexityXPC-Tray/1.0");
    }

    // ── Status ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls GET /status and returns the parsed <see cref="ServiceStatus"/>.
    /// Throws if the service is not reachable.
    /// </summary>
    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<StatusResponse>("status", JsonOpts, ct)
                       ?? throw new InvalidOperationException("Empty status response.");

        return response.Status.ToLowerInvariant() switch
        {
            "running"    => ServiceStatus.Running,
            "connecting" => ServiceStatus.Connecting,
            _            => ServiceStatus.Disconnected,
        };
    }

    // ── MCP servers ───────────────────────────────────────────────────────────

    /// <summary>Returns the list of configured MCP servers and their running state.</summary>
    public async Task<List<McpServerInfo>> GetMcpServersAsync(CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<StatusResponse>("status", JsonOpts, ct);
        return response?.McpServers?
            .Select(d => new McpServerInfo { Name = d.Name, Command = d.Command, IsRunning = d.IsRunning })
            .ToList() ?? [];
    }

    /// <summary>Sends a restart command for the named MCP server via the named pipe.</summary>
    public async Task<bool> RestartMcpServerAsync(string name, CancellationToken ct = default)
    {
        return await SendPipeCommandAsync($"RESTART_MCP:{name}", ct);
    }

    // ── Service control ───────────────────────────────────────────────────────

    /// <summary>
    /// Asks the service to start via the Windows Service Control Manager.
    /// Falls back to a pipe command if SCM access is restricted.
    /// </summary>
    public async Task StartServiceAsync(CancellationToken ct = default)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("PerplexityXPC");
            if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                sc.Start();
        }
        catch
        {
            await SendPipeCommandAsync("START_SERVICE", ct);
        }
    }

    /// <summary>Stops the PerplexityXPC background service.</summary>
    public async Task StopServiceAsync(CancellationToken ct = default)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("PerplexityXPC");
            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                sc.Stop();
        }
        catch
        {
            await SendPipeCommandAsync("STOP_SERVICE", ct);
        }
    }

    // ── Config ────────────────────────────────────────────────────────────────

    /// <summary>Fetches the full service configuration dictionary.</summary>
    public async Task<Dictionary<string, object>> GetConfigAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<Dictionary<string, object>>("config", JsonOpts, ct)
               ?? [];
    }

    /// <summary>Sets a single configuration value via POST /config.</summary>
    public async Task<bool> SetConfigAsync(string key, object value, CancellationToken ct = default)
    {
        var payload  = JsonSerializer.Serialize(new { key, value });
        var content  = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("config", content, ct);
        return response.IsSuccessStatusCode;
    }

    // ── Query (one-shot) ──────────────────────────────────────────────────────

    /// <summary>
    /// Sends a non-streaming query and returns the full response text.
    /// </summary>
    public async Task<string> QueryAsync(string query, string model = "sonar",
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await QueryStreamAsync(query, model, token => sb.Append(token), [], ct);
        return sb.ToString();
    }

    // ── Query (streaming SSE) ─────────────────────────────────────────────────

    /// <summary>
    /// Streams a query response using HTTP SSE (Server-Sent Events) from
    /// POST /query/stream.  Each text delta is forwarded to <paramref name="onToken"/>
    /// on the calling thread.  Citations are appended to <paramref name="citations"/>.
    /// </summary>
    public async Task QueryStreamAsync(
        string            query,
        string            model,
        Action<string>    onToken,
        List<string>      citations,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { query, model });
        var request = new HttpRequestMessage(HttpMethod.Post, "query/stream")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        // Use a long timeout for the streaming request
        using var streamHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        streamHttp.BaseAddress = new Uri(_baseUrl + "/");

        using var response = await streamHttp.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // Parse SSE lines
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                string data = line["data: ".Length..];
                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    // Token delta
                    if (root.TryGetProperty("delta", out var delta))
                        onToken(delta.GetString() ?? "");

                    // Citation URLs
                    if (root.TryGetProperty("citations", out var cites))
                        foreach (var cite in cites.EnumerateArray())
                        {
                            string? url = cite.GetString();
                            if (!string.IsNullOrEmpty(url) && !citations.Contains(url))
                                citations.Add(url);
                        }
                }
                catch (JsonException)
                {
                    // Malformed SSE frame — skip
                }
            }
        }
    }

    // ── API key test ──────────────────────────────────────────────────────────

    /// <summary>
    /// Asks the service to validate <paramref name="apiKey"/> against the
    /// Perplexity API.  Returns <c>true</c> when the key is accepted.
    /// </summary>
    public async Task<bool> TestApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        var payload  = JsonSerializer.Serialize(new { apiKey });
        var content  = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("config/test-api-key", content, ct);
        return response.IsSuccessStatusCode;
    }

    // ── Named pipe helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Sends a single-line command to the service via the named pipe and waits
    /// for a one-line <c>OK</c> / <c>ERROR:…</c> response.
    /// </summary>
    private async Task<bool> SendPipeCommandAsync(string command, CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(timeoutMs: 2_000, ct);

        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        await writer.WriteLineAsync(command.AsMemory(), ct);
        string? reply = await reader.ReadLineAsync(ct);

        return reply?.StartsWith("OK", StringComparison.OrdinalIgnoreCase) == true;
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose() => _http.Dispose();
}
