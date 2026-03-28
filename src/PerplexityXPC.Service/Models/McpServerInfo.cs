using System.Text.Json.Serialization;

namespace PerplexityXPC.Service.Models;

/// <summary>
/// Runtime status information for a running (or stopped) MCP server.
/// Returned by the /mcp/servers endpoint and ListServersAsync.
/// </summary>
public sealed class McpServerInfo
{
    /// <summary>
    /// The unique name of the MCP server as defined in mcp-servers.json.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Current operational status of the server process.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public McpServerStatus Status { get; set; } = McpServerStatus.Stopped;

    /// <summary>
    /// The OS process ID of the running server, or null if not running.
    /// </summary>
    [JsonPropertyName("pid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pid { get; set; }

    /// <summary>
    /// How long the server has been running in the current session.
    /// Null if the server is not running.
    /// </summary>
    [JsonPropertyName("uptime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uptime { get; set; }

    /// <summary>
    /// The last error message if the server is in Error state, or the most recent crash reason.
    /// </summary>
    [JsonPropertyName("last_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastError { get; set; }

    /// <summary>
    /// Number of consecutive restart attempts since the last successful start.
    /// </summary>
    [JsonPropertyName("restart_attempts")]
    public int RestartAttempts { get; set; }

    /// <summary>
    /// Timestamp of when the server was last started (ISO 8601 UTC).
    /// </summary>
    [JsonPropertyName("started_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// The command used to start this server, for display/debugging.
    /// </summary>
    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }
}

/// <summary>
/// Operational status values for an MCP server process.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum McpServerStatus
{
    /// <summary>The server process is running and accepting requests.</summary>
    Running,

    /// <summary>The server is not currently running.</summary>
    Stopped,

    /// <summary>The server crashed or failed to start; may be restarting.</summary>
    Error,

    /// <summary>The server is in the process of starting up.</summary>
    Starting,

    /// <summary>The server is in the process of stopping.</summary>
    Stopping
}

/// <summary>
/// JSON-RPC 2.0 request envelope used to communicate with MCP servers via stdio.
/// </summary>
public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object Id { get; set; } = 0;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? Params { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 response envelope from an MCP server.
/// </summary>
public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public System.Text.Json.JsonElement Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 error object.
/// </summary>
public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? Data { get; set; }
}

/// <summary>
/// Request body for the POST /mcp endpoint.
/// </summary>
public sealed class McpProxyRequest
{
    /// <summary>The name of the target MCP server.</summary>
    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    /// <summary>The MCP method to invoke (e.g., "tools/list", "tools/call").</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>Optional parameters for the method.</summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? Params { get; set; }
}
