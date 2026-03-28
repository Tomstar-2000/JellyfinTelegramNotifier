using Jellyfin.Plugin.TelegramNotifier.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Jellyfin.Data.Enums;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Jellyfin.Plugin.TelegramNotifier.Services;

/// <summary>
/// Core business logic: decides whether to send Movie, Episode, or Season
/// notifications and enforces deduplication.
/// </summary>
public class NotificationManager : IDisposable
{
    private readonly ILogger<NotificationManager> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly NotificationStore _store;
    private readonly TelegramService _telegram;
    private readonly TvDbService _tvDb;

    private Timer? _aggregationTimer;
    private readonly object _timerLock = new();
    private bool _isFlushing = false;

    public NotificationManager(
        ILogger<NotificationManager> logger,
        ILibraryManager libraryManager,
        NotificationStore store,
        TelegramService telegram,
        TvDbService tvDb)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _store = store;
        _telegram = telegram;
        _tvDb = tvDb;
    }

    // ── Public entry point ───────────────────────────────────────────────────

    public async Task ProcessItemAsync(BaseItem item)
    {
        string type = item switch
        {
            Movie => "Movie",
            Episode => "Episode",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(type)) return;

        _logger.LogInformation("NotificationManager: buffering {Type} {Title}", type, item.Name);
        await _store.AddPendingItemAsync(item.Id.ToString(), type).ConfigureAwait(false);

        ResetTimer();
    }

    private void ResetTimer()
    {
        var cfg = Plugin.Instance!.Configuration;
        var delay = TimeSpan.FromMinutes(cfg.AggregationDelayMinutes);

        lock (_timerLock)
        {
            _aggregationTimer?.Dispose();
            _aggregationTimer = new Timer(OnTimerFired, null, delay, Timeout.InfiniteTimeSpan);
        }
        _logger.LogDebug("NotificationManager: timer reset to {Delay} minutes", cfg.AggregationDelayMinutes);
    }

    private void OnTimerFired(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await FlushAggregationAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during FlushAggregation");
            }
        });
    }

    public async Task FlushAggregationAsync()
    {
        lock (_timerLock)
        {
            if (_isFlushing) return;
            _isFlushing = true;
        }

        try
        {
            var pending = await _store.GetPendingItemsAsync().ConfigureAwait(false);
            if (pending.Count == 0) return;

            _logger.LogInformation("NotificationManager: flushing {Count} items", pending.Count);
            
            // Group by type
            var movies = pending.Where(p => p.Type == "Movie").ToList();
            var episodes = pending.Where(p => p.Type == "Episode").ToList();

            // Process Movies
            foreach (var p in movies)
            {
                var item = _libraryManager.GetItemById(p.ItemId);
                if (item is Movie movie) await ProcessMovieAsync(movie).ConfigureAwait(false);
            }

            // Group episodes by Season
            var epGroups = episodes.GroupBy(e =>
            {
                var item = _libraryManager.GetItemById(Guid.Parse(e.ItemId));
                if (item is Episode ep) return (SeriesId: ep.SeriesId, SeasonNumber: ep.ParentIndexNumber ?? 0);
                return (SeriesId: Guid.Empty, SeasonNumber: 0);
            }).Where(g => g.Key.SeriesId != Guid.Empty);

            foreach (var group in epGroups)
            {
                await ProcessEpisodeBatchAsync(group.Key.SeriesId, group.Key.SeasonNumber, group.Select(x => x.ItemId).ToList()).ConfigureAwait(false);
            }

            await _store.ClearPendingItemsAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_timerLock) { _isFlushing = false; }
        }
    }

    public void Dispose()
    {
        _aggregationTimer?.Dispose();
    }

    // ── Movie ─────────────────────────────────────────────────────────────────

    private async Task ProcessMovieAsync(Movie movie)
    {
        var itemId = movie.Id.ToString();
        if (await _store.HasSentAsync(itemId).ConfigureAwait(false))
        {
            _logger.LogDebug("Movie {Title} already notified – skipping", movie.Name);
            return;
        }

        var cfg = Plugin.Instance!.Configuration;
        var caption = TemplateEngine.Render(cfg.MovieTemplate, movie);
        var imageUrl = GetItemImageUrl(movie);

        var record = new NotificationRecord
        {
            ItemId = itemId,
            Title = movie.Name ?? string.Empty,
            Type = "Movie",
            RenderedText = caption,
            ImageUrl = imageUrl ?? string.Empty,
            TopicId = cfg.MovieTopicId,
        };

        var sent = await _telegram.SendPhotoAsync(cfg.TelegramBotToken, cfg.TelegramChatId, caption, imageUrl, cfg.MovieTopicId).ConfigureAwait(false);
        record.WasSent = sent;
        if (!sent)
            record.LastError = "sendPhoto call returned false";

        await _store.AddOrUpdateAsync(record).ConfigureAwait(false);
    }

    // ── Episode / Season ──────────────────────────────────────────────────────

    // ── Episode / Season (Batch Aware) ────────────────────────────────────────

    private async Task ProcessEpisodeBatchAsync(Guid seriesId, int seasonNumber, List<string> batchEpisodeIds)
    {
        // 1. Get or create the group
        var group = await _store.GetPendingGroupAsync(seriesId.ToString(), seasonNumber).ConfigureAwait(false);
        if (group == null)
        {
            var firstEp = _libraryManager.GetItemById(Guid.Parse(batchEpisodeIds.First())) as Episode;
            if (firstEp == null) return;

            group = new PendingEpisodeGroup
            {
                SeriesId = seriesId.ToString(),
                SeriesName = firstEp.SeriesName ?? string.Empty,
                SeasonNumber = seasonNumber,
                SeasonItemId = GetSeasonItemId(firstEp) ?? string.Empty,
                TotalEpisodeCount = -1,
            };
        }

        // 2. Add ALL batch items to the persistent group
        foreach (var id in batchEpisodeIds)
        {
            if (!group.EpisodeItemIds.Contains(id))
                group.EpisodeItemIds.Add(id);
        }
        group.LastUpdatedAt = DateTime.UtcNow;

        // 3. Resolve total count if unknown or not yet authoritative (from TVDB)
        if (group.TotalEpisodeCount < 0 || !group.IsAuthoritative)
        {
            var sampleEp = _libraryManager.GetItemById(Guid.Parse(batchEpisodeIds.First())) as Episode;
            if (sampleEp != null)
            {
                var (total, isAuth) = await ResolveEpisodeCountAsync(sampleEp, seriesId.ToString(), seasonNumber).ConfigureAwait(false);
                group.TotalEpisodeCount = total;
                group.IsAuthoritative = isAuth;
            }
        }

        await _store.UpsertPendingGroupAsync(group).ConfigureAwait(false);

        // 4. Evaluate completeness
        var cfg = Plugin.Instance!.Configuration;
        var buffered = group.EpisodeItemIds.Count;
        var total = group.TotalEpisodeCount;

        _logger.LogInformation("{Series} S{Season}: {Buffered}/{Total} episodes buffered (Threshold {Threshold}%, Authoritative: {Auth})",
            group.SeriesName, seasonNumber, buffered, total < 0 ? "?" : total, cfg.SeasonThresholdPercent, group.IsAuthoritative);

        // A season is complete ONLY if we have an authoritative count AND we reached the threshold.
        bool seasonComplete = group.IsAuthoritative && total > 0 && (buffered * 100 / total) >= cfg.SeasonThresholdPercent;

        if (seasonComplete)
        {
            await SendSeasonNotificationAsync(group).ConfigureAwait(false);
        }
        else
        {
            // Send individual notifications for just the episodes in this batch
            foreach (var epId in batchEpisodeIds)
            {
                if (await _store.HasSentAsync(epId).ConfigureAwait(false)) continue;

                var ep = _libraryManager.GetItemById(Guid.Parse(epId)) as Episode;
                if (ep != null) await SendEpisodeNotificationAsync(ep).ConfigureAwait(false);
            }
        }
    }

    private async Task SendSeasonNotificationAsync(PendingEpisodeGroup group)
    {
        var cfg = Plugin.Instance!.Configuration;

        // Get the Season item from Jellyfin for the template
        BaseItem? seasonItem = string.IsNullOrEmpty(group.SeasonItemId)
            ? null
            : _libraryManager.GetItemById(Guid.Parse(group.SeasonItemId));

        var caption = seasonItem is not null
            ? TemplateEngine.Render(cfg.SeasonTemplate, seasonItem)
            : $"📺 *{EscapeMarkdown(group.SeriesName)}* – Season {group.SeasonNumber:D2} is now complete!";

        var imageUrl = seasonItem is not null ? GetItemImageUrl(seasonItem) : null;

        var record = new NotificationRecord
        {
            ItemId = group.SeasonItemId.Length > 0 ? group.SeasonItemId : $"{group.SeriesId}_S{group.SeasonNumber}",
            Title = $"{group.SeriesName} – Season {group.SeasonNumber}",
            Type = "Season",
            RenderedText = caption,
            ImageUrl = imageUrl ?? string.Empty,
            TopicId = cfg.SeasonTopicId,
        };

        var sent = await _telegram.SendPhotoAsync(cfg.TelegramBotToken, cfg.TelegramChatId, caption, imageUrl, cfg.SeasonTopicId).ConfigureAwait(false);
        record.WasSent = sent;
        if (!sent) record.LastError = "sendPhoto call returned false";

        await _store.AddOrUpdateAsync(record).ConfigureAwait(false);
        await _store.RemovePendingGroupAsync(group.SeriesId, group.SeasonNumber).ConfigureAwait(false);
    }

    private async Task SendEpisodeNotificationAsync(Episode episode)
    {
        var cfg = Plugin.Instance!.Configuration;
        var itemId = episode.Id.ToString();
        var caption = TemplateEngine.Render(cfg.EpisodeTemplate, episode);
        var imageUrl = GetItemImageUrl(episode);

        var record = new NotificationRecord
        {
            ItemId = itemId,
            Title = $"{episode.SeriesName} S{episode.ParentIndexNumber:D2}E{episode.IndexNumber:D2}",
            Type = "Episode",
            RenderedText = caption,
            ImageUrl = imageUrl ?? string.Empty,
            TopicId = cfg.EpisodeTopicId,
        };

        var sent = await _telegram.SendPhotoAsync(cfg.TelegramBotToken, cfg.TelegramChatId, caption, imageUrl, cfg.EpisodeTopicId).ConfigureAwait(false);
        record.WasSent = sent;
        if (!sent) record.LastError = "sendPhoto call returned false";

        await _store.AddOrUpdateAsync(record).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(int Total, bool IsAuthoritative)> ResolveEpisodeCountAsync(Episode episode, string seriesId, int seasonNumber)
    {
        // Try TVDB first
        var tvdbId = episode.GetProviderId(MetadataProvider.Tvdb);
        if (!string.IsNullOrEmpty(tvdbId))
        {
            // TVDB expects the series ID, not episode ID; try to get from the series item
            var series = _libraryManager.GetItemById(episode.SeriesId);
            var seriesTvdbId = series?.GetProviderId(MetadataProvider.Tvdb);
            if (!string.IsNullOrEmpty(seriesTvdbId))
            {
                var count = await _tvDb.GetSeasonEpisodeCountAsync(seriesTvdbId, seasonNumber).ConfigureAwait(false);
                if (count.HasValue && count.Value > 0)
                {
                    _logger.LogInformation("TVDB reports {Count} episodes for S{Season}", count.Value, seasonNumber);
                    return (count.Value, true);
                }
            }
        }

        // Fallback: count episodes in Jellyfin's local database for this season.
        // This is NOT authoritative as it only knows what's on disk right now.
        var localCount = _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = episode.ParentId,  // Season folder
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            Recursive = false,
        }).Count;

        _logger.LogInformation("Jellyfin local count for S{Season}: {Count} (Non-authoritative)", seasonNumber, localCount);
        return (localCount > 0 ? localCount : -1, false);
    }

    private string? GetSeasonItemId(Episode episode)
    {
        return episode.ParentId == Guid.Empty ? null : episode.ParentId.ToString();
    }

    private string? GetItemImageUrl(BaseItem item)
    {
        var cfg = Plugin.Instance!.Configuration;
        if (string.IsNullOrEmpty(cfg.ServerUrl))
        {
            _logger.LogDebug("Server URL not configured - skipping image for {Title}", item.Name);
            return null;
        }

        // Format: https://server/Items/id/Images/Primary
        var url = $"{cfg.ServerUrl.TrimEnd('/')}/Items/{item.Id:N}/Images/Primary";
        _logger.LogDebug("Generated image URL for {Title}: {Url}", item.Name, url);
        return url;
    }

    private static string EscapeMarkdown(string text)
        => text.Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`");

    // ── Public retrigger ─────────────────────────────────────────────────────

    /// <summary>
    /// Re-sends a notification by record. Used by the manual retrigger API.
    /// </summary>
    public async Task<bool> RetriggerAsync(NotificationRecord record)
    {
        var cfg = Plugin.Instance!.Configuration;
        var topicId = !string.IsNullOrEmpty(record.TopicId) ? record.TopicId : record.Type switch
        {
            "Movie" => cfg.MovieTopicId,
            "Season" => cfg.SeasonTopicId,
            "Episode" => cfg.EpisodeTopicId,
            _ => null
        };

        var sent = await _telegram.SendPhotoAsync(
            cfg.TelegramBotToken,
            cfg.TelegramChatId,
            record.RenderedText,
            string.IsNullOrEmpty(record.ImageUrl) ? null : record.ImageUrl,
            topicId)
            .ConfigureAwait(false);

        record.WasSent = sent;
        record.SentAt = DateTime.UtcNow;
        record.LastError = sent ? null : "Failed on retrigger";
        await _store.AddOrUpdateAsync(record).ConfigureAwait(false);
        return sent;
    }
}
