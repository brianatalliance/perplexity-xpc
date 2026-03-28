using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerplexityXPC.McpServer.Protocol;

/// <summary>
/// JSON-RPC 2.0 request message.
/// </summary>
public sealed class JsonRpcRequest
{
    /// <summary>Gets or sets the JSON-RPC version string, always "2.0".</summary>
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    /// <summary>Gets or sets the request identifier (number or string).</summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    /// <summary>Gets or sets the method name to invoke.</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>Gets or sets the method parameters.</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 response message.
/// </summary>
public sealed class JsonRpcResponse
{
    /// <summary>Gets or sets the JSON-RPC version string, always "2.0".</summary>
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    /// <summary>Gets or sets the request identifier echoed from the request.</summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    /// <summary>Gets or sets the successful result payload. Mutually exclusive with Error.</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    /// <summary>Gets or sets the error payload. Mutually exclusive with Result.</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 error object.
/// </summary>
public sealed class JsonRpcError
{
    /// <summary>Gets or sets the error code. Standard codes: -32700 parse error, -32600 invalid request, -32601 method not found, -32602 invalid params, -32603 internal error.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>Gets or sets the short human-readable error description.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets optional additional error data.</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }

    // Standard JSON-RPC error codes
    public static class Codes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }
}

/// <summary>
/// MCP tool definition with JSON Schema input specification.
/// </summary>
public sealed class McpTool
{
    /// <summary>Gets or sets the tool name (dot-separated, e.g. "filesystem.read_file").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable tool description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON Schema object describing the tool's input parameters.</summary>
    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = new { type = "object", properties = new { } };
}

/// <summary>
/// Content block returned in a tool call result.
/// </summary>
public sealed class ToolContentBlock
{
    /// <summary>Gets or sets the content type, typically "text".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>Gets or sets the text payload.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Result returned from a tools/call invocation.
/// </summary>
public sealed class ToolCallResult
{
    /// <summary>Gets or sets the content blocks comprising the tool output.</summary>
    [JsonPropertyName("content")]
    public List<ToolContentBlock> Content { get; set; } = [];

    /// <summary>Gets or sets whether the tool call resulted in an error.</summary>
    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; set; }

    /// <summary>Creates a successful text result.</summary>
    public static ToolCallResult Success(string text) =>
        new() { Content = [new ToolContentBlock { Type = "text", Text = text }] };

    /// <summary>Creates an error result with the given message.</summary>
    public static ToolCallResult Failure(string message) =>
        new() { IsError = true, Content = [new ToolContentBlock { Type = "text", Text = message }] };
}

/// <summary>
/// MCP server capabilities returned during initialization.
/// </summary>
public sealed class McpServerCapabilities
{
    [JsonPropertyName("tools")]
    public McpToolsCapability Tools { get; set; } = new();
}

/// <summary>
/// Indicates that the server supports the tools capability.
/// </summary>
public sealed class McpToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; } = false;
}

/// <summary>
/// MCP server information returned during initialization.
/// </summary>
public sealed class McpServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "PerplexityXPC.McpServer";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.4.0";
}

/// <summary>
/// Response payload for the "initialize" method.
/// </summary>
public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public McpServerInfo ServerInfo { get; set; } = new();
}

/// <summary>
/// Response payload for the "tools/list" method.
/// </summary>
public sealed class ToolsListResult
{
    [JsonPropertyName("tools")]
    public List<McpTool> Tools { get; set; } = [];
}
