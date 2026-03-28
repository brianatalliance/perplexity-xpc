using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PerplexityXPC.Tray.Models;

namespace PerplexityXPC.Tray.Services;

// ─── Result type ──────────────────────────────────────────────────────────────

/// <summary>
/// Contains the outcome of a quick action execution.
/// </summary>
/// <param name="ActionName">The action that was run.</param>
/// <param name="Success">Whether the action completed without error.</param>
/// <param name="Text">The response text (may be a truncated preview).</param>
/// <param name="FullText">The complete untruncated response.</param>
/// <param name="ErrorMessage">Error details when <see cref="Success"/> is <c>false</c>.</param>
public sealed record QuickActionResult(
    string ActionName,
    bool   Success,
    string Text,
    string FullText,
    string ErrorMessage = "");

// ─── Response DTO ─────────────────────────────────────────────────────────────

/// <summary>JSON payload returned by POST /perplexity on the broker.</summary>
internal sealed class PerplexityResponse
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("answer")]
    public string Answer { get; init; } = "";

    [JsonPropertyName("result")]
    public string Result { get; init; } = "";
}

// ─── QuickActionRunner ────────────────────────────────────────────────────────

/// <summary>
/// Executes named quick actions by building focused natural-language queries and
/// routing them through the PerplexityXPC broker's POST /perplexity endpoint.
/// Results are logged to the shared <see cref="NotificationStore"/> automatically.
/// </summary>
public sealed class QuickActionRunner : IDisposable
{
    // ── HTTP client ───────────────────────────────────────────────────────────

    private readonly HttpClient _http;
    private readonly NotificationStore _store;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Available action names (public constants for callers) ─────────────────

    /// <summary>Analyzes recent Windows Event Log errors.</summary>
    public const string ActionScanEvents     = "scan-events";

    /// <summary>Performs a security hardening audit query.</summary>
    public const string ActionSecurityAudit  = "security-audit";

    /// <summary>Retrieves service health from the broker status endpoint.</summary>
    public const string ActionServiceHealth  = "service-health";

    /// <summary>Queries the broker about general network diagnostics.</summary>
    public const string ActionNetworkDiag    = "network-diag";

    /// <summary>Reads the clipboard and asks Perplexity to analyze it.</summary>
    public const string ActionClipboard      = "clipboard-analyze";

    /// <summary>Queries the broker about Active Directory replication status.</summary>
    public const string ActionAdReplication  = "ad-replication";

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="QuickActionRunner"/> that communicates with
    /// the broker at <paramref name="brokerBaseUrl"/> and logs results to
    /// <paramref name="store"/>.
    /// </summary>
    /// <param name="store">Notification store to persist results.</param>
    /// <param name="brokerBaseUrl">Base URL of the PerplexityXPC broker.</param>
    public QuickActionRunner(NotificationStore store, string brokerBaseUrl = "http://127.0.0.1:47777")
    {
        _store = store;
        _http  = new HttpClient
        {
            BaseAddress = new Uri(brokerBaseUrl.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.Add("User-Agent", "PerplexityXPC-Tray/1.0");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the named quick action asynchronously and returns its result.
    /// The result is also logged to the <see cref="NotificationStore"/>.
    /// </summary>
    /// <param name="actionName">One of the <c>Action*</c> constants on this class.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="QuickActionResult"/> describing the outcome.</returns>
    public async Task<QuickActionResult> RunQuickActionAsync(
        string actionName, CancellationToken ct = default)
    {
        QuickActionResult result;

        try
        {
            result = actionName switch
            {
                ActionScanEvents    => await RunQueryActionAsync(actionName,
                    "Scan recent Windows Event Log entries on this machine, " +
                    "summarize the top errors and warnings from the last 24 hours, " +
                    "and provide remediation steps for the most critical issues.", ct),

                ActionSecurityAudit => await RunQueryActionAsync(actionName,
                    "What are the top security hardening steps for a Windows 10/11 enterprise endpoint? " +
                    "Include registry changes, audit policies, firewall rules, and patch management best practices.", ct),

                ActionServiceHealth => await RunServiceHealthAsync(ct),

                ActionNetworkDiag   => await RunQueryActionAsync(actionName,
                    "Perform a comprehensive Windows network diagnostic: check DNS resolution, " +
                    "gateway connectivity, IPv4/IPv6 configuration, and common networking issues. " +
                    "Provide specific PowerShell commands to run and what to look for.", ct),

                ActionClipboard     => await RunClipboardActionAsync(ct),

                ActionAdReplication => await RunQueryActionAsync(actionName,
                    "Explain how to check Active Directory replication health using repadmin /replsummary " +
                    "and Get-ADReplicationFailure. What do common replication errors mean and how are they fixed?", ct),

                _ => new QuickActionResult(actionName, false, "", "",
                    $"Unknown action: {actionName}"),
            };
        }
        catch (OperationCanceledException)
        {
            result = new QuickActionResult(actionName, false, "", "", "Action was cancelled.");
        }
        catch (Exception ex)
        {
            result = new QuickActionResult(actionName, false, "", "",
                $"Action failed: {ex.Message}");
        }

        // Always persist to the notification store
        await PersistResultAsync(result, ct).ConfigureAwait(false);

        return result;
    }

    // ── Private action implementations ────────────────────────────────────────

    /// <summary>
    /// Builds a Perplexity query payload, POSTs to /perplexity, and wraps the
    /// response in a <see cref="QuickActionResult"/>.
    /// </summary>
    private async Task<QuickActionResult> RunQueryActionAsync(
        string actionName, string query, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { query });
        using var content  = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("perplexity", content, ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        string raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string text = ExtractText(raw);

        return new QuickActionResult(
            actionName,
            Success:  true,
            Text:     Truncate(text, 200),
            FullText: text);
    }

    /// <summary>
    /// Queries GET /status for live health data and formats a human-readable summary.
    /// </summary>
    private async Task<QuickActionResult> RunServiceHealthAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("status", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string raw  = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string text = $"Service health response: {Truncate(raw, 400)}";

        return new QuickActionResult(
            ActionServiceHealth,
            Success:  true,
            Text:     Truncate(text, 200),
            FullText: text);
    }

    /// <summary>
    /// Reads the clipboard (must be called from the UI thread via Invoke),
    /// then sends the content to Perplexity for analysis.
    /// </summary>
    private async Task<QuickActionResult> RunClipboardActionAsync(CancellationToken ct)
    {
        string clipboardText = "";

        // Clipboard must be accessed on an STA thread
        Exception? clipEx = null;
        var readThread = new Thread(() =>
        {
            try
            {
                if (Clipboard.ContainsText())
                    clipboardText = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                clipEx = ex;
            }
        });
        readThread.SetApartmentState(ApartmentState.STA);
        readThread.Start();
        readThread.Join(TimeSpan.FromSeconds(3));

        if (clipEx is not null)
            return new QuickActionResult(ActionClipboard, false, "", "",
                $"Could not read clipboard: {clipEx.Message}");

        if (string.IsNullOrWhiteSpace(clipboardText))
            return new QuickActionResult(ActionClipboard, false, "",
                "", "Clipboard is empty or does not contain text.");

        string clipped = Truncate(clipboardText, 2000);
        string query   = $"Analyze the following clipboard content and explain what it does, " +
                         $"identify any potential security issues, and suggest improvements:\n\n{clipped}";

        return await RunQueryActionAsync(ActionClipboard, query, ct).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Extracts the response text from a raw JSON body, trying several field names.</summary>
    private static string ExtractText(string raw)
    {
        try
        {
            using var doc  = JsonDocument.Parse(raw);
            var root       = doc.RootElement;

            foreach (string prop in new[] { "text", "answer", "result", "response" })
            {
                if (root.TryGetProperty(prop, out var el))
                {
                    string? v = el.GetString();
                    if (!string.IsNullOrEmpty(v))
                        return v;
                }
            }
        }
        catch
        {
            // Not JSON or unexpected shape - return raw
        }

        return raw;
    }

    /// <summary>Truncates <paramref name="text"/> to at most <paramref name="maxLen"/> characters.</summary>
    private static string Truncate(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..maxLen] + "...";

    /// <summary>Converts an action name to a human-friendly title for the notification.</summary>
    private static string ActionTitle(string actionName) => actionName switch
    {
        ActionScanEvents    => "Event Log Scan",
        ActionSecurityAudit => "Security Audit",
        ActionServiceHealth => "Service Health Check",
        ActionNetworkDiag   => "Network Diagnostics",
        ActionClipboard     => "Clipboard Analysis",
        ActionAdReplication => "AD Replication Check",
        _                   => actionName,
    };

    /// <summary>Creates a <see cref="Notification"/> record from a result and persists it.</summary>
    private async Task PersistResultAsync(QuickActionResult result, CancellationToken ct)
    {
        try
        {
            var notification = new Notification(
                Id:        Guid.NewGuid().ToString(),
                Title:     ActionTitle(result.ActionName),
                Body:      result.Success ? result.FullText : $"Error: {result.ErrorMessage}",
                Timestamp: DateTimeOffset.Now,
                Source:    NotificationSource.QuickAction,
                IsRead:    false);

            await _store.AddAsync(notification, ct).ConfigureAwait(false);
        }
        catch
        {
            // Persist failures must never bubble up to callers
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
