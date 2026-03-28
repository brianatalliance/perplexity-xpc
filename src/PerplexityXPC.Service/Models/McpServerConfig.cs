using System.Text.Json.Serialization;

namespace PerplexityXPC.Service.Models;

/// <summary>
/// Configuration definition for a single MCP (Model Context Protocol) stdio server.
/// Persisted in mcp-servers.json.
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>
    /// Unique name for this MCP server (used as the key in API calls).
    /// Example: "filesystem", "brave-search"
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The executable to launch.
    /// Example: "npx", "node", "python"
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Command-line arguments passed to the executable.
    /// Example: ["-y", "@anthropic/mcp-filesystem-server", "C:\\Users"]
    /// </summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// Additional environment variables to inject into the server process.
    /// These are merged on top of the current process environment.
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>
    /// Whether to automatically start this server when the Windows Service starts.
    /// Defaults to true.
    /// </summary>
    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// Optional working directory for the server process.
    /// Defaults to the user's home directory if not set.
    /// </summary>
    [JsonPropertyName("working_directory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }
}

/// <summary>
/// Root schema for the mcp-servers.json configuration file.
/// </summary>
public sealed class McpServersFile
{
    /// <summary>
    /// List of MCP server definitions.
    /// </summary>
    [JsonPropertyName("servers")]
    public List<McpServerConfig> Servers { get; set; } = [];
}
