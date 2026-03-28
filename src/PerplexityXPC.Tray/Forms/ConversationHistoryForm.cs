using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PerplexityXPC.Tray.Helpers;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray.Forms;

/// <summary>
/// A form that lists all persisted conversations and lets the user browse,
/// search, delete, export, or resume any conversation.
///
/// <para>
/// Left panel: searchable list sorted newest-first.<br/>
/// Right panel: read-only full conversation view with Markdown rendering.<br/>
/// Bottom: Export All and Clear History actions.
/// </para>
/// </summary>
public sealed class ConversationHistoryForm : Form
{
    // ── Theme colours ──────────────────────────────────────────────────────────
    private static readonly Color BgColor      = Color.FromArgb(0x1A, 0x1A, 0x2E);
    private static readonly Color SurfaceColor = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color AccentColor  = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color TextColor    = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private static readonly Color TextSecColor = Color.FromArgb(0xA0, 0xA0, 0xA0);
    private static readonly Color BorderColor  = Color.FromArgb(0x2A, 0x2A, 0x4A);

    // ── Controls ──────────────────────────────────────────────────────────────
    private readonly TextBox       _searchBox;
    private readonly ListBox       _conversationList;
    private readonly RichTextBox   _previewBox;
    private readonly Button        _exportAllBtn;
    private readonly Button        _clearHistBtn;
    private readonly Label         _emptyLabel;

    // ── State ──────────────────────────────────────────────────────────────────
    private readonly ServiceClient  _client;
    private readonly QueryPopup?    _parentPopup;
    private List<SavedConversation> _allConversations = [];
    private List<SavedConversation> _filteredConversations = [];
    private readonly Dictionary<(int Start, int Length), string> _previewLinks = [];

    // ── Conversations directory ────────────────────────────────────────────────
    private static readonly string ConversationsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerplexityXPC", "conversations");

    /// <summary>
    /// Creates the history form.
    /// </summary>
    /// <param name="client">Service client (passed through for any future live queries).</param>
    /// <param name="parentPopup">
    /// Optional reference to the <see cref="QueryPopup"/> so double-click can
    /// resume a conversation there.
    /// </param>
    public ConversationHistoryForm(ServiceClient client, QueryPopup? parentPopup = null)
    {
        _client      = client;
        _parentPopup = parentPopup;

        // ── Form chrome ──────────────────────────────────────────────────────
        Text            = "Conversation History - PerplexityXPC";
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar   = true;
        BackColor       = BgColor;
        ForeColor       = TextColor;
        MinimumSize     = new Size(500, 400);
        Size            = new Size(760, 560);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9.5f);

        // ── Left panel ───────────────────────────────────────────────────────
        var leftPanel = new Panel
        {
            Width     = 220,
            Dock      = DockStyle.Left,
            BackColor = SurfaceColor,
            Padding   = new Padding(6, 6, 6, 6),
        };

        var searchLabel = new Label
        {
            Text      = "Search conversations",
            ForeColor = TextSecColor,
            Font      = new Font("Segoe UI", 8f),
            Dock      = DockStyle.Top,
            Height    = 18,
        };

        _searchBox = new TextBox
        {
            Dock        = DockStyle.Top,
            BackColor   = BgColor,
            ForeColor   = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font        = new Font("Segoe UI", 9.5f),
            Height      = 24,
        };

        _conversationList = new ListBox
        {
            Dock          = DockStyle.Fill,
            BackColor     = BgColor,
            ForeColor     = TextColor,
            BorderStyle   = BorderStyle.None,
            Font          = new Font("Segoe UI", 9f),
            DrawMode      = DrawMode.OwnerDrawFixed,
            ItemHeight    = 52,
            IntegralHeight = false,
        };

        _emptyLabel = new Label
        {
            Text      = "No conversations yet.\nStart chatting to see history here.",
            ForeColor = TextSecColor,
            Font      = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            Visible   = false,
        };

        leftPanel.Controls.Add(_conversationList);
        leftPanel.Controls.Add(_emptyLabel);
        leftPanel.Controls.Add(_searchBox);
        leftPanel.Controls.Add(searchLabel);

        // ── Splitter ─────────────────────────────────────────────────────────
        var splitter = new Splitter
        {
            Dock      = DockStyle.Left,
            Width     = 4,
            BackColor = BorderColor,
        };

        // ── Right panel ──────────────────────────────────────────────────────
        _previewBox = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            BackColor   = BgColor,
            ForeColor   = TextColor,
            Font        = new Font("Segoe UI", 9.5f),
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            DetectUrls  = false,
            WordWrap    = true,
        };

        // ── Bottom bar ───────────────────────────────────────────────────────
        var bottomBar = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 44,
            BackColor = SurfaceColor,
            Padding   = new Padding(8, 6, 8, 6),
        };

        _exportAllBtn = MakeButton("Export All as Markdown", AccentColor, Color.White, 170, 30);
        _clearHistBtn = MakeButton("Clear History",          SurfaceColor, Color.FromArgb(0xFF, 0x80, 0x80), 110, 30);
        _exportAllBtn.Dock = DockStyle.Left;
        _clearHistBtn.Dock = DockStyle.Right;

        bottomBar.Controls.Add(_exportAllBtn);
        bottomBar.Controls.Add(_clearHistBtn);

        // ── Context menu for list ────────────────────────────────────────────
        var contextMenu = new ContextMenuStrip
        {
            BackColor = SurfaceColor,
            ForeColor = TextColor,
        };
        var deleteItem    = new ToolStripMenuItem("Delete");
        var exportMdItem  = new ToolStripMenuItem("Export as Markdown");
        var resumeItem    = new ToolStripMenuItem("Resume in chat");
        contextMenu.Items.Add(resumeItem);
        contextMenu.Items.Add(exportMdItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(deleteItem);
        _conversationList.ContextMenuStrip = contextMenu;

        // ── Assembly ─────────────────────────────────────────────────────────
        Controls.Add(_previewBox);
        Controls.Add(splitter);
        Controls.Add(leftPanel);
        Controls.Add(bottomBar);

        // ── Event wiring ─────────────────────────────────────────────────────
        _conversationList.DrawItem        += ConversationList_DrawItem;
        _conversationList.SelectedIndexChanged += ConversationList_SelectedIndexChanged;
        _conversationList.DoubleClick     += ConversationList_DoubleClick;
        _searchBox.TextChanged            += SearchBox_TextChanged;
        _exportAllBtn.Click               += ExportAllBtn_Click;
        _clearHistBtn.Click               += ClearHistBtn_Click;
        deleteItem.Click                  += DeleteItem_Click;
        exportMdItem.Click                += ExportSingleItem_Click;
        resumeItem.Click                  += ResumeItem_Click;
        _previewBox.LinkClicked           += (_, e) => OpenUrl(e.LinkText);
        Load                              += (_, _) => LoadConversations();
    }

    // ── Load conversations ────────────────────────────────────────────────────

    /// <summary>Reads all JSON conversation files from disk and populates the list.</summary>
    private void LoadConversations()
    {
        _allConversations.Clear();

        try
        {
            if (!Directory.Exists(ConversationsDir))
            {
                ShowEmptyState(true);
                return;
            }

            foreach (string file in Directory.GetFiles(ConversationsDir, "*.json"))
            {
                try
                {
                    string json  = File.ReadAllText(file);
                    var    conv  = JsonSerializer.Deserialize<SavedConversation>(json);
                    if (conv is not null)
                        _allConversations.Add(conv);
                }
                catch { /* Skip malformed files */ }
            }

            _allConversations = [.. _allConversations.OrderByDescending(c => c.Created)];
        }
        catch { /* Directory read error - show empty state */ }

        ApplyFilter(_searchBox.Text);
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    private void ApplyFilter(string query)
    {
        string q = query.Trim().ToLowerInvariant();

        _filteredConversations = string.IsNullOrEmpty(q)
            ? [.. _allConversations]
            : [.. _allConversations.Where(c =>
                c.Messages.Any(m => m.Content.Contains(q, StringComparison.OrdinalIgnoreCase)))];

        _conversationList.Items.Clear();
        foreach (var conv in _filteredConversations)
            _conversationList.Items.Add(GetConversationTitle(conv));

        ShowEmptyState(_filteredConversations.Count == 0);
    }

    private void ShowEmptyState(bool empty)
    {
        _emptyLabel.Visible       = empty;
        _conversationList.Visible = !empty;
    }

    // ── Custom list drawing ───────────────────────────────────────────────────

    private void ConversationList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _filteredConversations.Count) return;

        var conv  = _filteredConversations[e.Index];
        bool sel  = (e.State & DrawItemState.Selected) != 0;
        var bg    = sel ? AccentColor : BgColor;
        var fg    = sel ? Color.White  : TextColor;
        var fg2   = sel ? Color.FromArgb(220, 255, 255, 255) : TextSecColor;

        e.DrawBackground();
        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        string title = GetConversationTitle(conv);
        string date  = conv.Created.ToLocalTime().ToString("MMM d, yyyy  h:mm tt");
        string model = conv.Model;

        var titleFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        var metaFont  = new Font("Segoe UI", 7.5f);

        using var fgBrush  = new SolidBrush(fg);
        using var fg2Brush = new SolidBrush(fg2);

        var titleRect = new RectangleF(e.Bounds.X + 8, e.Bounds.Y + 6,  e.Bounds.Width - 16, 18);
        var dateRect  = new RectangleF(e.Bounds.X + 8, e.Bounds.Y + 26, e.Bounds.Width - 16, 14);
        var modelRect = new RectangleF(e.Bounds.X + 8, e.Bounds.Y + 38, e.Bounds.Width - 16, 14);

        e.Graphics.DrawString(title, titleFont, fgBrush,  titleRect, new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
        e.Graphics.DrawString(date,  metaFont,  fg2Brush, dateRect,  StringFormat.GenericDefault);
        e.Graphics.DrawString(model, metaFont,  fg2Brush, modelRect, StringFormat.GenericDefault);

        // Separator line
        using var sepPen = new Pen(BorderColor);
        e.Graphics.DrawLine(sepPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        e.DrawFocusRectangle();
    }

    // ── Selection / preview ──────────────────────────────────────────────────

    private void ConversationList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        int idx = _conversationList.SelectedIndex;
        if (idx < 0 || idx >= _filteredConversations.Count) return;

        RenderConversationPreview(_filteredConversations[idx]);
    }

    private void RenderConversationPreview(SavedConversation conv)
    {
        _previewBox.Clear();
        _previewLinks.Clear();

        var palette = ThemeManager.Dark;

        // Header
        AppendPreviewText($"Model: {conv.Model}    Started: {conv.Created.ToLocalTime():MMM d, yyyy h:mm tt}\n\n",
            TextSecColor, new Font("Segoe UI", 8f));

        foreach (var msg in conv.Messages)
        {
            bool isUser = msg.Role == "user";

            // Role label
            string roleLabel = isUser ? "You" : "Assistant";
            string timeStr   = msg.Timestamp.ToLocalTime().ToString("h:mm tt");
            AppendPreviewText($"{roleLabel}  {timeStr}\n",
                isUser ? AccentColor : TextSecColor,
                new Font("Segoe UI", 8f, FontStyle.Bold));

            if (isUser)
            {
                AppendPreviewText(msg.Content + "\n\n", TextColor, _previewBox.Font);
            }
            else
            {
                var linkRanges = new Dictionary<(int Start, int Length), string>();
                MarkdownRenderer.RenderToRichTextBox(_previewBox, msg.Content, palette, linkRanges);
                foreach (var kv in linkRanges)
                    _previewLinks.TryAdd(kv.Key, kv.Value);

                // Separator
                int sepStart = _previewBox.TextLength;
                _previewBox.SelectionStart  = sepStart;
                _previewBox.SelectionLength = 0;
                _previewBox.SelectionColor  = BorderColor;
                _previewBox.AppendText("\n" + new string('\u2015', 40) + "\n\n");
            }
        }

        _previewBox.SelectionStart = 0;
        _previewBox.ScrollToCaret();
    }

    private void AppendPreviewText(string text, Color color, Font font)
    {
        int start = _previewBox.TextLength;
        _previewBox.SelectionStart  = start;
        _previewBox.SelectionLength = 0;
        _previewBox.SelectionFont   = font;
        _previewBox.SelectionColor  = color;
        _previewBox.AppendText(text);
    }

    // ── Double-click to resume ────────────────────────────────────────────────

    private void ConversationList_DoubleClick(object? sender, EventArgs e)
    {
        ResumeSelected();
    }

    private void ResumeSelected()
    {
        int idx = _conversationList.SelectedIndex;
        if (idx < 0 || idx >= _filteredConversations.Count) return;

        var conv = _filteredConversations[idx];

        if (_parentPopup is { IsDisposed: false })
        {
            _parentPopup.LoadConversation(conv);
        }
        else
        {
            // Fallback: create a new popup (ServiceClient is already available)
            var popup = new QueryPopup(_client);
            popup.Show();
            popup.LoadConversation(conv);
        }

        Close();
    }

    // ── Context menu actions ──────────────────────────────────────────────────

    private void ResumeItem_Click(object? sender, EventArgs e) => ResumeSelected();

    private void DeleteItem_Click(object? sender, EventArgs e)
    {
        int idx = _conversationList.SelectedIndex;
        if (idx < 0 || idx >= _filteredConversations.Count) return;

        var conv = _filteredConversations[idx];

        var result = MessageBox.Show(
            "Delete this conversation? This cannot be undone.",
            "PerplexityXPC",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        DeleteConversation(conv.Id);
        LoadConversations();
        _previewBox.Clear();
    }

    private void ExportSingleItem_Click(object? sender, EventArgs e)
    {
        int idx = _conversationList.SelectedIndex;
        if (idx < 0 || idx >= _filteredConversations.Count) return;

        ExportConversationToMarkdown(_filteredConversations[idx]);
    }

    // ── Bottom bar actions ───────────────────────────────────────────────────

    private void ExportAllBtn_Click(object? sender, EventArgs e)
    {
        if (_allConversations.Count == 0)
        {
            MessageBox.Show("No conversations to export.", "PerplexityXPC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder to export all conversations",
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        int exported = 0;
        foreach (var conv in _allConversations)
        {
            try
            {
                string mdContent = ConversationToMarkdown(conv);
                string filename  = $"conversation_{conv.Created:yyyyMMdd_HHmmss}.md";
                File.WriteAllText(Path.Combine(dlg.SelectedPath, filename), mdContent, Encoding.UTF8);
                exported++;
            }
            catch { /* Skip failed export */ }
        }

        MessageBox.Show($"Exported {exported} conversation(s).", "PerplexityXPC",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearHistBtn_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            $"Delete all {_allConversations.Count} conversation(s)? This cannot be undone.",
            "PerplexityXPC - Clear History",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try
        {
            if (Directory.Exists(ConversationsDir))
            {
                foreach (string f in Directory.GetFiles(ConversationsDir, "*.json"))
                {
                    try { File.Delete(f); } catch { /* Best-effort */ }
                }
            }
        }
        catch { /* Silently continue */ }

        LoadConversations();
        _previewBox.Clear();
    }

    // ── Export helpers ───────────────────────────────────────────────────────

    /// <summary>Exports a single conversation to a Markdown file chosen via SaveFileDialog.</summary>
    private static void ExportConversationToMarkdown(SavedConversation conv)
    {
        using var dlg = new SaveFileDialog
        {
            Title      = "Export conversation",
            Filter     = "Markdown files|*.md|All files|*.*",
            FileName   = $"conversation_{conv.Created:yyyyMMdd_HHmmss}.md",
            DefaultExt = ".md",
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            string md = ConversationToMarkdown(conv);
            File.WriteAllText(dlg.FileName, md, Encoding.UTF8);
            MessageBox.Show("Conversation exported.", "PerplexityXPC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "PerplexityXPC",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Converts a conversation to a Markdown document string.</summary>
    private static string ConversationToMarkdown(SavedConversation conv)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Conversation - {conv.Created.ToLocalTime():MMM d, yyyy h:mm tt}");
        sb.AppendLine($"**Model:** {conv.Model}  ");
        sb.AppendLine($"**ID:** {conv.Id}  ");
        sb.AppendLine();

        foreach (var msg in conv.Messages)
        {
            string role = msg.Role == "user" ? "**You**" : "**Assistant**";
            string ts   = msg.Timestamp.ToLocalTime().ToString("h:mm tt");
            sb.AppendLine($"### {role} - {ts}");
            sb.AppendLine();
            sb.AppendLine(msg.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Conversation management ───────────────────────────────────────────────

    private static void DeleteConversation(string id)
    {
        try
        {
            string path = Path.Combine(ConversationsDir, $"{id}.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* Best-effort */ }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        ApplyFilter(_searchBox.Text);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the first user query in a conversation as its display title.</summary>
    private static string GetConversationTitle(SavedConversation conv)
    {
        var first = conv.Messages.FirstOrDefault(m => m.Role == "user");
        if (first is null) return "(empty conversation)";

        string text = first.Content.Replace('\n', ' ').Trim();
        return text.Length > 60 ? text[..57] + "..." : text;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* Ignore */ }
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

    // ── Disposal ─────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // All child controls disposed by base
        }
        base.Dispose(disposing);
    }
}
