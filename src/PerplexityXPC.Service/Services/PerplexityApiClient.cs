using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PerplexityXPC.Service.Configuration;
using PerplexityXPC.Service.Models;

namespace PerplexityXPC.Service.Services;

/// <summary>
/// HTTP client wrapper for the Perplexity Sonar API.
/// Supports both blocking and streaming chat completions with automatic retry logic.
/// </summary>
public sealed class PerplexityApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private readonly ILogger<PerplexityApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Supported Sonar model identifiers.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedModels =
    [
        "sonar",
        "sonar-pro",
        "sonar-reasoning-pro",
        "sonar-deep-research"
    ];

    /// <summary>
    /// Initializes the Perplexity API client with the provided configuration and HTTP client factory.
    /// </summary>
    /// <param name="httpClient">Configured HttpClient instance (injected by DI).</param>
    /// <param name="config">Application configuration containing the API key and settings.</param>
    /// <param name="logger">Logger for request/response diagnostics.</param>
    public PerplexityApiClient(
        HttpClient httpClient,
        IOptions<AppConfig> config,
        ILogger<PerplexityApiClient> logger)
    {
        _config = config.Value;
        _logger = logger;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_config.PerplexityApiBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.ApiTimeoutSeconds);

        ConfigureAuthorization();
    }

    /// <summary>
    /// Sends a non-streaming chat request to the Perplexity Sonar API.
    /// Retries on HTTP 429 (rate limited) and 503 (service unavailable) with exponential backoff.
    /// </summary>
    /// <param name="request">The chat request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The complete chat response from the API.</returns>
    /// <exception cref="HttpRequestException">Thrown when all retry attempts are exhausted.</exception>
    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        // Ensure stream is disabled for non-streaming calls
        request.Stream = false;

        ValidateRequest(request);

        return await ExecuteWithRetryAsync(async () =>
        {
            using var jsonContent = JsonContent.Create(request, options: JsonOptions);
            using var response = await _httpClient.PostAsync("/chat/completions", jsonContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Perplexity API error {StatusCode}: {Body}",
                    (int)response.StatusCode, errorBody);

                // Let retry logic handle 429/503
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
            return result ?? throw new InvalidOperationException("Received null response from Perplexity API.");
        }, ct);
    }

    /// <summary>
    /// Sends a streaming chat request to the Perplexity Sonar API.
    /// Yields raw Server-Sent Event data lines as they arrive.
    /// Each yielded string is a "data: ..." line (without the "data: " prefix).
    /// The special "[DONE]" token signals end of stream.
    /// </summary>
    /// <param name="request">The chat request payload (stream will be forced to true).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of SSE data chunks.</returns>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        request.Stream = true;
        ValidateRequest(request);

        _logger.LogDebug("Starting streaming request with model {Model}", request.Model);

        var jsonBody = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        // Use ResponseHeadersRead to avoid buffering the full stream
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Streaming request failed {StatusCode}: {Body}",
                (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break; // Stream closed

            if (string.IsNullOrWhiteSpace(line)) continue; // SSE heartbeat

            if (line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
            {
                var data = line["data: ".Length..];
                yield return data; // Let caller decide whether data == "[DONE]"
                if (data == "[DONE]") break;
            }
        }
    }

    /// <summary>
    /// Updates the Authorization header when the API key changes at runtime.
    /// </summary>
    public void RefreshApiKey()
    {
        ConfigureAuthorization();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ConfigureAuthorization()
    {
        // Prefer environment variable PERPLEXITY_API_KEY over config file
        var apiKey = Environment.GetEnvironmentVariable("PERPLEXITY_API_KEY")
                     ?? _config.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "No Perplexity API key configured. Set PERPLEXITY_API_KEY environment variable " +
                "or PerplexityXPC:ApiKey in appsettings.json.");
        }

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private void ValidateRequest(ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new ArgumentException("Model must be specified.", nameof(request));

        if (request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        if (!SupportedModels.Contains(request.Model))
        {
            _logger.LogWarning(
                "Model '{Model}' is not in the known supported models list. Proceeding anyway.",
                request.Model);
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken ct)
    {
        var attempt = 0;
        var delay = _config.RetryDelayMs;

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex)
                when (attempt < _config.MaxRetries &&
                      ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                          or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                attempt++;
                _logger.LogWarning(
                    "Perplexity API returned {Status} (attempt {Attempt}/{Max}). " +
                    "Retrying in {Delay}ms.",
                    ex.StatusCode, attempt, _config.MaxRetries, delay);

                await Task.Delay(delay, ct);
                delay *= 2; // Exponential backoff
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _httpClient.Dispose();
}
