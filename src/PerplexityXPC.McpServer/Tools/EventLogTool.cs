using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PerplexityXPC.McpServer.Protocol;

namespace PerplexityXPC.McpServer.Tools;

/// <summary>
/// Provides read-only access to Windows Event Log entries.
/// Uses the classic <see cref="EventLog"/> API for broad compatibility.
/// </summary>
public sealed class EventLogTool
{
    // -------------------------------------------------------------------------
    //  Tool definitions
    // -------------------------------------------------------------------------

    /// <summary>Returns MCP tool definitions for event log operations.</summary>
    public IEnumerable<McpTool> GetToolDefinitions()
    {
        yield return new McpTool
        {
            Name        = "event_logs.get_events",
            Description = "Read recent Windows Event Log entries with optional filters by log name, level, source, and date.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    log_name   = new { type = "string",  description = "Log name: System, Application, Security, etc. Default System." },
                    level      = new { type = "string",  description = "Filter by entry type: Error, Warning, Information, FailureAudit, SuccessAudit." },
                    source     = new { type = "string",  description = "Optional event source name filter (substring match)." },
                    max_events = new { type = "integer", description = "Maximum entries to return. Default 20, max 200." },
                    after      = new { type = "string",  description = "Return only events after this datetime (ISO 8601 or parseable string)." },
                },
            },
        };

        yield return new McpTool
        {
            Name        = "event_logs.get_event_sources",
            Description = "List the event sources registered in a Windows Event Log.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    log_name = new { type = "string", description = "Log name. Default System." },
                },
            },
        };
    }

    // -------------------------------------------------------------------------
    //  Operations
    // -------------------------------------------------------------------------

    /// <summary>Reads event log entries matching the specified criteria.</summary>
    public ToolCallResult GetEvents(JsonElement args)
    {
        try
        {
            var logName   = GetString(args, "log_name",  "System");
            var levelStr  = GetString(args, "level",     null);
            var source    = GetString(args, "source",    null);
            int maxEvents = 20;
            DateTime? afterDt = null;

            if (args.TryGetProperty("max_events", out var mp) && mp.TryGetInt32(out var mv))
                maxEvents = Math.Clamp(mv, 1, 200);

            if (args.TryGetProperty("after", out var ap) && ap.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(ap.GetString(), out var dt))
                    afterDt = dt;
            }

            EventLogEntryType? levelFilter = null;
            if (!string.IsNullOrWhiteSpace(levelStr))
            {
                levelFilter = levelStr.ToLowerInvariant() switch
                {
                    "error"          => EventLogEntryType.Error,
                    "warning"        => EventLogEntryType.Warning,
                    "information"    => EventLogEntryType.Information,
                    "failureaudit"   => EventLogEntryType.FailureAudit,
                    "successaudit"   => EventLogEntryType.SuccessAudit,
                    _                => null,
                };
            }

            using var log = new EventLog(logName);

            // Iterate from most recent (reverse order)
            var entries = new List<EventLogEntry>();
            for (int i = log.Entries.Count - 1; i >= 0 && entries.Count < maxEvents; i--)
            {
                EventLogEntry entry;
                try { entry = log.Entries[i]; }
                catch { continue; }

                if (levelFilter.HasValue && entry.EntryType != levelFilter.Value)
                    continue;

                if (!string.IsNullOrWhiteSpace(source) &&
                    !entry.Source.Contains(source, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (afterDt.HasValue && entry.TimeGenerated <= afterDt.Value)
                    continue;

                entries.Add(entry);
            }

            if (entries.Count == 0)
                return ToolCallResult.Success($"No events found in '{logName}' matching the specified filters.");

            var sb = new StringBuilder();
            sb.AppendLine($"Event Log: {logName}  ({entries.Count} entries returned)");
            sb.AppendLine();

            foreach (var e in entries)
            {
                sb.AppendLine($"[{e.TimeGenerated:yyyy-MM-dd HH:mm:ss}] [{e.EntryType,-12}] EventId: {e.EventID}  Source: {e.Source}");
                var msg = e.Message;
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    // Cap per-entry message at 500 chars
                    if (msg.Length > 500)
                        msg = msg[..500] + " [...]";
                    sb.AppendLine($"  {msg.Replace("\n", "\n  ")}");
                }
                sb.AppendLine();
            }

            return ToolCallResult.Success(sb.ToString());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("does not exist"))
        {
            return ToolCallResult.Failure($"Event log not found. Use 'event_logs.get_event_sources' to list available logs. Details: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error reading event log: {ex.Message}");
        }
    }

    /// <summary>Lists event sources registered in the specified log.</summary>
    public ToolCallResult GetEventSources(JsonElement args)
    {
        try
        {
            var logName = GetString(args, "log_name", "System");

            // Known standard logs (EventLog API doesn't enumerate log names easily)
            var standardLogs = new[] { "Application", "System", "Security", "Setup", "ForwardedEvents" };

            using var log = new EventLog(logName);

            // Collect unique sources from recent entries (up to 1000)
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int limit = Math.Min(log.Entries.Count, 1000);
            for (int i = log.Entries.Count - 1; i >= log.Entries.Count - limit && i >= 0; i--)
            {
                try { sources.Add(log.Entries[i].Source); }
                catch { /* skip */ }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Event sources in '{logName}' log (sampled from last 1000 entries):");
            sb.AppendLine();

            foreach (var src in sources.OrderBy(s => s))
                sb.AppendLine($"  {src}");

            sb.AppendLine();
            sb.AppendLine($"Total unique sources: {sources.Count}");
            sb.AppendLine();
            sb.AppendLine("Standard log names: " + string.Join(", ", standardLogs));

            return ToolCallResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error listing event sources: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private static string? GetString(JsonElement args, string key, string? defaultValue)
    {
        if (args.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
        {
            var v = p.GetString();
            return string.IsNullOrWhiteSpace(v) ? defaultValue : v;
        }
        return defaultValue;
    }
}
