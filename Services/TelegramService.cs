using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace Jellyfin.Plugin.TelegramNotifier.Services;

/// <summary>
/// Sends notifications to Telegram using the Bot API sendPhoto endpoint.
/// </summary>
public class TelegramService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public TelegramService(ILogger<TelegramService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Sends a photo message with caption to Telegram.
    /// If <paramref name="imageUrl"/> is provided it will be sent as a URL-based photo.
    /// Otherwise sends a text-only message via sendMessage.
    /// </summary>
    public async Task<bool> SendPhotoAsync(string botToken, string chatId, string caption, string? imageUrl, string? topicId = null)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            _logger.LogWarning("Telegram bot token or chat ID is not configured");
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                _logger.LogInformation("Attempting to send photo via multipart upload: {Url}", imageUrl);
                
                var imageBytes = await DownloadImageAsync(imageUrl).ConfigureAwait(false);
                if (imageBytes != null)
                {
                    using var multipart = new MultipartFormDataContent();
                    multipart.Add(new StringContent(chatId), "chat_id");
                    multipart.Add(new ByteArrayContent(imageBytes), "photo", "image.jpg");
                    multipart.Add(new StringContent(caption), "caption");
                    multipart.Add(new StringContent("Markdown"), "parse_mode");

                    if (!string.IsNullOrWhiteSpace(topicId))
                    {
                        multipart.Add(new StringContent(topicId), "message_thread_id");
                    }

                    var photoUrl = $"https://api.telegram.org/bot{botToken}/sendPhoto";
                    var response = await client.PostAsync(photoUrl, multipart).ConfigureAwait(false);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Telegram sendPhoto (multipart) succeeded");
                        return true;
                    }

                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _logger.LogWarning("Telegram sendPhoto (multipart) failed {Status}: {Body}", response.StatusCode, body);
                }
                else
                {
                    _logger.LogWarning("Failed to download image from {Url}, falling back to sendMessage", imageUrl);
                }
            }

            // sendMessage fallback (no image or image failed)
            var msgUrl = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var msgParameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("chat_id", chatId),
                new KeyValuePair<string, string>("text", caption),
                new KeyValuePair<string, string>("parse_mode", "Markdown"),
            };

            if (!string.IsNullOrWhiteSpace(topicId))
            {
                msgParameters.Add(new KeyValuePair<string, string>("message_thread_id", topicId));
            }

            var msgContent = new FormUrlEncodedContent(msgParameters);
            var msgResponse = await client.PostAsync(msgUrl, msgContent).ConfigureAwait(false);
            if (msgResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram sendMessage succeeded");
                return true;
            }

            var msgBody = await msgResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logger.LogError("Telegram sendMessage failed {Status}: {Body}", msgResponse.StatusCode, msgBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending Telegram notification");
            return false;
        }
    }

    private async Task<byte[]?> DownloadImageAsync(string url)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Telegram-Notifier/1.0");
            
            var response = await client.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download image {Status}: {Url}", response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception downloading image from {Url}", url);
            return null;
        }
    }
}
