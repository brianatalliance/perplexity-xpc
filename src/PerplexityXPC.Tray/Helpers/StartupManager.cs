using Microsoft.Win32;

namespace PerplexityXPC.Tray.Helpers;

/// <summary>
/// Manages the Windows "Start with Windows" registry entry for the tray app.
///
/// <para>
/// Uses <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> which does
/// not require elevation — any standard user can write to it.
/// </para>
///
/// <para>
/// The registry value name is <c>PerplexityXPC.Tray</c> and its value is the
/// full path to the running executable (with <c>--minimized</c> appended so the
/// app opens to the tray instead of showing a window).
/// </para>
/// </summary>
public static class StartupManager
{
    private const string RunKey    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PerplexityXPC.Tray";

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds the current executable to the Windows startup registry.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the registry key cannot be opened or written to.
    /// </exception>
    public static void Register()
    {
        string executablePath = GetExecutablePath();

        using var key = OpenRunKey(writable: true);
        key.SetValue(ValueName, $"\"{executablePath}\" --minimized");
    }

    /// <summary>
    /// Removes the startup registry entry.
    /// Safe to call even when the entry does not exist (idempotent).
    /// </summary>
    public static void Unregister()
    {
        using var key = OpenRunKey(writable: true);

        // DeleteValue throws if the value doesn't exist unless we pass false
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Returns <c>true</c> if the startup registry value currently exists and
    /// points to the current executable.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = OpenRunKey(writable: false);
            object? val   = key.GetValue(ValueName);
            if (val is not string strVal) return false;

            // Strip surrounding quotes for comparison
            string stored = strVal.Trim('"', ' ');
            string current = GetExecutablePath();

            return stored.Equals(current, StringComparison.OrdinalIgnoreCase) ||
                   strVal.Contains(current, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static RegistryKey OpenRunKey(bool writable)
    {
        return Registry.CurrentUser.OpenSubKey(RunKey, writable: writable)
               ?? throw new InvalidOperationException(
                   $"Cannot open registry key HKCU\\{RunKey}.");
    }

    /// <summary>
    /// Returns the full path of the running executable.
    /// When running as a single-file publish the path is the host executable,
    /// not the DLL.
    /// </summary>
    private static string GetExecutablePath()
    {
        // Environment.ProcessPath is the preferred way on .NET 6+
        string? path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        // Fallback: use the executing assembly location
        return System.Reflection.Assembly.GetExecutingAssembly().Location;
    }
}
