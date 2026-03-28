using System.Text.Json.Serialization;

namespace PerplexityXPC.Service.Models;

/// <summary>
/// Represents a chat completion request to the Perplexity Sonar API.
/// </summary>
public sealed class ChatRequest
{
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

    /// <summary>
    /// Sampling temperature between 0 and 2. Higher values produce more random output.
    /// </summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }

    /// <summary>
    /// Nucleus sampling probability mass. Values between 0 and 1.
    /// </summary>
    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }

    /// <summary>
    /// Maximum number of tokens to generate in the response.
    /// </summary>
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Search mode for retrieval. Values: "web" (default) or "academic".
    /// </summary>
    [JsonPropertyName("search_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchMode { get; set; }

    /// <summary>
    /// Restrict web search results to specific domains (e.g. ["nytimes.com", "bbc.com"]).
    /// </summary>
    [JsonPropertyName("search_domain_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DomainFilter { get; set; }

    /// <summary>
    /// Restrict results to a time window. Values: "month", "week", "day", "hour".
    /// </summary>
    [JsonPropertyName("search_recency_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecencyFilter { get; set; }

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
    /// Response format constraint (e.g., JSON schema).
    /// </summary>
    [JsonPropertyName("response_format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponseFormat? ResponseFormat { get; set; }

    /// <summary>
    /// Whether to stream the response via SSE.
    /// </summary>
    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }
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
/// Specifies the format of the model's response.
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
