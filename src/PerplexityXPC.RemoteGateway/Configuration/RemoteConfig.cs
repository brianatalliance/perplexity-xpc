namespace PerplexityXPC.RemoteGateway.Configuration;

/// <summary>
/// Strongly typed configuration for the RemoteGateway service.
/// Bound from the "RemoteGateway" section of appsettings.json and from
/// environment variables prefixed with PERPLEXITYXPC_REMOTE_.
/// </summary>
public sealed class RemoteConfig
{
    /// <summary>
    /// Bearer token every caller must include in the Authorization header.
    /// Required - the gateway refuses all requests (except /health) when empty.
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Absolute directory paths that file operations are allowed to access.
    /// Environment variable placeholders (e.g. %USERPROFILE%) are expanded at
    /// runtime by <see cref="FileManager"/>.
    /// </summary>
    public string[] AllowedDirectories { get; set; } =
    [
        "%USERPROFILE%",
        @"C:\Reports",
        @"C:\Logs"
    ];

    /// <summary>
    /// Maximum number of seconds a PowerShell process is allowed to run before
    /// it is forcibly terminated.
    /// </summary>
    public int MaxCommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Regex patterns for commands that are explicitly permitted.
    /// A command must match at least one pattern to be allowed.
    /// </summary>
    public string[] AllowedCommands { get; set; } =
    [
        @"^Get-.*",
        @"^Invoke-Perplexity.*",
        @"^Invoke-RestMethod.*",
        @"^Test-.*",
        @"^Measure-.*",
        @"^Select-.*",
        @"^Where-Object.*",
        @"^Sort-Object.*",
        @"^Format-.*",
        @"^ConvertTo-.*",
        @"^ConvertFrom-.*",
        @"^Import-Module.*",
        @"^Get-XPC.*",
        @"^Invoke-PerplexityXPC.*"
    ];

    /// <summary>
    /// Regex patterns for commands that are always blocked, regardless of
    /// AllowedCommands.  Evaluated before AllowedCommands.
    /// </summary>
    public string[] BlockedCommands { get; set; } =
    [
        @"^Remove-Item.*C:\\Windows",
        @"^Format-Volume.*",
        @"^Stop-Computer.*",
        @"^Restart-Computer.*",
        @"^Clear-EventLog.*",
        @".*-Credential.*",
        @"^Set-ExecutionPolicy.*",
        @"^Uninstall-.*"
    ];

    /// <summary>
    /// Maximum number of API requests allowed per IP address per minute.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 30;

    /// <summary>
    /// Master switch for PowerShell command execution.
    /// Set to false to disable /api/execute endpoints entirely.
    /// </summary>
    public bool EnablePowerShellExecution { get; set; } = true;

    /// <summary>
    /// Master switch for file operation endpoints.
    /// Set to false to disable /api/files endpoints entirely.
    /// </summary>
    public bool EnableFileOperations { get; set; } = true;

    /// <summary>
    /// Base URL of the local PerplexityXPC broker.
    /// </summary>
    public string BrokerUrl { get; set; } = "http://127.0.0.1:47777";
}
