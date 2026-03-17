using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TelegramNotifier.Services;

/// <summary>
/// Replaces template tokens with values from a Jellyfin BaseItem.
/// </summary>
public static class TemplateEngine
{
    /// <summary>
    /// Supported tokens:
    ///   {Title}         – Item name
    ///   {Year}          – Production year
    ///   {Overview}      – Item overview (truncated to 300 chars)
    ///   {SeriesName}    – TV series name (episodes/seasons)
    ///   {SeasonNumber}  – Season number (2-digit padded)
    ///   {EpisodeNumber} – Episode number (2-digit padded)
    ///   {EpisodeTitle}  – Episode name
    ///   {AddedDate}     – UTC date the item was added
    ///   {Rating}        – Community rating
    ///   {Genres}        – Comma-separated genres
    /// </summary>
    public static string Render(string template, BaseItem item)
    {
        var overview = item.Overview ?? string.Empty;
        if (overview.Length > 300)
            overview = overview[..297] + "…";

        var result = template
            .Replace("{Title}", item.Name ?? string.Empty)
            .Replace("{Year}", item.ProductionYear?.ToString() ?? string.Empty)
            .Replace("{Overview}", overview)
            .Replace("{AddedDate}", item.DateCreated.ToString("yyyy-MM-dd"))
            .Replace("{Rating}", item.CommunityRating?.ToString("F1") ?? string.Empty)
            .Replace("{Genres}", string.Join(", ", item.Genres ?? Array.Empty<string>()));

        if (item is Episode ep)
        {
            result = result
                .Replace("{SeriesName}", ep.SeriesName ?? string.Empty)
                .Replace("{SeasonNumber}", ep.ParentIndexNumber?.ToString("D2") ?? "??")
                .Replace("{EpisodeNumber}", ep.IndexNumber?.ToString("D2") ?? "??")
                .Replace("{EpisodeTitle}", ep.Name ?? string.Empty);
        }
        else if (item is Season season)
        {
            result = result
                .Replace("{SeriesName}", season.SeriesName ?? string.Empty)
                .Replace("{SeasonNumber}", season.IndexNumber?.ToString("D2") ?? "??")
                .Replace("{EpisodeNumber}", string.Empty)
                .Replace("{EpisodeTitle}", string.Empty);
        }
        else
        {
            result = result
                .Replace("{SeriesName}", string.Empty)
                .Replace("{SeasonNumber}", string.Empty)
                .Replace("{EpisodeNumber}", string.Empty)
                .Replace("{EpisodeTitle}", string.Empty);
        }

        return result;
    }
}
