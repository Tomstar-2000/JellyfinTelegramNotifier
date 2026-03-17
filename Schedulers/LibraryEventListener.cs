using Jellyfin.Plugin.TelegramNotifier.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TelegramNotifier.Schedulers;

/// <summary>
/// IHostedService that subscribes to Jellyfin library events and routes
/// newly-added items to the NotificationManager.
/// </summary>
public class LibraryEventListener : IHostedService
{
    private readonly ILogger<LibraryEventListener> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly NotificationManager _notificationManager;

    public LibraryEventListener(
        ILogger<LibraryEventListener> logger,
        ILibraryManager libraryManager,
        NotificationManager notificationManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _notificationManager = notificationManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _logger.LogInformation("TelegramNotifier: library event listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _logger.LogInformation("TelegramNotifier: library event listener stopped");
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        // Fire-and-forget; log exceptions so they don't bubble up and crash Jellyfin
        _ = Task.Run(async () =>
        {
            try
            {
                await _notificationManager.ProcessItemAsync(e.Item).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing new item {ItemId}", e.Item?.Id);
            }
        });
    }
}
