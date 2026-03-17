using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.TelegramNotifier.Models;

/// <summary>
/// Tracks an item that has been added to the library but not yet notified
/// due to aggregation delay or an ongoing library scan.
/// </summary>
public class PendingItem
{
    public string ItemId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Movie, Episode
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
