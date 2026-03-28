using System.Text.Json.Serialization;

namespace PerplexityXPC.Service.Models;

/// <summary>
/// Represents a chat completion request to the Perplexity Sonar API.
/// Supports all parameters available to Enterprise Pro Max subscribers.
/// </summary>
public sealed class ChatRequest
{
    // -------------------------------------------------------------------------
    // Required fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// The Sonar model to use for the request.
    /// Valid values: sonar, sonar-pro, sonar-reasoning-pro, sonar-deep-research
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "sonar";

    /// <summary>
    /// The conversation messages to send to the model.
    /// </summary>
    [JsonPropertyName("messages")]
    public List<Message> Messages { get; set; } = [];

    // -------------------------------------------------------------------------
    // Generation parameters
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maximum number of tokens to generate in the response (0-128000).
    /// </summary>
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Whether to stream the response via Server-Sent Events.
    /// </summary>
    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }

    /// <summary>
    /// Stop sequence(s). The model stops generating when it produces this token or
    /// one of these tokens. Can be a single string or an array of strings.
    /// Serialized as an array; a single string value should be wrapped in a list.
    /// </summary>
    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stop { get; set; }

    /// <summary>
    /// Sampling temperature between 0 and 2. Higher values produce more random
    /// output. Default: 0.2.
    /// </summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }

    /// <summary>
    /// Nucleus sampling probability mass between 0 and 1. Default: 0.9.
    /// </summary>
    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }

    /// <summary>
    /// Response format constraint for structured JSON output via json_schema.
    /// </summary>
    [JsonPropertyName("response_format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponseFormat? ResponseFormat { get; set; }

    /// <summary>
    /// Controls how much of the streamed response is sent.
    /// Values: "full" (complete response) or "concise" (summary only).
    /// </summary>
    [JsonPropertyName("stream_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StreamMode { get; set; }

    /// <summary>
    /// Reasoning effort level for sonar-reasoning-pro model only.
    /// Values: "minimal", "low", "medium", "high".
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// Preferred response language as an ISO 639-1 code (e.g., "en", "fr", "de").
    /// </summary>
    [JsonPropertyName("language_preference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LanguagePreference { get; set; }

    // -------------------------------------------------------------------------
    // Search control parameters
    // -------------------------------------------------------------------------

    /// <summary>
    /// Search mode for retrieval.
    /// Values: "web" (default), "academic", "sec".
    /// </summary>
    [JsonPropertyName("search_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchMode { get; set; }

    /// <summary>
    /// When true, disables all web search and the model responds from training
    /// data only.
    /// </summary>
    [JsonPropertyName("disable_search")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DisableSearch { get; set; }

    /// <summary>
    /// When true, enables the search classifier which automatically selects the
    /// best search strategy for the query.
    /// </summary>
    [JsonPropertyName("enable_search_classifier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnableSearchClassifier { get; set; }

    /// <summary>
    /// Additional web search options for fine-grained control.
    /// </summary>
    [JsonPropertyName("web_search_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? WebSearchOptions { get; set; }

    /// <summary>
    /// Whether to include images in the response.
    /// </summary>
    [JsonPropertyName("return_images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReturnImages { get; set; }

    /// <summary>
    /// Whether to include related questions in the response.
    /// </summary>
    [JsonPropertyName("return_related_questions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReturnRelatedQuestions { get; set; }

    /// <summary>
    /// Restrict web search results to specific domains (e.g., ["nytimes.com"]).
    /// </summary>
    [JsonPropertyName("search_domain_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DomainFilter { get; set; }

    /// <summary>
    /// Restrict search results to content in the specified language codes
    /// (ISO 639-1, e.g., ["en", "fr"]).
    /// </summary>
    [JsonPropertyName("search_language_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SearchLanguageFilter { get; set; }

    /// <summary>
    /// Restrict results to a time window relative to now.
    /// Values: "hour", "day", "week", "month", "year".
    /// </summary>
    [JsonPropertyName("search_recency_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecencyFilter { get; set; }

    /// <summary>
    /// Only include results published after this date (MM/DD/YYYY format).
    /// </summary>
    [JsonPropertyName("search_after_date_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchAfterDateFilter { get; set; }

    /// <summary>
    /// Only include results published before this date (MM/DD/YYYY format).
    /// </summary>
    [JsonPropertyName("search_before_date_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchBeforeDateFilter { get; set; }

    /// <summary>
    /// Only include results last updated before this date (MM/DD/YYYY format).
    /// </summary>
    [JsonPropertyName("last_updated_before_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastUpdatedBeforeFilter { get; set; }

    /// <summary>
    /// Only include results last updated after this date (MM/DD/YYYY format).
    /// </summary>
    [JsonPropertyName("last_updated_after_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastUpdatedAfterFilter { get; set; }

    /// <summary>
    /// Restrict returned images to specific file formats (e.g., ["jpg", "png"]).
    /// Only applicable when ReturnImages is true.
    /// </summary>
    [JsonPropertyName("image_format_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ImageFormatFilter { get; set; }

    /// <summary>
    /// Restrict returned images to those hosted on specific domains.
    /// Only applicable when ReturnImages is true.
    /// </summary>
    [JsonPropertyName("image_domain_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ImageDomainFilter { get; set; }
}

/// <summary>
/// A single message in a conversation thread.
/// </summary>
public sealed class Message
{
    /// <summary>
    /// The role of the message author. Valid values: system, user, assistant.
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    /// <summary>
    /// The text content of the message.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Specifies the format of the model's response for structured output.
/// </summary>
public sealed class ResponseFormat
{
    /// <summary>
    /// The format type. Supported value: "json_schema".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "json_schema";

    /// <summary>
    /// The JSON schema definition when type is "json_schema".
    /// </summary>
    [JsonPropertyName("json_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? JsonSchema { get; set; }
}
