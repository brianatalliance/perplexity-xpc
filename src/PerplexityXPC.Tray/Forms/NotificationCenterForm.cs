using PerplexityXPC.Tray.Models;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray.Forms;

/// <summary>
/// Scrollable notification center that displays all recent notifications from
/// quick actions and scheduled tasks.
///
/// <list type="bullet">
///   <item>Size: 500 x 600</item>
///   <item>Dark theme matching the rest of the app</item>
///   <item>Supports filtering by source via a dropdown</item>
///   <item>Notifications are loaded from <see cref="NotificationStore"/></item>
///   <item>Cards expand on click to show the full body</item>
/// </list>
/// </summary>
public sealed class NotificationCenterForm : Form
{
    // ── Theme constants ────────────────────────────────────────────────────────

    private static readonly Color BgColor      = Color.FromArgb(0x1A, 0x1A, 0x2E);
    private static readonly Color SurfaceColor = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color AccentColor  = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color TextColor    = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private static readonly Color TextSecColor = Color.FromArgb(0xA0, 0xA0, 0xA0);
    private static readonly Color BorderColor  = Color.FromArgb(0x2A, 0x2A, 0x4A);

    private static readonly Color BadgeQuickAction = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color BadgeScheduled   = Color.FromArgb(0x00, 0x96, 0x88);
    private static readonly Color BadgeAlert       = Color.FromArgb(0xF4, 0x43, 0x36);
    private static readonly Color BadgeSystem      = Color.FromArgb(0xFF, 0x98, 0x00);

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly Panel            _topBar;
    private readonly Button           _btnClearAll;
    private readonly ComboBox         _filterCombo;
    private readonly Panel            _listContainer;
    private readonly FlowLayoutPanel  _cardPanel;
    private readonly Label            _lblEmpty;

    // ── Services ──────────────────────────────────────────────────────────────

    private readonly NotificationStore _store;

    // ── State ─────────────────────────────────────────────────────────────────

    private List<Notification> _allNotifications = [];
    private string _currentFilter = "All";
    private readonly HashSet<string> _expandedIds = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the Notification Center and binds it to
    /// <paramref name="store"/> for load and dismiss operations.
    /// </summary>
    /// <param name="store">The shared notification store.</param>
    public NotificationCenterForm(NotificationStore store)
    {
        _store = store;

        // Subscribe so new notifications appear without manual refresh
        _store.NotificationAdded += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
                Invoke(() => _ = LoadAndRenderAsync());
        };

        // ── Form properties ──────────────────────────────────────────────────
        Text            = "Notification Center";
        Size            = new Size(500, 600);
        MinimumSize     = new Size(400, 300);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BgColor;
        ForeColor       = TextColor;
        ShowInTaskbar   = true;

        // ── Top bar ──────────────────────────────────────────────────────────
        _topBar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 44,
            BackColor = SurfaceColor,
            Padding   = new Padding(8, 8, 8, 0),
        };

        _btnClearAll = new Button
        {
            Text      = "Clear All",
            Size      = new Size(82, 28),
            Location  = new Point(8, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x3A, 0x3A, 0x5A),
            ForeColor = TextColor,
            Cursor    = Cursors.Hand,
        };
        _btnClearAll.FlatAppearance.BorderColor = BorderColor;
        _btnClearAll.Click += async (_, _) =>
        {
            await _store.ClearAllAsync().ConfigureAwait(false);
            await LoadAndRenderAsync().ConfigureAwait(false);
        };

        var filterLabel = new Label
        {
            Text      = "Filter:",
            Location  = new Point(100, 14),
            Size      = new Size(40, 18),
            ForeColor = TextSecColor,
            BackColor = Color.Transparent,
        };

        _filterCombo = new ComboBox
        {
            Location  = new Point(142, 10),
            Size      = new Size(140, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _filterCombo.Items.AddRange(new object[] { "All", "Quick Actions", "Scheduled", "Alerts", "System" });
        _filterCombo.SelectedIndex = 0;
        _filterCombo.SelectedIndexChanged += (_, _) =>
        {
            _currentFilter = _filterCombo.SelectedItem?.ToString() ?? "All";
            RenderCards();
        };

        var btnRefresh = new Button
        {
            Text      = "Refresh",
            Size      = new Size(72, 28),
            Location  = new Point(290, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x3A, 0x3A, 0x5A),
            ForeColor = TextColor,
            Cursor    = Cursors.Hand,
        };
        btnRefresh.FlatAppearance.BorderColor = BorderColor;
        btnRefresh.Click += async (_, _) => await LoadAndRenderAsync().ConfigureAwait(false);

        _topBar.Controls.AddRange(new Control[] { _btnClearAll, filterLabel, _filterCombo, btnRefresh });

        // ── Card list area ────────────────────────────────────────────────────
        _listContainer = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BgColor,
            Padding   = new Padding(4),
            AutoScroll = false,
        };

        _cardPanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll    = true,
            WrapContents  = false,
            BackColor     = BgColor,
            Padding       = new Padding(4, 4, 4, 4),
        };

        _lblEmpty = new Label
        {
            Text      = "No notifications yet.",
            ForeColor = TextSecColor,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(16, 16),
            Font      = new Font("Segoe UI", 10f),
            Visible   = false,
        };

        _cardPanel.Controls.Add(_lblEmpty);
        _listContainer.Controls.Add(_cardPanel);

        Controls.Add(_listContainer);
        Controls.Add(_topBar);

        Load += async (_, _) => await LoadAndRenderAsync().ConfigureAwait(false);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Forces an immediate reload from the store and re-renders all cards.
    /// Safe to call from any thread.
    /// </summary>
    public async Task LoadAndRenderAsync()
    {
        var notifications = await _store.LoadAsync().ConfigureAwait(false);

        if (!IsHandleCreated || IsDisposed) return;

        Invoke(() =>
        {
            _allNotifications = notifications;
            RenderCards();
        });
    }

    // ── Private: rendering ────────────────────────────────────────────────────

    private void RenderCards()
    {
        var filtered = _currentFilter switch
        {
            "Quick Actions" => _allNotifications.Where(n => n.Source == NotificationSource.QuickAction),
            "Scheduled"     => _allNotifications.Where(n => n.Source == NotificationSource.Scheduled),
            "Alerts"        => _allNotifications.Where(n => n.Source == NotificationSource.Alert),
            "System"        => _allNotifications.Where(n => n.Source == NotificationSource.System),
            _               => _allNotifications.AsEnumerable(),
        };

        _cardPanel.SuspendLayout();
        _cardPanel.Controls.Clear();

        var list = filtered.ToList();

        if (list.Count == 0)
        {
            _lblEmpty.Visible = true;
            _cardPanel.Controls.Add(_lblEmpty);
            _cardPanel.ResumeLayout(true);
            return;
        }

        _lblEmpty.Visible = false;

        foreach (var n in list)
        {
            var card = BuildCard(n);
            _cardPanel.Controls.Add(card);
        }

        _cardPanel.ResumeLayout(true);
    }

    private Panel BuildCard(Notification notification)
    {
        bool isExpanded = _expandedIds.Contains(notification.Id);

        var card = new Panel
        {
            Width     = _cardPanel.ClientSize.Width - 12,
            BackColor = notification.IsRead
                ? Color.FromArgb(0x1E, 0x1E, 0x38)
                : SurfaceColor,
            Margin    = new Padding(0, 0, 0, 6),
            Cursor    = Cursors.Hand,
            Padding   = new Padding(10, 8, 10, 8),
        };

        // ── Title row ──────────────────────────────────────────────────────
        var lblTitle = new Label
        {
            Text      = notification.Title,
            Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = TextColor,
            BackColor = Color.Transparent,
            Location  = new Point(10, 8),
            Size      = new Size(320, 18),
            AutoEllipsis = true,
        };

        // Source badge
        var lblBadge = new Label
        {
            Text      = BadgeText(notification.Source),
            ForeColor = Color.White,
            BackColor = BadgeColor(notification.Source),
            Font      = new Font("Segoe UI", 7f),
            Location  = new Point(340, 9),
            AutoSize  = true,
            Padding   = new Padding(4, 2, 4, 2),
        };

        // Timestamp
        var lblTime = new Label
        {
            Text      = RelativeTime(notification.Timestamp),
            ForeColor = TextSecColor,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 7.5f),
            Location  = new Point(10, 28),
            Size      = new Size(200, 16),
        };

        // Body text
        string bodyDisplay = isExpanded
            ? notification.Body
            : TruncateLines(notification.Body, 5);

        var lblBody = new Label
        {
            Text      = bodyDisplay,
            ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC),
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8.5f),
            Location  = new Point(10, 48),
            MaximumSize = new Size(460, 0),
            AutoSize  = true,
        };

        // Action buttons
        var btnDismiss = new Button
        {
            Text      = notification.IsRead ? "Dismissed" : "Dismiss",
            Size      = new Size(68, 22),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x2A, 0x2A, 0x4A),
            ForeColor = TextSecColor,
            Cursor    = Cursors.Hand,
            Enabled   = !notification.IsRead,
        };
        btnDismiss.FlatAppearance.BorderColor = BorderColor;
        btnDismiss.Click += async (_, _) =>
        {
            await _store.DismissAsync(notification.Id).ConfigureAwait(false);
            await LoadAndRenderAsync().ConfigureAwait(false);
        };

        var btnCopy = new Button
        {
            Text      = "Copy",
            Size      = new Size(52, 22),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x2A, 0x2A, 0x4A),
            ForeColor = TextSecColor,
            Cursor    = Cursors.Hand,
        };
        btnCopy.FlatAppearance.BorderColor = BorderColor;
        btnCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText(notification.Body); }
            catch { /* clipboard unavailable */ }
        };

        // Position buttons dynamically after body size is known
        card.Controls.AddRange(new Control[] { lblTitle, lblBadge, lblTime, lblBody, btnDismiss, btnCopy });

        card.Layout += (_, _) =>
        {
            int bodyBottom = lblBody.Bottom + 10;
            btnDismiss.Location = new Point(10, bodyBottom);
            btnCopy.Location    = new Point(84, bodyBottom);
            card.Height         = bodyBottom + 30;
            card.Width          = _cardPanel.ClientSize.Width - 12;
        };

        // Click to expand/collapse body
        void ToggleExpand(object? s, EventArgs e)
        {
            if (_expandedIds.Contains(notification.Id))
                _expandedIds.Remove(notification.Id);
            else
                _expandedIds.Add(notification.Id);

            RenderCards();
        }

        card.Click      += ToggleExpand;
        lblTitle.Click  += ToggleExpand;
        lblBody.Click   += ToggleExpand;
        lblTime.Click   += ToggleExpand;

        // Draw left accent border on paint
        card.Paint += (_, pe) =>
        {
            using var brush = new SolidBrush(BadgeColor(notification.Source));
            pe.Graphics.FillRectangle(brush, 0, 0, 3, card.Height);
        };

        return card;
    }

    // ── Private: helpers ──────────────────────────────────────────────────────

    private static string RelativeTime(DateTimeOffset ts)
    {
        TimeSpan diff = DateTimeOffset.Now - ts;

        if (diff.TotalSeconds < 60)  return "just now";
        if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 24)    return $"{(int)diff.TotalHours} hr ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    private static string TruncateLines(string text, int maxLines)
    {
        var lines = text.Split('\n');
        if (lines.Length <= maxLines)
            return text;

        return string.Join('\n', lines.Take(maxLines)) + "\n...";
    }

    private static string BadgeText(NotificationSource source) => source switch
    {
        NotificationSource.QuickAction => "Quick Action",
        NotificationSource.Scheduled   => "Scheduled",
        NotificationSource.Alert        => "Alert",
        NotificationSource.System       => "System",
        _                               => source.ToString(),
    };

    private static Color BadgeColor(NotificationSource source) => source switch
    {
        NotificationSource.QuickAction => BadgeQuickAction,
        NotificationSource.Scheduled   => BadgeScheduled,
        NotificationSource.Alert        => BadgeAlert,
        NotificationSource.System       => BadgeSystem,
        _                               => TextSecColor,
    };

    // ── Disposal ──────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _topBar.Dispose();
            _cardPanel.Dispose();
            _listContainer.Dispose();
        }

        base.Dispose(disposing);
    }
}
