using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PerplexityXPC.McpServer.Configuration;
using PerplexityXPC.McpServer.Protocol;

namespace PerplexityXPC.McpServer.Tools;

/// <summary>
/// Provides sandboxed PowerShell command execution and module listing.
/// Commands are validated against configurable allow/block lists before execution.
/// </summary>
public sealed class PowerShellTool
{
    private readonly McpServerConfig _config;

    /// <summary>Initialises the tool with the server configuration.</summary>
    public PowerShellTool(McpServerConfig config) => _config = config;

    // -------------------------------------------------------------------------
    //  Tool definitions
    // -------------------------------------------------------------------------

    /// <summary>Returns MCP tool definitions for PowerShell operations.</summary>
    public IEnumerable<McpTool> GetToolDefinitions()
    {
        yield return new McpTool
        {
            Name        = "powershell.execute",
            Description = "Execute a sandboxed PowerShell command. Blocked commands include Remove-Item, Invoke-Expression, and other destructive or download operations.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    command = new { type = "string", description = "The PowerShell command(s) to run." },
                    timeout = new { type = "integer", description = "Timeout in seconds. Default 30, max 120." },
                },
                required = new[] { "command" },
            },
        };

        yield return new McpTool
        {
            Name        = "powershell.get_modules",
            Description = "List installed PowerShell modules available on this system.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    filter = new { type = "string", description = "Optional module name filter substring." },
                },
            },
        };
    }

    // -------------------------------------------------------------------------
    //  Operations
    // -------------------------------------------------------------------------

    /// <summary>Executes a sandboxed PowerShell command and returns stdout, stderr, and exit code.</summary>
    public ToolCallResult Execute(JsonElement args)
    {
        try
        {
            var command = GetRequiredString(args, "command");
            int timeout = _config.CommandTimeoutSeconds;

            if (args.TryGetProperty("timeout", out var tp) && tp.TryGetInt32(out var tv))
                timeout = Math.Clamp(tv, 1, 120);

            // Security validation
            if (!_config.IsCommandAllowed(command))
            {
                // Identify which blocked pattern matched
                var blocked = _config.BlockedCommands
                    .FirstOrDefault(p => command.Contains(p, StringComparison.OrdinalIgnoreCase));

                return ToolCallResult.Failure(
                    $"Command blocked by security policy. Matched pattern: '{blocked}'.\n" +
                    "Destructive, download, and code-injection commands are not permitted.");
            }

            // Build the process
            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -Command \"{EscapeCommandArg(command)}\"",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                RedirectStandardInput  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };

            // Strip any inherited console environment that could change behavior
            psi.Environment["PSModulePath"] = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;

            using var process = new Process { StartInfo = psi };

            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) stdoutSb.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) stderrSb.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Close();

            bool exited = process.WaitForExit(timeout * 1000);

            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return ToolCallResult.Failure(
                    $"Command timed out after {timeout} seconds and was terminated.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"ExitCode: {process.ExitCode}");
            sb.AppendLine();

            var stdout = stdoutSb.ToString().TrimEnd();
            var stderr = stderrSb.ToString().TrimEnd();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                sb.AppendLine("--- STDOUT ---");
                sb.AppendLine(stdout);
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine();
                sb.AppendLine("--- STDERR ---");
                sb.AppendLine(stderr);
            }

            if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
                sb.AppendLine("(No output)");

            return ToolCallResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error executing PowerShell command: {ex.Message}");
        }
    }

    /// <summary>Lists installed PowerShell modules.</summary>
    public ToolCallResult GetModules(JsonElement args)
    {
        try
        {
            var filter = GetString(args, "filter", null);
            var command = "Get-Module -ListAvailable | Select-Object Name, Version, ModuleType, Description | Sort-Object Name | Format-Table -AutoSize";

            if (!string.IsNullOrWhiteSpace(filter))
                command = $"Get-Module -ListAvailable | Where-Object {{$_.Name -like '*{EscapePs(filter)}*'}} | Select-Object Name, Version, ModuleType, Description | Sort-Object Name | Format-Table -AutoSize";

            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -Command \"{EscapeCommandArg(command)}\"",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                RedirectStandardInput  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error  = process.StandardError.ReadToEnd();
            process.WaitForExit(60_000);

            if (!string.IsNullOrWhiteSpace(error))
                return ToolCallResult.Failure($"PowerShell error: {error}");

            return ToolCallResult.Success(
                string.IsNullOrWhiteSpace(output)
                    ? "No modules found."
                    : output);
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error listing PowerShell modules: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Escapes a command string for embedding inside a double-quoted PowerShell -Command argument.
    /// Replaces double-quotes with escaped backtick-quote sequences.
    /// </summary>
    private static string EscapeCommandArg(string command) =>
        command.Replace("\"", "`\"").Replace("\r\n", " ").Replace("\n", " ; ");

    /// <summary>Escapes a string for safe embedding in a PowerShell -like wildcard filter.</summary>
    private static string EscapePs(string s) =>
        s.Replace("'", "''").Replace("[", "`[").Replace("]", "`]");

    private static string GetRequiredString(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var p) || p.ValueKind == JsonValueKind.Null)
            throw new ToolException($"Required parameter '{key}' is missing.", JsonRpcError.Codes.InvalidParams);
        var v = p.GetString();
        if (string.IsNullOrWhiteSpace(v))
            throw new ToolException($"Parameter '{key}' must not be empty.", JsonRpcError.Codes.InvalidParams);
        return v;
    }

    private static string? GetString(JsonElement args, string key, string? defaultValue)
    {
        if (args.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? defaultValue;
        return defaultValue;
    }
}
