using System.Text.Json.Serialization;

namespace PerplexityXPC.Service.Models;

/// <summary>
/// Represents the full response from the Perplexity async job API.
/// Used for both job submission responses and job status polling responses.
/// </summary>
public sealed class AsyncJobResponse
{
    /// <summary>
    /// Unique identifier for the async job.
    /// Use this ID to poll for job status via GET /v1/async/sonar/{id}.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The model used for this async job (e.g., "sonar-deep-research").
    /// </summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>
    /// ISO 8601 timestamp when the job was created.
    /// </summary>
    [JsonPropertyName("created_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedAt { get; set; }

    /// <summary>
    /// Current status of the job.
    /// Values: CREATED, IN_PROGRESS, COMPLETED, FAILED.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 timestamp when the job began processing.
    /// Null until the job transitions to IN_PROGRESS.
    /// </summary>
    [JsonPropertyName("started_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartedAt { get; set; }

    /// <summary>
    /// ISO 8601 timestamp when the job completed successfully.
    /// Null unless status is COMPLETED.
    /// </summary>
    [JsonPropertyName("completed_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletedAt { get; set; }

    /// <summary>
    /// ISO 8601 timestamp when the job failed.
    /// Null unless status is FAILED.
    /// </summary>
    [JsonPropertyName("failed_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailedAt { get; set; }

    /// <summary>
    /// Human-readable error message describing why the job failed.
    /// Null unless status is FAILED.
    /// </summary>
    [JsonPropertyName("error_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The full chat response payload when status is COMPLETED.
    /// Null for jobs that are still CREATED or IN_PROGRESS.
    /// </summary>
    [JsonPropertyName("response")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatResponse? Response { get; set; }
}

/// <summary>
/// A lightweight summary of an async job returned in list responses.
/// Does not include the full response payload.
/// </summary>
public sealed class AsyncJobSummary
{
    /// <summary>
    /// Unique identifier for the async job.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 timestamp when the job was created.
    /// </summary>
    [JsonPropertyName("created_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedAt { get; set; }

    /// <summary>
    /// The model used for this async job.
    /// </summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>
    /// Current status of the job.
    /// Values: CREATED, IN_PROGRESS, COMPLETED, FAILED.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 timestamp when the job began processing.
    /// </summary>
    [JsonPropertyName("started_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartedAt { get; set; }

    /// <summary>
    /// ISO 8601 timestamp when the job completed successfully.
    /// </summary>
    [JsonPropertyName("completed_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletedAt { get; set; }

    /// <summary>
    /// ISO 8601 timestamp when the job failed.
    /// </summary>
    [JsonPropertyName("failed_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailedAt { get; set; }
}
