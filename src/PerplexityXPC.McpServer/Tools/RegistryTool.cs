using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PerplexityXPC.McpServer.Protocol;

namespace PerplexityXPC.McpServer.Tools;

/// <summary>
/// Provides read-only registry access. Write operations are not implemented.
/// Supports HKLM, HKCU, HKCR, HKU, HKCC abbreviations in paths.
/// </summary>
public sealed class RegistryTool
{
    // -------------------------------------------------------------------------
    //  Tool definitions
    // -------------------------------------------------------------------------

    /// <summary>Returns MCP tool definitions for registry read operations.</summary>
    public IEnumerable<McpTool> GetToolDefinitions()
    {
        yield return new McpTool
        {
            Name        = "registry.get_value",
            Description = "Read a single registry value. Example path: HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    path = new { type = "string", description = "Registry key path. Use HKLM, HKCU, HKCR, HKU, or HKCC prefix." },
                    name = new { type = "string", description = "Value name. Use empty string or omit for the default value." },
                },
                required = new[] { "path" },
            },
        };

        yield return new McpTool
        {
            Name        = "registry.list_keys",
            Description = "List subkeys of a registry path.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    path = new { type = "string", description = "Registry key path. Use HKLM, HKCU, HKCR, HKU, or HKCC prefix." },
                },
                required = new[] { "path" },
            },
        };

        yield return new McpTool
        {
            Name        = "registry.search_values",
            Description = "Search value names within a registry key by a pattern substring.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    path    = new { type = "string", description = "Registry key path to search within." },
                    pattern = new { type = "string", description = "Substring to match against value names (case-insensitive)." },
                },
                required = new[] { "path", "pattern" },
            },
        };
    }

    // -------------------------------------------------------------------------
    //  Operations
    // -------------------------------------------------------------------------

    /// <summary>Reads a single registry value.</summary>
    public ToolCallResult GetValue(JsonElement args)
    {
        try
        {
            var path = GetRequiredString(args, "path");
            var name = GetString(args, "name", string.Empty);

            var (hive, subKey) = ParsePath(path);
            using var key = hive.OpenSubKey(subKey, writable: false);

            if (key is null)
                return ToolCallResult.Failure($"Registry key not found: {path}");

            var value = key.GetValue(name);
            if (value is null)
            {
                var availableNames = key.GetValueNames();
                var hint = availableNames.Length > 0
                    ? $" Available values: {string.Join(", ", availableNames.Take(20))}"
                    : " Key exists but has no values.";
                return ToolCallResult.Failure($"Value '{name}' not found in {path}.{hint}");
            }

            var kind   = key.GetValueKind(name);
            var result = FormatValue(value, kind);

            var sb = new StringBuilder();
            sb.AppendLine($"Path:  {path}");
            sb.AppendLine($"Name:  {(string.IsNullOrEmpty(name) ? "(Default)" : name)}");
            sb.AppendLine($"Kind:  {kind}");
            sb.AppendLine($"Value: {result}");

            return ToolCallResult.Success(sb.ToString());
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolCallResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error reading registry value: {ex.Message}");
        }
    }

    /// <summary>Lists subkeys of the specified registry path.</summary>
    public ToolCallResult ListKeys(JsonElement args)
    {
        try
        {
            var path = GetRequiredString(args, "path");
            var (hive, subKey) = ParsePath(path);

            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null)
                return ToolCallResult.Failure($"Registry key not found: {path}");

            var subKeys = key.GetSubKeyNames();
            var values  = key.GetValueNames();

            var sb = new StringBuilder();
            sb.AppendLine($"Registry key: {path}");
            sb.AppendLine();

            if (subKeys.Length > 0)
            {
                sb.AppendLine($"Subkeys ({subKeys.Length}):");
                foreach (var sk in subKeys.OrderBy(s => s))
                    sb.AppendLine($"  {sk}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Subkeys: (none)");
                sb.AppendLine();
            }

            if (values.Length > 0)
            {
                sb.AppendLine($"Values ({values.Length}):");
                sb.AppendLine($"  {"Name",-40} {"Kind",-15} {"Value"}");
                sb.AppendLine("  " + new string('-', 80));
                foreach (var vn in values.OrderBy(v => v))
                {
                    try
                    {
                        var val  = key.GetValue(vn);
                        var kind = key.GetValueKind(vn);
                        var displayName = string.IsNullOrEmpty(vn) ? "(Default)" : vn;
                        var displayVal  = FormatValue(val, kind);
                        if (displayVal.Length > 80) displayVal = displayVal[..80] + "...";
                        sb.AppendLine($"  {displayName,-40} {kind,-15} {displayVal}");
                    }
                    catch { /* skip inaccessible values */ }
                }
            }
            else
            {
                sb.AppendLine("Values: (none)");
            }

            return ToolCallResult.Success(sb.ToString());
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolCallResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error listing registry keys: {ex.Message}");
        }
    }

    /// <summary>Searches for value names matching a pattern within a registry key.</summary>
    public ToolCallResult SearchValues(JsonElement args)
    {
        try
        {
            var path    = GetRequiredString(args, "path");
            var pattern = GetRequiredString(args, "pattern");

            var (hive, subKey) = ParsePath(path);
            using var key = hive.OpenSubKey(subKey, writable: false);

            if (key is null)
                return ToolCallResult.Failure($"Registry key not found: {path}");

            var matches = key.GetValueNames()
                .Where(n => n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return ToolCallResult.Success($"No values matching '{pattern}' found in {path}");

            var sb = new StringBuilder();
            sb.AppendLine($"Values matching '{pattern}' in {path}:");
            sb.AppendLine();
            sb.AppendLine($"{"Name",-40} {"Kind",-15} {"Value"}");
            sb.AppendLine(new string('-', 90));

            foreach (var name in matches)
            {
                try
                {
                    var val  = key.GetValue(name);
                    var kind = key.GetValueKind(name);
                    var displayVal = FormatValue(val, kind);
                    if (displayVal.Length > 60) displayVal = displayVal[..60] + "...";
                    sb.AppendLine($"{name,-40} {kind,-15} {displayVal}");
                }
                catch { /* skip inaccessible */ }
            }

            sb.AppendLine($"\nTotal: {matches.Count} matching value(s)");
            return ToolCallResult.Success(sb.ToString());
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolCallResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error searching registry: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Private helpers
    // -------------------------------------------------------------------------

    private static (RegistryKey Hive, string SubKey) ParsePath(string path)
    {
        // Normalize separators and split on first backslash
        var normalized = path.Replace('/', '\\').TrimStart('\\');
        var sep = normalized.IndexOf('\\');

        string hivePart  = sep < 0 ? normalized : normalized[..sep];
        string subKey    = sep < 0 ? string.Empty : normalized[(sep + 1)..];

        RegistryKey hive = hivePart.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE"   => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER"    => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT"    => Registry.ClassesRoot,
            "HKU"  or "HKEY_USERS"           => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG"  => Registry.CurrentConfig,
            _ => throw new ToolException(
                $"Unknown registry hive '{hivePart}'. Use HKLM, HKCU, HKCR, HKU, or HKCC.",
                JsonRpcError.Codes.InvalidParams),
        };

        return (hive, subKey);
    }

    private static string FormatValue(object? value, RegistryValueKind kind) => value switch
    {
        null          => "(null)",
        byte[] bytes  => BitConverter.ToString(bytes).Replace("-", " "),
        string[] arr  => string.Join("; ", arr),
        _             => value.ToString() ?? "(null)",
    };

    private static string GetRequiredString(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var p) || p.ValueKind == JsonValueKind.Null)
            throw new ToolException($"Required parameter '{key}' is missing.", JsonRpcError.Codes.InvalidParams);
        var v = p.GetString();
        if (string.IsNullOrWhiteSpace(v))
            throw new ToolException($"Parameter '{key}' must not be empty.", JsonRpcError.Codes.InvalidParams);
        return v;
    }

    private static string? GetString(JsonElement args, string key, string? defaultValue)
    {
        if (args.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? defaultValue;
        return defaultValue;
    }
}
