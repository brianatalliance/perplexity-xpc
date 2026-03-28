using System.Diagnostics;
using PerplexityXPC.Tray.Helpers;
using PerplexityXPC.Tray.Services;

namespace PerplexityXPC.Tray.Forms;

/// <summary>
/// Settings dialog with four tabs: General, MCP Servers, Appearance, and About.
/// All changes are committed only when the user clicks Save.
/// </summary>
public sealed class SettingsForm : Form
{
    // ── Palette (matches QueryPopup dark theme) ────────────────────────────────
    private static readonly Color BgColor     = Color.FromArgb(0x1A, 0x1A, 0x2E);
    private static readonly Color SurfaceColor = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color AccentColor  = Color.FromArgb(0x6C, 0x63, 0xFF);
    private static readonly Color TextColor    = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private static readonly Color TextSecColor = Color.FromArgb(0xA0, 0xA0, 0xA0);

    // ── Services ───────────────────────────────────────────────────────────────
    private readonly ServiceClient _client;

    // ── Tab 1 – General ────────────────────────────────────────────────────────
    private readonly TextBox  _apiKeyBox;
    private readonly Button   _toggleApiKey;
    private readonly Button   _testApiKey;
    private readonly NumericUpDown _portBox;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _startMinimized;
    private readonly TextBox  _hotkeyBox;

    // ── Tab 2 – MCP Servers ────────────────────────────────────────────────────
    private readonly DataGridView _mcpGrid;
    private readonly Button _addServerBtn;
    private readonly Button _importClaudeBtn;

    // ── Tab 3 – Appearance ─────────────────────────────────────────────────────
    private readonly RadioButton _darkTheme;
    private readonly RadioButton _lightTheme;
    private readonly TrackBar    _opacitySlider;
    private readonly Label       _opacityLabel;
    private readonly CheckBox    _autoHide;

    public SettingsForm(ServiceClient client)
    {
        _client = client;

        Text            = "PerplexityXPC Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = false;
        BackColor       = BgColor;
        ForeColor       = TextColor;
        Size            = new Size(560, 460);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);

        // ──────────────────────────────────────────────────────────────────────
        // Tab control
        // ──────────────────────────────────────────────────────────────────────
        var tabs = new TabControl
        {
            Dock      = DockStyle.Fill,
            DrawMode  = TabDrawMode.OwnerDrawFixed,
        };
        tabs.DrawItem += Tabs_DrawItem;

        // ── Tab 1: General ────────────────────────────────────────────────────
        var tabGeneral = new TabPage("General") { BackColor = BgColor, ForeColor = TextColor };

        // API Key
        var apiKeyLabel = MakeLabel("Perplexity API Key:");
        _apiKeyBox = new TextBox
        {
            PasswordChar = '\u2022',
            Dock         = DockStyle.Fill,
            BackColor    = SurfaceColor,
            ForeColor    = TextColor,
            BorderStyle  = BorderStyle.FixedSingle,
            Font         = new Font("Consolas", 9.5f),
        };
        _toggleApiKey = MakeButton("Show", 60);
        _toggleApiKey.Click += (_, _) =>
        {
            bool hiding = _apiKeyBox.PasswordChar == '\0';
            _apiKeyBox.PasswordChar = hiding ? '\u2022' : '\0';
            _toggleApiKey.Text      = hiding ? "Show" : "Hide";
        };
        _testApiKey = MakeButton("Test", 60);
        _testApiKey.BackColor = AccentColor;
        _testApiKey.ForeColor = Color.White;
        _testApiKey.Click    += TestApiKey_Click;

        var apiKeyRow = MakeRow(apiKeyLabel, _apiKeyBox, _toggleApiKey, _testApiKey);

        // Port
        var portLabel = MakeLabel("Service Port:");
        _portBox = new NumericUpDown
        {
            Minimum   = 1024,
            Maximum   = 65535,
            Value     = 47777,
            Width     = 90,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
        };
        var portRow = MakeRow(portLabel, _portBox);

        // Startup options
        _startWithWindows = MakeCheckBox("Start with Windows");
        _startWithWindows.Checked = StartupManager.IsRegistered();
        _startMinimized   = MakeCheckBox("Start minimized (to tray)");

        // Hotkey
        var hotkeyLabel = MakeLabel("Global Hotkey:");
        _hotkeyBox = new TextBox
        {
            Text        = "Ctrl+Shift+P",
            Width       = 140,
            ReadOnly    = true,
            BackColor   = SurfaceColor,
            ForeColor   = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
        };
        var hotkeyRow = MakeRow(hotkeyLabel, _hotkeyBox);

        var genLayout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize    = true,
            Padding     = new Padding(12, 8, 12, 8),
        };
        genLayout.Controls.Add(apiKeyRow);
        genLayout.Controls.Add(portRow);
        genLayout.Controls.Add(_startWithWindows);
        genLayout.Controls.Add(_startMinimized);
        genLayout.Controls.Add(hotkeyRow);
        tabGeneral.Controls.Add(genLayout);

        // ── Tab 2: MCP Servers ────────────────────────────────────────────────
        var tabMcp = new TabPage("MCP Servers") { BackColor = BgColor, ForeColor = TextColor };

        _mcpGrid = new DataGridView
        {
            Dock                    = DockStyle.Fill,
            BackgroundColor         = SurfaceColor,
            ForeColor               = TextColor,
            GridColor               = Color.FromArgb(0x30, 0x30, 0x50),
            BorderStyle             = BorderStyle.None,
            RowHeadersVisible       = false,
            AllowUserToAddRows      = false,
            AllowUserToDeleteRows   = false,
            SelectionMode           = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode     = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
        };
        _mcpGrid.ColumnHeadersDefaultCellStyle.BackColor  = BgColor;
        _mcpGrid.ColumnHeadersDefaultCellStyle.ForeColor  = TextSecColor;
        _mcpGrid.DefaultCellStyle.BackColor               = SurfaceColor;
        _mcpGrid.DefaultCellStyle.ForeColor               = TextColor;
        _mcpGrid.DefaultCellStyle.SelectionBackColor      = AccentColor;
        _mcpGrid.Columns.Add("Name",    "Name");
        _mcpGrid.Columns.Add("Command", "Command");
        _mcpGrid.Columns.Add("Status",  "Status");

        var btnCol = new DataGridViewButtonColumn { Name = "Action", HeaderText = "Action", Text = "Toggle", UseColumnTextForButtonValue = true };
        _mcpGrid.Columns.Add(btnCol);
        _mcpGrid.CellClick += McpGrid_CellClick;

        _addServerBtn    = MakeButton("Add Server\u2026", 120);
        _importClaudeBtn = MakeButton("Import from Claude", 150);
        _addServerBtn.Click    += AddServer_Click;
        _importClaudeBtn.Click += ImportClaude_Click;

        var mcpBtnBar = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 40,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor     = BgColor,
        };
        mcpBtnBar.Controls.Add(_addServerBtn);
        mcpBtnBar.Controls.Add(_importClaudeBtn);

        tabMcp.Controls.Add(_mcpGrid);
        tabMcp.Controls.Add(mcpBtnBar);

        // ── Tab 3: Appearance ─────────────────────────────────────────────────
        var tabAppearance = new TabPage("Appearance") { BackColor = BgColor, ForeColor = TextColor };

        _darkTheme  = new RadioButton { Text = "Dark",  Checked = true, ForeColor = TextColor, BackColor = BgColor };
        _lightTheme = new RadioButton { Text = "Light",               ForeColor = TextColor, BackColor = BgColor };

        _opacitySlider = new TrackBar
        {
            Minimum   = 40,
            Maximum   = 100,
            Value     = 95,
            TickStyle = TickStyle.None,
            Width     = 200,
            BackColor = BgColor,
        };
        _opacityLabel = MakeLabel("Popup Opacity: 95%");
        _opacitySlider.Scroll += (_, _) =>
            _opacityLabel.Text = $"Popup Opacity: {_opacitySlider.Value}%";

        _autoHide = MakeCheckBox("Auto-hide popup on focus loss");
        _autoHide.Checked = false;

        var appearLayout = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoSize      = true,
            Padding       = new Padding(12, 8, 12, 8),
        };
        appearLayout.Controls.Add(MakeLabel("Theme:"));
        appearLayout.Controls.Add(_darkTheme);
        appearLayout.Controls.Add(_lightTheme);
        appearLayout.Controls.Add(new Label { Height = 8, BackColor = BgColor });
        appearLayout.Controls.Add(_opacityLabel);
        appearLayout.Controls.Add(_opacitySlider);
        appearLayout.Controls.Add(new Label { Height = 8, BackColor = BgColor });
        appearLayout.Controls.Add(_autoHide);
        tabAppearance.Controls.Add(appearLayout);

        // ── Tab 4: About ──────────────────────────────────────────────────────
        var tabAbout = new TabPage("About") { BackColor = BgColor, ForeColor = TextColor };

        var aboutLayout = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoSize      = true,
            Padding       = new Padding(12, 12, 12, 8),
        };

        aboutLayout.Controls.Add(MakeBigLabel("PerplexityXPC"));
        aboutLayout.Controls.Add(MakeLabel($"Version {GetVersion()}"));
        aboutLayout.Controls.Add(new Label { Height = 12, BackColor = BgColor });
        aboutLayout.Controls.Add(MakeLink("GitHub Repository", "https://github.com/PerplexityXPC/PerplexityXPC"));
        aboutLayout.Controls.Add(MakeLink("Perplexity API Docs", "https://docs.perplexity.ai"));
        aboutLayout.Controls.Add(new Label { Height = 12, BackColor = BgColor });

        var serviceStatusLabel = MakeLabel("Service: checking\u2026");
        aboutLayout.Controls.Add(serviceStatusLabel);

        tabAbout.Controls.Add(aboutLayout);

        // Poll and display service status
        _ = Task.Run(async () =>
        {
            try
            {
                var status = await _client.GetStatusAsync();
                serviceStatusLabel.Invoke(() =>
                    serviceStatusLabel.Text = $"Service: {status}");
            }
            catch { serviceStatusLabel.Invoke(() => serviceStatusLabel.Text = "Service: unavailable"); }
        });

        // ── Assemble tabs ─────────────────────────────────────────────────────
        tabs.TabPages.Add(tabGeneral);
        tabs.TabPages.Add(tabMcp);
        tabs.TabPages.Add(tabAppearance);
        tabs.TabPages.Add(tabAbout);

        // ── Save / Cancel buttons ─────────────────────────────────────────────
        var saveBtn   = MakeButton("Save",   80);
        var cancelBtn = MakeButton("Cancel", 80);
        saveBtn.BackColor   = AccentColor;
        saveBtn.ForeColor   = Color.White;
        saveBtn.Click      += SaveBtn_Click;
        cancelBtn.Click    += (_, _) => Close();

        var bottomBar = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding       = new Padding(8, 6, 8, 6),
            BackColor     = BgColor,
        };
        bottomBar.Controls.Add(cancelBtn);
        bottomBar.Controls.Add(saveBtn);

        Controls.Add(tabs);
        Controls.Add(bottomBar);

        // Load MCP servers
        _ = LoadMcpServersAsync();
    }

    // ── MCP grid ───────────────────────────────────────────────────────────────

    private async Task LoadMcpServersAsync()
    {
        try
        {
            var servers = await _client.GetMcpServersAsync();
            _mcpGrid.Invoke(() =>
            {
                _mcpGrid.Rows.Clear();
                foreach (var s in servers)
                    _mcpGrid.Rows.Add(s.Name, s.Command, s.IsRunning ? "Running" : "Stopped");
            });
        }
        catch { /* service may not be available */ }
    }

    private async void McpGrid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
        if (_mcpGrid.Columns[e.ColumnIndex].Name != "Action") return;

        string? serverName = _mcpGrid.Rows[e.RowIndex].Cells["Name"].Value?.ToString();
        if (serverName is null) return;

        try
        {
            await _client.RestartMcpServerAsync(serverName);
            await LoadMcpServersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to toggle {serverName}:\n{ex.Message}", "PerplexityXPC");
        }
    }

    private void AddServer_Click(object? sender, EventArgs e)
    {
        using var dlg = new AddMcpServerDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _mcpGrid.Rows.Add(dlg.ServerName, dlg.Command, "Stopped");
        }
    }

    private void ImportClaude_Click(object? sender, EventArgs e)
    {
        string claudeConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude", "claude_desktop_config.json");

        if (!File.Exists(claudeConfig))
        {
            MessageBox.Show("Claude Desktop config not found.\nExpected: " + claudeConfig,
                "Import from Claude", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(claudeConfig));
            if (!doc.RootElement.TryGetProperty("mcpServers", out var servers))
            {
                MessageBox.Show("No mcpServers found in Claude config.", "Import from Claude");
                return;
            }

            int count = 0;
            foreach (var kv in servers.EnumerateObject())
            {
                string name    = kv.Name;
                string command = kv.Value.TryGetProperty("command", out var cmd) ? cmd.GetString() ?? "" : "";
                _mcpGrid.Rows.Add(name, command, "Stopped");
                count++;
            }

            MessageBox.Show($"Imported {count} server(s) from Claude Desktop config.",
                "Import from Claude", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to parse Claude config:\n{ex.Message}", "Import from Claude");
        }
    }

    // ── API key test ───────────────────────────────────────────────────────────

    private async void TestApiKey_Click(object? sender, EventArgs e)
    {
        _testApiKey.Enabled = false;
        _testApiKey.Text    = "\u2026";

        try
        {
            bool ok = await _client.TestApiKeyAsync(_apiKeyBox.Text.Trim());
            MessageBox.Show(ok ? "API key is valid!" : "API key validation failed.",
                "API Key Test", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Test failed: {ex.Message}", "API Key Test",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _testApiKey.Enabled = true;
            _testApiKey.Text    = "Test";
        }
    }

    // ── Save ───────────────────────────────────────────────────────────────────

    private async void SaveBtn_Click(object? sender, EventArgs e)
    {
        try
        {
            // Persist API key and port
            if (!string.IsNullOrWhiteSpace(_apiKeyBox.Text))
                await _client.SetConfigAsync("ApiKey", _apiKeyBox.Text.Trim());

            await _client.SetConfigAsync("Port", (int)_portBox.Value);

            // Startup registry
            if (_startWithWindows.Checked)
                StartupManager.Register();
            else
                StartupManager.Unregister();

            // Appearance settings stored locally
            Properties.Settings.Default.Theme           = _darkTheme.Checked ? "dark" : "light";
            Properties.Settings.Default.PopupOpacity    = _opacitySlider.Value;
            Properties.Settings.Default.AutoHidePopup   = _autoHide.Checked;
            Properties.Settings.Default.Save();

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings:\n{ex.Message}", "Settings",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── TabControl custom draw (dark header) ───────────────────────────────────

    private void Tabs_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var tab   = ((TabControl)sender!).TabPages[e.Index];
        bool active = e.Index == ((TabControl)sender!).SelectedIndex;

        e.Graphics.FillRectangle(
            new SolidBrush(active ? BgColor : SurfaceColor),
            e.Bounds);

        TextRenderer.DrawText(e.Graphics, tab.Text, Font,
            e.Bounds, active ? Color.White : TextSecColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // ── Version helper ─────────────────────────────────────────────────────────

    private static string GetVersion()
        => System.Reflection.Assembly.GetExecutingAssembly()
               .GetName().Version?.ToString(3) ?? "1.0.0";

    // ── UI factory helpers ─────────────────────────────────────────────────────

    private static Label MakeLabel(string text)
        => new Label { Text = text, ForeColor = TextColor, BackColor = Color.Transparent,
                       AutoSize = true, Margin = new Padding(0, 4, 8, 4) };

    private static Label MakeBigLabel(string text)
        => new Label { Text = text, ForeColor = Color.White,
                       BackColor = Color.Transparent, AutoSize = true,
                       Font = new Font("Segoe UI", 14f, FontStyle.Bold) };

    private static CheckBox MakeCheckBox(string text)
        => new CheckBox { Text = text, ForeColor = TextColor, BackColor = Color.Transparent,
                          AutoSize = true, Margin = new Padding(0, 4, 0, 0) };

    private static Button MakeButton(string text, int width)
        => new Button
        {
            Text      = text,
            Width     = width,
            Height    = 28,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(0x40, 0x40, 0x60) },
            Margin    = new Padding(4, 4, 0, 0),
        };

    private static LinkLabel MakeLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true,
                                   ForeColor = Color.FromArgb(0x6C, 0x63, 0xFF),
                                   BackColor = Color.Transparent, LinkColor = Color.FromArgb(0x6C, 0x63, 0xFF),
                                   Margin    = new Padding(0, 4, 0, 0) };
        link.LinkClicked += (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        return link;
    }

    private static Panel MakeRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize      = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin        = new Padding(0, 4, 0, 4),
            BackColor     = Color.Transparent,
        };
        row.Controls.AddRange(controls);
        return row;
    }
}

// ─── Inner dialog: Add MCP Server ─────────────────────────────────────────────

/// <summary>Simple dialog to add a new MCP server entry.</summary>
internal sealed class AddMcpServerDialog : Form
{
    public string ServerName { get; private set; } = "";
    public string Command    { get; private set; } = "";

    private readonly TextBox _nameBox;
    private readonly TextBox _cmdBox;

    internal AddMcpServerDialog()
    {
        Text            = "Add MCP Server";
        Size            = new Size(380, 180);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.CenterParent;
        BackColor       = Color.FromArgb(0x1A, 0x1A, 0x2E);
        ForeColor       = Color.FromArgb(0xE0, 0xE0, 0xE0);

        _nameBox = new TextBox { BackColor = Color.FromArgb(0x16, 0x21, 0x3E), ForeColor = Color.FromArgb(0xE0, 0xE0, 0xE0), Width = 280 };
        _cmdBox  = new TextBox { BackColor = Color.FromArgb(0x16, 0x21, 0x3E), ForeColor = Color.FromArgb(0xE0, 0xE0, 0xE0), Width = 280 };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        layout.Controls.Add(new Label { Text = "Name:",    ForeColor = Color.FromArgb(0xE0, 0xE0, 0xE0), AutoSize = true });
        layout.Controls.Add(_nameBox);
        layout.Controls.Add(new Label { Text = "Command:", ForeColor = Color.FromArgb(0xE0, 0xE0, 0xE0), AutoSize = true });
        layout.Controls.Add(_cmdBox);

        var ok = new Button
        {
            Text         = "Add",
            DialogResult = DialogResult.OK,
            BackColor    = Color.FromArgb(0x6C, 0x63, 0xFF),
            ForeColor    = Color.White,
            FlatStyle    = FlatStyle.Flat,
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) =>
        {
            ServerName = _nameBox.Text.Trim();
            Command    = _cmdBox.Text.Trim();
        };

        layout.Controls.Add(ok);
        Controls.Add(layout);

        AcceptButton = ok;
    }
}
