using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using PerplexityXPC.Tray.Helpers;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray.Forms;

/// <summary>
/// Borderless, dark-themed floating window that lets the user type a query,
/// select a model, and view the streamed response with citations.
/// Opens near the mouse cursor; hides when it loses focus (configurable).
/// </summary>
public sealed class QueryPopup : Form
{
    // ── Win32 for rounded corners & shadow ─────────────────────────────────────
    [DllImport("Gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    // ── Perplexity purple dark theme palette ───────────────────────────────────
    private static readonly Color BgColor      = Color.FromArgb(0x1A, 0x1A, 0x2E); // #1a1a2e
    private static readonly Color SurfaceColor = Color.FromArgb(0x16, 0x21, 0x3E); // #16213e
    private static readonly Color AccentColor  = Color.FromArgb(0x6C, 0x63, 0xFF); // #6c63ff
    private static readonly Color TextColor    = Color.FromArgb(0xE0, 0xE0, 0xE0); // #e0e0e0
    private static readonly Color TextSecColor = Color.FromArgb(0xA0, 0xA0, 0xA0); // #a0a0a0
    private static readonly Color BorderColor  = Color.FromArgb(0x2A, 0x2A, 0x4A);

    // ── Controls ───────────────────────────────────────────────────────────────
    private readonly RichTextBox _inputBox;
    private readonly ComboBox    _modelCombo;
    private readonly RichTextBox _responseBox;
    private readonly Label       _citationsLabel;
    private readonly FlowLayoutPanel _citationsPanel;
    private readonly Button      _submitBtn;
    private readonly Button      _copyBtn;
    private readonly Button      _browserBtn;
    private readonly PictureBox  _spinner;
    private readonly Label       _placeholderLabel;
    private readonly Panel       _inputPanel;
    private readonly Panel       _responsePanel;

    // ── State ──────────────────────────────────────────────────────────────────
    private readonly ServiceClient _client;
    private CancellationTokenSource? _queryCts;
    private bool _hasResponse;

    // ── Fade-in animation ──────────────────────────────────────────────────────
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private const double FadeStep = 0.08;

    public QueryPopup(ServiceClient client)
    {
        _client = client;

        // ── Form chrome ────────────────────────────────────────────────────────
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = true;
        BackColor       = BgColor;
        StartPosition   = FormStartPosition.Manual;
        MinimumSize     = new Size(480, 120);
        Size            = new Size(540, 130);
        Opacity         = 0;        // start transparent for fade-in
        Padding         = new Padding(1); // border

        // ── Input panel ────────────────────────────────────────────────────────
        _inputPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 90,
            BackColor = SurfaceColor,
            Padding   = new Padding(10, 8, 10, 8),
        };

        // placeholder label (simulated placeholder for RichTextBox)
        _placeholderLabel = new Label
        {
            Text      = "Ask Perplexity\u2026",
            ForeColor = TextSecColor,
            BackColor = Color.Transparent,
            Location  = new Point(14, 14),
            AutoSize  = true,
            Font      = new Font("Segoe UI", 10.5f),
        };

        _inputBox = new RichTextBox
        {
            Multiline        = true,
            ScrollBars       = RichTextBoxScrollBars.Vertical,
            BorderStyle      = BorderStyle.None,
            BackColor        = SurfaceColor,
            ForeColor        = TextColor,
            Font             = new Font("Segoe UI", 10.5f),
            DetectUrls       = false,
            Dock             = DockStyle.Fill,
            WordWrap         = true,
            AcceptsTab       = false,
        };

        _modelCombo = new ComboBox
        {
            DropDownStyle    = ComboBoxStyle.DropDownList,
            FlatStyle        = FlatStyle.Flat,
            BackColor        = SurfaceColor,
            ForeColor        = TextSecColor,
            Font             = new Font("Segoe UI", 8.5f),
            Dock             = DockStyle.Bottom,
            Height           = 22,
        };
        _modelCombo.Items.AddRange(["sonar", "sonar-pro", "sonar-reasoning-pro", "sonar-deep-research"]);
        _modelCombo.SelectedIndex = 0;

        _inputPanel.Controls.Add(_placeholderLabel);
        _inputPanel.Controls.Add(_inputBox);
        _inputPanel.Controls.Add(_modelCombo);

        // ── Submit button ──────────────────────────────────────────────────────
        _submitBtn = MakeButton("Ask", AccentColor, Color.White);
        _submitBtn.Size     = new Size(60, 28);
        _submitBtn.Anchor   = AnchorStyles.Bottom | AnchorStyles.Right;
        _submitBtn.Location = new Point(Width - 74, _inputPanel.Height - 36);
        _inputPanel.Controls.Add(_submitBtn);

        // ── Response panel ─────────────────────────────────────────────────────
        _responsePanel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BgColor,
            Padding   = new Padding(10, 6, 10, 6),
            Visible   = false,
        };

        _responseBox = new RichTextBox
        {
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            BackColor   = BgColor,
            ForeColor   = TextColor,
            Font        = new Font("Segoe UI", 10f),
            Dock        = DockStyle.Fill,
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            DetectUrls  = true,
        };

        _spinner = new PictureBox
        {
            Size     = new Size(20, 20),
            Visible  = false,
            BackColor = Color.Transparent,
        };

        _citationsLabel = new Label
        {
            Text      = "Sources:",
            ForeColor = TextSecColor,
            Font      = new Font("Segoe UI", 8f, FontStyle.Bold),
            Dock      = DockStyle.Top,
            Height    = 18,
            Visible   = false,
        };

        _citationsPanel = new FlowLayoutPanel
        {
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = true,
            Dock          = DockStyle.Bottom,
            BackColor     = BgColor,
        };

        // Action buttons row
        var btnPanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 36,
            BackColor     = BgColor,
            FlowDirection = FlowDirection.RightToLeft,
        };

        _copyBtn    = MakeButton("Copy",           SurfaceColor, TextColor);
        _browserBtn = MakeButton("Open in browser", SurfaceColor, TextColor);
        _copyBtn.Size    = new Size(70, 26);
        _browserBtn.Size = new Size(120, 26);

        btnPanel.Controls.Add(_copyBtn);
        btnPanel.Controls.Add(_browserBtn);

        _responsePanel.Controls.Add(_responseBox);
        _responsePanel.Controls.Add(btnPanel);
        _responsePanel.Controls.Add(_citationsLabel);
        _responsePanel.Controls.Add(_citationsPanel);

        Controls.Add(_responsePanel);
        Controls.Add(_inputPanel);

        // ── Fade-in timer ──────────────────────────────────────────────────────
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _fadeTimer.Tick += FadeTimer_Tick;

        // ── Wire events ────────────────────────────────────────────────────────
        _inputBox.TextChanged  += InputBox_TextChanged;
        _inputBox.KeyDown      += InputBox_KeyDown;
        _submitBtn.Click       += async (_, _) => await SubmitQueryAsync();
        _copyBtn.Click         += (_, _) => CopyResponse();
        _browserBtn.Click      += (_, _) => OpenInBrowser();
        _responseBox.LinkClicked += (_, e) => OpenUrl(e.LinkText);
        Deactivate             += (_, _) => OnDeactivated();
        Load                   += QueryPopup_Load;
        Resize                 += (_, _) => ApplyRoundedCorners();
    }

    // ── Load / position ────────────────────────────────────────────────────────

    private void QueryPopup_Load(object? sender, EventArgs e)
    {
        PositionNearCursor();
        ApplyRoundedCorners();
        EnableDropShadow();
        _inputBox.Focus();
        _fadeTimer.Start();
    }

    private void PositionNearCursor()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor).WorkingArea;

        int x = cursor.X - Width / 2;
        int y = cursor.Y - Height - 10;

        // Keep fully on screen
        x = Math.Max(screen.Left,  Math.Min(x, screen.Right  - Width));
        y = Math.Max(screen.Top,   Math.Min(y, screen.Bottom - Height));

        Location = new Point(x, y);
    }

    private void ApplyRoundedCorners()
    {
        var region = CreateRoundRectRgn(0, 0, Width, Height, 16, 16);
        Region = Region.FromHrgn(region);
    }

    private void EnableDropShadow()
    {
        try
        {
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);

            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* DWM may not be available in all environments */ }
    }

    // ── Fade animation ─────────────────────────────────────────────────────────

    private void FadeTimer_Tick(object? sender, EventArgs e)
    {
        Opacity = Math.Min(1.0, Opacity + FadeStep);
        if (Opacity >= 1.0) _fadeTimer.Stop();
    }

    private async Task FadeOutAsync()
    {
        _fadeTimer.Stop();
        while (Opacity > 0)
        {
            Opacity = Math.Max(0, Opacity - FadeStep * 2);
            await Task.Delay(16);
        }
    }

    // ── Placeholder ────────────────────────────────────────────────────────────

    private void InputBox_TextChanged(object? sender, EventArgs e)
    {
        _placeholderLabel.Visible = _inputBox.TextLength == 0;
    }

    // ── Keyboard handling ──────────────────────────────────────────────────────

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Return when !e.Control:
                e.SuppressKeyPress = true;
                _ = SubmitQueryAsync();
                break;

            case Keys.Escape:
                e.SuppressKeyPress = true;
                _ = CloseWithFadeAsync();
                break;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            _ = CloseWithFadeAsync();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── Query execution ────────────────────────────────────────────────────────

    private async Task SubmitQueryAsync()
    {
        string query = _inputBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        // Cancel any in-flight query
        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var ct = _queryCts.Token;

        string model = _modelCombo.SelectedItem?.ToString() ?? "sonar";

        // Prepare UI for response
        _responseBox.Clear();
        _citationsPanel.Controls.Clear();
        _citationsLabel.Visible = false;
        _responsePanel.Visible  = true;
        _hasResponse             = false;

        // Grow form to show response panel
        int newHeight = Math.Min(600, Height + 280);
        Size = new Size(Width, newHeight);
        ApplyRoundedCorners();

        SetLoading(true);

        try
        {
            var citations = new List<string>();

            await _client.QueryStreamAsync(query, model, token =>
            {
                if (ct.IsCancellationRequested) return;
                _responseBox.Invoke(() =>
                {
                    _responseBox.AppendText(token);
                    _responseBox.ScrollToCaret();
                    _hasResponse = true;
                });
            }, citations, ct);

            // Display citations
            if (citations.Count > 0)
            {
                _citationsLabel.Visible = true;
                foreach (string url in citations)
                {
                    var link = new LinkLabel
                    {
                        Text      = ShortenUrl(url),
                        Tag       = url,
                        AutoSize  = true,
                        Font      = new Font("Segoe UI", 8f),
                        LinkColor = AccentColor,
                        Margin    = new Padding(2),
                    };
                    link.LinkClicked += (_, _) => OpenUrl(url);
                    _citationsPanel.Controls.Add(link);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _responseBox.AppendText("\n[Query cancelled]");
        }
        catch (Exception ex)
        {
            _responseBox.ForeColor = Color.FromArgb(0xFF, 0x80, 0x80);
            _responseBox.AppendText($"Error: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool loading)
    {
        _submitBtn.Enabled   = !loading;
        _spinner.Visible     = loading;
        _copyBtn.Enabled     = !loading;
        _browserBtn.Enabled  = !loading;
    }

    // ── Clipboard / browser ────────────────────────────────────────────────────

    private void CopyResponse()
    {
        if (!string.IsNullOrWhiteSpace(_responseBox.Text))
            Clipboard.SetText(_responseBox.Text);
    }

    private void OpenInBrowser()
    {
        string query = Uri.EscapeDataString(_inputBox.Text.Trim());
        OpenUrl($"https://www.perplexity.ai/search?q={query}");
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private static string ShortenUrl(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
    }

    // ── Auto-hide on deactivate ────────────────────────────────────────────────

    private void OnDeactivated()
    {
        // TODO: check settings for auto-hide preference
        // For now, we keep the popup open until Escape or explicit close.
    }

    private async Task CloseWithFadeAsync()
    {
        await FadeOutAsync();
        Close();
    }

    // ── Helper: create styled button ───────────────────────────────────────────

    private static Button MakeButton(string text, Color bg, Color fg)
    {
        return new Button
        {
            Text       = text,
            BackColor  = bg,
            ForeColor  = fg,
            FlatStyle  = FlatStyle.Flat,
            Font       = new Font("Segoe UI", 8.5f),
            Cursor     = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 },
        };
    }

    // ── Painting (border) ──────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    // ── Drag to reposition ─────────────────────────────────────────────────────

    private Point _dragStart;
    private bool  _dragging;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
            Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    // ── Disposal ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queryCts?.Cancel();
            _queryCts?.Dispose();
            _fadeTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
