using System.Text;
using System.Text.RegularExpressions;
using PerplexityXPC.Tray.Helpers;

namespace PerplexityXPC.Tray.Helpers;

/// <summary>
/// Static helper that renders a subset of Markdown into a <see cref="RichTextBox"/>
/// using Windows Forms selection-based formatting.
///
/// <para>Supported syntax:</para>
/// <list type="bullet">
///   <item><description><c># Heading</c> / <c>## Heading</c> / <c>### Heading</c> - larger bold text</description></item>
///   <item><description><c>**bold**</c> or <c>__bold__</c> - bold weight</description></item>
///   <item><description><c>`inline code`</c> - Consolas, slightly highlighted</description></item>
///   <item><description><c>```code block```</c> (fenced, multi-line) - Consolas, indented</description></item>
///   <item><description><c>- item</c> or <c>* item</c> - bullet list</description></item>
///   <item><description><c>[text](url)</c> - blue underlined clickable link</description></item>
/// </list>
///
/// <para>Images and tables are not supported.</para>
/// </summary>
public static class MarkdownRenderer
{
    // ── Regex patterns ──────────────────────────────────────────────────────────

    private static readonly Regex HeadingRegex     = new(@"^(#{1,3})\s+(.+)$",                 RegexOptions.Compiled);
    private static readonly Regex BoldRegex        = new(@"\*\*(.+?)\*\*|__(.+?)__",            RegexOptions.Compiled);
    private static readonly Regex InlineCodeRegex  = new(@"`([^`\n]+)`",                         RegexOptions.Compiled);
    private static readonly Regex LinkRegex        = new(@"\[([^\]]+)\]\((https?://[^\)]+)\)",  RegexOptions.Compiled);
    private static readonly Regex BulletRegex      = new(@"^[\-\*]\s+(.+)$",                   RegexOptions.Compiled);
    private static readonly Regex FencedCodeOpen   = new(@"^```",                               RegexOptions.Compiled);

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears <paramref name="rtb"/> and renders <paramref name="markdownText"/>
    /// with formatting applied via <see cref="RichTextBox"/> selection properties.
    /// </summary>
    /// <param name="rtb">The target <see cref="RichTextBox"/>. Must not be null.</param>
    /// <param name="markdownText">Markdown-ish source text.</param>
    /// <param name="palette">Theme palette for colour choices.</param>
    /// <param name="linkRanges">
    /// Optional dictionary that will be populated with (startIndex, length) -> URL
    /// entries so the caller can handle link clicks.
    /// </param>
    public static void RenderToRichTextBox(
        RichTextBox rtb,
        string markdownText,
        ThemePalette palette,
        Dictionary<(int Start, int Length), string>? linkRanges = null)
    {
        if (rtb is null) throw new ArgumentNullException(nameof(rtb));
        if (string.IsNullOrEmpty(markdownText)) return;

        rtb.Clear();

        var lines = markdownText.Replace("\r\n", "\n").Split('\n');
        bool inCodeBlock = false;
        var codeBlockLines = new List<string>();

        void FlushCodeBlock()
        {
            if (codeBlockLines.Count == 0) return;
            AppendCodeBlock(rtb, string.Join("\n", codeBlockLines), palette);
            codeBlockLines.Clear();
        }

        foreach (string rawLine in lines)
        {
            // Detect fenced code block boundaries
            if (FencedCodeOpen.IsMatch(rawLine))
            {
                if (inCodeBlock)
                {
                    // Closing fence
                    inCodeBlock = false;
                    FlushCodeBlock();
                }
                else
                {
                    // Opening fence - flush any pending normal content first
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines.Add(rawLine);
                continue;
            }

            // Heading
            var headingMatch = HeadingRegex.Match(rawLine);
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Length;
                string text = headingMatch.Groups[2].Value;
                AppendHeading(rtb, text, level, palette);
                continue;
            }

            // Bullet list item
            var bulletMatch = BulletRegex.Match(rawLine);
            if (bulletMatch.Success)
            {
                string content = bulletMatch.Groups[1].Value;
                AppendBulletLine(rtb, content, palette, linkRanges);
                continue;
            }

            // Empty line -> paragraph break
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                AppendPlain(rtb, "\n", palette.Text, rtb.Font);
                continue;
            }

            // Normal paragraph line with inline markup
            AppendInlineMarkup(rtb, rawLine + "\n", palette, linkRanges);
        }

        // If file ended while inside a code block, flush it
        if (inCodeBlock)
            FlushCodeBlock();
    }

    /// <summary>
    /// Returns a plain-text version of <paramref name="markdownText"/> with all
    /// Markdown syntax stripped.  Useful for clipboard export.
    /// </summary>
    public static string StripMarkdown(string markdownText)
    {
        if (string.IsNullOrEmpty(markdownText)) return string.Empty;

        var sb = new StringBuilder(markdownText);

        // Remove fenced code block markers
        sb.Replace("```", string.Empty);

        // Remove heading markers
        string result = Regex.Replace(sb.ToString(), @"^#{1,3}\s+", string.Empty, RegexOptions.Multiline);

        // Replace bold markers
        result = Regex.Replace(result, @"\*\*(.+?)\*\*|__(.+?)__", m =>
            m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        // Replace inline code
        result = Regex.Replace(result, @"`([^`\n]+)`", "$1");

        // Replace links
        result = Regex.Replace(result, @"\[([^\]]+)\]\(https?://[^\)]+\)", "$1");

        // Remove bullet markers
        result = Regex.Replace(result, @"^[\-\*]\s+", string.Empty, RegexOptions.Multiline);

        return result.Trim();
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    /// <summary>Appends a heading line at the appropriate font size.</summary>
    private static void AppendHeading(RichTextBox rtb, string text, int level, ThemePalette palette)
    {
        float size = level switch
        {
            1 => 15f,
            2 => 13f,
            _ => 11.5f,
        };

        int start = rtb.TextLength;
        rtb.SelectionStart  = start;
        rtb.SelectionLength = 0;
        rtb.SelectionFont   = new Font(rtb.Font.FontFamily, size, FontStyle.Bold);
        rtb.SelectionColor  = palette.Text;
        rtb.AppendText(text + "\n");
    }

    /// <summary>Appends a code block in monospace with a slightly indented style.</summary>
    private static void AppendCodeBlock(RichTextBox rtb, string code, ThemePalette palette)
    {
        // Leading blank line for spacing
        AppendPlain(rtb, "\n", palette.Text, rtb.Font);

        var codeFont = new Font("Consolas", rtb.Font.Size - 0.5f, FontStyle.Regular);

        int start = rtb.TextLength;
        rtb.SelectionStart  = start;
        rtb.SelectionLength = 0;
        rtb.SelectionFont   = codeFont;
        rtb.SelectionColor  = Color.FromArgb(0xBB, 0xBB, 0xCC);
        rtb.SelectionIndent = 16;
        rtb.AppendText(code + "\n");

        // Reset indent
        rtb.SelectionIndent = 0;

        AppendPlain(rtb, "\n", palette.Text, rtb.Font);
    }

    /// <summary>Appends a bullet list item with a bullet character prefix.</summary>
    private static void AppendBulletLine(
        RichTextBox rtb,
        string content,
        ThemePalette palette,
        Dictionary<(int Start, int Length), string>? linkRanges)
    {
        int start = rtb.TextLength;
        rtb.SelectionStart  = start;
        rtb.SelectionLength = 0;
        rtb.SelectionFont   = rtb.Font;
        rtb.SelectionColor  = palette.Accent;
        rtb.SelectionIndent = 8;
        rtb.AppendText("\u2022 ");

        AppendInlineMarkup(rtb, content + "\n", palette, linkRanges);
        rtb.SelectionIndent = 0;
    }

    /// <summary>
    /// Appends a line that may contain bold, inline code, and link markup,
    /// splitting the line into styled segments.
    /// </summary>
    private static void AppendInlineMarkup(
        RichTextBox rtb,
        string line,
        ThemePalette palette,
        Dictionary<(int Start, int Length), string>? linkRanges)
    {
        // Build a list of (startIndex, length, type, extra) segments
        // We process the line character by character using regex offsets.

        var segments = BuildSegments(line);

        foreach (var seg in segments)
        {
            switch (seg.Kind)
            {
                case SegmentKind.Plain:
                    AppendPlain(rtb, seg.Text, palette.Text, rtb.Font);
                    break;

                case SegmentKind.Bold:
                    AppendPlain(rtb, seg.Text,
                        palette.Text,
                        new Font(rtb.Font, FontStyle.Bold));
                    break;

                case SegmentKind.InlineCode:
                    AppendPlain(rtb, seg.Text,
                        Color.FromArgb(0xBB, 0xBB, 0xCC),
                        new Font("Consolas", rtb.Font.Size - 0.5f));
                    break;

                case SegmentKind.Link:
                    int linkStart = rtb.TextLength;
                    AppendPlain(rtb, seg.Text,
                        Color.FromArgb(0x64, 0x9B, 0xFF),
                        new Font(rtb.Font, FontStyle.Underline));
                    int linkLen = rtb.TextLength - linkStart;
                    linkRanges?.TryAdd((linkStart, linkLen), seg.Url ?? string.Empty);
                    break;
            }
        }
    }

    /// <summary>Appends plain text with the given font and colour.</summary>
    private static void AppendPlain(RichTextBox rtb, string text, Color color, Font font)
    {
        int start = rtb.TextLength;
        rtb.SelectionStart  = start;
        rtb.SelectionLength = 0;
        rtb.SelectionFont   = font;
        rtb.SelectionColor  = color;
        rtb.AppendText(text);
    }

    // ── Segment model ───────────────────────────────────────────────────────────

    private enum SegmentKind { Plain, Bold, InlineCode, Link }

    private sealed record Segment(SegmentKind Kind, string Text, string? Url = null);

    /// <summary>
    /// Splits <paramref name="line"/> into a flat list of typed segments by
    /// scanning for the earliest-starting match of bold / inline-code / link
    /// patterns and recursively processing the remainder.
    /// </summary>
    private static List<Segment> BuildSegments(string line)
    {
        var result = new List<Segment>();
        BuildSegmentsInto(line, result);
        return result;
    }

    private static void BuildSegmentsInto(string text, List<Segment> result)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Find the earliest match across all inline patterns
        Match? earliest = null;
        SegmentKind kind = SegmentKind.Plain;
        string? extraUrl = null;

        Check(BoldRegex.Match(text),       SegmentKind.Bold);
        Check(InlineCodeRegex.Match(text), SegmentKind.InlineCode);
        Check(LinkRegex.Match(text),       SegmentKind.Link);

        void Check(Match m, SegmentKind k)
        {
            if (m.Success && (earliest is null || m.Index < earliest.Index))
            {
                earliest = m;
                kind     = k;
            }
        }

        if (earliest is null)
        {
            result.Add(new Segment(SegmentKind.Plain, text));
            return;
        }

        // Text before the match
        if (earliest.Index > 0)
            result.Add(new Segment(SegmentKind.Plain, text[..earliest.Index]));

        // The matched segment
        string matchText = kind switch
        {
            SegmentKind.Bold       => earliest.Groups[1].Success
                                       ? earliest.Groups[1].Value
                                       : earliest.Groups[2].Value,
            SegmentKind.InlineCode => earliest.Groups[1].Value,
            SegmentKind.Link       => earliest.Groups[1].Value,
            _                      => earliest.Value,
        };

        if (kind == SegmentKind.Link)
            extraUrl = earliest.Groups[2].Value;

        result.Add(new Segment(kind, matchText, extraUrl));

        // Remainder
        int after = earliest.Index + earliest.Length;
        if (after < text.Length)
            BuildSegmentsInto(text[after..], result);
    }
}
