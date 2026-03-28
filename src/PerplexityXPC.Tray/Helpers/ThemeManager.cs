namespace PerplexityXPC.Tray.Helpers;

// ─── Theme definitions ────────────────────────────────────────────────────────

/// <summary>Selects between the two built-in themes.</summary>
public enum AppTheme { Dark, Light }

/// <summary>A complete set of colours that describe one visual theme.</summary>
public sealed record ThemePalette(
    Color Background,
    Color Surface,
    Color Accent,
    Color Text,
    Color TextSecondary,
    Color Border);

// ─── ThemeManager ─────────────────────────────────────────────────────────────

/// <summary>
/// Applies a consistent colour palette to Windows Forms controls.
///
/// <para>
/// Call <see cref="ApplyTheme(Form, AppTheme)"/> after constructing a form (or
/// after dynamically adding controls) to recursively paint every control with
/// the chosen theme colours.
/// </para>
///
/// <para>Predefined palettes:</para>
/// <list type="bullet">
///   <item><description><see cref="Dark"/>  — #1a1a2e background, #6c63ff accent</description></item>
///   <item><description><see cref="Light"/> — #ffffff background, #6c63ff accent</description></item>
/// </list>
/// </summary>
public static class ThemeManager
{
    // ── Palette singletons ─────────────────────────────────────────────────────

    /// <summary>
    /// Dark theme:
    /// <list type="bullet">
    ///   <item><term>Background</term><description>#1a1a2e</description></item>
    ///   <item><term>Surface</term>    <description>#16213e</description></item>
    ///   <item><term>Accent</term>     <description>#6c63ff (Perplexity purple)</description></item>
    ///   <item><term>Text</term>       <description>#e0e0e0</description></item>
    ///   <item><term>TextSecondary</term><description>#a0a0a0</description></item>
    ///   <item><term>Border</term>     <description>#2a2a4a</description></item>
    /// </list>
    /// </summary>
    public static readonly ThemePalette Dark = new(
        Background:    Color.FromArgb(0x1A, 0x1A, 0x2E),
        Surface:       Color.FromArgb(0x16, 0x21, 0x3E),
        Accent:        Color.FromArgb(0x6C, 0x63, 0xFF),
        Text:          Color.FromArgb(0xE0, 0xE0, 0xE0),
        TextSecondary: Color.FromArgb(0xA0, 0xA0, 0xA0),
        Border:        Color.FromArgb(0x2A, 0x2A, 0x4A));

    /// <summary>
    /// Light theme:
    /// <list type="bullet">
    ///   <item><term>Background</term>    <description>#ffffff</description></item>
    ///   <item><term>Surface</term>       <description>#f5f5f5</description></item>
    ///   <item><term>Accent</term>        <description>#6c63ff (Perplexity purple)</description></item>
    ///   <item><term>Text</term>          <description>#1a1a1a</description></item>
    ///   <item><term>TextSecondary</term> <description>#666666</description></item>
    ///   <item><term>Border</term>        <description>#dddddd</description></item>
    /// </list>
    /// </summary>
    public static readonly ThemePalette Light = new(
        Background:    Color.White,
        Surface:       Color.FromArgb(0xF5, 0xF5, 0xF5),
        Accent:        Color.FromArgb(0x6C, 0x63, 0xFF),
        Text:          Color.FromArgb(0x1A, 0x1A, 0x1A),
        TextSecondary: Color.FromArgb(0x66, 0x66, 0x66),
        Border:        Color.FromArgb(0xDD, 0xDD, 0xDD));

    // ── Current theme ──────────────────────────────────────────────────────────

    /// <summary>The palette that is currently active across all themed forms.</summary>
    public static ThemePalette Current { get; private set; } = Dark;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies <paramref name="theme"/> to <paramref name="form"/> and all
    /// descendant controls recursively.  Also sets <see cref="Current"/> so
    /// newly created controls can query it.
    /// </summary>
    public static void ApplyTheme(Form form, AppTheme theme = AppTheme.Dark)
    {
        Current = theme == AppTheme.Light ? Light : Dark;
        ApplyPalette(form, Current);
    }

    /// <summary>
    /// Applies an explicit <paramref name="palette"/> to <paramref name="form"/>
    /// and all descendants without changing <see cref="Current"/>.
    /// </summary>
    public static void ApplyPalette(Control root, ThemePalette palette)
    {
        ApplyToControl(root, palette);
        ApplyRecursive(root.Controls, palette);
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    private static void ApplyRecursive(Control.ControlCollection controls, ThemePalette p)
    {
        foreach (Control ctrl in controls)
        {
            ApplyToControl(ctrl, p);
            if (ctrl.Controls.Count > 0)
                ApplyRecursive(ctrl.Controls, p);
        }
    }

    private static void ApplyToControl(Control ctrl, ThemePalette p)
    {
        ctrl.ForeColor = p.Text;

        // Order matters: most-derived types must come before their base types.
        switch (ctrl)
        {
            // ── Forms ──────────────────────────────────────────────────────────
            case Form form:
                form.BackColor = p.Background;
                break;

            // ── Panels (specific subtypes before Panel) ───────────────────────
            case GroupBox gb:
                gb.BackColor = p.Background;
                gb.ForeColor = p.TextSecondary;
                break;

            case TabPage tp:
                tp.BackColor = p.Background;
                break;

            case FlowLayoutPanel flp:
                flp.BackColor = p.Background;
                break;

            case TableLayoutPanel tlp:
                tlp.BackColor = p.Background;
                break;

            case Panel panel:
                panel.BackColor = p.Surface;
                break;

            case TabControl:
                ctrl.BackColor = p.Background;
                break;

            // ── Text inputs (RichTextBox before TextBox) ──────────────────────
            case RichTextBox rtb:
                rtb.BackColor = p.Surface;
                rtb.ForeColor = p.Text;
                break;

            case TextBox tb:
                tb.BackColor   = p.Surface;
                tb.ForeColor   = p.Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox cb:
                cb.BackColor = p.Surface;
                cb.ForeColor = p.Text;
                cb.FlatStyle = FlatStyle.Flat;
                break;

            // ── Labels (LinkLabel before Label) ───────────────────────────────
            case LinkLabel ll:
                ll.BackColor  = Color.Transparent;
                ll.ForeColor  = p.Accent;
                ll.LinkColor  = p.Accent;
                ll.ActiveLinkColor = p.Accent;
                break;

            case Label lbl:
                lbl.BackColor = Color.Transparent;
                lbl.ForeColor = p.Text;
                break;

            // ── Buttons ───────────────────────────────────────────────────────
            case Button btn:
                btn.BackColor  = p.Surface;
                btn.ForeColor  = p.Text;
                btn.FlatStyle  = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = p.Border;
                break;

            // ── CheckBoxes / RadioButtons ──────────────────────────────────────
            case CheckBox chk:
                chk.BackColor = Color.Transparent;
                chk.ForeColor = p.Text;
                break;

            case RadioButton rb:
                rb.BackColor = Color.Transparent;
                rb.ForeColor = p.Text;
                break;

            // ── Numeric updown ─────────────────────────────────────────────────
            case NumericUpDown nud:
                nud.BackColor = p.Surface;
                nud.ForeColor = p.Text;
                break;

            // ── DataGridView ───────────────────────────────────────────────────
            case DataGridView dgv:
                dgv.BackgroundColor = p.Surface;
                dgv.ForeColor       = p.Text;
                dgv.GridColor       = p.Border;
                dgv.DefaultCellStyle.BackColor          = p.Surface;
                dgv.DefaultCellStyle.ForeColor          = p.Text;
                dgv.DefaultCellStyle.SelectionBackColor = p.Accent;
                dgv.DefaultCellStyle.SelectionForeColor = Color.White;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = p.Background;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = p.TextSecondary;
                dgv.EnableHeadersVisualStyles            = false;
                break;

            // ── TrackBar ──────────────────────────────────────────────────────
            case TrackBar tb2:
                tb2.BackColor = p.Background;
                break;

            default:
                ctrl.BackColor = p.Background;
                break;
        }
    }
}
