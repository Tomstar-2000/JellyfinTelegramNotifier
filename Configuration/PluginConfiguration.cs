using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TelegramNotifier.Configuration;

/// <summary>
/// Plugin configuration with all settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ── Telegram ────────────────────────────────────────────────────────────
    public string TelegramBotToken { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the Telegram Chat ID.
    /// </summary>
    public string TelegramChatId { get; set; } = string.Empty;
 
    /// <summary>
    /// Gets or sets the public-facing Jellyfin Server URL.
    /// E.g. https://jellyfin.tomstar2000.synology.me
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Telegram Topic ID for Movies.
    /// </summary>
    public string MovieTopicId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Telegram Topic ID for Seasons.
    /// </summary>
    public string SeasonTopicId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Telegram Topic ID for Episodes.
    /// </summary>
    public string EpisodeTopicId { get; set; } = string.Empty;

    // ── Message Templates ────────────────────────────────────────────────────
    // Supported tokens: {Title} {Year} {Overview} {SeriesName} {SeasonNumber}
    //                   {EpisodeNumber} {EpisodeTitle} {AddedDate} {Rating}
    public string MovieTemplate { get; set; } =
        "🎬 *{Title}* ({Year})\n\n{Overview}";

    public string SeasonTemplate { get; set; } =
        "📺 *{SeriesName}* – Season {SeasonNumber} is now complete!\n\n{Overview}";

    public string EpisodeTemplate { get; set; } =
        "📺 *{SeriesName}* S{SeasonNumber}E{EpisodeNumber} – *{EpisodeTitle}*\n\n{Overview}";

    // ── Season Completion Logic ──────────────────────────────────────────────
    /// <summary>
    /// Percentage (0-100) of a season's episodes that must be present before
    /// a season-level notification is sent.  Default = 100 (all episodes).
    /// </summary>
    public int SeasonThresholdPercent { get; set; } = 100;

    // ── TVDB Integration ─────────────────────────────────────────────────────
    public bool EnableTvDb { get; set; } = false;
    public string TvDbApiKey { get; set; } = string.Empty;
    public string TvDbPin { get; set; } = string.Empty;

    // ── Recent Events ────────────────────────────────────────────────────────
    /// <summary>Maximum number of past events kept for the retrigger panel.</summary>
    public int MaxRecentEvents { get; set; } = 50;

    /// <summary>
    /// Gets or sets the aggregation delay in minutes.
    /// Default is 5 minutes. Fallback for non-scan additions.
    /// </summary>
    public int AggregationDelayMinutes { get; set; } = 5;
}
