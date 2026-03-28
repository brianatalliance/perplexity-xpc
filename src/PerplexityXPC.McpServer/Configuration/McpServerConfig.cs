namespace PerplexityXPC.McpServer.Configuration;

/// <summary>
/// Runtime configuration for the bundled MCP server.
/// Values are populated from command-line arguments and/or environment variables.
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>
    /// Directories that the filesystem tool is permitted to read.
    /// Defaults to user profile, Documents, Downloads, and Desktop.
    /// </summary>
    public List<string> AllowedDirectories { get; set; } = [];

    /// <summary>
    /// Glob-style patterns of PowerShell commands that are explicitly permitted.
    /// When non-empty, only matched commands are allowed.
    /// </summary>
    public List<string> AllowedCommands { get; set; } = [];

    /// <summary>
    /// Glob-style patterns of PowerShell commands that are explicitly blocked
    /// regardless of AllowedCommands.
    /// </summary>
    public List<string> BlockedCommands { get; set; } =
    [
        "Remove-Item",
        "rm ",
        "del ",
        "rd ",
        "rmdir",
        "Format-",
        "Clear-Disk",
        "Initialize-Disk",
        "New-Partition",
        "Set-Partition",
        "Stop-Computer",
        "Restart-Computer",
        "Invoke-Expression",
        "iex ",
        "DownloadFile",
        "DownloadString",
        "WebClient",
        "Start-BitsTransfer",
        "Invoke-WebRequest",
        "curl ",
        "wget ",
        "Invoke-RestMethod",
        "irm ",
        "New-Object Net.WebClient",
        "Net.WebClient",
        "Start-Process",
        "saps ",
        "Disable-",
        "Enable-",
        "Set-MpPreference",
        "Add-MpPreference",
        "sc.exe",
        "net user",
        "net localgroup",
        "reg add",
        "reg delete",
        "reg import",
        "regedit",
        "bcdedit",
        "schtasks",
        "at.exe",
        "certutil",
        "mshta",
        "wscript",
        "cscript",
        "msiexec",
        "rundll32",
        "regsvr32",
        "powershell -enc",
        "powershell -e ",
        "EncodedCommand",
        "-WindowStyle Hidden",
        "-W Hidden",
        "bypass",
        "-exec bypass",
        "ExecutionPolicy Bypass",
        "Set-ExecutionPolicy",
        "Add-Type",
        "[Reflection.Assembly]",
        "[System.Reflection",
        "::Load(",
        "::LoadFrom(",
        "PInvoke",
        "VirtualAlloc",
        "WriteProcessMemory",
        "CreateThread",
    ];

    /// <summary>
    /// Base URL of the local PerplexityXPC broker service.
    /// Defaults to http://127.0.0.1:47777.
    /// </summary>
    public string BrokerUrl { get; set; } = "http://127.0.0.1:47777";

    /// <summary>
    /// Maximum number of characters returned when reading a file.
    /// Content beyond this limit is truncated with a warning appended.
    /// </summary>
    public int MaxFileReadChars { get; set; } = 50_000;

    /// <summary>
    /// Timeout in seconds for PowerShell command execution.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Loads configuration from command-line arguments and environment variables.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the process.</param>
    /// <returns>Populated <see cref="McpServerConfig"/>.</returns>
    public static McpServerConfig Load(string[] args)
    {
        var config = new McpServerConfig();

        // Default allowed directories
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docs    = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var down    = Path.Combine(profile, "Downloads");
        var desk    = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        config.AllowedDirectories =
        [
            profile,
            docs,
            down,
            desk,
        ];

        // Parse --key=value or --key value style args
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--broker-url=", StringComparison.OrdinalIgnoreCase))
            {
                config.BrokerUrl = args[i]["--broker-url=".Length..];
            }
            else if (args[i].Equals("--broker-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                config.BrokerUrl = args[++i];
            }
            else if (args[i].StartsWith("--allowed-dir=", StringComparison.OrdinalIgnoreCase))
            {
                config.AllowedDirectories.Add(args[i]["--allowed-dir=".Length..]);
            }
            else if (args[i].Equals("--allowed-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                config.AllowedDirectories.Add(args[++i]);
            }
            else if (args[i].StartsWith("--max-file-chars=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args[i]["--max-file-chars=".Length..], out int v))
                    config.MaxFileReadChars = v;
            }
            else if (args[i].StartsWith("--command-timeout=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args[i]["--command-timeout=".Length..], out int v))
                    config.CommandTimeoutSeconds = v;
            }
        }

        // Environment variable overrides
        var envBroker = Environment.GetEnvironmentVariable("PXPC_BROKER_URL");
        if (!string.IsNullOrWhiteSpace(envBroker))
            config.BrokerUrl = envBroker;

        var envDirs = Environment.GetEnvironmentVariable("PXPC_ALLOWED_DIRS");
        if (!string.IsNullOrWhiteSpace(envDirs))
        {
            foreach (var d in envDirs.Split(';', StringSplitOptions.RemoveEmptyEntries))
                config.AllowedDirectories.Add(d.Trim());
        }

        return config;
    }

    /// <summary>
    /// Returns true if the resolved absolute path is within one of the allowed directories.
    /// </summary>
    public bool IsPathAllowed(string resolvedPath)
    {
        foreach (var dir in AllowedDirectories)
        {
            var normalized = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            if (resolvedPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                return true;

            // Also allow exact match to the directory itself
            if (resolvedPath.Equals(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the given PowerShell command should be blocked.
    /// </summary>
    public bool IsCommandBlocked(string command)
    {
        foreach (var pattern in BlockedCommands)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the command passes the allow-list check.
    /// When AllowedCommands is empty, all non-blocked commands are permitted.
    /// </summary>
    public bool IsCommandAllowed(string command)
    {
        if (IsCommandBlocked(command))
            return false;

        if (AllowedCommands.Count == 0)
            return true;

        foreach (var pattern in AllowedCommands)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
