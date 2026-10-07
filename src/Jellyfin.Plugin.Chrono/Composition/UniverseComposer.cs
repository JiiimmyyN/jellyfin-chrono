using System.Globalization;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Library;
using Jellyfin.Plugin.Chrono.Sources;

namespace Jellyfin.Plugin.Chrono.Composition;

public static class UniverseComposer
{
    public const string DiscoveredFlag = "discovered";
    public const string ReleaseOrderId = "release";

    public static UniverseDefinition FromSources(string id, string name, string? description, IReadOnlyList<(SourceResult Result, string Title)> sources)
    {
        var universe = new UniverseDefinition
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? sources.Select(s => s.Result.SuggestedName).FirstOrDefault(n => n is not null) ?? id : name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description
        };

        var entries = new Dictionary<string, EntryDefinition>(StringComparer.Ordinal);
        foreach (var entry in sources.SelectMany(s => s.Result.Entries))
        {
            if (entries.TryGetValue(entry.Id, out var existing))
            {
                existing.Released ??= entry.Released;
                existing.Imdb ??= entry.Imdb;
                existing.Tvdb ??= entry.Tvdb;
                existing.Poster ??= entry.Poster;
                existing.Groups = existing.Groups.Union(entry.Groups).ToList();
            }
            else
            {
                entries[entry.Id] = Clone(entry);
            }
        }

        var seasonsByShow = entries.Values
            .Where(e => e.Type == EntryType.Season)
            .GroupBy(e => e.Tmdb)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Season).Select(e => e.Id).ToList());
        foreach (var show in seasonsByShow.Keys)
        {
            entries.Remove(SourceResult.SeriesEntryId(show));
        }

        universe.Entries = entries.Values.ToList();
        universe.Groups = sources.SelectMany(s => s.Result.Groups).DistinctBy(g => g.Id, StringComparer.Ordinal).ToList();

        var rows = new List<string>();
        var orderIds = new HashSet<string>(StringComparer.Ordinal) { ReleaseOrderId };
        var listNumber = 0;
        foreach (var (result, title) in sources.Where(s => s.Result.RankedEntryIds is { Count: > 0 }))
        {
            listNumber++;
            var orderTitle = string.IsNullOrWhiteSpace(title) ? (listNumber == 1 ? "Timeline" : $"List {listNumber}") : title;
            var orderId = UniqueId(Slug.From(orderTitle), orderIds, "list");
            var items = new List<string>();
            foreach (var entryId in result.RankedEntryIds!)
            {
                if (entries.ContainsKey(entryId))
                {
                    items.Add(entryId);
                }
                else if (entryId.StartsWith("tv-", StringComparison.Ordinal)
                    && int.TryParse(entryId[3..], CultureInfo.InvariantCulture, out var show)
                    && seasonsByShow.TryGetValue(show, out var seasonIds))
                {
                    items.AddRange(seasonIds.Where(s => !items.Contains(s) && !result.RankedEntryIds.Contains(s)));
                }
            }

            universe.Orders.Add(new OrderDefinition { Id = orderId, Title = orderTitle, Items = items.Distinct(StringComparer.Ordinal).ToList() });
            rows.Add(orderId);
        }

        universe.Orders.Add(new OrderDefinition
        {
            Id = ReleaseOrderId,
            Title = "Release Order",
            Description = "Everything in the order it was released.",
            Derive = new DeriveDefinition { SortBy = OrderSort.Released }
        });
        rows.Add(ReleaseOrderId);

        foreach (var group in universe.Groups)
        {
            var members = universe.Entries.Count(e => e.Groups.Contains(group.Id));
            if (members == universe.Entries.Count || string.Equals(group.Title, universe.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var orderId = UniqueId(group.Id, orderIds, "group");
            universe.Orders.Add(new OrderDefinition
            {
                Id = orderId,
                Title = group.Title,
                Derive = new DeriveDefinition { Groups = [group.Id], SortBy = OrderSort.Released }
            });
            rows.Add(orderId);
        }

        universe.Hub = new HubDefinition { Rows = rows };
        return universe;
    }

    public static UniverseDefinition AddDiscovered(UniverseDefinition curated, SourceResult discovered)
    {
        var movies = curated.Entries.Where(e => e.Type == EntryType.Movie).Select(e => e.Tmdb).ToHashSet();
        var shows = curated.Entries.Where(e => e.IsTv).Select(e => e.Tmdb).ToHashSet();
        var imdb = curated.Entries.Select(e => e.Imdb).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var ids = curated.Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var excluded = (curated.Discover?.Exclude ?? []).ToHashSet(StringComparer.Ordinal);

        var additions = discovered.Entries
            .Where(e => e.Type == EntryType.Movie ? !movies.Contains(e.Tmdb) : !shows.Contains(e.Tmdb))
            .Where(e => e.Imdb is null || !imdb.Contains(e.Imdb))
            .Where(e => !ids.Contains(e.Id))
            .Where(e => !excluded.Contains(ExclusionKey(e)))
            .Select(e =>
            {
                var clone = Clone(e);
                clone.Groups = [];
                clone.Flags = [DiscoveredFlag];
                return clone;
            })
            .ToList();
        if (additions.Count == 0)
        {
            return curated;
        }

        var copy = Copy(curated);
        copy.Entries.AddRange(additions);
        copy.Flags.TryAdd(DiscoveredFlag, "Not yet placed in the timeline");
        return copy;
    }

    public static string ExclusionKey(EntryDefinition entry)
        => (entry.Type == EntryType.Movie ? "movie:" : "tv:") + entry.Tmdb.ToString(CultureInfo.InvariantCulture);

    public static UniverseDefinition DropUnowned(UniverseDefinition universe, Func<EntryDefinition, bool> isOwned, Func<EntryDefinition, bool> shouldDrop)
    {
        var drop = universe.Entries.Where(e => shouldDrop(e) && !isOwned(e)).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        if (drop.Count == 0)
        {
            return universe;
        }

        var copy = Copy(universe);
        copy.Entries = copy.Entries.Where(e => !drop.Contains(e.Id)).ToList();
        foreach (var order in copy.Orders.Where(o => o.Items is not null))
        {
            order.Items = order.Items!.Where(i => !drop.Contains(i)).ToList();
        }

        return copy;
    }

    public static UniverseDefinition SplitSeasons(UniverseDefinition universe, Func<EntryDefinition, IReadOnlyList<OwnedSeason>> ownedSeasons)
    {
        var replacements = new Dictionary<string, List<EntryDefinition>>(StringComparer.Ordinal);
        foreach (var series in universe.Entries.Where(e => e.Type == EntryType.Series))
        {
            var seasons = ownedSeasons(series);
            if (seasons.Count < 2)
            {
                continue;
            }

            replacements[series.Id] = seasons.Select(season =>
            {
                var entry = Clone(series);
                entry.Id = $"{series.Id}-s{season.Number.ToString(CultureInfo.InvariantCulture)}";
                entry.Type = EntryType.Season;
                entry.Season = season.Number;
                entry.Poster = null;
                entry.Released = season.Premiere?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? series.Released;
                return entry;
            }).ToList();
        }

        if (replacements.Count == 0)
        {
            return universe;
        }

        var copy = Copy(universe);
        copy.Entries = copy.Entries.SelectMany(e => replacements.TryGetValue(e.Id, out var seasons) ? seasons : [e]).ToList();
        foreach (var order in copy.Orders.Where(o => o.Items is not null))
        {
            order.Items = order.Items!
                .SelectMany(i => replacements.TryGetValue(i, out var seasons) ? seasons.Select(s => s.Id) : [i])
                .ToList();
        }

        return copy;
    }

    public static (IReadOnlyList<string> Rows, IReadOnlyList<HubPageDefinition> Pages) HubLayout(UniverseDefinition universe)
    {
        var orderIds = universe.Orders.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        if (universe.Hub is { Rows.Count: > 0 } hub)
        {
            var pages = hub.Pages
                .Select(p => new HubPageDefinition { Id = p.Id, Title = p.Title, Rows = p.Rows.Where(orderIds.Contains).ToList() })
                .Where(p => p.Rows.Count > 0)
                .ToList();
            return (hub.Rows.Where(orderIds.Contains).ToList(), pages);
        }

        return (universe.Orders.Select(o => o.Id).ToList(), []);
    }

    public static string PrimaryRow(UniverseDefinition universe, IReadOnlyList<string> rows)
    {
        var explicitOrders = universe.Orders.Where(o => o.Items is not null).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        return rows.FirstOrDefault(explicitOrders.Contains) ?? rows.FirstOrDefault() ?? universe.Orders[0].Id;
    }

    private static string UniqueId(string candidate, HashSet<string> used, string fallback)
    {
        var id = string.IsNullOrEmpty(candidate) ? fallback : candidate;
        var unique = id;
        var counter = 2;
        while (!used.Add(unique))
        {
            unique = $"{id}-{counter.ToString(CultureInfo.InvariantCulture)}";
            counter++;
        }

        return unique;
    }

    private static EntryDefinition Clone(EntryDefinition entry) => new()
    {
        Id = entry.Id,
        Type = entry.Type,
        Title = entry.Title,
        Tmdb = entry.Tmdb,
        Imdb = entry.Imdb,
        Tvdb = entry.Tvdb,
        Season = entry.Season,
        Released = entry.Released,
        Poster = entry.Poster,
        Groups = [.. entry.Groups],
        Flags = [.. entry.Flags],
        Note = entry.Note
    };

    private static UniverseDefinition Copy(UniverseDefinition universe) => new()
    {
        SchemaVersion = universe.SchemaVersion,
        Id = universe.Id,
        Name = universe.Name,
        Description = universe.Description,
        Revision = universe.Revision,
        Discover = universe.Discover,
        Attribution = universe.Attribution,
        Flags = new Dictionary<string, string>(universe.Flags, StringComparer.Ordinal),
        Groups = universe.Groups,
        Entries = universe.Entries.Select(Clone).ToList(),
        Orders = universe.Orders.Select(o => new OrderDefinition
        {
            Id = o.Id,
            Title = o.Title,
            Description = o.Description,
            Basis = o.Basis,
            Items = o.Items is null ? null : [.. o.Items],
            Derive = o.Derive
        }).ToList(),
        Hub = universe.Hub
    };
}
