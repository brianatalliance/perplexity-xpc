namespace PerplexityXPC.Tray.Models;

/// <summary>
/// Immutable record representing a single notification entry in the notification center.
/// </summary>
/// <param name="Id">Unique identifier (GUID string).</param>
/// <param name="Title">Short display title shown in bold.</param>
/// <param name="Body">Full body text of the notification.</param>
/// <param name="Timestamp">When the notification was created.</param>
/// <param name="Source">Which subsystem produced this notification.</param>
/// <param name="IsRead">Whether the user has acknowledged/dismissed this notification.</param>
public sealed record Notification(
    string Id,
    string Title,
    string Body,
    DateTimeOffset Timestamp,
    NotificationSource Source,
    bool IsRead);

/// <summary>
/// Identifies the subsystem that produced a <see cref="Notification"/>.
/// </summary>
public enum NotificationSource
{
    /// <summary>Produced by a manually-triggered quick action from the tray menu.</summary>
    QuickAction,

    /// <summary>Produced by a background scheduled task or polling loop.</summary>
    Scheduled,

    /// <summary>Produced by an automated alert or threshold breach.</summary>
    Alert,

    /// <summary>Produced by the PerplexityXPC service itself (startup, shutdown, errors).</summary>
    System,
}
