using System.Configuration;

namespace PerplexityXPC.Tray.Properties;

/// <summary>
/// Lightweight strongly-typed wrapper around <see cref="ApplicationSettingsBase"/>.
///
/// <para>
/// Settings are persisted in the user-scoped .NET settings store
/// (<c>%LOCALAPPDATA%\PerplexityXPC\…\user.config</c>).
/// </para>
///
/// <para>
/// Call <see cref="Save"/> after modifying any property to commit changes to disk.
/// </para>
/// </summary>
[SettingsGroupName("PerplexityXPC.Tray")]
internal sealed class Settings : ApplicationSettingsBase
{
    private static readonly Settings _default = (Settings)Synchronized(new Settings());

    /// <summary>The global singleton instance.</summary>
    public static Settings Default => _default;

    // ── Appearance ─────────────────────────────────────────────────────────────

    /// <summary>Current theme: <c>"dark"</c> or <c>"light"</c>.</summary>
    [UserScopedSetting]
    [DefaultSettingValue("dark")]
    public string Theme
    {
        get => (string)(this[nameof(Theme)] ?? "dark");
        set => this[nameof(Theme)] = value;
    }

    /// <summary>Query popup opacity, in percent (40–100).</summary>
    [UserScopedSetting]
    [DefaultSettingValue("95")]
    public int PopupOpacity
    {
        get => (int)(this[nameof(PopupOpacity)] ?? 95);
        set => this[nameof(PopupOpacity)] = value;
    }

    /// <summary>Whether the query popup auto-hides when it loses focus.</summary>
    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool AutoHidePopup
    {
        get => (bool)(this[nameof(AutoHidePopup)] ?? false);
        set => this[nameof(AutoHidePopup)] = value;
    }

    // ── General ────────────────────────────────────────────────────────────────

    /// <summary>Global hotkey string, e.g. <c>"Ctrl+Alt+P"</c>.</summary>
    [UserScopedSetting]
    [DefaultSettingValue("Ctrl+Alt+P")]
    public string Hotkey
    {
        get => (string)(this[nameof(Hotkey)] ?? "Ctrl+Alt+P");
        set => this[nameof(Hotkey)] = value;
    }

    /// <summary>Whether to start minimized to tray.</summary>
    [UserScopedSetting]
    [DefaultSettingValue("True")]
    public bool StartMinimized
    {
        get => (bool)(this[nameof(StartMinimized)] ?? true);
        set => this[nameof(StartMinimized)] = value;
    }

    /// <summary>HTTP port the background service listens on.</summary>
    [UserScopedSetting]
    [DefaultSettingValue("47777")]
    public int ServicePort
    {
        get => (int)(this[nameof(ServicePort)] ?? 47777);
        set => this[nameof(ServicePort)] = value;
    }

    /// <summary>Last selected Perplexity model.</summary>
    [UserScopedSetting]
    [DefaultSettingValue("sonar")]
    public string LastModel
    {
        get => (string)(this[nameof(LastModel)] ?? "sonar");
        set => this[nameof(LastModel)] = value;
    }
}
