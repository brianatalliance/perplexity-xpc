// ============================================================================
// PerplexityXPC.ContextMenu — ContextMenuHandler.cs
// ============================================================================
// Small .NET 8 console application launched by the Windows Explorer context
// menu integration. Reads a file or folder listing, sends the content to the
// PerplexityXPC service via Named Pipe (with HTTP fallback), and displays the
// response in the tray app query popup.
//
// Usage:
//   PerplexityXPC.ContextMenu.exe --file   "C:\path\to\file.txt"
//   PerplexityXPC.ContextMenu.exe --folder "C:\path\to\folder"
//   PerplexityXPC.ContextMenu.exe --text   "selected text"
//   PerplexityXPC.ContextMenu.exe --file   (used from SendTo — reads all args as paths)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PerplexityXPC.ContextMenu;

// ── Constants ────────────────────────────────────────────────────────────────
internal static class Constants
{
    public const string PipeName       = "PerplexityXPCPipe";
    public const string HttpBaseUrl    = "http://localhost:47777";
    public const int    MaxFileChars   = 10_000;
    public const int    MaxFolderItems = 200;
    public const int    PipeTimeoutMs  = 3_000;
    public const int    HttpTimeoutSec = 30;
}

// ── Message models ───────────────────────────────────────────────────────────
internal sealed class PipeMessage
{
    [JsonPropertyName("action")] public string Action  { get; init; } = "query";
    [JsonPropertyName("query")]  public string Query   { get; init; } = "";
    [JsonPropertyName("source")] public string Source  { get; init; } = "contextmenu";
}

internal sealed class PipeResponse
{
    [JsonPropertyName("success")]  public bool   Success  { get; init; }
    [JsonPropertyName("response")] public string Response { get; init; } = "";
    [JsonPropertyName("error")]    public string? Error   { get; init; }
}

// ── Entry point ──────────────────────────────────────────────────────────────
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false
    };

    static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                ShowUsage();
                return 1;
            }

            string query = BuildQuery(args);

            if (string.IsNullOrWhiteSpace(query))
            {
                ShowError("No content could be extracted from the provided arguments.");
                return 1;
            }

            // Attempt Named Pipe first, fall back to HTTP
            string? response = await TrySendViaPipeAsync(query)
                            ?? await TrySendViaHttpAsync(query);

            if (response is null)
            {
                ShowError("PerplexityXPC service is not reachable.\n\n" +
                          "Make sure the service is running:\n" +
                          "  Get-Service PerplexityXPC | Start-Service");
                return 2;
            }

            // Launch tray popup with the response
            ShowResponsePopup(query, response);
            return 0;
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}\n\n{ex.StackTrace}");
            return 99;
        }
    }

    // ── Query builder ────────────────────────────────────────────────────────

    private static string BuildQuery(string[] args)
    {
        // Parse mode from first argument
        string mode = args[0].TrimStart('-').ToLowerInvariant();

        return mode switch
        {
            "file"   => BuildFileQuery(args.Skip(1).ToArray()),
            "folder" => BuildFolderQuery(GetSinglePath(args, 1)),
            "text"   => BuildTextQuery(string.Join(" ", args.Skip(1))),
            _        => BuildFileQuery(args) // SendTo passes all paths without a mode flag
        };
    }

    private static string GetSinglePath(string[] args, int index)
        => args.Length > index ? args[index] : "";

    /// <summary>
    /// Handles one or more file paths (SendTo can pass multiple).
    /// </summary>
    private static string BuildFileQuery(string[] paths)
    {
        if (paths.Length == 0) return "";

        // Single file
        if (paths.Length == 1)
            return BuildSingleFileQuery(paths[0]);

        // Multiple files — summarise each briefly
        var sb = new StringBuilder();
        sb.AppendLine($"I'm sharing {paths.Length} files for analysis. Please review each and provide insights.\n");

        foreach (string path in paths.Take(5)) // cap at 5 files
        {
            sb.AppendLine($"---");
            sb.AppendLine(BuildSingleFileQuery(path));
        }
        if (paths.Length > 5)
            sb.AppendLine($"\n[{paths.Length - 5} additional files omitted to stay within limits]");

        return sb.ToString();
    }

    private static string BuildSingleFileQuery(string filePath)
    {
        if (!File.Exists(filePath))
            return $"Analyze this path (file not found locally): {filePath}";

        string fileName  = Path.GetFileName(filePath);
        long   sizeBytes = new FileInfo(filePath).Length;
        string sizeHuman = FormatBytes(sizeBytes);

        // Check if binary
        if (IsBinaryFile(filePath))
            return $"Analyze this file: {fileName}\nType: binary / non-text\nSize: {sizeHuman}\n\n" +
                   $"(Binary file — content not extracted. Describe what you know about files of this type " +
                   $"and any common issues.)";

        string content = ReadTextSafe(filePath, Constants.MaxFileChars);
        bool   wasTruncated = sizeBytes > Constants.MaxFileChars;

        var sb = new StringBuilder();
        sb.AppendLine($"Analyze this file: {fileName}");
        sb.AppendLine($"Size: {sizeHuman}");
        if (wasTruncated)
            sb.AppendLine($"Note: File is large — only the first {Constants.MaxFileChars:N0} characters are shown.\n");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(content);
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string BuildFolderQuery(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return $"Analyze this folder (not found locally): {folderPath}";

        string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var entries = new List<string>();
        try
        {
            // Directories first, then files
            foreach (var dir in Directory.GetDirectories(folderPath).Take(Constants.MaxFolderItems / 2).OrderBy(d => d))
                entries.Add($"[DIR]  {Path.GetFileName(dir)}/");

            foreach (var file in Directory.GetFiles(folderPath).Take(Constants.MaxFolderItems).OrderBy(f => f))
            {
                var fi = new FileInfo(file);
                entries.Add($"[FILE] {fi.Name} ({FormatBytes(fi.Length)})");
            }
        }
        catch (UnauthorizedAccessException)
        {
            entries.Add("[Access Denied — some items may not be listed]");
        }

        bool hasMore = false;
        try
        {
            int total = Directory.GetFileSystemEntries(folderPath).Length;
            hasMore   = total > Constants.MaxFolderItems;
        }
        catch { }

        var sb = new StringBuilder();
        sb.AppendLine($"Analyze this folder: {folderName}");
        sb.AppendLine($"Full path: {folderPath}");
        sb.AppendLine($"Contents ({entries.Count} items shown{(hasMore ? ", truncated" : "")}):\n");
        foreach (var e in entries) sb.AppendLine(e);
        if (hasMore)
            sb.AppendLine($"\n[Listing truncated — folder contains more items]");
        sb.AppendLine("\nWhat can you tell me about this folder's structure and purpose?");

        return sb.ToString();
    }

    private static string BuildTextQuery(string selectedText)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
            return "";

        string text = selectedText.Length > Constants.MaxFileChars
            ? selectedText[..Constants.MaxFileChars] + "\n\n[truncated]"
            : selectedText;

        return $"Please analyze the following text:\n\n```\n{text}\n```";
    }

    // ── Transport: Named Pipe ────────────────────────────────────────────────

    private static async Task<string?> TrySendViaPipeAsync(string query)
    {
        try
        {
            using var cts  = new CancellationTokenSource(Constants.PipeTimeoutMs);
            using var pipe = new NamedPipeClientStream(".", Constants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipe.ConnectAsync(Constants.PipeTimeoutMs, cts.Token);

            var message = new PipeMessage { Action = "query", Query = query, Source = "contextmenu" };
            string json = JsonSerializer.Serialize(message, JsonOpts);

            // Write message length prefix (4 bytes, little-endian) then payload
            byte[] payload    = Encoding.UTF8.GetBytes(json);
            byte[] lengthBuf  = BitConverter.GetBytes(payload.Length);
            await pipe.WriteAsync(lengthBuf, cts.Token);
            await pipe.WriteAsync(payload,   cts.Token);
            await pipe.FlushAsync(cts.Token);

            // Read response with length prefix
            byte[] lenResponse = new byte[4];
            await pipe.ReadExactlyAsync(lenResponse, cts.Token);
            int responseLen  = BitConverter.ToInt32(lenResponse);
            byte[] respBytes = new byte[responseLen];
            await pipe.ReadExactlyAsync(respBytes, cts.Token);

            string responseJson  = Encoding.UTF8.GetString(respBytes);
            var    pipeResponse  = JsonSerializer.Deserialize<PipeResponse>(responseJson, JsonOpts);

            return pipeResponse?.Success == true ? pipeResponse.Response : null;
        }
        catch (Exception ex) when (ex is TimeoutException
                                      or IOException
                                      or OperationCanceledException
                                      or UnauthorizedAccessException)
        {
            // Service not available — fall through to HTTP
            return null;
        }
    }

    // ── Transport: HTTP ──────────────────────────────────────────────────────

    private static async Task<string?> TrySendViaHttpAsync(string query)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Constants.HttpTimeoutSec) };
            client.DefaultRequestHeaders.Add("User-Agent", "PerplexityXPC-ContextMenu/1.0");

            var payload = new { query, source = "contextmenu" };
            var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{Constants.HttpBaseUrl}/v1/query", content);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            using var doc       = JsonDocument.Parse(responseBody);

            if (doc.RootElement.TryGetProperty("response", out var resp))
                return resp.GetString();

            if (doc.RootElement.TryGetProperty("content", out var contentProp))
                return contentProp.GetString();

            return responseBody; // Return raw if we can't parse
        }
        catch (HttpRequestException)
        {
            return null; // Service not reachable
        }
        catch (Exception ex)
        {
            ShowError($"HTTP transport error: {ex.Message}");
            return null;
        }
    }

    // ── Response display ─────────────────────────────────────────────────────

    private static void ShowResponsePopup(string query, string response)
    {
        // Try to send an "open popup" command to the tray application via pipe
        // The tray app listens for { action: "show_popup", query, response } messages
        Task.Run(async () =>
        {
            try
            {
                using var cts  = new CancellationTokenSource(2000);
                using var pipe = new NamedPipeClientStream(".", "PerplexityXPCTrayPipe", PipeDirection.Out, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000, cts.Token);

                var msg     = new { action = "show_popup", query, response };
                byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, JsonOpts));
                byte[] len  = BitConverter.GetBytes(data.Length);
                await pipe.WriteAsync(len,  cts.Token);
                await pipe.WriteAsync(data, cts.Token);
                await pipe.FlushAsync(cts.Token);
            }
            catch
            {
                // Tray app may not be running; fall back to console / balloon
            }
        }).GetAwaiter().GetResult();

        // Always print to console (visible if launched from terminal)
        Console.WriteLine();
        Console.WriteLine("=== Perplexity Response ===");
        Console.WriteLine(response);
        Console.WriteLine("===========================");

        // On Windows, attempt to show a balloon notification as last resort
        TryShowBalloonNotification("PerplexityXPC", response.Length > 200 ? response[..200] + "…" : response);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ReadTextSafe(string path, int maxChars)
    {
        try
        {
            // Try UTF-8 first, fall back to current system encoding
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            char[] buffer = new char[maxChars];
            int read      = reader.ReadBlock(buffer, 0, maxChars);
            return new string(buffer, 0, read);
        }
        catch
        {
            return "[Error reading file content]";
        }
    }

    private static bool IsBinaryFile(string path)
    {
        try
        {
            // Sample first 512 bytes; if >30% are non-printable and non-whitespace, treat as binary
            const int sampleSize = 512;
            using var fs = File.OpenRead(path);
            byte[] buf = new byte[sampleSize];
            int read   = fs.Read(buf, 0, sampleSize);
            if (read == 0) return false;

            int nonText = buf.Take(read).Count(b => b < 0x09 || (b > 0x0D && b < 0x20) || b == 0x7F);
            return nonText > read * 0.30;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024               => $"{bytes} B",
        < 1024 * 1024        => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _                    => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };

    private static void ShowError(string message)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Use MessageBox via P/Invoke to show a visible error dialog
            _ = NativeMethods.MessageBox(IntPtr.Zero, message, "PerplexityXPC Error",
                0x10 /* MB_ICONERROR */ | 0x0 /* MB_OK */);
        }
        else
        {
            Console.Error.WriteLine($"[ERROR] {message}");
        }
    }

    private static void TryShowBalloonNotification(string title, string text)
    {
        // Attempt via PowerShell (avoids a heavy WinForms dependency in this small helper)
        try
        {
            string escapedText  = text.Replace("'", "''");
            string escapedTitle = title.Replace("'", "''");
            string psCmd = $@"
Add-Type -AssemblyName System.Windows.Forms
$n = New-Object System.Windows.Forms.NotifyIcon
$n.Icon = [System.Drawing.SystemIcons]::Information
$n.Visible = $true
$n.ShowBalloonTip(5000, '{escapedTitle}', '{escapedText}', [System.Windows.Forms.ToolTipIcon]::Info)
Start-Sleep -Seconds 6
$n.Visible = $false
$n.Dispose()";
            Process.Start(new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NoProfile -WindowStyle Hidden -Command \"{psCmd}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = false
            });
        }
        catch
        {
            // Non-fatal
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("""
            PerplexityXPC Context Menu Handler
            Usage:
              PerplexityXPC.ContextMenu.exe --file   <path>          Analyze a file
              PerplexityXPC.ContextMenu.exe --file   <p1> <p2> ...   Analyze multiple files
              PerplexityXPC.ContextMenu.exe --folder <path>          Analyze a folder listing
              PerplexityXPC.ContextMenu.exe --text   "<text>"        Analyze arbitrary text
            """);
    }
}

// ── P/Invoke ─────────────────────────────────────────────────────────────────
internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
