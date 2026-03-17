using Jellyfin.Plugin.TelegramNotifier.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TelegramNotifier;

/// <summary>
/// Registers all plugin services with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrar : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<NotificationStore>();
        serviceCollection.AddSingleton<TelegramService>();
        serviceCollection.AddSingleton<TvDbService>();
        serviceCollection.AddSingleton<NotificationManager>();
        serviceCollection.AddHostedService<Schedulers.LibraryEventListener>();
    }
}
