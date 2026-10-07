using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Chrono.Definitions;

public enum EntryType
{
    Movie,
    Series,
    Season
}

public enum OrderSort
{
    Source,
    Released,
    Title
}

public sealed class UniverseDefinition
{
    public int SchemaVersion { get; set; } = 1;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Revision { get; set; }

    public DiscoverDefinition? Discover { get; set; }

    public List<AttributionDefinition> Attribution { get; set; } = [];

    public Dictionary<string, string> Flags { get; set; } = [];

    public List<GroupDefinition> Groups { get; set; } = [];

    public List<EntryDefinition> Entries { get; set; } = [];

    public List<OrderDefinition> Orders { get; set; } = [];

    public HubDefinition? Hub { get; set; }
}

public sealed class DiscoverDefinition
{
    public List<string> Wikidata { get; set; } = [];

    public List<int> TmdbCollections { get; set; } = [];

    public List<string> Exclude { get; set; } = [];
}

public sealed class AttributionDefinition
{
    public string Name { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? License { get; set; }
}

public sealed class GroupDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Parent { get; set; }
}

public sealed class EntryDefinition
{
    public string Id { get; set; } = string.Empty;

    public EntryType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public int Tmdb { get; set; }

    public string? Imdb { get; set; }

    public int? Tvdb { get; set; }

    public int? Season { get; set; }

    public string? Released { get; set; }

    public string? Poster { get; set; }

    public List<string> Groups { get; set; } = [];

    public List<string> Flags { get; set; } = [];

    public string? Note { get; set; }

    [JsonIgnore]
    public bool IsTv => Type != EntryType.Movie;

    [JsonIgnore]
    public DateOnly? ReleaseDate => DateOnly.TryParse(Released, System.Globalization.CultureInfo.InvariantCulture, out var date) ? date : null;
}

public sealed class OrderDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Basis { get; set; }

    public List<string>? Items { get; set; }

    public DeriveDefinition? Derive { get; set; }
}

public sealed class DeriveDefinition
{
    public string? From { get; set; }

    public List<EntryType>? Types { get; set; }

    public List<string>? Groups { get; set; }

    public List<string>? Flags { get; set; }

    public List<string>? ExcludeFlags { get; set; }

    public OrderSort SortBy { get; set; } = OrderSort.Source;
}

public sealed class HubDefinition
{
    public List<string> Rows { get; set; } = [];

    public List<HubPageDefinition> Pages { get; set; } = [];
}

public sealed class HubPageDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<string> Rows { get; set; } = [];
}

public sealed class RegistryIndex
{
    public int SchemaVersion { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public List<RegistryIndexEntry> Universes { get; set; } = [];
}

public sealed class RegistryIndexEntry
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string File { get; set; } = string.Empty;

    public string? Revision { get; set; }
}
