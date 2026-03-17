using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.TelegramNotifier.Services;

/// <summary>
/// Optional TVDB v4 API client used to get authoritative episode counts per season.
/// Falls back gracefully when disabled or on error.
/// </summary>
public class TvDbService
{
    private readonly ILogger<TvDbService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private const string BaseUrl = "https://api4.thetvdb.com/v4";

    public TvDbService(ILogger<TvDbService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Gets the total number of episodes for a given TVDB series + season.
    /// Returns null if TVDB is disabled, credentials are missing, or on error.
    /// </summary>
    public async Task<int?> GetSeasonEpisodeCountAsync(string tvdbSeriesId, int seasonNumber)
    {
        var cfg = Plugin.Instance!.Configuration;
        if (!cfg.EnableTvDb || string.IsNullOrWhiteSpace(cfg.TvDbApiKey))
            return null;

        try
        {
            var token = await GetTokenAsync(cfg.TvDbApiKey, cfg.TvDbPin).ConfigureAwait(false);
            if (token is null) return null;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var url = $"{BaseUrl}/series/{tvdbSeriesId}/episodes/official?season={seasonNumber}&page=0";
            var response = await client.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TVDB episode fetch failed: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var obj = JObject.Parse(json);
            var episodes = obj["data"]?["episodes"] as JArray;
            return episodes?.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TVDB request failed");
            return null;
        }
    }

    private async Task<string?> GetTokenAsync(string apiKey, string pin)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = JsonConvert.SerializeObject(new { apikey = apiKey, pin });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{BaseUrl}/login", content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TVDB login failed: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var obj = JObject.Parse(json);
            _cachedToken = obj["data"]?["token"]?.ToString();
            _tokenExpiry = DateTime.UtcNow.AddHours(1);
            return _cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TVDB login exception");
            return null;
        }
    }
}
