using Jellyfin.Plugin.Chrono.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Chrono;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("4b1c2a8e-7c39-4d2e-9a51-0f6c3e8d2b17");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Chrono";

    public override Guid Id => PluginId;

    public override string Description => "Disney+-style universe hubs (MCU, Star Wars, …) with timeline, phase and release orders.";

    public string CacheDirectory => Path.Combine(DataFolderPath, "cache");

    public string LocalUniversesDirectory => Path.Combine(DataFolderPath, "universes");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = "Chrono",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuIcon = "movie_filter"
            }
        ];
    }
}
