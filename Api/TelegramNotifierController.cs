using Jellyfin.Plugin.TelegramNotifier.Models;
using Jellyfin.Plugin.TelegramNotifier.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TelegramNotifier.Api;

/// <summary>
/// REST API controller for listing and retriggering Telegram notifications.
/// All endpoints require administrator authentication.
/// </summary>
[ApiController]
[Route("Plugins/TelegramNotifier")]
[Authorize(Policy = "RequiresElevation")]
public class TelegramNotifierController : ControllerBase
{
    private readonly ILogger<TelegramNotifierController> _logger;
    private readonly NotificationStore _store;
    private readonly NotificationManager _manager;
    private readonly ILibraryManager _libraryManager;

    public TelegramNotifierController(
        ILogger<TelegramNotifierController> logger,
        NotificationStore store,
        NotificationManager manager,
        ILibraryManager libraryManager)
    {
        _logger = logger;
        _store = store;
        _manager = manager;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// GET /TelegramNotifier/events
    /// Returns the most recent notification records.
    /// </summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(IEnumerable<NotificationRecord>), 200)]
    public async Task<IActionResult> GetEvents([FromQuery] int count = 50)
    {
        var records = await _store.GetRecentAsync(Math.Clamp(count, 1, 200)).ConfigureAwait(false);
        return Ok(records);
    }

    /// <summary>
    /// POST /TelegramNotifier/retrigger/{itemId}
    /// Re-sends the notification for the given item ID.  Optionally accepts a
    /// body with a custom text override.
    /// </summary>
    [HttpPost("retrigger/{itemId}")]
    [ProducesResponseType(typeof(RetriggerResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Retrigger(string itemId, [FromBody] RetriggerRequest? request)
    {
        var record = await _store.GetByIdAsync(itemId).ConfigureAwait(false);
        if (record is null)
            return NotFound(new { message = $"No record found for item {itemId}" });

        // Allow the user to override text and image
        if (!string.IsNullOrWhiteSpace(request?.CustomText))
            record.RenderedText = request.CustomText;
        if (!string.IsNullOrWhiteSpace(request?.PhotoUrl))
            record.ImageUrl = request.PhotoUrl;

        _logger.LogInformation("Retriggering notification for {ItemId}", itemId);
        var success = await _manager.RetriggerAsync(record).ConfigureAwait(false);

        return Ok(new RetriggerResult { Success = success, Record = record });
    }

    /// <summary>
    /// DELETE /TelegramNotifier/events/{itemId}
    /// Removes the record so the item can be re-notified on next library scan.
    /// </summary>
    [HttpDelete("events/{itemId}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteEvent(string itemId)
    {
        await _store.DeleteAsync(itemId).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// GET /TelegramNotifier/images/{itemId}
    /// Returns available image options for the given item ID based on its type.
    /// </summary>
    [HttpGet("images/{itemId}")]
    [ProducesResponseType(typeof(IEnumerable<ImageOption>), 200)]
    public IActionResult GetImageOptions(string itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
            return NotFound(new { message = $"Item {itemId} not found in library" });

        var options = new List<ImageOption>();

        // Add the item itself
        options.Add(new ImageOption { Name = $"[Current] {item.Name ?? "Unknown"}", ImageUrl = GetItemImageUrl(item) ?? string.Empty });

        if (item is Movie)
        {
            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true
            });
            foreach (var m in movies.Cast<Movie>())
            {
                if (m.Id == item.Id) continue;
                options.Add(new ImageOption { Name = m.Name ?? "Unknown", ImageUrl = GetItemImageUrl(m) ?? string.Empty });
            }
        }
        else if (item is Season season)
        {
            var seasons = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = season.SeriesId,
                IncludeItemTypes = new[] { BaseItemKind.Season }
            });
            foreach (var s in seasons.Cast<Season>())
            {
                if (s.Id == item.Id) continue;
                options.Add(new ImageOption { Name = s.Name ?? "Unknown", ImageUrl = GetItemImageUrl(s) ?? string.Empty });
            }
        }
        else if (item is Episode episode)
        {
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = episode.SeasonId,
                IncludeItemTypes = new[] { BaseItemKind.Episode }
            });
            foreach (var e in episodes.Cast<Episode>())
            {
                if (e.Id == item.Id) continue;
                options.Add(new ImageOption { Name = $"E{e.IndexNumber:D2} - {e.Name ?? "Unknown"}", ImageUrl = GetItemImageUrl(e) ?? string.Empty });
            }
        }

        return Ok(options.Where(o => !string.IsNullOrEmpty(o.ImageUrl)));
    }

    private string? GetItemImageUrl(BaseItem item)
    {
        var cfg = Plugin.Instance!.Configuration;
        if (string.IsNullOrEmpty(cfg.ServerUrl)) return null;
        return $"{cfg.ServerUrl.TrimEnd('/')}/Items/{item.Id:N}/Images/Primary";
    }

    /// <summary>
    /// POST /TelegramNotifier/events/bulk-delete
    /// Removes multiple records at once.
    /// </summary>
    [HttpPost("events/bulk-delete")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> BulkDelete([FromBody] List<string> itemIds)
    {
        if (itemIds == null || itemIds.Count == 0)
            return BadRequest("No item IDs provided");

        await _store.DeleteBulkAsync(itemIds).ConfigureAwait(false);
        return NoContent();
    }
    [HttpGet("search")]
    public IActionResult Search([FromQuery] string searchTerm, [FromQuery] string type, [FromQuery] int? seasonNumber = null)
    {
        var includeTypes = type switch
        {
            "Movie" => new[] { BaseItemKind.Movie },
            "Season" => new[] { BaseItemKind.Series }, // Search for series first
            "Episode" => new[] { BaseItemKind.Series }, // Search for series first
            _ => new[] { BaseItemKind.Movie, BaseItemKind.Series }
        };

        var query = new InternalItemsQuery
        {
            SearchTerm = searchTerm,
            IncludeItemTypes = includeTypes,
            Recursive = true,
            Limit = 20
        };

        var items = _libraryManager.GetItemList(query);
        var finalResults = new List<object>();

        foreach (var item in items)
        {
            if (type == "Movie" && item is Movie movie)
            {
                finalResults.Add(new
                {
                    Id = movie.Id.ToString(),
                    Name = movie.Name,
                    Type = "Movie"
                });
            }
            else if (type == "Season" && item is Series series)
            {
                var seasons = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = series.Id,
                    IncludeItemTypes = new[] { BaseItemKind.Season }
                });

                foreach (var s in seasons.Cast<Season>())
                {
                    finalResults.Add(new
                    {
                        Id = s.Id.ToString(),
                        Name = $"{series.Name} - {s.Name}",
                        Type = "Season"
                    });
                }
            }
            else if (type == "Episode" && item is Series epSeries)
            {
                var seasons = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = epSeries.Id,
                    IncludeItemTypes = new[] { BaseItemKind.Season }
                });

                foreach (var s in seasons.Cast<Season>())
                {
                    if (seasonNumber.HasValue && s.IndexNumber != seasonNumber.Value)
                        continue;

                    var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ParentId = s.Id,
                        IncludeItemTypes = new[] { BaseItemKind.Episode }
                    });

                    foreach (var e in episodes.Cast<Episode>())
                    {
                        finalResults.Add(new
                        {
                            Id = e.Id.ToString(),
                            Name = $"{epSeries.Name} S{e.ParentIndexNumber:D2}E{e.IndexNumber:D2} - {e.Name}",
                            Type = "Episode"
                        });
                    }
                }
            }
        }

        return Ok(finalResults.Take(50));
    }

    [HttpGet("preview/{itemId}")]
    public IActionResult GetPreview(string itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null) return NotFound();

        var cfg = Plugin.Instance!.Configuration;
        string template = item switch
        {
            Movie => cfg.MovieTemplate,
            Season => cfg.SeasonTemplate,
            Episode => cfg.EpisodeTemplate,
            _ => string.Empty
        };

        var caption = TemplateEngine.Render(template, item);
        var imageUrl = GetItemImageUrl(item);

        return Ok(new
        {
            Caption = caption,
            ImageUrl = imageUrl,
            Title = item is Episode ep ? $"{ep.SeriesName} S{ep.ParentIndexNumber:D2}E{ep.IndexNumber:D2}" : item.Name
        });
    }

    [HttpPost("manual-send")]
    public async Task<IActionResult> ManualSend([FromBody] ManualSendRequest request)
    {
        if (string.IsNullOrEmpty(request.ItemId)) return BadRequest("Item ID is required");

        var record = new NotificationRecord
        {
            ItemId = request.ItemId,
            Title = request.Title ?? "Manual Notification",
            Type = request.Type ?? "Custom",
            RenderedText = request.Caption ?? string.Empty,
            ImageUrl = request.PhotoUrl ?? string.Empty,
            IsManual = true,
            SentAt = DateTime.UtcNow
        };

        var success = await _manager.RetriggerAsync(record).ConfigureAwait(false);
        return Ok(new { Success = success });
    }
}

public class ManualSendRequest
{
    public string? ItemId { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? Caption { get; set; }
    public string? PhotoUrl { get; set; }
}

/// <summary>Optional body for the retrigger endpoint.</summary>
public class RetriggerRequest
{
    public string? CustomText { get; set; }
    public string? PhotoUrl { get; set; }
}

/// <summary>Response from the retrigger endpoint.</summary>
public class RetriggerResult
{
    public bool Success { get; set; }
    public NotificationRecord? Record { get; set; }
}

public class ImageOption
{
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}
