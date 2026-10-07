using Jellyfin.Plugin.Chrono.Definitions;

namespace Jellyfin.Plugin.Chrono.Sources;

public sealed class SourceResult
{
    public List<EntryDefinition> Entries { get; } = [];

    public List<GroupDefinition> Groups { get; } = [];

    public List<string>? RankedEntryIds { get; set; }

    public string? SuggestedName { get; set; }

    public static string MovieEntryId(int tmdb) => $"movie-{tmdb}";

    public static string SeriesEntryId(int tmdb) => $"tv-{tmdb}";

    public static string SeasonEntryId(int tmdb, int season) => $"tv-{tmdb}-s{season}";
}
