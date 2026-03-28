using System.Text;
using System.Text.Json;
using PerplexityXPC.McpServer.Configuration;
using PerplexityXPC.McpServer.Protocol;

namespace PerplexityXPC.McpServer.Tools;

/// <summary>
/// Provides read-only filesystem operations restricted to configured allowed directories.
/// </summary>
public sealed class FilesystemTool
{
    private readonly McpServerConfig _config;

    /// <summary>Initialises the tool with the server configuration.</summary>
    public FilesystemTool(McpServerConfig config) => _config = config;

    // -------------------------------------------------------------------------
    //  Tool definitions
    // -------------------------------------------------------------------------

    /// <summary>Returns MCP tool definitions for all filesystem operations.</summary>
    public IEnumerable<McpTool> GetToolDefinitions()
    {
        yield return new McpTool
        {
            Name        = "filesystem.list_directory",
            Description = "List files and folders in a directory. Restricted to allowed directories.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    path = new { type = "string", description = "Absolute path to the directory to list." },
                },
                required = new[] { "path" },
            },
        };

        yield return new McpTool
        {
            Name        = "filesystem.read_file",
            Description = "Read the text contents of a file. Limited to 50,000 characters by default.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    path = new { type = "string", description = "Absolute path to the file to read." },
                },
                required = new[] { "path" },
            },
        };

        yield return new McpTool
        {
            Name        = "filesystem.search_files",
            Description = "Search for files matching a glob pattern within a directory.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    path    = new { type = "string", description = "Root directory to search within." },
                    pattern = new { type = "string", description = "File name pattern, e.g. *.txt or report_*.csv" },
                    recurse = new { type = "boolean", description = "Search subdirectories recursively. Default true." },
                },
                required = new[] { "path", "pattern" },
            },
        };

        yield return new McpTool
        {
            Name        = "filesystem.file_info",
            Description = "Get metadata for a file or directory: size, creation date, last modified, attributes.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    path = new { type = "string", description = "Absolute path to the file or directory." },
                },
                required = new[] { "path" },
            },
        };
    }

    // -------------------------------------------------------------------------
    //  Operations
    // -------------------------------------------------------------------------

    /// <summary>Lists entries in the specified directory.</summary>
    public ToolCallResult ListDirectory(JsonElement args)
    {
        try
        {
            var path = GetRequiredString(args, "path");
            var resolved = ResolveSafe(path);

            if (!Directory.Exists(resolved))
                return ToolCallResult.Failure($"Directory not found: {resolved}");

            var sb = new StringBuilder();
            sb.AppendLine($"Directory: {resolved}");
            sb.AppendLine();

            var entries = new List<(string Kind, string Name, long Size, DateTime Modified)>();

            foreach (var dir in Directory.EnumerateDirectories(resolved))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    entries.Add(("DIR ", info.Name, 0, info.LastWriteTime));
                }
                catch { /* skip inaccessible */ }
            }

            foreach (var file in Directory.EnumerateFiles(resolved))
            {
                try
                {
                    var info = new FileInfo(file);
                    entries.Add(("FILE", info.Name, info.Length, info.LastWriteTime));
                }
                catch { /* skip inaccessible */ }
            }

            sb.AppendLine($"{"Type",-5} {"Name",-50} {"Size",12} {"Modified",-22}");
            sb.AppendLine(new string('-', 92));

            foreach (var (kind, name, size, mod) in entries.OrderBy(e => e.Kind).ThenBy(e => e.Name))
            {
                var sizeStr = kind == "DIR " ? "<dir>" : FormatBytes(size);
                sb.AppendLine($"{kind,-5} {name,-50} {sizeStr,12} {mod:yyyy-MM-dd HH:mm:ss}");
            }

            sb.AppendLine();
            sb.AppendLine($"Total: {entries.Count} entries");

            return ToolCallResult.Success(sb.ToString());
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolCallResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error listing directory: {ex.Message}");
        }
    }

    /// <summary>Reads the text content of a file.</summary>
    public ToolCallResult ReadFile(JsonElement args)
    {
        try
        {
            var path = GetRequiredString(args, "path");
            var resolved = ResolveSafe(path);

            if (!File.Exists(resolved))
                return ToolCallResult.Failure($"File not found: {resolved}");

            string content;
            bool truncated = false;

            using var reader = new StreamReader(resolved, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[_config.MaxFileReadChars + 1];
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read > _config.MaxFileReadChars)
            {
                content   = new string(buffer, 0, _config.MaxFileReadChars);
                truncated = true;
            }
            else
            {
                content = new string(buffer, 0, read);
            }

            if (truncated)
                content += $"\n\n[TRUNCATED: file exceeds {_config.MaxFileReadChars:N0} characters. Only the first portion is shown.]";

            return ToolCallResult.Success(content);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolCallResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error reading file: {ex.Message}");
        }
    }

    /// <summary>Searches for files matching a glob pattern.</summary>
    public ToolCallResult SearchFiles(JsonElement args)
    {
        try
        {
            var path    = GetRequiredString(args, "path");
            var pattern = GetRequiredString(args, "pattern");
            var recurse = args.TryGetProperty("recurse", out var rp) ? rp.GetBoolean() : true;

            var resolved = ResolveSafe(path);

            if (!Directory.Exists(resolved))
                return ToolCallResult.Failure($"Directory not found: {resolved}");

            var option  = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var matches = Directory.EnumerateFiles(resolved, pattern, option)
                                   .Take(500)
                                   .ToList();

            if (matches.Count == 0)
                return ToolCallResult.Success($"No files matching '{pattern}' found in {resolved}");

            var sb = new StringBuilder();
            sb.AppendLine($"Search results for pattern '{pattern}' in {resolved}:");
            sb.AppendLine();
            foreach (var m in matches)
            {
                try
                {
                    var fi = new FileInfo(m);
                    sb.AppendLine($"{m,-80} {FormatBytes(fi.Length),10}  {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                }
                catch
                {
                    sb.AppendLine(m);
                }
            }

            if (matches.Count == 500)
                sb.AppendLine("\n[Results truncated at 500 entries]");
            else
                sb.AppendLine($"\nTotal: {matches.Count} file(s) found");

            return ToolCallResult.Success(sb.ToString());
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolCallResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error searching files: {ex.Message}");
        }
    }

    /// <summary>Returns metadata for a file or directory.</summary>
    public ToolCallResult FileInfo(JsonElement args)
    {
        try
        {
            var path     = GetRequiredString(args, "path");
            var resolved = ResolveSafe(path);

            if (File.Exists(resolved))
            {
                var fi = new FileInfo(resolved);
                var sb = new StringBuilder();
                sb.AppendLine($"Path:          {fi.FullName}");
                sb.AppendLine($"Type:          File");
                sb.AppendLine($"Size:          {FormatBytes(fi.Length)} ({fi.Length:N0} bytes)");
                sb.AppendLine($"Created:       {fi.CreationTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"LastModified:  {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"LastAccessed:  {fi.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Attributes:    {fi.Attributes}");
                sb.AppendLine($"ReadOnly:      {fi.IsReadOnly}");
                sb.AppendLine($"Extension:     {fi.Extension}");
                return ToolCallResult.Success(sb.ToString());
            }
            else if (Directory.Exists(resolved))
            {
                var di = new DirectoryInfo(resolved);
                var sb = new StringBuilder();
                sb.AppendLine($"Path:          {di.FullName}");
                sb.AppendLine($"Type:          Directory");
                sb.AppendLine($"Created:       {di.CreationTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"LastModified:  {di.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"LastAccessed:  {di.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Attributes:    {di.Attributes}");
                return ToolCallResult.Success(sb.ToString());
            }
            else
            {
                return ToolCallResult.Failure($"Path not found: {resolved}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolCallResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error getting file info: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Private helpers
    // -------------------------------------------------------------------------

    private string ResolveSafe(string path)
    {
        var resolved = Path.GetFullPath(path);

        if (!_config.IsPathAllowed(resolved))
            throw new UnauthorizedAccessException(
                $"Path '{resolved}' is outside the allowed directories. " +
                $"Allowed: {string.Join(", ", _config.AllowedDirectories)}");

        return resolved;
    }

    private static string GetRequiredString(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null)
            throw new ToolException($"Required parameter '{key}' is missing.", JsonRpcError.Codes.InvalidParams);

        var value = prop.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ToolException($"Parameter '{key}' must not be empty.", JsonRpcError.Codes.InvalidParams);

        return value;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024             => $"{bytes} B",
        < 1024 * 1024      => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _                  => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}
