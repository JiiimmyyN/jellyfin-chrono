using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Chrono.Configuration;

public enum SourceType
{
    Wikidata,
    TmdbCollection,
    MdbList,
    Trakt
}

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public const string DefaultRegistryUrl = "https://raw.githubusercontent.com/JiiimmyyN/jellyfin-chrono/main/registry/index.json";

    public string[] RegistryUrls { get; set; } = [DefaultRegistryUrl];

    public int RegistryRefreshHours { get; set; } = 24;

    public int DiscoveryRefreshHours { get; set; } = 168;

    public UniverseSettings[] Universes { get; set; } =
    [
        new UniverseSettings { Id = "mcu" },
        new UniverseSettings { Id = "star-wars" }
    ];

    public CustomUniverse[] CustomUniverses { get; set; } = [];

    public bool InjectWebClient { get; set; } = true;

    public bool AddMenuLink { get; set; } = true;

    public string PlaylistOwnerUserId { get; set; } = string.Empty;

    public bool SeerrEnabled { get; set; }

    public string SeerrUrl { get; set; } = string.Empty;

    public string SeerrApiKey { get; set; } = string.Empty;

    public bool SeerrAllowRequests { get; set; } = true;

    public string TraktClientId { get; set; } = string.Empty;

    public string MdbListApiKey { get; set; } = string.Empty;
}

public sealed class UniverseSettings
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string[] HiddenFlags { get; set; } = [];

    public bool ShowMissing { get; set; } = true;

    public bool IncludeDiscovered { get; set; } = true;

    public bool CreatePlaylists { get; set; }

    public string[] PlaylistRows { get; set; } = [];

    public bool CreateCollections { get; set; }
}

public sealed class CustomUniverse
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SourceSpec[] Sources { get; set; } = [];
}

public sealed class SourceSpec
{
    public SourceType Type { get; set; }

    public string Value { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
