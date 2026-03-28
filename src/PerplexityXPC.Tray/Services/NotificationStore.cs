using System.Text.Json;
using System.Text.Json.Serialization;
using PerplexityXPC.Tray.Models;

namespace PerplexityXPC.Tray.Services;

/// <summary>
/// Manages persistent storage of <see cref="Notification"/> entries.
/// Notifications are serialised as JSON to
/// <c>%LOCALAPPDATA%\PerplexityXPC\notifications.json</c>.
/// The store is capped at 100 entries; older entries are pruned automatically.
/// All public methods are thread-safe via a private <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class NotificationStore
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int MaxNotifications = 100;

    // ── File path ─────────────────────────────────────────────────────────────

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerplexityXPC",
        "notifications.json");

    // ── Serialisation options ─────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
    };

    // ── Concurrency guard ─────────────────────────────────────────────────────

    private readonly SemaphoreSlim _lock = new(1, 1);

    // ── In-memory event ───────────────────────────────────────────────────────

    /// <summary>Fired whenever a new notification is added.</summary>
    public event EventHandler<Notification>? NotificationAdded;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Loads all persisted notifications from disk, newest first.</summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>List of notifications ordered by descending timestamp.</returns>
    public async Task<List<Notification>> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadFileAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Appends <paramref name="notification"/> to the store, pruning the oldest
    /// entries if the total would exceed <see cref="MaxNotifications"/>.
    /// Also raises <see cref="NotificationAdded"/>.
    /// </summary>
    /// <param name="notification">The notification to persist.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = await ReadFileAsync(ct).ConfigureAwait(false);

            list.Insert(0, notification); // newest first

            // Prune if over the cap
            if (list.Count > MaxNotifications)
                list.RemoveRange(MaxNotifications, list.Count - MaxNotifications);

            await WriteFileAsync(list, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        // Raise event outside the lock
        NotificationAdded?.Invoke(this, notification);
    }

    /// <summary>
    /// Marks the notification identified by <paramref name="id"/> as read/dismissed
    /// by setting <see cref="Notification.IsRead"/> to <c>true</c>.
    /// </summary>
    /// <param name="id">The notification ID to dismiss.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task DismissAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list    = await ReadFileAsync(ct).ConfigureAwait(false);
            int idx     = list.FindIndex(n => n.Id == id);
            if (idx >= 0)
            {
                var old  = list[idx];
                list[idx] = old with { IsRead = true };
                await WriteFileAsync(list, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Removes all notifications from the store.</summary>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteFileAsync([], ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Reads and deserialises the notification file. Returns an empty list on any error.</summary>
    private static async Task<List<Notification>> ReadFileAsync(CancellationToken ct)
    {
        if (!File.Exists(StorePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(StorePath);
            return await JsonSerializer.DeserializeAsync<List<Notification>>(stream, JsonOpts, ct)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Serialises <paramref name="list"/> and writes it atomically using a temp file.</summary>
    private static async Task WriteFileAsync(List<Notification> list, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);

        string tempPath = StorePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, list, JsonOpts, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, StorePath, overwrite: true);
    }
}
