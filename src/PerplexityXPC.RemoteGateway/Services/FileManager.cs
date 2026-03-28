using PerplexityXPC.RemoteGateway.Configuration;

namespace PerplexityXPC.RemoteGateway.Services;

// -----------------------------------------------------------------------
// Data transfer objects
// -----------------------------------------------------------------------

/// <summary>Directory listing response.</summary>
public sealed record DirectoryListing(
    string Path,
    IReadOnlyList<DirectoryEntry> Entries,
    int TotalCount);

/// <summary>A single file-system entry within a directory listing.</summary>
public sealed record DirectoryEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTime LastModified);

/// <summary>File content response.</summary>
public sealed record FileContent(
    string Path,
    long SizeBytes,
    int LinesReturned,
    bool Truncated,
    string Content);

// -----------------------------------------------------------------------
// Service
// -----------------------------------------------------------------------

/// <summary>
/// Provides read-only file-system operations scoped to the directories
/// listed in <see cref="RemoteConfig.AllowedDirectories"/>.
///
/// Security rules enforced here:
/// - All paths are fully resolved before any operation so directory-traversal
///   sequences (../) cannot escape the allowed roots.
/// - Write and delete operations are not supported.
/// - File reads are capped at 10,000 lines or 1 MB, whichever comes first.
/// - Binary files are rejected; only UTF-8 / ASCII text is served.
/// </summary>
public sealed class FileManager
{
    private readonly RemoteConfig _config;
    private readonly ILogger<FileManager> _logger;

    // Pre-expanded allowed directory roots (resolved at startup).
    private readonly IReadOnlyList<string> _allowedRoots;

    // Hard cap on content returned per read request.
    private const int MaxLines = 10_000;
    private const long MaxBytes = 1 * 1024 * 1024; // 1 MB

    /// <summary>Initializes the <see cref="FileManager"/>.</summary>
    public FileManager(RemoteConfig config, ILogger<FileManager> logger)
    {
        _config = config;
        _logger = logger;

        _allowedRoots = config.AllowedDirectories
            .Select(d => Path.GetFullPath(Environment.ExpandEnvironmentVariables(d)))
            .ToList();
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a listing of files and subdirectories at <paramref name="path"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when file operations are disabled or the path is outside the
    /// allowed directories.
    /// </exception>
    public Task<DirectoryListing> ListDirectoryAsync(string path)
    {
        EnsureEnabled();
        string resolved = ResolveSafePath(path);

        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException($"Directory not found: {resolved}");

        var entries = new List<DirectoryEntry>();

        foreach (string dir in Directory.EnumerateDirectories(resolved))
        {
            var info = new DirectoryInfo(dir);
            entries.Add(new DirectoryEntry(
                Name: info.Name,
                FullPath: info.FullName,
                IsDirectory: true,
                SizeBytes: 0,
                LastModified: info.LastWriteTimeUtc));
        }

        foreach (string file in Directory.EnumerateFiles(resolved))
        {
            var info = new FileInfo(file);
            entries.Add(new DirectoryEntry(
                Name: info.Name,
                FullPath: info.FullName,
                IsDirectory: false,
                SizeBytes: info.Length,
                LastModified: info.LastWriteTimeUtc));
        }

        var listing = new DirectoryListing(
            Path: resolved,
            Entries: entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name).ToList(),
            TotalCount: entries.Count);

        return Task.FromResult(listing);
    }

    /// <summary>
    /// Reads up to <paramref name="maxLines"/> lines from a text file.
    /// </summary>
    /// <param name="path">Absolute or relative path to the file.</param>
    /// <param name="maxLines">
    /// Maximum lines to return. Clamped to <see cref="MaxLines"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown for binary files, oversized files, or when operations are
    /// disabled.
    /// </exception>
    public async Task<FileContent> ReadFileAsync(string path, int maxLines = 100)
    {
        EnsureEnabled();
        string resolved = ResolveSafePath(path);

        if (!File.Exists(resolved))
            throw new FileNotFoundException($"File not found: {resolved}");

        var info = new FileInfo(resolved);

        if (IsBinaryFile(resolved))
            throw new InvalidOperationException($"Binary files cannot be read: {info.Name}");

        int clampedLines = Math.Min(Math.Max(1, maxLines), MaxLines);

        var lines = new List<string>(clampedLines);
        bool truncated = false;
        long bytesRead = 0;

        using var reader = new StreamReader(resolved, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (lines.Count < clampedLines && !reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null) break;

            bytesRead += System.Text.Encoding.UTF8.GetByteCount(line) + 1;
            if (bytesRead > MaxBytes)
            {
                truncated = true;
                break;
            }

            lines.Add(line);
        }

        if (!reader.EndOfStream && !truncated)
            truncated = true;

        string content = string.Join(Environment.NewLine, lines);

        return new FileContent(
            Path: resolved,
            SizeBytes: info.Length,
            LinesReturned: lines.Count,
            Truncated: truncated,
            Content: content);
    }

    /// <summary>
    /// Returns true if <paramref name="path"/> exists within an allowed
    /// directory and is a file.
    /// </summary>
    public Task<bool> FileExistsAsync(string path)
    {
        EnsureEnabled();

        try
        {
            string resolved = ResolveSafePath(path);
            return Task.FromResult(File.Exists(resolved));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Returns the size in bytes of the file at <paramref name="path"/>.
    /// Returns -1 if the file does not exist.
    /// </summary>
    public Task<long> GetFileSizeAsync(string path)
    {
        EnsureEnabled();
        string resolved = ResolveSafePath(path);
        var info = new FileInfo(resolved);
        return Task.FromResult(info.Exists ? info.Length : -1L);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void EnsureEnabled()
    {
        if (!_config.EnableFileOperations)
            throw new InvalidOperationException("File operations are disabled by configuration.");
    }

    /// <summary>
    /// Resolves and validates a path, throwing when it falls outside all
    /// allowed roots.
    /// </summary>
    private string ResolveSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.");

        string expanded = Environment.ExpandEnvironmentVariables(path);
        string resolved = Path.GetFullPath(expanded);

        foreach (string root in _allowedRoots)
        {
            if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Path '{Path}' permitted under root '{Root}'.", resolved, root);
                return resolved;
            }
        }

        _logger.LogWarning("Path access denied - not under any allowed root: {Path}", resolved);
        throw new UnauthorizedAccessException(
            $"Access denied: '{resolved}' is not under any configured allowed directory.");
    }

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".json", ".xml", ".yaml", ".yml", ".csv", ".ini",
        ".config", ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".sh", ".toml",
        ".html", ".htm", ".css", ".js", ".ts", ".cs", ".py", ".sql"
    };

    private static bool IsBinaryFile(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && TextExtensions.Contains(ext))
            return false;

        // Sniff first 8 KB for null bytes (reliable binary indicator).
        try
        {
            Span<byte> buffer = stackalloc byte[8192];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read = fs.Read(buffer);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return true;
            }
        }
        catch
        {
            // If we cannot read for sniffing treat as binary for safety.
            return true;
        }

        return false;
    }
}
