using System.Drawing.Drawing2D;
using System.IO.Pipes;
using System.Reflection;
using PerplexityXPC.Tray.Forms;
using PerplexityXPC.Tray.Helpers;
using PerplexityXPC.Tray.Models;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray;

/// <summary>
/// Root application context.  Owns the <see cref="NotifyIcon"/>, context menu,
/// hotkey registration, and the background polling loop that refreshes service
/// status every five seconds.
///
/// <para>
/// Expanded with quick actions, a live dashboard widget, and a notification center.
/// </para>
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    // ── UI elements ────────────────────────────────────────────────────────────
    private readonly NotifyIcon        _trayIcon;
    private readonly ContextMenuStrip  _contextMenu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _mcpSubmenu;
    private readonly ToolStripMenuItem _toggleServiceItem;

    // ── Services ───────────────────────────────────────────────────────────────
    private readonly ServiceClient      _serviceClient;
    private readonly HotkeyManager      _hotkeyManager;
    private readonly NotificationStore  _notificationStore;
    private readonly QuickActionRunner  _quickActionRunner;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly CancellationTokenSource    _actionCts = new();

    // ── State ──────────────────────────────────────────────────────────────────
    private ServiceStatus       _currentStatus = ServiceStatus.Disconnected;
    private QueryPopup?              _queryPopup;
    private SettingsForm?             _settingsForm;
    private DashboardWidget?          _dashboardWidget;
    private NotificationCenterForm?   _notificationCenter;
    private ConversationHistoryForm?  _conversationHistory;

    // ── IPC server (single-instance wakeup) ───────────────────────────────────
    private readonly CancellationTokenSource _ipcCts = new();
    private readonly Task _ipcServerTask;

    // ── Icons ──────────────────────────────────────────────────────────────────
    private readonly Icon _iconGreen;
    private readonly Icon _iconYellow;
    private readonly Icon _iconRed;

    // ── Constructor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the tray application context, registers the global hotkey,
    /// creates all menu items, and starts the background status-polling timer.
    /// </summary>
    public TrayApplicationContext()
    {
        // Load the Alliance for Empowerment icon from embedded resources
        // Fall back to programmatic circles if resource not found
        var allianceIcon = LoadEmbeddedIcon("alliance-tray.ico");
        _iconGreen  = allianceIcon ?? CreateCircleIcon(Color.FromArgb(0x4C, 0xAF, 0x50));
        _iconYellow = allianceIcon ?? CreateCircleIcon(Color.FromArgb(0xFF, 0xC1, 0x07));
        _iconRed    = allianceIcon ?? CreateCircleIcon(Color.FromArgb(0xF4, 0x43, 0x36));

        // ── Services ──────────────────────────────────────────────────────────
        _serviceClient     = new ServiceClient();
        _notificationStore = new NotificationStore();
        _quickActionRunner = new QuickActionRunner(_notificationStore);

        // ── Context menu ──────────────────────────────────────────────────────
        _contextMenu = new ContextMenuStrip();
        ApplyDarkMenu(_contextMenu);

        // "Summon Aunties..." - bold default item
        var queryItem = new ToolStripMenuItem("Summon Aunties...");
        queryItem.Font = new Font(queryItem.Font!, FontStyle.Bold);
        queryItem.Click += (_, _) => ShowQueryPopup();
        _contextMenu.Items.Add(queryItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // ── Quick Actions submenu ─────────────────────────────────────────────
        var quickActionsMenu = new ToolStripMenuItem("Quick Actions");
        BuildQuickActionsSubmenu(quickActionsMenu);
        _contextMenu.Items.Add(quickActionsMenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Service status label (disabled, read-only)
        _statusItem = new ToolStripMenuItem("Service Status: Checking...") { Enabled = false };
        _contextMenu.Items.Add(_statusItem);

        // MCP servers submenu
        _mcpSubmenu = new ToolStripMenuItem("MCP Servers >");
        _contextMenu.Items.Add(_mcpSubmenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Dashboard toggle
        var dashboardItem = new ToolStripMenuItem("Dashboard");
        dashboardItem.Click += (_, _) => ToggleDashboard();
        _contextMenu.Items.Add(dashboardItem);

        // Conversation history (reuses QueryPopup history tab concept)
        var historyItem = new ToolStripMenuItem("Conversation History");
        historyItem.Click += (_, _) => ShowConversationHistory();
        _contextMenu.Items.Add(historyItem);

        // Notification center
        var notifItem = new ToolStripMenuItem("Notification Center");
        notifItem.Click += (_, _) => ShowNotificationCenter();
        _contextMenu.Items.Add(notifItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Settings
        var settingsItem = new ToolStripMenuItem("Settings...");
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
            Visible          = true,
        };

        _trayIcon.DoubleClick += (_, _) => ShowQueryPopup();

        // ── Hotkey ────────────────────────────────────────────────────────────
        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyPressed += (_, _) => ShowQueryPopup();
        _hotkeyManager.Register(Keys.A, ModifierKeys.Control | ModifierKeys.Shift);

        // ── Status polling ────────────────────────────────────────────────────
        _pollTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _pollTimer.Tick += (_, _) => { try { _ = PollServiceStatusAsync(); } catch { /* swallow */ } };
        // Delay start so handles are ready
        var delayTimer = new System.Windows.Forms.Timer { Interval = 3_000 };
        delayTimer.Tick += (_, _) =>
        {
            delayTimer.Stop();
            delayTimer.Dispose();
            _pollTimer.Start();
        };
        delayTimer.Start();

        // ── IPC server (listen for "SHOW" from second instance) ───────────────
        _ipcServerTask = Task.Run(() => RunIpcServerAsync(_ipcCts.Token));
    }

    // ── Quick actions menu builder ─────────────────────────────────────────────

    /// <summary>
    /// Populates the Quick Actions submenu with all available commands,
    /// including a nested Network Diagnostics sub-submenu.
    /// </summary>
    private void BuildQuickActionsSubmenu(ToolStripMenuItem parent)
    {
        // Scan Event Logs
        var scanEvents = new ToolStripMenuItem("Scan Event Logs");
        scanEvents.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionScanEvents, "Scanning event logs...");
        parent.DropDownItems.Add(scanEvents);

        // Network Diagnostics >
        var networkMenu = new ToolStripMenuItem("Network Diagnostics");

        var pingGateway = new ToolStripMenuItem("Ping Gateway");
        pingGateway.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionNetworkDiag,
            "Pinging gateway...",
            "How do I ping the default gateway in Windows and interpret the results?");

        var dnsCheck = new ToolStripMenuItem("DNS Check");
        dnsCheck.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionNetworkDiag,
            "Running DNS check...",
            "How do I use nslookup to diagnose DNS resolution problems on Windows?");

        var fullDiag = new ToolStripMenuItem("Full Diagnostic");
        fullDiag.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionNetworkDiag, "Running full diagnostics...");

        networkMenu.DropDownItems.Add(pingGateway);
        networkMenu.DropDownItems.Add(dnsCheck);
        networkMenu.DropDownItems.Add(fullDiag);
        parent.DropDownItems.Add(networkMenu);

        // Security Audit
        var secAudit = new ToolStripMenuItem("Security Audit");
        secAudit.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionSecurityAudit, "Running security audit...");
        parent.DropDownItems.Add(secAudit);

        // Service Health Check
        var svcHealth = new ToolStripMenuItem("Service Health Check");
        svcHealth.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionServiceHealth, "Checking service health...");
        parent.DropDownItems.Add(svcHealth);

        // Check AD Replication
        var adReplication = new ToolStripMenuItem("Check AD Replication");
        adReplication.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionAdReplication, "Checking AD replication...");
        parent.DropDownItems.Add(adReplication);

        // Analyze Clipboard
        var clipboard = new ToolStripMenuItem("Analyze Clipboard");
        clipboard.Click += (_, _) => RunQuickAction(QuickActionRunner.ActionClipboard, "Analyzing clipboard...");
        parent.DropDownItems.Add(clipboard);
    }

    // ── Quick action execution ─────────────────────────────────────────────────

    /// <summary>
    /// Runs a named quick action asynchronously.  Sets a "Running..." tooltip,
    /// executes via <see cref="QuickActionRunner"/>, then shows a balloon
    /// notification with the result.
    /// </summary>
    /// <param name="actionName">One of the <c>Action*</c> constants on <see cref="QuickActionRunner"/>.</param>
    /// <param name="runningTooltip">Short tooltip text to show while the action is in progress.</param>
    /// <param name="overrideQuery">
    /// Optional query override used for network sub-actions that share an action name
    /// but need a custom query. Currently handled at the runner level for standard actions.
    /// </param>
    private void RunQuickAction(string actionName, string runningTooltip,
        string? overrideQuery = null)
    {
        string originalText = _trayIcon.Text;
        _trayIcon.Text = $"PerplexityXPC - {runningTooltip}";

        _ = Task.Run(async () =>
        {
            QuickActionResult result;
            try
            {
                result = await _quickActionRunner.RunQuickActionAsync(actionName, _actionCts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new QuickActionResult(actionName, false, "", "",
                    ex.Message);
            }

            // Marshal back to UI thread for balloon notification
            if (_trayIcon.ContextMenuStrip?.IsHandleCreated == true)
            {
                _trayIcon.ContextMenuStrip.Invoke(() =>
                {
                    _trayIcon.Text = originalText;

                    string body = result.Success
                        ? (string.IsNullOrEmpty(result.Text) ? "(no response)" : result.Text)
                        : $"Error: {result.ErrorMessage}";

                    ShowBalloon(ActionFriendlyName(actionName), body, result.Success
                        ? ToolTipIcon.Info
                        : ToolTipIcon.Warning);

                    // Also ensure the notification center gets the update if it is open
                    if (_notificationCenter is { IsDisposed: false })
                        _ = _notificationCenter.LoadAndRenderAsync();
                });
            }
        });
    }

    /// <summary>
    /// Shows a Windows balloon notification via the tray icon.
    /// </summary>
    private void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        string safeText = text.Length > 200 ? text[..200] + "..." : text;
        _trayIcon.ShowBalloonTip(6_000, title, safeText, icon);
    }

    /// <summary>Maps action name constants to human-friendly display names.</summary>
    private static string ActionFriendlyName(string actionName) => actionName switch
    {
        QuickActionRunner.ActionScanEvents    => "Event Log Scan",
        QuickActionRunner.ActionSecurityAudit => "Security Audit",
        QuickActionRunner.ActionServiceHealth => "Service Health Check",
        QuickActionRunner.ActionNetworkDiag   => "Network Diagnostics",
        QuickActionRunner.ActionClipboard     => "Clipboard Analysis",
        QuickActionRunner.ActionAdReplication => "AD Replication Check",
        _                                     => actionName,
    };

    // ── Window management ─────────────────────────────────────────────────────

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

    /// <summary>Opens (or focuses) the Settings form.</summary>
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

    /// <summary>
    /// Toggles the live dashboard widget.  Creates it on first use, then
    /// shows or hides it on subsequent calls.
    /// </summary>
    private void ToggleDashboard()
    {
        if (_dashboardWidget is { IsDisposed: false })
        {
            if (_dashboardWidget.Visible)
                _dashboardWidget.Hide();
            else
                _dashboardWidget.Show();
            return;
        }

        _dashboardWidget = new DashboardWidget(_serviceClient);
        _dashboardWidget.OpenQueryRequested += (_, _) => ShowQueryPopup();
        _dashboardWidget.Show();
    }

    /// <summary>
    /// Opens (or focuses) the Notification Center form.
    /// </summary>
    private void ShowNotificationCenter()
    {
        if (_notificationCenter is { IsDisposed: false })
        {
            _notificationCenter.BringToFront();
            _notificationCenter.Activate();
            return;
        }

        _notificationCenter = new NotificationCenterForm(_notificationStore);
        _notificationCenter.Show();
    }

    /// <summary>
    /// Opens (or focuses) the Conversation History form.
    /// </summary>
    private void ShowConversationHistory()
    {
        if (_conversationHistory is { IsDisposed: false })
        {
            _conversationHistory.BringToFront();
            _conversationHistory.Activate();
            return;
        }

        _conversationHistory = new ConversationHistoryForm(_serviceClient);
        _conversationHistory.Show();
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
    /// Polls the service HTTP endpoint and updates the tray icon and menu items
    /// on the UI thread.
    /// </summary>
    private async Task PollServiceStatusAsync()
    {
        ServiceStatus       newStatus;
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

        // Timer fires on the UI thread so no marshaling is needed
        try
        {
            _currentStatus = newStatus;
            ApplyStatus(newStatus);
            RefreshMcpSubmenu(mcpServers);
        }
        catch
        {
            // Swallow UI update errors during startup
        }
    }

    private void ApplyStatus(ServiceStatus status)
    {
        switch (status)
        {
            case ServiceStatus.Running:
                _trayIcon.Icon           = _iconGreen;
                _trayIcon.Text           = "PerplexityXPC - Ready";
                _statusItem.Text         = "Service Status: Running";
                _toggleServiceItem.Text  = "Stop Service";
                break;

            case ServiceStatus.Connecting:
                _trayIcon.Icon           = _iconYellow;
                _trayIcon.Text           = "PerplexityXPC - Connecting...";
                _statusItem.Text         = "Service Status: Connecting...";
                _toggleServiceItem.Text  = "Start Service";
                break;

            default: // Disconnected / Stopped
                _trayIcon.Icon           = _iconRed;
                _trayIcon.Text           = "PerplexityXPC - Disconnected";
                _statusItem.Text         = "Service Status: Stopped";
                _toggleServiceItem.Text  = "Start Service";
                break;
        }
    }

    private void RefreshMcpSubmenu(List<McpServerInfo> servers)
    {
        _mcpSubmenu.DropDownItems.Clear();

        if (servers.Count == 0)
        {
            _mcpSubmenu.DropDownItems.Add(
                new ToolStripMenuItem("(no servers configured)") { Enabled = false });
            return;
        }

        foreach (var server in servers)
        {
            string label = $"{server.Name}  [{(server.IsRunning ? "Running" : "Stopped")}]";
            var item = new ToolStripMenuItem(label) { Tag = server };

            item.Click += async (_, _) =>
            {
                try { await _serviceClient.RestartMcpServerAsync(server.Name); }
                catch { /* will surface on next poll */ }
                await PollServiceStatusAsync();
            };

            _mcpSubmenu.DropDownItems.Add(item);
        }
    }

    // ── Dark menu helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Applies a dark colour scheme to a <see cref="ContextMenuStrip"/> so the
    /// right-click menu matches the rest of the application theme.
    /// </summary>
    private static void ApplyDarkMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Color.FromArgb(0x16, 0x21, 0x3E);
        menu.ForeColor = Color.FromArgb(0xE0, 0xE0, 0xE0);
        menu.Renderer  = new DarkMenuRenderer();
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
                    _trayIcon.ContextMenuStrip?.Invoke(ShowQueryPopup);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(1_000, ct).ConfigureAwait(false);
            }
        }
    }

    // ── Icon factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a simple 16x16 filled-circle icon in the given <paramref name="color"/>.
    /// Used instead of embedded resources so the project compiles without asset files.
    /// </summary>
    /// <summary>
    /// Loads an icon from embedded resources by filename.
    /// Returns null if not found.
    /// </summary>
    private static Icon? LoadEmbeddedIcon(string resourceName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = asm.GetManifestResourceStream(name);
                    if (stream is not null)
                        return new Icon(stream);
                }
            }
        }
        catch { /* fall back to generated icon */ }
        return null;
    }

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
            _actionCts.Cancel();
            _actionCts.Dispose();
            _ipcCts.Cancel();

            _pollTimer.Stop();
            _pollTimer.Dispose();

            _hotkeyManager.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _contextMenu.Dispose();
            _serviceClient.Dispose();
            _quickActionRunner.Dispose();

            _dashboardWidget?.Dispose();
            _notificationCenter?.Dispose();
            _conversationHistory?.Dispose();

            _iconGreen.Dispose();
            _iconYellow.Dispose();
            _iconRed.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ExitApplication()
    {
        _dashboardWidget?.SavePosition();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}

// ── Dark menu renderer ────────────────────────────────────────────────────────

/// <summary>
/// Custom <see cref="ToolStripProfessionalRenderer"/> that paints the context
/// menu with the application dark theme colours.
/// </summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color BgColor      = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color HoverColor   = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color BorderColor  = Color.FromArgb(0x2A, 0x2A, 0x4A);
    private static readonly Color SeparatorClr = Color.FromArgb(0x2A, 0x2A, 0x4A);
    private static readonly Color TextColor    = Color.FromArgb(0xE0, 0xE0, 0xE0);

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item   = e.Item;
        var bounds = new Rectangle(Point.Empty, item.Size);

        using var brush = new SolidBrush(item.Selected ? HoverColor : BgColor);
        e.Graphics.FillRectangle(brush, bounds);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(BgColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen,
            e.AffectedBounds.X,
            e.AffectedBounds.Y,
            e.AffectedBounds.Width - 1,
            e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(SeparatorClr);
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? Color.White : TextColor;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = TextColor;
        base.OnRenderArrow(e);
    }
}
