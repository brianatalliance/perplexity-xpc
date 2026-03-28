using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PerplexityXPC.Tray.Helpers;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray.Forms;

// ── Conversation data models ────────────────────────────────────────────────────

/// <summary>A single message within a conversation (user or assistant turn).</summary>
public sealed class ConversationMessage
{
    /// <summary>Role: "user" or "assistant".</summary>
    public string Role      { get; set; } = string.Empty;
    /// <summary>Message content, potentially containing Markdown.</summary>
    public string Content   { get; set; } = string.Empty;
    /// <summary>UTC timestamp when the message was recorded.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>An entire saved conversation, persisted as a JSON file.</summary>
public sealed class SavedConversation
{
    /// <summary>Unique identifier (GUID).</summary>
    public string Id      { get; set; } = Guid.NewGuid().ToString();
    /// <summary>UTC creation time of this conversation.</summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;
    /// <summary>Model used for this conversation.</summary>
    public string Model   { get; set; } = "sonar";
    /// <summary>Ordered list of messages.</summary>
    public List<ConversationMessage> Messages { get; set; } = [];
}

// ── QueryPopup ─────────────────────────────────────────────────────────────────

/// <summary>
/// Enhanced floating chat popup. Supports multi-turn conversations, Markdown
/// rendering, model selection, file attachment, pin toggle, conversation
/// persistence, and smooth fade animations.
/// </summary>
public sealed class QueryPopup : Form
{
    // ── Win32 helpers ──────────────────────────────────────────────────────────
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

    // ── Theme palette ──────────────────────────────────────────────────────────
    private static readonly Color BgColor        = Color.FromArgb(0x1A, 0x1A, 0x2E);
    private static readonly Color SurfaceColor   = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color AccentColor    = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color TextColor      = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private static readonly Color TextSecColor   = Color.FromArgb(0xA0, 0xA0, 0xA0);
    private static readonly Color BorderColor    = Color.FromArgb(0x2A, 0x2A, 0x4A);
    private static readonly Color UserBubbleBg   = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color AsstBubbleBg   = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color CodeBlockBg    = Color.FromArgb(0x0D, 0x14, 0x2C);

    // ── Controls ───────────────────────────────────────────────────────────────
    private readonly Panel              _topBar;
    private readonly ComboBox           _modelCombo;
    private readonly Button             _historyBtn;
    private readonly Button             _newChatBtn;
    private readonly Button             _pinBtn;
    private readonly Button             _closeBtn;
    private readonly Panel              _chatArea;
    private readonly Panel              _messagesPanel;
    private readonly Panel              _dropOverlay;
    private readonly Panel              _inputArea;
    private readonly Panel              _attachChipPanel;
    private readonly RichTextBox        _inputBox;
    private readonly Label              _placeholderLabel;
    private readonly Button             _sendBtn;
    private readonly Button             _attachBtn;

    // ── State ──────────────────────────────────────────────────────────────────
    private readonly ServiceClient                              _client;
    private readonly List<ConversationMessage>                  _messages       = [];
    private readonly Dictionary<(int Start, int Length), string> _linkRanges   = [];
    private CancellationTokenSource?                            _queryCts;
    private bool                                                _isPinned;
    private string?                                             _attachedFilePath;
    private string?                                             _attachedFileContent;
    private SavedConversation                                   _currentConversation = new();

    // ── Settings persistence keys ──────────────────────────────────────────────
    private const string SettingsFile = "querypopup_settings.json";

    // ── Fade animation ─────────────────────────────────────────────────────────
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private bool _fadingOut;
    private const double FadeStep = 0.08;

    // ── Drag repositioning ─────────────────────────────────────────────────────
    private Point _dragStart;
    private bool  _dragging;

    // ── Conversations directory ────────────────────────────────────────────────
    private static readonly string ConversationsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerplexityXPC", "conversations");

    /// <summary>
    /// Initializes the popup with a reference to the <see cref="ServiceClient"/>.
    /// </summary>
    public QueryPopup(ServiceClient client)
    {
        _client = client;

        // ── Form chrome ──────────────────────────────────────────────────────
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = true;
        BackColor       = BgColor;
        StartPosition   = FormStartPosition.Manual;
        MinimumSize     = new Size(500, 400);
        Size            = new Size(700, 550);
        Opacity         = 0;
        AllowDrop       = true;

        // ── Top bar ──────────────────────────────────────────────────────────
        _topBar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 40,
            BackColor = SurfaceColor,
            Padding   = new Padding(8, 4, 8, 4),
        };

        _modelCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle     = FlatStyle.Flat,
            BackColor     = BgColor,
            ForeColor     = TextSecColor,
            Font          = new Font("Segoe UI", 9f),
            Width         = 170,
            Left          = 8,
            Top           = 7,
        };
        _modelCombo.Items.AddRange(["sonar", "sonar-pro", "sonar-reasoning-pro", "sonar-deep-research"]);
        _modelCombo.SelectedIndex = 0;

        _newChatBtn = MakeTopBtn("+ New", 62, AccentColor, Color.White);
        _historyBtn = MakeTopBtn("History", 68, SurfaceColor, TextSecColor);
        _pinBtn     = MakeTopBtn("Pin", 46, SurfaceColor, TextSecColor);
        _closeBtn   = MakeTopBtn("x", 28, SurfaceColor, TextSecColor);

        _topBar.Controls.Add(_modelCombo);
        _topBar.Controls.Add(_newChatBtn);
        _topBar.Controls.Add(_historyBtn);
        _topBar.Controls.Add(_pinBtn);
        _topBar.Controls.Add(_closeBtn);

        // ── Chat area ────────────────────────────────────────────────────────
        _chatArea = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BgColor,
        };

        // Scrollable messages panel inside the chat area
        _messagesPanel = new Panel
        {
            AutoScroll  = true,
            Dock        = DockStyle.Fill,
            BackColor   = BgColor,
            Padding     = new Padding(8, 4, 8, 4),
        };

        // Drag-and-drop overlay (hidden unless dragging)
        _dropOverlay = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(180, 0x1A, 0x1A, 0x2E),
            Visible   = false,
        };
        var dropLabel = new Label
        {
            Text      = "Drop file to analyze",
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
        };
        _dropOverlay.Controls.Add(dropLabel);

        _chatArea.Controls.Add(_dropOverlay);
        _chatArea.Controls.Add(_messagesPanel);

        // ── Attach chip panel ────────────────────────────────────────────────
        _attachChipPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 0,
            BackColor = BgColor,
            Padding   = new Padding(8, 2, 8, 2),
            Visible   = false,
        };

        // ── Input area ───────────────────────────────────────────────────────
        _inputArea = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 90,
            BackColor = SurfaceColor,
            Padding   = new Padding(8, 6, 8, 6),
        };

        _placeholderLabel = new Label
        {
            Text      = "Summon the Aunties...",
            ForeColor = TextSecColor,
            BackColor = Color.Transparent,
            Location  = new Point(14, 14),
            AutoSize  = true,
            Font      = new Font("Segoe UI", 10f),
        };

        _inputBox = new RichTextBox
        {
            Multiline   = true,
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor   = SurfaceColor,
            ForeColor   = TextColor,
            Font        = new Font("Segoe UI", 10f),
            DetectUrls  = false,
            WordWrap    = true,
            AcceptsTab  = false,
            Location    = new Point(8, 8),
        };

        _sendBtn   = MakeButton("Send",   AccentColor,  Color.White,  60, 30);
        _attachBtn = MakeButton("Attach", SurfaceColor, TextSecColor, 58, 30);

        _inputArea.Controls.Add(_placeholderLabel);
        _inputArea.Controls.Add(_inputBox);
        _inputArea.Controls.Add(_sendBtn);
        _inputArea.Controls.Add(_attachBtn);

        // ── Assembly ─────────────────────────────────────────────────────────
        var bottomStack = new Panel
        {
            Dock      = DockStyle.Bottom,
            BackColor = BgColor,
        };
        bottomStack.Controls.Add(_inputArea);
        bottomStack.Controls.Add(_attachChipPanel);

        Controls.Add(_chatArea);
        Controls.Add(bottomStack);
        Controls.Add(_topBar);

        // ── Fade timer ───────────────────────────────────────────────────────
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _fadeTimer.Tick += FadeTimer_Tick;

        // ── Event wiring ─────────────────────────────────────────────────────
        _inputBox.TextChanged  += InputBox_TextChanged;
        _inputBox.KeyDown      += InputBox_KeyDown;
        _inputBox.SizeChanged  += (_, _) => LayoutInputArea();
        _sendBtn.Click         += async (_, _) => await SubmitQueryAsync();
        _attachBtn.Click       += (_, _) => OpenAttachDialog();
        _newChatBtn.Click      += (_, _) => StartNewChat();
        _historyBtn.Click      += (_, _) => OpenHistory();
        _pinBtn.Click          += (_, _) => TogglePin();
        _closeBtn.Click        += async (_, _) => await CloseWithFadeAsync();
        Deactivate             += QueryPopup_Deactivate;
        Load                   += QueryPopup_Load;
        Resize                 += (_, _) => { ApplyRoundedCorners(); LayoutInputArea(); LayoutTopBar(); };
        DragEnter              += QueryPopup_DragEnter;
        DragLeave              += QueryPopup_DragLeave;
        DragDrop               += QueryPopup_DragDrop;

        // Allow the whole form to be dragged by top bar
        _topBar.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
        _topBar.MouseMove += (_, e) => { if (_dragging) Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y); };
        _topBar.MouseUp   += (_, _) => _dragging = false;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    private void QueryPopup_Load(object? sender, EventArgs e)
    {
        PositionNearCursor();
        ApplyRoundedCorners();
        EnableDropShadow();
        LayoutTopBar();
        LayoutInputArea();
        LoadSettings();
        _inputBox.Focus();
        _fadeTimer.Start();
    }

    // ── Layout helpers ───────────────────────────────────────────────────────

    /// <summary>Positions top-bar buttons right-aligned.</summary>
    private void LayoutTopBar()
    {
        int right = _topBar.ClientSize.Width - 6;
        foreach (var btn in new[] { _closeBtn, _pinBtn, _historyBtn, _newChatBtn })
        {
            btn.Top  = (_topBar.ClientSize.Height - btn.Height) / 2;
            btn.Left = right - btn.Width;
            right   -= btn.Width + 4;
        }
    }

    /// <summary>Sizes the input TextBox to fill the input area minus buttons.</summary>
    private void LayoutInputArea()
    {
        int btnWidth = _sendBtn.Width + _attachBtn.Width + 12;
        int boxW     = _inputArea.ClientSize.Width - btnWidth - 16;
        int lineH    = _inputBox.Font.Height;

        // Measure desired lines (min 3, max 6)
        int lineCount = Math.Max(3, Math.Min(6, _inputBox.Lines.Length == 0 ? 3 : _inputBox.Lines.Length));
        int boxH      = lineH * lineCount + 8;

        _inputBox.Size     = new Size(boxW, boxH);
        _inputBox.Location = new Point(8, 8);

        _inputArea.Height  = boxH + 20;

        // Position buttons at the right, vertically centred
        int btnTop = (_inputArea.Height - _sendBtn.Height) / 2;
        _sendBtn.Location   = new Point(_inputArea.ClientSize.Width - _sendBtn.Width - 8,   btnTop);
        _attachBtn.Location = new Point(_inputArea.ClientSize.Width - _sendBtn.Width - _attachBtn.Width - 14, btnTop);

        _placeholderLabel.Location = new Point(14, boxH / 2 - _placeholderLabel.Height / 2 + 4);
    }

    // ── Positioning / window decoration ─────────────────────────────────────

    private void PositionNearCursor()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor).WorkingArea;

        int x = cursor.X - Width / 2;
        int y = cursor.Y - Height - 10;

        x = Math.Max(screen.Left,  Math.Min(x, screen.Right  - Width));
        y = Math.Max(screen.Top,   Math.Min(y, screen.Bottom - Height));

        Location = new Point(x, y);
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var region = CreateRoundRectRgn(0, 0, Width, Height, 16, 16);
            Region = Region.FromHrgn(region);
        }
        catch { /* Non-critical */ }
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
        catch { /* DWM unavailable */ }
    }

    // ── Fade animation ───────────────────────────────────────────────────────

    private void FadeTimer_Tick(object? sender, EventArgs e)
    {
        if (_fadingOut)
        {
            Opacity = Math.Max(0, Opacity - FadeStep * 2);
            if (Opacity <= 0) { _fadeTimer.Stop(); Close(); }
        }
        else
        {
            Opacity = Math.Min(1.0, Opacity + FadeStep);
            if (Opacity >= 1.0) _fadeTimer.Stop();
        }
    }

    private void StartFadeOut()
    {
        _fadingOut = true;
        _fadeTimer.Start();
    }

    private async Task CloseWithFadeAsync()
    {
        _fadingOut = true;
        _fadeTimer.Start();
        await Task.Delay(300); // Let animation complete before returning
    }

    // ── Placeholder ──────────────────────────────────────────────────────────

    private void InputBox_TextChanged(object? sender, EventArgs e)
    {
        _placeholderLabel.Visible = _inputBox.TextLength == 0;
    }

    // ── Keyboard handling ────────────────────────────────────────────────────

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
                if (_isPinned)
                    WindowState = FormWindowState.Minimized;
                else
                    StartFadeOut();
                break;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_isPinned)
                WindowState = FormWindowState.Minimized;
            else
                StartFadeOut();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── Pin toggle ───────────────────────────────────────────────────────────

    /// <summary>Toggles always-on-top pinning and updates the button label.</summary>
    private void TogglePin()
    {
        _isPinned = !_isPinned;
        TopMost   = _isPinned;
        _pinBtn.BackColor = _isPinned ? AccentColor : SurfaceColor;
        _pinBtn.ForeColor = _isPinned ? Color.White  : TextSecColor;
        _pinBtn.Text      = _isPinned ? "Pinned"     : "Pin";
    }

    // ── Deactivate ───────────────────────────────────────────────────────────

    private void QueryPopup_Deactivate(object? sender, EventArgs e)
    {
        // Auto-hide when not pinned and not busy
        // Intentionally left as a no-op to keep popup open until Escape or close.
    }

    // ── New chat ─────────────────────────────────────────────────────────────

    /// <summary>Clears the current conversation and starts fresh.</summary>
    private void StartNewChat()
    {
        _messages.Clear();
        _linkRanges.Clear();
        _currentConversation = new SavedConversation
        {
            Model = _modelCombo.SelectedItem?.ToString() ?? "sonar"
        };

        // Clear chat display
        foreach (Control c in _messagesPanel.Controls.Cast<Control>().ToList())
            c.Dispose();
        _messagesPanel.Controls.Clear();

        ClearAttachment();
        _inputBox.Clear();
        _inputBox.Focus();
    }

    // ── File attachment ──────────────────────────────────────────────────────

    /// <summary>Opens a file dialog and attaches the selected file.</summary>
    private void OpenAttachDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Attach a file",
            Filter = "Text files|*.txt;*.md;*.cs;*.py;*.js;*.ts;*.json;*.xml;*.yaml;*.yml;*.csv;*.log|All files|*.*",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            AttachFile(dlg.FileName);
    }

    private void AttachFile(string path)
    {
        try
        {
            _attachedFilePath    = path;
            _attachedFileContent = ReadFileSafe(path, 10_000);
            ShowAttachChip(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read file:\n{ex.Message}", "PerplexityXPC",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string ReadFileSafe(string path, int maxChars)
    {
        using var sr = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buf = new char[maxChars];
        int read = sr.Read(buf, 0, maxChars);
        return new string(buf, 0, read);
    }

    private void ShowAttachChip(string filename)
    {
        _attachChipPanel.Controls.Clear();

        var chip = new Panel
        {
            BackColor   = Color.FromArgb(0x2A, 0x2A, 0x4A),
            Height      = 26,
            AutoSize    = false,
            Width       = 200,
            Top         = 4,
            Left        = 8,
            Cursor      = Cursors.Default,
        };

        var fileLabel = new Label
        {
            Text      = "\U0001F4CE " + filename,
            ForeColor = TextColor,
            Font      = new Font("Segoe UI", 8.5f),
            AutoSize  = true,
            Location  = new Point(6, 5),
        };

        var removeBtn = new Label
        {
            Text      = "x",
            ForeColor = TextSecColor,
            Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            AutoSize  = true,
            Location  = new Point(chip.Width - 20, 5),
            Cursor    = Cursors.Hand,
        };
        removeBtn.Click += (_, _) => ClearAttachment();

        chip.Controls.Add(fileLabel);
        chip.Controls.Add(removeBtn);
        _attachChipPanel.Controls.Add(chip);

        _attachChipPanel.Height  = 34;
        _attachChipPanel.Visible = true;
    }

    private void ClearAttachment()
    {
        _attachedFilePath    = null;
        _attachedFileContent = null;
        _attachChipPanel.Controls.Clear();
        _attachChipPanel.Height  = 0;
        _attachChipPanel.Visible = false;
    }

    // ── Drag-and-drop ────────────────────────────────────────────────────────

    private void QueryPopup_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect          = DragDropEffects.Copy;
            _dropOverlay.Visible = true;
            _dropOverlay.BringToFront();
        }
    }

    private void QueryPopup_DragLeave(object? sender, EventArgs e)
    {
        _dropOverlay.Visible = false;
    }

    private void QueryPopup_DragDrop(object? sender, DragEventArgs e)
    {
        _dropOverlay.Visible = false;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            AttachFile(files[0]);
    }

    // ── Query execution ──────────────────────────────────────────────────────

    /// <summary>Sends the current input as a query and streams the response.</summary>
    private async Task SubmitQueryAsync()
    {
        string query = _inputBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        string model = _modelCombo.SelectedItem?.ToString() ?? "sonar";

        // Compose effective content (with optional file attachment)
        string effectiveContent = BuildEffectiveContent(query);

        // Add user message to state
        var userMsg = new ConversationMessage
        {
            Role      = "user",
            Content   = effectiveContent,
            Timestamp = DateTime.UtcNow,
        };
        _messages.Add(userMsg);

        // Trim context window to 20 messages
        while (_messages.Count > 20)
            _messages.RemoveAt(0);

        // Display the user bubble (show only query text, not file dump)
        AppendMessageBubble("user", query, DateTime.UtcNow);

        // Clear input
        _inputBox.Clear();
        ClearAttachment();

        // Cancel prior query if any
        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var ct = _queryCts.Token;

        SetLoading(true);

        // Create assistant bubble placeholder
        var (asstPanel, asstRtb) = AppendAssistantBubblePlaceholder();

        var fullResponse  = new StringBuilder();
        var citations     = new List<string>();
        var asstLinkRanges = new Dictionary<(int Start, int Length), string>();

        try
        {
            await _client.QueryStreamAsync(effectiveContent, model, token =>
            {
                if (ct.IsCancellationRequested) return;
                fullResponse.Append(token);

                asstRtb.Invoke(() =>
                {
                    // Re-render incrementally: for performance we just append text
                    // and do a full render only on completion
                    asstRtb.SelectionStart  = asstRtb.TextLength;
                    asstRtb.SelectionLength = 0;
                    asstRtb.SelectionFont   = asstRtb.Font;
                    asstRtb.SelectionColor  = TextColor;
                    asstRtb.AppendText(token);
                    asstRtb.ScrollToCaret();
                });
            }, citations, ct);

            // Full markdown render now that streaming is complete
            if (!ct.IsCancellationRequested)
            {
                string responseText = fullResponse.ToString();
                asstRtb.Invoke(() =>
                {
                    asstLinkRanges.Clear();
                    MarkdownRenderer.RenderToRichTextBox(asstRtb, responseText,
                        ThemeManager.Dark, asstLinkRanges);

                    // Merge link ranges into main dictionary with offset
                    foreach (var kv in asstLinkRanges)
                        _linkRanges.TryAdd(kv.Key, kv.Value);

                    // Add citations below the bubble
                    if (citations.Count > 0)
                        AppendCitationsToPanel(asstPanel, citations);

                    ResizeBubbleRtb(asstRtb);
                    ScrollChatToBottom();
                });

                // Record assistant message
                var asstMsg = new ConversationMessage
                {
                    Role      = "assistant",
                    Content   = responseText,
                    Timestamp = DateTime.UtcNow,
                };
                _messages.Add(asstMsg);

                // Update current conversation and persist
                _currentConversation.Model = model;
                _currentConversation.Messages = [.. _messages];
                await SaveConversationAsync(_currentConversation);
            }
        }
        catch (OperationCanceledException)
        {
            asstRtb.Invoke(() => asstRtb.AppendText("\n[Cancelled]"));
        }
        catch (Exception ex)
        {
            asstRtb.Invoke(() =>
            {
                asstRtb.ForeColor = Color.FromArgb(0xFF, 0x80, 0x80);
                asstRtb.AppendText($"\nError: {ex.Message}");
            });
        }
        finally
        {
            SetLoading(false);
        }
    }

    private string BuildEffectiveContent(string query)
    {
        if (_attachedFileContent is null)
            return query;

        string fname = Path.GetFileName(_attachedFilePath ?? "attachment");
        return $"{query}\n\n[Attached file: {fname}]\n```\n{_attachedFileContent}\n```";
    }

    private void SetLoading(bool loading)
    {
        if (IsDisposed) return;
        _sendBtn.Enabled   = !loading;
        _attachBtn.Enabled = !loading;
        _sendBtn.Text      = loading ? "..." : "Send";
    }

    // ── History ──────────────────────────────────────────────────────────────

    /// <summary>Opens the conversation history form.</summary>
    private void OpenHistory()
    {
        var hist = new ConversationHistoryForm(_client, this);
        hist.Show();
    }

    /// <summary>
    /// Loads a saved conversation into the current popup (called by
    /// <see cref="ConversationHistoryForm"/> on double-click).
    /// </summary>
    public void LoadConversation(SavedConversation conv)
    {
        StartNewChat();
        _currentConversation = conv;
        _messages.AddRange(conv.Messages);

        // Set model
        int idx = _modelCombo.Items.IndexOf(conv.Model);
        if (idx >= 0) _modelCombo.SelectedIndex = idx;

        // Replay all messages visually
        foreach (var msg in conv.Messages)
        {
            if (msg.Role == "user")
                AppendMessageBubble("user", msg.Content, msg.Timestamp);
            else
                AppendMessageBubble("assistant", msg.Content, msg.Timestamp);
        }

        BringToFront();
        Activate();
    }

    // ── Chat bubble rendering ────────────────────────────────────────────────

    /// <summary>Appends a user or assistant message bubble to the chat panel.</summary>
    private void AppendMessageBubble(string role, string content, DateTime timestamp)
    {
        bool isUser = role == "user";

        var bubble = new Panel
        {
            BackColor   = isUser ? UserBubbleBg : AsstBubbleBg,
            AutoSize    = false,
            Margin      = new Padding(4, 4, 4, 4),
        };

        var rtb = new RichTextBox
        {
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            BackColor   = isUser ? UserBubbleBg : AsstBubbleBg,
            ForeColor   = isUser ? Color.White   : TextColor,
            Font        = new Font("Segoe UI", 9.5f),
            ScrollBars  = RichTextBoxScrollBars.None,
            DetectUrls  = false,
            WordWrap    = true,
            TabStop     = false,
        };

        if (isUser)
        {
            // User: plain text, right-aligned
            rtb.Text = content;
        }
        else
        {
            // Assistant: render markdown
            var linkRanges = new Dictionary<(int Start, int Length), string>();
            MarkdownRenderer.RenderToRichTextBox(rtb, content, ThemeManager.Dark, linkRanges);
            foreach (var kv in linkRanges)
                _linkRanges.TryAdd(kv.Key, kv.Value);
        }

        // Timestamp label
        var tsLabel = new Label
        {
            Text      = timestamp.ToLocalTime().ToString("h:mm tt"),
            ForeColor = isUser ? Color.FromArgb(200, 255, 255, 255) : TextSecColor,
            Font      = new Font("Segoe UI", 7.5f),
            AutoSize  = true,
            Margin    = new Padding(0),
        };

        ResizeBubbleRtb(rtb);
        int bubbleW = Math.Min(Math.Max(rtb.Width + 20, 120), _messagesPanel.ClientSize.Width - 80);

        bubble.Width = bubbleW;

        // Stack: timestamp on top, rtb below
        tsLabel.Location = new Point(8, 6);
        rtb.Location     = new Point(8, tsLabel.Bottom + 2);
        bubble.Height    = rtb.Bottom + 8;

        bubble.Controls.Add(tsLabel);
        bubble.Controls.Add(rtb);

        // Right-align user messages
        int panelW = _messagesPanel.ClientSize.Width;
        bubble.Left = isUser ? panelW - bubbleW - 10 : 10;
        bubble.Top  = GetNextBubbleTop();

        _messagesPanel.Controls.Add(bubble);
        ScrollChatToBottom();

        // Handle link clicks
        rtb.LinkClicked += (_, e) => OpenUrl(e.LinkText);
    }

    /// <summary>
    /// Creates an empty assistant bubble and returns the panel and RTB
    /// so streaming tokens can be appended.
    /// </summary>
    private (Panel panel, RichTextBox rtb) AppendAssistantBubblePlaceholder()
    {
        var bubble = new Panel
        {
            BackColor = AsstBubbleBg,
            AutoSize  = false,
            Width     = Math.Max(200, _messagesPanel.ClientSize.Width - 80),
            Height    = 40,
            Left      = 10,
            Top       = GetNextBubbleTop(),
            Margin    = new Padding(4),
        };

        var rtb = new RichTextBox
        {
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            BackColor   = AsstBubbleBg,
            ForeColor   = TextColor,
            Font        = new Font("Segoe UI", 9.5f),
            ScrollBars  = RichTextBoxScrollBars.None,
            DetectUrls  = false,
            WordWrap    = true,
            TabStop     = false,
            Location    = new Point(8, 8),
            Width       = bubble.Width - 16,
        };
        rtb.LinkClicked += (_, e) => OpenUrl(e.LinkText);

        bubble.Controls.Add(rtb);
        _messagesPanel.Controls.Add(bubble);
        ScrollChatToBottom();

        return (bubble, rtb);
    }

    /// <summary>Appends clickable citation links below an assistant panel.</summary>
    private void AppendCitationsToPanel(Panel asstPanel, List<string> citations)
    {
        var citFlow = new FlowLayoutPanel
        {
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = true,
            BackColor     = AsstBubbleBg,
            Padding       = new Padding(0),
        };

        var srcLabel = new Label
        {
            Text      = "Sources:",
            ForeColor = TextSecColor,
            Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            AutoSize  = true,
        };
        citFlow.Controls.Add(srcLabel);

        foreach (string url in citations)
        {
            var link = new LinkLabel
            {
                Text      = ShortenUrl(url),
                Tag       = url,
                AutoSize  = true,
                Font      = new Font("Segoe UI", 7.5f),
                LinkColor = Color.FromArgb(0x64, 0x9B, 0xFF),
                Margin    = new Padding(2, 0, 2, 0),
            };
            link.LinkClicked += (_, _) => OpenUrl(url);
            citFlow.Controls.Add(link);
        }

        // Insert citations flow after the last RTB in the bubble
        int insertY = asstPanel.Height;
        citFlow.Top   = insertY;
        citFlow.Left  = 8;
        citFlow.Width = asstPanel.Width - 16;
        asstPanel.Controls.Add(citFlow);
        asstPanel.Height = citFlow.Bottom + 8;
    }

    private void ResizeBubbleRtb(RichTextBox rtb)
    {
        // Measure the required height for the text
        int panelW  = _messagesPanel.ClientSize.Width;
        int rtbW    = Math.Max(200, (int)(panelW * 0.75) - 20);
        rtb.Width   = rtbW;

        using var g = rtb.CreateGraphics();
        SizeF size  = g.MeasureString(
            string.IsNullOrEmpty(rtb.Text) ? " " : rtb.Text,
            rtb.Font,
            rtbW);
        rtb.Height = Math.Max(20, (int)size.Height + 10);
    }

    private int GetNextBubbleTop()
    {
        int max = 0;
        foreach (Control c in _messagesPanel.Controls)
            max = Math.Max(max, c.Bottom + 6);
        return max;
    }

    private void ScrollChatToBottom()
    {
        if (_messagesPanel.VerticalScroll.Visible)
        {
            _messagesPanel.VerticalScroll.Value = _messagesPanel.VerticalScroll.Maximum;
            _messagesPanel.PerformLayout();
        }
    }

    // ── Conversation persistence ─────────────────────────────────────────────

    /// <summary>Auto-saves the conversation to JSON after each exchange.</summary>
    private static async Task SaveConversationAsync(SavedConversation conv)
    {
        try
        {
            Directory.CreateDirectory(ConversationsDir);
            string path = Path.Combine(ConversationsDir, $"{conv.Id}.json");
            string json = JsonSerializer.Serialize(conv, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch { /* Non-fatal - conversation storage best-effort */ }
    }

    // ── Settings persistence ─────────────────────────────────────────────────

    private void LoadSettings()
    {
        try
        {
            string dir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PerplexityXPC");
            string path = Path.Combine(dir, SettingsFile);
            if (!File.Exists(path)) return;

            string json    = File.ReadAllText(path);
            var settings   = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (settings is null) return;

            if (settings.TryGetValue("model", out string? model))
            {
                int idx = _modelCombo.Items.IndexOf(model);
                if (idx >= 0) _modelCombo.SelectedIndex = idx;
            }
        }
        catch { /* Silently ignore settings load errors */ }
    }

    private void SaveSettings()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PerplexityXPC");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, SettingsFile);
            var settings = new Dictionary<string, string>
            {
                ["model"] = _modelCombo.SelectedItem?.ToString() ?? "sonar",
            };
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
        }
        catch { /* Non-fatal */ }
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* Ignore */ }
    }

    private static string ShortenUrl(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
    }

    // ── Button factories ─────────────────────────────────────────────────────

    private static Button MakeTopBtn(string text, int width, Color bg, Color fg)
    {
        return new Button
        {
            Text      = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 8f),
            Size      = new Size(width, 26),
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 },
        };
    }

    private static Button MakeButton(string text, Color bg, Color fg, int width, int height)
    {
        return new Button
        {
            Text      = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 9f),
            Size      = new Size(width, height),
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 },
        };
    }

    // ── Paint (border) ───────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    // ── Drag to reposition (on chat area) ───────────────────────────────────

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

    // ── Disposal ─────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queryCts?.Cancel();
            _queryCts?.Dispose();
            _fadeTimer.Dispose();
            SaveSettings();
        }
        base.Dispose(disposing);
    }
}
