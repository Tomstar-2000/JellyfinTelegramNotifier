namespace Jellyfin.Plugin.TelegramNotifier.Models;

/// <summary>
/// Groups episodes buffered for a specific series season, waiting for
/// season completeness evaluation.
/// </summary>
public class PendingEpisodeGroup
{
    /// <summary>Jellyfin Series item ID.</summary>
    public string SeriesId { get; set; } = string.Empty;

    public string SeriesName { get; set; } = string.Empty;

    public int SeasonNumber { get; set; }

    /// <summary>Jellyfin Season item ID (for image lookup).</summary>
    public string SeasonItemId { get; set; } = string.Empty;

    /// <summary>IDs of episodes collected so far.</summary>
    public List<string> EpisodeItemIds { get; set; } = new();

    /// <summary>
    /// Total episode count from TVDB (or Jellyfin). -1 = unknown.
    /// </summary>
    public int TotalEpisodeCount { get; set; } = -1;

    public DateTime FirstAddedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
