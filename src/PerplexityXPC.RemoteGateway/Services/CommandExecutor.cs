using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PerplexityXPC.RemoteGateway.Configuration;

namespace PerplexityXPC.RemoteGateway.Services;

/// <summary>
/// Result returned by <see cref="CommandExecutor.ExecuteAsync"/>.
/// </summary>
/// <param name="Output">Standard output captured from the process.</param>
/// <param name="Errors">Standard error captured from the process.</param>
/// <param name="ExitCode">Process exit code (0 = success).</param>
/// <param name="DurationMs">Wall-clock time in milliseconds.</param>
/// <param name="Command">The original command string that was executed.</param>
public sealed record CommandResult(
    string Output,
    string Errors,
    int ExitCode,
    long DurationMs,
    string Command);

/// <summary>
/// Validates and executes PowerShell commands in a sandboxed subprocess.
///
/// Security model:
/// - Commands are matched against <see cref="RemoteConfig.BlockedCommands"/> first.
///   Any match causes immediate rejection.
/// - Commands must then match at least one pattern in
///   <see cref="RemoteConfig.AllowedCommands"/> to be permitted.
/// - Execution spawns powershell.exe with -NoProfile -NonInteractive so no
///   user profile or interactive prompts can interfere.
/// - A configurable timeout forcibly kills the process if it runs too long.
/// </summary>
public sealed class CommandExecutor
{
    private readonly RemoteConfig _config;
    private readonly ILogger<CommandExecutor> _logger;

    private readonly IReadOnlyList<Regex> _blockedPatterns;
    private readonly IReadOnlyList<Regex> _allowedPatterns;

    // Hard-coded safety patterns that are always blocked regardless of config.
    private static readonly string[] AlwaysBlockedPatterns =
    [
        @"Format-Volume",
        @"Remove-Item.+C:\\Windows",
        @"Stop-Computer",
        @"Restart-Computer",
        @"Clear-EventLog",
        @"-Credential",
        @"Set-ExecutionPolicy",
        @"Uninstall-"
    ];

    /// <summary>
    /// Initializes a new <see cref="CommandExecutor"/> with the supplied
    /// configuration and logger.
    /// </summary>
    public CommandExecutor(RemoteConfig config, ILogger<CommandExecutor> logger)
    {
        _config = config;
        _logger = logger;

        var blocked = AlwaysBlockedPatterns
            .Concat(config.BlockedCommands)
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();

        _blockedPatterns = blocked;

        _allowedPatterns = config.AllowedCommands
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates whether <paramref name="command"/> is permitted by the
    /// configured allow/block lists.
    /// </summary>
    /// <returns>
    /// A tuple where <c>isAllowed</c> is true when the command may be executed,
    /// and <c>reason</c> provides a human-readable explanation when blocked.
    /// </returns>
    public (bool isAllowed, string reason) ValidateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return (false, "Command must not be empty.");

        // Check blocked list first - these are always rejected.
        foreach (Regex blocked in _blockedPatterns)
        {
            if (blocked.IsMatch(command))
            {
                return (false, $"Command matches blocked pattern: {blocked}");
            }
        }

        // Command must match at least one allowed pattern.
        foreach (Regex allowed in _allowedPatterns)
        {
            if (allowed.IsMatch(command))
                return (true, "Command is permitted.");
        }

        return (false, "Command does not match any allowed pattern.");
    }

    /// <summary>
    /// Executes <paramref name="command"/> in a new powershell.exe subprocess.
    /// </summary>
    /// <param name="command">The PowerShell command string to run.</param>
    /// <param name="timeoutSeconds">
    /// Override for the per-request timeout.  Clamped to
    /// <see cref="RemoteConfig.MaxCommandTimeoutSeconds"/>.
    /// </param>
    /// <param name="ct">Cancellation token from the HTTP request.</param>
    /// <returns>A <see cref="CommandResult"/> with captured output.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when PowerShell execution is disabled via configuration.
    /// </exception>
    public async Task<CommandResult> ExecuteAsync(
        string command,
        int timeoutSeconds,
        CancellationToken ct)
    {
        if (!_config.EnablePowerShellExecution)
            throw new InvalidOperationException("PowerShell execution is disabled.");

        int clampedTimeout = Math.Min(
            Math.Max(1, timeoutSeconds),
            _config.MaxCommandTimeoutSeconds);

        var (isAllowed, reason) = ValidateCommand(command);
        if (!isAllowed)
        {
            _logger.LogWarning("Command rejected: {Reason} | Command: {Command}", reason, command);
            return new CommandResult(
                Output: string.Empty,
                Errors: $"Command blocked by security policy: {reason}",
                ExitCode: -1,
                DurationMs: 0,
                Command: command);
        }

        _logger.LogInformation("Executing command (timeout {Timeout}s): {Command}", clampedTimeout, command);

        var sw = Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(clampedTimeout));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{EscapeForShell(command)}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) outputBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) errorBuilder.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            sw.Stop();

            _logger.LogWarning("Command timed out after {Timeout}s: {Command}", clampedTimeout, command);

            return new CommandResult(
                Output: outputBuilder.ToString(),
                Errors: $"Command timed out after {clampedTimeout} seconds.",
                ExitCode: -2,
                DurationMs: sw.ElapsedMilliseconds,
                Command: command);
        }

        sw.Stop();

        int exitCode = process.ExitCode;
        _logger.LogInformation(
            "Command completed in {Ms}ms with exit code {ExitCode}: {Command}",
            sw.ElapsedMilliseconds,
            exitCode,
            command);

        return new CommandResult(
            Output: outputBuilder.ToString(),
            Errors: errorBuilder.ToString(),
            ExitCode: exitCode,
            DurationMs: sw.ElapsedMilliseconds,
            Command: command);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Escapes a command string for embedding inside a double-quoted PowerShell
    /// -Command argument passed via the process start info.
    /// </summary>
    private static string EscapeForShell(string command) =>
        command.Replace("\"", "\\\"");
}
