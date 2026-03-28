using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray.Forms;

// ─── Status DTO ───────────────────────────────────────────────────────────────

/// <summary>JSON shape returned by GET /status for the dashboard.</summary>
internal sealed class DashboardStatus
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("mcpServers")]
    public List<DashboardMcpServer>? McpServers { get; init; }

    [JsonPropertyName("lastQuery")]
    public string? LastQuery { get; init; }

    [JsonPropertyName("uptimeSeconds")]
    public long UptimeSeconds { get; init; }
}

internal sealed class DashboardMcpServer
{
    [JsonPropertyName("name")]    public string Name      { get; init; } = "";
    [JsonPropertyName("running")] public bool   IsRunning { get; init; }
}

// ─── DashboardWidget ──────────────────────────────────────────────────────────

/// <summary>
/// Small always-on-top floating widget that displays live system metrics and
/// PerplexityXPC service status.  The widget is borderless, semi-transparent,
/// dark-themed, and draggable by clicking anywhere on its surface.
///
/// <list type="bullet">
///   <item>Size: 300 x 250</item>
///   <item>Updates every 5 seconds via GET /status and local WMI/CIM calls</item>
///   <item>Double-click opens the full query popup via <see cref="OpenQueryRequested"/></item>
///   <item>Position is remembered across sessions via <see cref="PositionX"/> / <see cref="PositionY"/></item>
/// </list>
/// </summary>
public sealed class DashboardWidget : Form
{
    // ── Win32 ──────────────────────────────────────────────────────────────────

    [DllImport("Gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    // ── Theme constants ────────────────────────────────────────────────────────

    private static readonly Color BgColor      = Color.FromArgb(0x1A, 0x1A, 0x2E);
    private static readonly Color SurfaceColor = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color AccentColor  = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color TextColor    = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private static readonly Color TextSecColor = Color.FromArgb(0xA0, 0xA0, 0xA0);
    private static readonly Color GreenColor   = Color.FromArgb(0x4C, 0xAF, 0x50);
    private static readonly Color RedColor     = Color.FromArgb(0xF4, 0x43, 0x36);
    private static readonly Color YellowColor  = Color.FromArgb(0xFF, 0xC1, 0x07);

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly Label       _lblHeader;
    private readonly Label       _lblService;
    private readonly Label       _lblCpu;
    private readonly ProgressBar _pbCpu;
    private readonly Label       _lblMemory;
    private readonly ProgressBar _pbMemory;
    private readonly Label       _lblDisk;
    private readonly ProgressBar _pbDisk;
    private readonly Label       _lblMcp;
    private readonly Label       _lblLastQuery;
    private readonly Label       _lblUptime;
    private readonly Button      _btnClose;
    private readonly ContextMenuStrip _widgetMenu;

    // ── Services ──────────────────────────────────────────────────────────────

    private readonly ServiceClient _serviceClient;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // ── WMI performance counters ──────────────────────────────────────────────

    private PerformanceCounter? _cpuCounter;

    // ── Drag support ──────────────────────────────────────────────────────────

    private Point _dragStart;
    private bool  _isDragging;

    // ── Settings keys ─────────────────────────────────────────────────────────

    private const string PrefX = "DashboardX";
    private const string PrefY = "DashboardY";

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised when the user double-clicks the widget to open the query popup.</summary>
    public event EventHandler? OpenQueryRequested;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the dashboard widget and binds it to <paramref name="serviceClient"/>
    /// for live status polling.
    /// </summary>
    /// <param name="serviceClient">The active service client for broker communication.</param>
    public DashboardWidget(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient;

        // ── Form properties ──────────────────────────────────────────────────
        Text            = "PerplexityXPC Dashboard";
        Size            = new Size(300, 250);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        TopMost         = true;
        BackColor       = BgColor;
        Opacity         = 0.92;
        ShowInTaskbar   = false;

        // Restore saved position
        int savedX = Properties.Settings.Default.DashboardX;
        int savedY = Properties.Settings.Default.DashboardY;
        Location = (savedX == 0 && savedY == 0)
            ? new Point(Screen.PrimaryScreen!.WorkingArea.Right - 320, 40)
            : new Point(savedX, savedY);

        // ── CPU counter (may fail if not admin) ──────────────────────────────
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // first call always 0 - discard
        }
        catch
        {
            _cpuCounter = null;
        }

        // ── Controls ─────────────────────────────────────────────────────────

        // Close button
        _btnClose = new Button
        {
            Text     = "x",
            Size     = new Size(20, 20),
            Location = new Point(274, 4),
            FlatStyle = FlatStyle.Flat,
            ForeColor = TextSecColor,
            BackColor = Color.Transparent,
            Cursor    = Cursors.Hand,
            TabStop   = false,
        };
        _btnClose.FlatAppearance.BorderSize  = 0;
        _btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x3A, 0x3A, 0x5A);
        _btnClose.Click += (_, _) => Hide();

        // Header
        _lblHeader = MakeLabel("Summon-Aunties v1.4.0", 8, 4, 260, 18,
            new Font("Segoe UI", 7.5f), TextSecColor);

        // Service
        _lblService = MakeLabel("Service: Checking...", 8, 28, 280, 18,
            new Font("Segoe UI", 8.5f), TextColor);

        // CPU
        _lblCpu = MakeLabel("CPU: --", 8, 52, 140, 16,
            new Font("Segoe UI", 8f), TextColor);
        _pbCpu = MakeProgressBar(8, 68, 282, 8);

        // Memory
        _lblMemory = MakeLabel("Memory: --", 8, 82, 280, 16,
            new Font("Segoe UI", 8f), TextColor);
        _pbMemory = MakeProgressBar(8, 98, 282, 8);

        // Disk
        _lblDisk = MakeLabel("Disk C: --", 8, 112, 280, 16,
            new Font("Segoe UI", 8f), TextColor);
        _pbDisk = MakeProgressBar(8, 128, 282, 8);

        // MCP
        _lblMcp = MakeLabel("MCP Servers: --", 8, 146, 280, 16,
            new Font("Segoe UI", 8f), TextColor);

        // Last query
        _lblLastQuery = MakeLabel("Last Query: --", 8, 168, 280, 30,
            new Font("Segoe UI", 7.5f), TextSecColor);

        // Uptime
        _lblUptime = MakeLabel("Uptime: --", 8, 204, 280, 16,
            new Font("Segoe UI", 7.5f), TextSecColor);

        Controls.AddRange(new Control[]
        {
            _btnClose,
            _lblHeader,
            _lblService,
            _lblCpu, _pbCpu,
            _lblMemory, _pbMemory,
            _lblDisk, _pbDisk,
            _lblMcp,
            _lblLastQuery,
            _lblUptime,
        });

        // ── Right-click context menu ──────────────────────────────────────────
        _widgetMenu = BuildWidgetMenu();
        ContextMenuStrip = _widgetMenu;

        // ── Drag support ──────────────────────────────────────────────────────
        AttachDragHandlers(this);
        foreach (Control c in Controls)
        {
            if (c != _btnClose)
                AttachDragHandlers(c);
        }

        // ── Refresh timer ─────────────────────────────────────────────────────
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _refreshTimer.Tick += (_, _) => { try { _ = RefreshAsync(); } catch { /* swallow */ } };

        Load += async (_, _) =>
        {
            ApplyRoundedCorners();
            await RefreshAsync();
            _refreshTimer.Start();
        };
    }

    // ── Public helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the current window position to user settings so it persists between sessions.
    /// </summary>
    public void SavePosition()
    {
        Properties.Settings.Default.DashboardX = Location.X;
        Properties.Settings.Default.DashboardY = Location.Y;
        Properties.Settings.Default.Save();
    }

    // ── Private: refresh ──────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        // -- Local metrics (synchronous, fast) ---------------------------------
        RefreshLocalMetrics();

        // -- Broker metrics (async) --------------------------------------------
        try
        {
            var status = await _serviceClient.GetStatusAsync().ConfigureAwait(false);
            var mcp    = await _serviceClient.GetMcpServersAsync().ConfigureAwait(false);

            if (IsDisposed || !IsHandleCreated) return;

            Invoke(() =>
            {
                // Service dot + text
                int running = mcp.Count(s => s.IsRunning);
                int stopped = mcp.Count(s => !s.IsRunning);

                _lblService.Text      = $"Service:  {(status == ServiceStatus.Running ? "Running" : "Stopped")}";
                _lblService.ForeColor = status == ServiceStatus.Running ? GreenColor : RedColor;
                _lblMcp.Text          = $"MCP Servers: {running} running / {stopped} stopped";
            });
        }
        catch
        {
            if (!IsDisposed && IsHandleCreated)
                Invoke(() =>
                {
                    _lblService.Text      = "Service:  Unavailable";
                    _lblService.ForeColor = YellowColor;
                });
        }
    }

    private void RefreshLocalMetrics()
    {
        // CPU
        float cpu = 0f;
        try { cpu = _cpuCounter?.NextValue() ?? 0f; } catch { /* ignore */ }

        // Memory via WMI (Win32_OperatingSystem)
        long totalMemKb = 0, freeMemKb = 0;
        try
        {
            using var query  = new System.Management.ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (System.Management.ManagementObject obj in query.Get())
            {
                totalMemKb = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                freeMemKb  = Convert.ToInt64(obj["FreePhysicalMemory"]);
            }
        }
        catch { /* WMI unavailable */ }

        double totalMemGb = totalMemKb / 1_048_576.0;
        double usedMemGb  = (totalMemKb - freeMemKb) / 1_048_576.0;
        int    memPct     = totalMemKb > 0 ? (int)((double)(totalMemKb - freeMemKb) / totalMemKb * 100) : 0;

        // Disk C:
        int diskPct = 0;
        try
        {
            var drive = new DriveInfo("C");
            if (drive.IsReady)
                diskPct = (int)((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100);
        }
        catch { /* ignore */ }

        if (IsDisposed || !IsHandleCreated) return;

        Invoke(() =>
        {
            _lblCpu.Text  = $"CPU: {cpu:F0}%";
            _pbCpu.Value  = Math.Clamp((int)cpu, 0, 100);

            _lblMemory.Text = totalMemKb > 0
                ? $"Memory: {memPct}%  ({usedMemGb:F1} / {totalMemGb:F1} GB)"
                : "Memory: N/A";
            _pbMemory.Value = Math.Clamp(memPct, 0, 100);

            _lblDisk.Text  = $"Disk C: {diskPct}%";
            _pbDisk.Value  = Math.Clamp(diskPct, 0, 100);

            _lblUptime.Text = FormatUptime();
        });
    }

    // ── Private: drag ─────────────────────────────────────────────────────────

    private void AttachDragHandlers(Control ctrl)
    {
        ctrl.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStart  = e.Location;
            }
        };

        ctrl.MouseMove += (_, e) =>
        {
            if (_isDragging)
            {
                Left += e.X - _dragStart.X;
                Top  += e.Y - _dragStart.Y;
            }
        };

        ctrl.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                SavePosition();
            }
        };

        ctrl.DoubleClick += (_, _) => OpenQueryRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Private: context menu ─────────────────────────────────────────────────

    private ContextMenuStrip BuildWidgetMenu()
    {
        var menu = new ContextMenuStrip();
        menu.BackColor = SurfaceColor;
        menu.ForeColor = TextColor;

        var alwaysOnTopItem = new ToolStripMenuItem("Always on Top") { Checked = true };
        alwaysOnTopItem.Click += (_, _) =>
        {
            alwaysOnTopItem.Checked = !alwaysOnTopItem.Checked;
            TopMost = alwaysOnTopItem.Checked;
        };

        var detachItem = new ToolStripMenuItem("Detach (Normal Window)");
        detachItem.Click += (_, _) =>
        {
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            TopMost         = false;
            alwaysOnTopItem.Checked = false;
        };

        var closeItem = new ToolStripMenuItem("Close");
        closeItem.Click += (_, _) => Hide();

        menu.Items.Add(alwaysOnTopItem);
        menu.Items.Add(detachItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(closeItem);

        return menu;
    }

    // ── Private: helpers ──────────────────────────────────────────────────────

    private static Label MakeLabel(string text, int x, int y, int w, int h, Font font, Color fore)
        => new()
        {
            Text      = text,
            Location  = new Point(x, y),
            Size      = new Size(w, h),
            Font      = font,
            ForeColor = fore,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };

    private static ProgressBar MakeProgressBar(int x, int y, int w, int h)
    {
        var pb = new ProgressBar
        {
            Location = new Point(x, y),
            Size     = new Size(w, h),
            Minimum  = 0,
            Maximum  = 100,
            Style    = ProgressBarStyle.Continuous,
        };
        return pb;
    }

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return uptime.TotalHours >= 24
            ? $"Uptime: {(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
            : $"Uptime: {uptime.Hours}h {uptime.Minutes}m";
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            // Try Windows 11 DWM rounded corners first
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // Fallback: GDI region clipping for Windows 10
            Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 12, 12));
        }
    }

    // ── OnPaint: draw border ──────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(0x2A, 0x2A, 0x4A), 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _cpuCounter?.Dispose();
            _widgetMenu.Dispose();
        }

        base.Dispose(disposing);
    }
}
