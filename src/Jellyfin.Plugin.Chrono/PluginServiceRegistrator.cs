using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Hub;
using Jellyfin.Plugin.Chrono.Library;
using Jellyfin.Plugin.Chrono.Output;
using Jellyfin.Plugin.Chrono.Seerr;
using Jellyfin.Plugin.Chrono.Services;
using Jellyfin.Plugin.Chrono.Sources;
using Jellyfin.Plugin.Chrono.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Chrono;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("Chrono");
        serviceCollection.AddSingleton<CachedHttp>();
        serviceCollection.AddSingleton<RegistryClient>();
        serviceCollection.AddSingleton<WikidataClient>();
        serviceCollection.AddSingleton<MdbListClient>();
        serviceCollection.AddSingleton<TraktClient>();
        serviceCollection.AddSingleton<SeerrClient>();
        serviceCollection.AddSingleton<LibraryResolver>();
        serviceCollection.AddSingleton<UniverseService>();
        serviceCollection.AddSingleton<HubService>();
        serviceCollection.AddSingleton<PlaybackService>();
        serviceCollection.AddSingleton<RequestService>();
        serviceCollection.AddSingleton<OutputSync>();
        serviceCollection.AddSingleton<RefreshCoordinator>();
        serviceCollection.AddHostedService<LibraryWatcher>();
        serviceCollection.AddSingleton<IStartupFilter, WebInjectionStartupFilter>();
    }
}
