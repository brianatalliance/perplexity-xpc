namespace PerplexityXPC.Service.Configuration;

/// <summary>
/// Strongly-typed application configuration.
/// Populated from appsettings.json with overrides from PERPLEXITYXPC_* environment variables.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "PerplexityXPC";

    /// <summary>
    /// Perplexity Sonar API key.
    /// Set via environment variable PERPLEXITY_API_KEY or PerplexityXPC:ApiKey in appsettings.json.
    /// Never exposed through HTTP endpoints.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// TCP port for the local HTTP/WebSocket listener.
    /// Default: 47777. Bound to 127.0.0.1 only.
    /// Override via PERPLEXITYXPC_PORT environment variable.
    /// </summary>
    public int Port { get; set; } = 47777;

    /// <summary>
    /// Named pipe name suffix. Full pipe path: \\.\pipe\PerplexityXPC-{Environment.UserName}.
    /// Changing this should be rare; only affects custom integrations.
    /// </summary>
    public string PipeName { get; set; } = $"PerplexityXPC-{Environment.UserName}";

    /// <summary>
    /// Minimum log level. Values: Verbose, Debug, Information, Warning, Error, Fatal.
    /// Default: Information.
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// Path to the mcp-servers.json file.
    /// Default: %LOCALAPPDATA%\PerplexityXPC\mcp-servers.json
    /// </summary>
    public string McpServersPath { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PerplexityXPC",
            "mcp-servers.json");

    /// <summary>
    /// Directory for log files.
    /// Default: %LOCALAPPDATA%\PerplexityXPC\logs\
    /// </summary>
    public string LogDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PerplexityXPC",
            "logs");

    /// <summary>
    /// Whether to automatically start all MCP servers marked auto_start=true when the service starts.
    /// Default: true.
    /// </summary>
    public bool AutoStartMcpServers { get; set; } = true;

    /// <summary>
    /// Maximum number of retries for failed Perplexity API calls (HTTP 429/503).
    /// Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay in milliseconds before the first retry.
    /// Subsequent retries use exponential backoff: delay * 2^attempt.
    /// Default: 1000 ms.
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Perplexity Sonar API base URL. Override only for testing/proxy scenarios.
    /// Default: https://api.perplexity.ai
    /// </summary>
    public string PerplexityApiBaseUrl { get; set; } = "https://api.perplexity.ai";

    /// <summary>
    /// HTTP request timeout for Perplexity API calls in seconds.
    /// For sonar-deep-research, consider setting this higher (e.g., 300).
    /// Default: 120 seconds.
    /// </summary>
    public int ApiTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum number of crash-restart attempts for an MCP server within the restart window.
    /// If exceeded, the server enters Error state and is not restarted automatically.
    /// Default: 3.
    /// </summary>
    public int McpMaxRestartAttempts { get; set; } = 3;

    /// <summary>
    /// Time window in seconds during which MCP restart attempts are counted.
    /// If the server crashes more than McpMaxRestartAttempts times in this window,
    /// auto-restart is disabled for that server.
    /// Default: 60 seconds.
    /// </summary>
    public int McpRestartWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Whether to add a Windows Firewall rule blocking inbound access to Port on startup.
    /// Requires the service to be running with appropriate privileges (typically LocalSystem).
    /// Default: true.
    /// </summary>
    public bool AddFirewallRule { get; set; } = true;
}
