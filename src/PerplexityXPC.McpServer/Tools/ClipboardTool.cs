using System.Text.Json;
using System.Windows.Forms;
using PerplexityXPC.McpServer.Protocol;

namespace PerplexityXPC.McpServer.Tools;

/// <summary>
/// Provides clipboard read and write access.
/// All clipboard operations must run on an STA thread because the Win32 clipboard API requires it.
/// </summary>
public sealed class ClipboardTool
{
    // -------------------------------------------------------------------------
    //  Tool definitions
    // -------------------------------------------------------------------------

    /// <summary>Returns MCP tool definitions for clipboard operations.</summary>
    public IEnumerable<McpTool> GetToolDefinitions()
    {
        yield return new McpTool
        {
            Name        = "clipboard.get_clipboard",
            Description = "Read the current text content from the system clipboard.",
            InputSchema = new { type = "object", properties = new { } },
        };

        yield return new McpTool
        {
            Name        = "clipboard.set_clipboard",
            Description = "Set the system clipboard to the specified text.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    text = new { type = "string", description = "Text to place on the clipboard." },
                },
                required = new[] { "text" },
            },
        };
    }

    // -------------------------------------------------------------------------
    //  Operations
    // -------------------------------------------------------------------------

    /// <summary>Reads the current clipboard text content.</summary>
    public ToolCallResult GetClipboard(JsonElement args)
    {
        string? text = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                text = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            return ToolCallResult.Failure($"Error reading clipboard: {error.Message}");

        if (text is null || text.Length == 0)
            return ToolCallResult.Success("(Clipboard is empty or contains non-text data)");

        return ToolCallResult.Success(text);
    }

    /// <summary>Sets the clipboard to the specified text.</summary>
    public ToolCallResult SetClipboard(JsonElement args)
    {
        try
        {
            var text = GetRequiredString(args, "text");

            Exception? error = null;

            var thread = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error is not null)
                return ToolCallResult.Failure($"Error setting clipboard: {error.Message}");

            return ToolCallResult.Success($"Clipboard updated ({text.Length:N0} characters).");
        }
        catch (ToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error setting clipboard: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private static string GetRequiredString(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var p) || p.ValueKind == JsonValueKind.Null)
            throw new ToolException($"Required parameter '{key}' is missing.", JsonRpcError.Codes.InvalidParams);
        var v = p.GetString();
        if (v is null)
            throw new ToolException($"Parameter '{key}' must not be null.", JsonRpcError.Codes.InvalidParams);
        return v;
    }
}
