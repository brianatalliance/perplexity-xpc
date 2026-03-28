using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PerplexityXPC.Service.Models;

namespace PerplexityXPC.Service.Services;

/// <summary>
/// Registers all HTTP/WebSocket routes for the local broker.
/// Bound exclusively to 127.0.0.1:47777 via Kestrel configuration in Program.cs.
///
/// All routes use minimal API style (app.MapXxx).
/// </summary>
public static class HttpBroker
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly DateTimeOffset ServiceStartTime = DateTimeOffset.UtcNow;

    /// <summary>
    /// Maps all broker routes onto the provided <see cref="WebApplication"/>.
    /// Call this from Program.cs after building the app.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    public static void MapRoutes(WebApplication app)
    {
        // -----------------------------------------------------------------------
        // Perplexity API proxy
        // -----------------------------------------------------------------------

        /// <summary>Proxy a non-streaming chat request to the Perplexity Sonar API.</summary>
        app.MapPost("/perplexity", async (
            [FromBody] ChatRequest request,
            PerplexityApiClient apiClient,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            logger.LogDebug("POST /perplexity model={Model}", request.Model);

            try
            {
                var response = await apiClient.ChatAsync(request, ct);
                return Results.Ok(response);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Perplexity API call failed.");
                return Results.Problem(
                    title: "Upstream API Error",
                    detail: ex.Message,
                    statusCode: (int)(ex.StatusCode ?? HttpStatusCode.BadGateway));
            }
        })
        .WithName("PerplexityChat");

        /// <summary>
        /// Proxy a streaming chat request to Perplexity and forward as Server-Sent Events (SSE).
        /// The client should handle text/event-stream responses.
        /// </summary>
        app.MapPost("/perplexity/stream", async (
            [FromBody] ChatRequest request,
            PerplexityApiClient apiClient,
            HttpContext ctx,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            logger.LogDebug("POST /perplexity/stream model={Model}", request.Model);

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            try
            {
                await foreach (var chunk in apiClient.ChatStreamAsync(request, ct))
                {
                    // Relay SSE data lines verbatim
                    var line = $"data: {chunk}\n\n";
                    await ctx.Response.WriteAsync(line, Encoding.UTF8, ct);
                    await ctx.Response.Body.FlushAsync(ct);

                    if (chunk == "[DONE]") break;
                }
            }
            catch (OperationCanceledException) { /* Client disconnected */ }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Streaming Perplexity API call failed.");
                var errorLine = $"data: {{\"error\":\"{ex.Message}\"}}\n\n";
                await ctx.Response.WriteAsync(errorLine, Encoding.UTF8, ct);
            }
        })
        .WithName("PerplexityChatStream");

        // -----------------------------------------------------------------------
        // Async job endpoints (sonar-deep-research and long-running queries)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Submit a request as an async job. Use this for sonar-deep-research
        /// queries that may take minutes to complete.
        /// Returns an AsyncJobResponse with the job ID and initial status CREATED.
        /// </summary>
        app.MapPost("/perplexity/async", async (
            [FromBody] ChatRequest request,
            PerplexityApiClient apiClient,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            logger.LogDebug(
                "POST /perplexity/async model={Model}",
                request.Model);

            try
            {
                var job = await apiClient.SubmitAsyncAsync(request, ct);
                return Results.Ok(job);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Async job submission failed.");
                return Results.Problem(
                    title: "Async Job Submission Failed",
                    detail: ex.Message,
                    statusCode: (int)(ex.StatusCode ?? HttpStatusCode.BadGateway));
            }
        })
        .WithName("PerplexityAsyncSubmit");

        /// <summary>
        /// List all async jobs for the configured API key.
        /// Returns a list of AsyncJobSummary objects.
        /// </summary>
        app.MapGet("/perplexity/async", async (
            PerplexityApiClient apiClient,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            logger.LogDebug("GET /perplexity/async");

            try
            {
                var jobs = await apiClient.ListAsyncJobsAsync(ct);
                return Results.Ok(jobs);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to list async jobs.");
                return Results.Problem(
                    title: "List Async Jobs Failed",
                    detail: ex.Message,
                    statusCode: (int)(ex.StatusCode ?? HttpStatusCode.BadGateway));
            }
        })
        .WithName("PerplexityAsyncList");

        /// <summary>
        /// Get the status and result of a specific async job by ID.
        /// Poll this endpoint until status is COMPLETED or FAILED.
        /// When COMPLETED, the full ChatResponse is included in the response field.
        /// </summary>
        app.MapGet("/perplexity/async/{id}", async (
            string id,
            PerplexityApiClient apiClient,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            logger.LogDebug("GET /perplexity/async/{JobId}", id);

            try
            {
                var job = await apiClient.GetAsyncJobAsync(id, ct);
                return Results.Ok(job);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to get async job {JobId}.", id);
                return Results.Problem(
                    title: "Get Async Job Failed",
                    detail: ex.Message,
                    statusCode: (int)(ex.StatusCode ?? HttpStatusCode.BadGateway));
            }
        })
        .WithName("PerplexityAsyncGet");

        // -----------------------------------------------------------------------
        // MCP proxy endpoints
        // -----------------------------------------------------------------------

        /// <summary>Send a request to a configured MCP server via the broker.</summary>
        app.MapPost("/mcp", async (
            [FromBody] McpProxyRequest request,
            McpServerManager mcpManager,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            logger.LogDebug("POST /mcp server={Server} method={Method}", request.Server, request.Method);

            try
            {
                // Rebuild the JSON element the McpServerManager expects
                var rpcPayload = JsonSerializer.SerializeToElement(new
                {
                    method = request.Method,
                    @params = request.Params
                }, JsonOpts);

                var result = await mcpManager.SendRequestAsync(request.Server, rpcPayload, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: "MCP Server Not Available", detail: ex.Message, statusCode: 503);
            }
            catch (McpException ex)
            {
                return Results.Problem(
                    title: "MCP Error",
                    detail: ex.Message,
                    statusCode: 502,
                    extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
            }
        })
        .WithName("McpProxy");

        /// <summary>List all configured MCP servers and their current runtime status.</summary>
        app.MapGet("/mcp/servers", async (
            McpServerManager mcpManager,
            CancellationToken ct) =>
        {
            var servers = await mcpManager.ListServersAsync();
            return Results.Ok(servers);
        })
        .WithName("McpListServers");

        /// <summary>Restart a specific MCP server by name.</summary>
        app.MapPost("/mcp/servers/{name}/restart", async (
            string name,
            McpServerManager mcpManager,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation("POST /mcp/servers/{Name}/restart", name);

            try
            {
                await mcpManager.RestartServerAsync(name, ct);
                return Results.Ok(new { restarted = true, server = name });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to restart MCP server '{Name}'.", name);
                return Results.Problem(title: "Restart Failed", detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("McpRestartServer");

        // -----------------------------------------------------------------------
        // Service status and configuration
        // -----------------------------------------------------------------------

        /// <summary>Service health check. Returns version, uptime, and MCP server summary.</summary>
        app.MapGet("/status", async (
            McpServerManager mcpManager,
            CancellationToken ct) =>
        {
            var servers = await mcpManager.ListServersAsync();
            var uptime = DateTimeOffset.UtcNow - ServiceStartTime;

            return Results.Ok(new
            {
                version = "1.2.0",
                status = "ok",
                uptime = FormatUptime(uptime),
                started_at = ServiceStartTime,
                mcp_servers = servers
            });
        })
        .WithName("ServiceStatus");

        /// <summary>Return non-sensitive configuration values.</summary>
        app.MapGet("/config", (
            Configuration.AppConfig config) =>
        {
            // ApiKey is intentionally excluded from all HTTP responses
            return Results.Ok(new
            {
                port = config.Port,
                pipe_name = config.PipeName,
                log_level = config.LogLevel,
                mcp_servers_path = config.McpServersPath,
                auto_start_mcp_servers = config.AutoStartMcpServers,
                max_retries = config.MaxRetries,
                retry_delay_ms = config.RetryDelayMs,
                api_timeout_seconds = config.ApiTimeoutSeconds,
                perplexity_api_base_url = config.PerplexityApiBaseUrl
            });
        })
        .WithName("GetConfig");

        /// <summary>
        /// Update mutable configuration values at runtime.
        /// Note: Some settings (port, pipe name) require a service restart to take effect.
        /// </summary>
        app.MapPut("/config", (
            [FromBody] JsonElement body,
            Configuration.AppConfig config,
            ILogger<WebApplication> logger) =>
        {
            logger.LogInformation("PUT /config requested.");

            // Apply supported mutable settings
            if (body.TryGetProperty("log_level", out var logLevel))
                config.LogLevel = logLevel.GetString() ?? config.LogLevel;

            if (body.TryGetProperty("auto_start_mcp_servers", out var autoStart))
                config.AutoStartMcpServers = autoStart.GetBoolean();

            if (body.TryGetProperty("max_retries", out var maxRetries))
                config.MaxRetries = maxRetries.GetInt32();

            if (body.TryGetProperty("retry_delay_ms", out var retryDelay))
                config.RetryDelayMs = retryDelay.GetInt32();

            return Results.Ok(new
            {
                updated = true,
                note = "Port and pipe name changes require a service restart."
            });
        })
        .WithName("UpdateConfig");

        // -----------------------------------------------------------------------
        // WebSocket endpoint for streaming
        // -----------------------------------------------------------------------

        /// <summary>
        /// WebSocket endpoint for streaming Perplexity responses.
        /// Clients send a JSON ChatRequest, receive JSON-encoded SSE chunks, then a {"done":true} message.
        /// </summary>
        app.MapGet("/ws", async (
            HttpContext ctx,
            PerplexityApiClient apiClient,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("WebSocket connection required.", ct);
                return;
            }

            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            logger.LogDebug("WebSocket client connected from {Endpoint}.", ctx.Connection.RemoteIpAddress);

            try
            {
                await HandleWebSocketAsync(ws, apiClient, logger, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WebSocket session ended with error.");
            }
        })
        .WithName("WebSocketStream");
    }

    /// <summary>
    /// Handles a WebSocket session: reads one ChatRequest, streams response chunks back.
    /// </summary>
    private static async Task HandleWebSocketAsync(
        System.Net.WebSockets.WebSocket ws,
        PerplexityApiClient apiClient,
        ILogger logger,
        CancellationToken ct)
    {
        var buffer = new byte[8192];
        var msgBuilder = new StringBuilder();

        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            // Read a full message (may span multiple frames)
            msgBuilder.Clear();
            System.Net.WebSockets.WebSocketReceiveResult result;

            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        "Closing", ct);
                    return;
                }

                msgBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            var message = msgBuilder.ToString();

            ChatRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ChatRequest>(message, JsonOpts);
            }
            catch (JsonException ex)
            {
                var errBytes = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { error = "invalid_json", detail = ex.Message }));
                await ws.SendAsync(errBytes, System.Net.WebSockets.WebSocketMessageType.Text, true, ct);
                continue;
            }

            if (request is null) continue;

            // Stream chunks back to WebSocket client
            await foreach (var chunk in apiClient.ChatStreamAsync(request, ct))
            {
                if (chunk == "[DONE]") break;

                var chunkBytes = Encoding.UTF8.GetBytes(chunk);
                await ws.SendAsync(
                    chunkBytes,
                    System.Net.WebSockets.WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct);
            }

            // Signal completion
            var doneBytes = Encoding.UTF8.GetBytes("{\"done\":true}");
            await ws.SendAsync(
                doneBytes,
                System.Net.WebSockets.WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
    }

    private static string FormatUptime(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s"
            : $"{ts.Minutes}m {ts.Seconds}s";
}
