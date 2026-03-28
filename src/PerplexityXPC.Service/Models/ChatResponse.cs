using System.Text.Json.Serialization;

namespace PerplexityXPC.Service.Models;

/// <summary>
/// Represents a chat completion response from the Perplexity Sonar API.
/// Covers all fields returned for Enterprise Pro Max subscribers.
/// </summary>
public sealed class ChatResponse
{
    /// <summary>
    /// Unique identifier for the completion.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The object type. Always "chat.completion" for non-streaming responses.
    /// </summary>
    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";

    /// <summary>
    /// Unix timestamp of when the completion was created.
    /// </summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>
    /// The model used for this completion.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// List of completion choices returned by the model.
    /// </summary>
    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = [];

    /// <summary>
    /// Token usage statistics and cost breakdown for this request.
    /// </summary>
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UsageInfo? Usage { get; set; }

    /// <summary>
    /// Source URLs cited in the model's response.
    /// </summary>
    [JsonPropertyName("citations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Citations { get; set; }

    /// <summary>
    /// Web search results used to ground the response.
    /// Populated when search_results are included in the upstream response.
    /// </summary>
    [JsonPropertyName("search_results")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SearchResult>? SearchResults { get; set; }

    /// <summary>
    /// Images returned when return_images is true in the request.
    /// </summary>
    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ImageResult>? Images { get; set; }

    /// <summary>
    /// Related questions suggested by the model.
    /// Populated when return_related_questions is true in the request.
    /// </summary>
    [JsonPropertyName("related_questions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RelatedQuestions { get; set; }
}

/// <summary>
/// A single completion choice returned by the model.
/// </summary>
public sealed class Choice
{
    /// <summary>
    /// Zero-based index of this choice.
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// The generated message (non-streaming responses).
    /// </summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Message? Message { get; set; }

    /// <summary>
    /// The delta content chunk for streaming responses.
    /// Present only in streaming (SSE) mode.
    /// </summary>
    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MessageDelta? Delta { get; set; }

    /// <summary>
    /// Reason the model stopped generating.
    /// Values: stop, length, content_filter.
    /// </summary>
    [JsonPropertyName("finish_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishReason { get; set; }
}

/// <summary>
/// Streaming delta content for a choice in an SSE response.
/// </summary>
public sealed class MessageDelta
{
    /// <summary>
    /// The role of the message author in this delta.
    /// Present only in the first chunk.
    /// </summary>
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    /// <summary>
    /// The partial content chunk for this delta event.
    /// </summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
}

/// <summary>
/// Token usage statistics and cost breakdown for a completion request.
/// </summary>
public sealed class UsageInfo
{
    /// <summary>Number of tokens in the prompt.</summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    /// <summary>Number of tokens generated in the completion.</summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    /// <summary>Total tokens consumed (prompt + completion).</summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    /// <summary>
    /// The size category of the search context used (e.g., "low", "medium", "high").
    /// Enterprise-specific field.
    /// </summary>
    [JsonPropertyName("search_context_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchContextSize { get; set; }

    /// <summary>
    /// Detailed cost breakdown for this request. Enterprise Pro Max field.
    /// </summary>
    [JsonPropertyName("cost")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CostBreakdown? Cost { get; set; }
}

/// <summary>
/// Detailed cost breakdown for an API request (Enterprise Pro Max).
/// </summary>
public sealed class CostBreakdown
{
    /// <summary>Cost attributed to input/prompt tokens.</summary>
    [JsonPropertyName("input_tokens_cost")]
    public double InputTokensCost { get; set; }

    /// <summary>Cost attributed to output/completion tokens.</summary>
    [JsonPropertyName("output_tokens_cost")]
    public double OutputTokensCost { get; set; }

    /// <summary>Fixed per-request cost.</summary>
    [JsonPropertyName("request_cost")]
    public double RequestCost { get; set; }

    /// <summary>Total cost for this request (input + output + request).</summary>
    [JsonPropertyName("total_cost")]
    public double TotalCost { get; set; }
}

/// <summary>
/// A web search result used to ground the model's response.
/// </summary>
public sealed class SearchResult
{
    /// <summary>The title of the web page.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>The URL of the web page.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Publication or indexed date of the page (ISO 8601 or human-readable).</summary>
    [JsonPropertyName("date")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Date { get; set; }

    /// <summary>The date the page was last updated, if available.</summary>
    [JsonPropertyName("last_updated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastUpdated { get; set; }

    /// <summary>A brief excerpt or summary from the page.</summary>
    [JsonPropertyName("snippet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snippet { get; set; }
}

/// <summary>
/// An image result returned when return_images is true in the request.
/// </summary>
public sealed class ImageResult
{
    /// <summary>The direct URL to the image file.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Description or alt text for the image.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>The source page URL where this image was found.</summary>
    [JsonPropertyName("origin_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginUrl { get; set; }
}
