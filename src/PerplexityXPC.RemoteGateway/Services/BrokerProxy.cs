using System.Net.Http.Json;
using System.Text.Json;
using PerplexityXPC.RemoteGateway.Configuration;

namespace PerplexityXPC.RemoteGateway.Services;

/// <summary>
/// Proxies requests to the local PerplexityXPC broker running on
/// <see cref="RemoteConfig.BrokerUrl"/> (default http://127.0.0.1:47777).
///
/// This service is a thin HTTP forwarding layer; it does not reinterpret
/// broker responses beyond basic error handling.
/// </summary>
public sealed class BrokerProxy
{
    private readonly HttpClient _http;
    private readonly RemoteConfig _config;
    private readonly ILogger<BrokerProxy> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>Initializes the <see cref="BrokerProxy"/>.</summary>
    public BrokerProxy(HttpClient httpClient, RemoteConfig config, ILogger<BrokerProxy> logger)
    {
        _http = httpClient;
        _config = config;
        _logger = logger;

        _http.BaseAddress = new Uri(config.BrokerUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sends a natural-language query to the broker's /perplexity endpoint
    /// and returns the response as a raw JSON string.
    /// </summary>
    /// <param name="query">The user query to process.</param>
    /// <param name="model">Optional Perplexity model override (e.g. "sonar").</param>
    /// <returns>
    /// Raw JSON string returned by the broker, or an error message.
    /// </returns>
    public async Task<string> QueryAsync(string query, string? model = null)
    {
        var payload = new
        {
            query,
            model = model ?? "sonar"
        };

        _logger.LogInformation("Forwarding query to broker: {Query}", query);

        try
        {
            using var response = await _http.PostAsJsonAsync("perplexity", payload);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Broker returned non-success status {Status} for query.",
                    (int)response.StatusCode);
            }

            return body;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reach broker at {Url}.", _config.BrokerUrl);
            return JsonSerializer.Serialize(new
            {
                error = "Broker unreachable",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Retrieves the broker's status object from /status.
    /// </summary>
    public async Task<object> GetStatusAsync()
    {
        _logger.LogDebug("Fetching broker status.");

        try
        {
            using var response = await _http.GetAsync("status");
            string body = await response.Content.ReadAsStringAsync();

            return JsonDocument.Parse(body).RootElement;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch broker status.");
            return new { error = "Broker unreachable", detail = ex.Message };
        }
    }

    /// <summary>
    /// Retrieves the list of registered MCP servers from the broker.
    /// </summary>
    public async Task<object> ListMcpServersAsync()
    {
        _logger.LogDebug("Fetching MCP server list from broker.");

        try
        {
            using var response = await _http.GetAsync("mcp/servers");
            string body = await response.Content.ReadAsStringAsync();

            return JsonDocument.Parse(body).RootElement;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch MCP server list.");
            return new { error = "Broker unreachable", detail = ex.Message };
        }
    }
}
