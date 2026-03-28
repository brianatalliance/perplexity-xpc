using System.Runtime.InteropServices;

namespace PerplexityXPC.Tray.Helpers;

// ─── Supporting types ─────────────────────────────────────────────────────────

/// <summary>Modifier key flags for <see cref="HotkeyManager"/>.</summary>
[Flags]
public enum ModifierKeys
{
    None    = 0x0000,
    Alt     = 0x0001,
    Control = 0x0002,
    Shift   = 0x0004,
    Win     = 0x0008,
}

// ─── HotkeyManager ────────────────────────────────────────────────────────────

/// <summary>
/// Registers and unregisters a system-wide (global) hotkey using the Win32
/// <c>RegisterHotKey</c> / <c>UnregisterHotKey</c> APIs.
///
/// <para>
/// Internally this creates a hidden <see cref="HotkeyWindow"/> that intercepts
/// <c>WM_HOTKEY</c> messages from the OS message loop.  The hidden window
/// lives on the UI thread, so <see cref="HotkeyPressed"/> is always raised on
/// the UI thread — no cross-thread invoke is required.
/// </para>
///
/// <para>Usage:</para>
/// <code>
/// var hk = new HotkeyManager();
/// hk.HotkeyPressed += (_, _) => ShowPopup();
/// hk.Register(Keys.P, ModifierKeys.Control | ModifierKeys.Shift);
/// // … later …
/// hk.Dispose();
/// </code>
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    // ── Win32 ──────────────────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    // ── State ──────────────────────────────────────────────────────────────────
    private HotkeyWindow? _window;
    private int           _hotkeyId = -1;
    private bool          _disposed;

    /// <summary>Raised on the UI thread when the registered hotkey is pressed.</summary>
    public event EventHandler? HotkeyPressed;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <paramref name="key"/> combined with <paramref name="modifiers"/>
    /// as a global hotkey.  Replaces any previously registered hotkey.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the hotkey cannot be registered (e.g. already taken by another app).
    /// </exception>
    public void Register(Keys key, ModifierKeys modifiers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Unregister previous hotkey if any
        Unregister();

        // Create (or reuse) the hidden message-only window
        _window ??= new HotkeyWindow(OnWmHotkey);

        // Use a stable, arbitrary ID in a range unlikely to conflict
        _hotkeyId = 0xBF00;

        if (!RegisterHotKey(_window.Handle, _hotkeyId,
                (uint)modifiers, (uint)key))
        {
            int err = Marshal.GetLastWin32Error();
            _hotkeyId = -1;
            throw new InvalidOperationException(
                $"RegisterHotKey failed (Win32 error {err}).  " +
                "The combination may already be registered by another application.");
        }
    }

    /// <summary>Unregisters the current hotkey without disposing the manager.</summary>
    public void Unregister()
    {
        if (_window is not null && _hotkeyId >= 0)
        {
            UnregisterHotKey(_window.Handle, _hotkeyId);
            _hotkeyId = -1;
        }
    }

    private void OnWmHotkey(int id)
    {
        if (id == _hotkeyId)
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }

    // ── Disposal ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Unregister();
        _window?.DestroyHandle();
        _window = null;
    }

    // ─── Hidden message window ────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="NativeWindow"/> that listens for <c>WM_HOTKEY</c>
    /// messages and forwards them to a delegate.
    /// </summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action<int> _callback;

        internal HotkeyWindow(Action<int> callback)
        {
            _callback = callback;

            // Create a message-only window (HWND_MESSAGE parent) so it doesn't
            // appear in the taskbar or Alt+Tab list.
            CreateHandle(new CreateParams
            {
                // HWND_MESSAGE = -3 (cast to IntPtr)
                Parent = new IntPtr(-3),
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _callback((int)m.WParam);
                return;
            }

            base.WndProc(ref m);
        }
    }
}
