namespace Jellyfin.Plugin.TelegramNotifier.Models;

/// <summary>
/// Represents a notification that has been sent (or is pending retrigger).
/// </summary>
public class NotificationRecord
{
    /// <summary>Jellyfin item ID (stable across restarts).</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Human-readable display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>"Movie" | "Season" | "Episode"</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>When the notification was first dispatched.</summary>
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>The final rendered caption text that was sent to Telegram.</summary>
    public string RenderedText { get; set; } = string.Empty;

    /// <summary>Telegram image URL that was used for the sendPhoto call.</summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Whether the notification was successfully delivered.</summary>
    public bool WasSent { get; set; } = false;

    /// <summary>Optional error message from the last send attempt.</summary>
    public string? LastError { get; set; }

    public string? TopicId { get; set; }

    /// <summary>Whether this was a manual build/send or an automatic system notification.</summary>
    public bool IsManual { get; set; } = false;
}
