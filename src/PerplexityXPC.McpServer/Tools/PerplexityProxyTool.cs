using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PerplexityXPC.McpServer.Configuration;
using PerplexityXPC.McpServer.Protocol;

namespace PerplexityXPC.McpServer.Tools;

/// <summary>
/// Proxies queries to the local PerplexityXPC broker service running on 127.0.0.1:47777.
/// The broker handles authentication and forwarding to the Perplexity AI API.
/// </summary>
public sealed class PerplexityProxyTool : IDisposable
{
    private readonly McpServerConfig _config;
    private readonly HttpClient _http;

    /// <summary>Initialises the proxy tool with the server configuration.</summary>
    public PerplexityProxyTool(McpServerConfig config)
    {
        _config = config;
        _http   = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    // -------------------------------------------------------------------------
    //  Tool definitions
    // -------------------------------------------------------------------------

    /// <summary>Returns MCP tool definitions for the Perplexity proxy operations.</summary>
    public IEnumerable<McpTool> GetToolDefinitions()
    {
        yield return new McpTool
        {
            Name        = "perplexity.query",
            Description = "Send a query through the local PerplexityXPC broker to the Perplexity AI API.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    query = new { type = "string",  description = "The question or prompt to send to Perplexity AI." },
                    model = new { type = "string",  description = "Model to use. Default: sonar. Options: sonar, sonar-pro, sonar-reasoning." },
                },
                required = new[] { "query" },
            },
        };

        yield return new McpTool
        {
            Name        = "perplexity.status",
            Description = "Check the status of the local PerplexityXPC broker service.",
            InputSchema = new { type = "object", properties = new { } },
        };
    }

    // -------------------------------------------------------------------------
    //  Operations
    // -------------------------------------------------------------------------

    /// <summary>Sends a query to the broker and returns the AI response.</summary>
    public ToolCallResult Query(JsonElement args)
    {
        try
        {
            var query = GetRequiredString(args, "query");
            var model = GetString(args, "model", "sonar");

            var payload = new
            {
                query,
                model,
            };

            var json    = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(55));
            var response  = _http.PostAsync($"{_config.BrokerUrl}/perplexity", content, cts.Token)
                                 .GetAwaiter().GetResult();

            var responseBody = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return ToolCallResult.Failure(
                    $"Broker returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.\n{responseBody}");
            }

            // Try to extract the answer text from the response
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                // Support both {answer:...} and {choices:[{message:{content:...}}]} shapes
                if (root.TryGetProperty("answer", out var answerProp))
                    return ToolCallResult.Success(answerProp.GetString() ?? responseBody);

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var textProp))
                    return ToolCallResult.Success(textProp.GetString() ?? responseBody);
            }
            catch
            {
                // Fall through and return raw body
            }

            return ToolCallResult.Success(responseBody);
        }
        catch (HttpRequestException ex)
        {
            return ToolCallResult.Failure(
                $"Could not connect to PerplexityXPC broker at {_config.BrokerUrl}. " +
                $"Ensure the PerplexityXPC service is running. Details: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ToolCallResult.Failure("Query timed out after 55 seconds.");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error querying Perplexity: {ex.Message}");
        }
    }

    /// <summary>Checks broker health and returns status information.</summary>
    public ToolCallResult Status(JsonElement args)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response  = _http.GetAsync($"{_config.BrokerUrl}/status", cts.Token)
                                 .GetAwaiter().GetResult();
            var body      = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();

            var sb = new StringBuilder();
            sb.AppendLine($"Broker URL:  {_config.BrokerUrl}");
            sb.AppendLine($"HTTP Status: {(int)response.StatusCode} {response.ReasonPhrase}");
            sb.AppendLine();
            sb.AppendLine("Response:");
            sb.AppendLine(body);

            return ToolCallResult.Success(sb.ToString());
        }
        catch (HttpRequestException ex)
        {
            return ToolCallResult.Failure(
                $"Broker not reachable at {_config.BrokerUrl}. " +
                $"Ensure PerplexityXPC is installed and running. Details: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ToolCallResult.Failure($"Status check timed out. Broker may be down at {_config.BrokerUrl}.");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error checking broker status: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private static string GetRequiredString(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var p) || p.ValueKind == JsonValueKind.Null)
            throw new ToolException($"Required parameter '{key}' is missing.", JsonRpcError.Codes.InvalidParams);
        var v = p.GetString();
        if (string.IsNullOrWhiteSpace(v))
            throw new ToolException($"Parameter '{key}' must not be empty.", JsonRpcError.Codes.InvalidParams);
        return v;
    }

    private static string? GetString(JsonElement args, string key, string? defaultValue)
    {
        if (args.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? defaultValue;
        return defaultValue;
    }
}
