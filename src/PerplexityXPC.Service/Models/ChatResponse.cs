using System.Text.Json.Serialization;

namespace PerplexityXPC.Service.Models;

/// <summary>
/// Represents a chat completion response from the Perplexity Sonar API.
/// </summary>
public sealed class ChatResponse
{
    /// <summary>
    /// Unique identifier for the completion.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The object type. Always "chat.completion".
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
    /// Source URLs cited in the model's response.
    /// </summary>
    [JsonPropertyName("citations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Citations { get; set; }

    /// <summary>
    /// Token usage statistics for this request.
    /// </summary>
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UsageInfo? Usage { get; set; }

    /// <summary>
    /// Web search results used to ground the response.
    /// </summary>
    [JsonPropertyName("search_results")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SearchResult>? SearchResults { get; set; }

    /// <summary>
    /// Related questions suggested by the model.
    /// </summary>
    [JsonPropertyName("related_questions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RelatedQuestions { get; set; }

    /// <summary>
    /// Images returned when return_images is true.
    /// </summary>
    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ImageResult>? Images { get; set; }
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
    /// The generated message.
    /// </summary>
    [JsonPropertyName("message")]
    public Message Message { get; set; } = new();

    /// <summary>
    /// The delta for streaming responses.
    /// </summary>
    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MessageDelta? Delta { get; set; }

    /// <summary>
    /// Reason the model stopped generating. Values: stop, length, content_filter.
    /// </summary>
    [JsonPropertyName("finish_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishReason { get; set; }
}

/// <summary>
/// Streaming delta content for a choice.
/// </summary>
public sealed class MessageDelta
{
    /// <summary>
    /// The role of the message author in this delta.
    /// </summary>
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    /// <summary>
    /// The partial content chunk in this delta.
    /// </summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
}

/// <summary>
/// Token usage statistics for a completion request.
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

    /// <summary>A brief excerpt from the page.</summary>
    [JsonPropertyName("snippet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snippet { get; set; }

    /// <summary>Publication or last-updated date of the page.</summary>
    [JsonPropertyName("date")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Date { get; set; }
}

/// <summary>
/// An image result returned when return_images is true.
/// </summary>
public sealed class ImageResult
{
    /// <summary>The image URL.</summary>
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
