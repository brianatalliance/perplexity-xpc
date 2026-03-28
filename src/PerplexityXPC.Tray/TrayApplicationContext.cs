using System.Drawing.Drawing2D;
using System.IO.Pipes;
using PerplexityXPC.Tray.Forms;
using PerplexityXPC.Tray.Helpers;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray;

/// <summary>
/// Root application context.  Owns the <see cref="NotifyIcon"/>, context menu,
/// hotkey registration, and the background polling loop that refreshes service
/// status every five seconds.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    // ── UI elements ────────────────────────────────────────────────────────────
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _mcpSubmenu;
    private readonly ToolStripMenuItem _toggleServiceItem;

    // ── Services ───────────────────────────────────────────────────────────────
    private readonly ServiceClient _serviceClient;
    private readonly HotkeyManager _hotkeyManager;
    private readonly System.Windows.Forms.Timer _pollTimer;

    // ── State ──────────────────────────────────────────────────────────────────
    private ServiceStatus _currentStatus = ServiceStatus.Disconnected;
    private QueryPopup? _queryPopup;
    private SettingsForm? _settingsForm;

    // ── IPC server (single-instance wakeup) ───────────────────────────────────
    private readonly CancellationTokenSource _ipcCts = new();
    private readonly Task _ipcServerTask;

    // ── Icons ──────────────────────────────────────────────────────────────────
    private readonly Icon _iconGreen;
    private readonly Icon _iconYellow;
    private readonly Icon _iconRed;

    public TrayApplicationContext()
    {
        // Create the three status icons programmatically so we don't need
        // embedded .ico resources at build time.
        _iconGreen  = CreateCircleIcon(Color.FromArgb(0x4C, 0xAF, 0x50)); // green
        _iconYellow = CreateCircleIcon(Color.FromArgb(0xFF, 0xC1, 0x07)); // yellow
        _iconRed    = CreateCircleIcon(Color.FromArgb(0xF4, 0x43, 0x36)); // red

        // ── Context menu ──────────────────────────────────────────────────────
        _contextMenu = new ContextMenuStrip();

        // "Query Perplexity…" — bold default item
        var queryItem = new ToolStripMenuItem("Query Perplexity\u2026");
        queryItem.Font = new Font(queryItem.Font!, FontStyle.Bold);
        queryItem.Click += (_, _) => ShowQueryPopup();
        _contextMenu.Items.Add(queryItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Service status label (disabled, read-only)
        _statusItem = new ToolStripMenuItem("Service Status: Checking\u2026") { Enabled = false };
        _contextMenu.Items.Add(_statusItem);

        // MCP servers submenu
        _mcpSubmenu = new ToolStripMenuItem("MCP Servers \u25b6");
        _contextMenu.Items.Add(_mcpSubmenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Settings
        var settingsItem = new ToolStripMenuItem("Settings\u2026");
        settingsItem.Click += (_, _) => ShowSettings();
        _contextMenu.Items.Add(settingsItem);

        // View logs
        var logsItem = new ToolStripMenuItem("View Logs");
        logsItem.Click += (_, _) => OpenLogFolder();
        _contextMenu.Items.Add(logsItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Start / Stop service toggle
        _toggleServiceItem = new ToolStripMenuItem("Start Service");
        _toggleServiceItem.Click += ToggleService_Click;
        _contextMenu.Items.Add(_toggleServiceItem);

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        _contextMenu.Items.Add(exitItem);

        // ── Tray icon ─────────────────────────────────────────────────────────
        _trayIcon = new NotifyIcon
        {
            Icon             = _iconRed,
            Text             = "PerplexityXPC - Disconnected",
            ContextMenuStrip = _contextMenu,
            Visible          = true
        };

        _trayIcon.DoubleClick += (_, _) => ShowQueryPopup();

        // ── Service client ────────────────────────────────────────────────────
        _serviceClient = new ServiceClient();

        // ── Hotkey ────────────────────────────────────────────────────────────
        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyPressed += (_, _) => ShowQueryPopup();
        _hotkeyManager.Register(Keys.P, ModifierKeys.Control | ModifierKeys.Alt);

        // ── Status polling ────────────────────────────────────────────────────
        _pollTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _pollTimer.Tick += async (_, _) => await PollServiceStatusAsync();
        _pollTimer.Start();

        // Delay first poll to let the window handle be created
        _ = Task.Run(async () => { await Task.Delay(2000); await PollServiceStatusAsync(); });

        // ── IPC server (listen for "SHOW" from second instance) ───────────────
        _ipcServerTask = Task.Run(() => RunIpcServerAsync(_ipcCts.Token));
    }

    // ── Query popup ────────────────────────────────────────────────────────────

    /// <summary>Opens (or focuses) the floating query popup near the mouse cursor.</summary>
    private void ShowQueryPopup()
    {
        if (_queryPopup is { IsDisposed: false })
        {
            _queryPopup.BringToFront();
            _queryPopup.Activate();
            return;
        }

        _queryPopup = new QueryPopup(_serviceClient);
        _queryPopup.Show();
    }

    // ── Settings ───────────────────────────────────────────────────────────────

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_serviceClient);
        _settingsForm.Show();
    }

    // ── Log folder ─────────────────────────────────────────────────────────────

    private static void OpenLogFolder()
    {
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PerplexityXPC", "logs");

        Directory.CreateDirectory(logPath);
        System.Diagnostics.Process.Start("explorer.exe", logPath);
    }

    // ── Service toggle ─────────────────────────────────────────────────────────

    private async void ToggleService_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_currentStatus == ServiceStatus.Running)
                await _serviceClient.StopServiceAsync();
            else
                await _serviceClient.StartServiceAsync();

            await PollServiceStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to toggle service:\n{ex.Message}",
                "PerplexityXPC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ── Status polling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Polls the service HTTP endpoint and updates the tray icon / menu items on
    /// the UI thread.
    /// </summary>
    private async Task PollServiceStatusAsync()
    {
        ServiceStatus newStatus;
        List<McpServerInfo> mcpServers;

        try
        {
            newStatus  = await _serviceClient.GetStatusAsync();
            mcpServers = await _serviceClient.GetMcpServersAsync();
        }
        catch
        {
            newStatus  = ServiceStatus.Disconnected;
            mcpServers = [];
        }

        // Marshal back to UI thread (guard against handle not yet created)
        var strip = _trayIcon.ContextMenuStrip;
        if (strip is not null && strip.IsHandleCreated)
        {
            strip.Invoke(() =>
            {
                _currentStatus = newStatus;
                ApplyStatus(newStatus);
                RefreshMcpSubmenu(mcpServers);
            });
        }
        else
        {
            // Handle not ready yet - update directly (safe if called from UI thread)
            _currentStatus = newStatus;
            ApplyStatus(newStatus);
            RefreshMcpSubmenu(mcpServers);
        }
    }

    private void ApplyStatus(ServiceStatus status)
    {
        switch (status)
        {
            case ServiceStatus.Running:
                _trayIcon.Icon = _iconGreen;
                _trayIcon.Text = "PerplexityXPC - Ready";
                _statusItem.Text = "Service Status: Running";
                _toggleServiceItem.Text = "Stop Service";
                break;

            case ServiceStatus.Connecting:
                _trayIcon.Icon = _iconYellow;
                _trayIcon.Text = "PerplexityXPC - Connecting\u2026";
                _statusItem.Text = "Service Status: Connecting\u2026";
                _toggleServiceItem.Text = "Start Service";
                break;

            default: // Disconnected / Stopped
                _trayIcon.Icon = _iconRed;
                _trayIcon.Text = "PerplexityXPC - Disconnected";
                _statusItem.Text = "Service Status: Stopped";
                _toggleServiceItem.Text = "Start Service";
                break;
        }
    }

    private void RefreshMcpSubmenu(List<McpServerInfo> servers)
    {
        _mcpSubmenu.DropDownItems.Clear();

        if (servers.Count == 0)
        {
            _mcpSubmenu.DropDownItems.Add(new ToolStripMenuItem("(no servers configured)") { Enabled = false });
            return;
        }

        foreach (var server in servers)
        {
            string label = $"{server.Name}  [{(server.IsRunning ? "Running" : "Stopped")}]";
            var item = new ToolStripMenuItem(label);
            item.Tag = server;

            item.Click += async (_, _) =>
            {
                try { await _serviceClient.RestartMcpServerAsync(server.Name); }
                catch { /* swallow — will surface on next poll */ }
                await PollServiceStatusAsync();
            };

            _mcpSubmenu.DropDownItems.Add(item);
        }
    }

    // ── IPC server ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Listens on a named pipe so a second process invocation can wake up the
    /// running tray and open the query popup.
    /// </summary>
    private async Task RunIpcServerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    $"PerplexityXPC-Tray-IPC-{Environment.UserName}",
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipe);
                string? line = await reader.ReadLineAsync(ct);

                if (line?.Trim() == "SHOW")
                {
                    _trayIcon.ContextMenuStrip?.Invoke(ShowQueryPopup);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Pipe error — wait briefly, then recreate.
                await Task.Delay(1_000, ct).ConfigureAwait(false);
            }
        }
    }

    // ── Icon factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a simple 16×16 filled-circle icon in the given <paramref name="color"/>.
    /// Used instead of embedded resources so the project compiles without asset files.
    /// </summary>
    private static Icon CreateCircleIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ── Disposal ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ipcCts.Cancel();

            _pollTimer.Stop();
            _pollTimer.Dispose();

            _hotkeyManager.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _contextMenu.Dispose();
            _serviceClient.Dispose();

            _iconGreen.Dispose();
            _iconYellow.Dispose();
            _iconRed.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
