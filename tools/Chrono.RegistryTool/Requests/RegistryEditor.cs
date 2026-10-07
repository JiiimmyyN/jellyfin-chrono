using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chrono.RegistryTool.Metadata;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Sources;

namespace Chrono.RegistryTool.Requests;

public sealed class RegistryEditor
{
    public const string AddTitle = "add-title";
    public const string MoveTitle = "move-title";
    public const string ExcludeTitle = "exclude-title";
    public const string NewUniverse = "new-universe";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _registry;
    private readonly IMetadataResolver _metadata;
    private readonly Func<IReadOnlyCollection<string>, Task<SourceResult?>> _franchise;
    private readonly DateOnly _today;
    private readonly Dictionary<string, JsonObject> _changed = new(StringComparer.Ordinal);
    private JsonObject? _index;
    private bool _indexChanged;

    public RegistryEditor(string registry, IMetadataResolver metadata, Func<IReadOnlyCollection<string>, Task<SourceResult?>> franchise, DateOnly today)
    {
        _registry = registry;
        _metadata = metadata;
        _franchise = franchise;
        _today = today;
    }

    public List<string> Summary { get; } = [];

    public List<string> Warnings { get; } = [];

    public Task ApplyAsync(string kind, IssueForm form) => kind switch
    {
        AddTitle => AddTitleAsync(form),
        MoveTitle => Task.Run(() => Move(form)),
        ExcludeTitle => Task.Run(() => Exclude(form)),
        NewUniverse => NewUniverseAsync(form),
        _ => throw new RequestException($"Unknown request type \"{kind}\".")
    };

    public IReadOnlyList<string> Save()
    {
        var written = new List<string>();
        foreach (var (file, universe) in _changed)
        {
            universe["revision"] = NextRevision((string?)universe["revision"]);
            var definition = DefinitionJson.ParseUniverse(universe.ToJsonString());
            var errors = DefinitionValidator.Validate(definition);
            if (errors.Count > 0)
            {
                throw new RequestException("The change produces an invalid universe:\n- " + string.Join("\n- ", errors));
            }

            File.WriteAllText(Path.Combine(_registry, file), universe.ToJsonString(WriteOptions) + "\n");
            written.Add(file);
        }

        if (_index is not null && _indexChanged)
        {
            File.WriteAllText(Path.Combine(_registry, "index.json"), _index.ToJsonString(WriteOptions) + "\n");
            written.Add("index.json");
        }

        return written;
    }

    public string NextRevision(string? current)
    {
        var today = _today.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        if (current is null || !current.StartsWith(today, StringComparison.Ordinal))
        {
            return today;
        }

        var suffix = current.Length > today.Length && int.TryParse(current[(today.Length + 1)..], CultureInfo.InvariantCulture, out var n) ? n : 0;
        return $"{today}.{(suffix + 1).ToString(CultureInfo.InvariantCulture)}";
    }

    private async Task AddTitleAsync(IssueForm form)
    {
        var (file, universe) = LoadUniverse(form.Required("Universe"));
        var type = form.Required("Title type");
        var tmdb = ParseTmdb(form.Required("TMDB ID"));
        var groups = IssueForm.List(form.Optional("Groups"));
        var flags = IssueForm.List(form.Optional("Flags"));
        CheckDeclared(universe, groups, flags);
        var note = form.Optional("Note");
        var titleOverride = form.Optional("Title override");
        var releasedOverride = form.Optional("Release date override");
        if (releasedOverride.Length > 0 && !DateOnly.TryParseExact(releasedOverride, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new RequestException("Release date override must look like 2026-12-18.");
        }

        var entries = Entries(universe);
        var added = new List<JsonObject>();
        if (type.StartsWith("Movie", StringComparison.OrdinalIgnoreCase))
        {
            if (entries.Any(e => (string?)e["type"] == "movie" && (int?)e["tmdb"] == tmdb))
            {
                throw new RequestException($"Movie {tmdb} is already in this universe. Use the \"Move a title\" form to change its position.");
            }

            var info = await MovieInfoAsync(tmdb, titleOverride, releasedOverride);
            added.Add(Entry(UniqueId(entries, Slug.From(info.Title), info.Released), "movie", info.Title, tmdb, info.Imdb, null, null, info.Released, info.Poster, groups, flags, note));
        }
        else if (type.StartsWith("Whole series", StringComparison.OrdinalIgnoreCase))
        {
            if (entries.Any(e => (string?)e["type"] != "movie" && (int?)e["tmdb"] == tmdb))
            {
                throw new RequestException($"Series {tmdb} already has entries in this universe.");
            }

            var info = await SeriesInfoAsync(tmdb, titleOverride, releasedOverride);
            added.Add(Entry(UniqueId(entries, Slug.From(info.Title), info.Released), "series", info.Title, tmdb, info.Imdb, info.Tvdb, null, info.Released, info.Poster, groups, flags, note));
        }
        else
        {
            var info = await SeriesInfoAsync(tmdb, titleOverride, releasedOverride);
            var requested = TitleReference.ParseSeasons(form.Optional("Seasons"));
            var seasons = SelectSeasons(info, requested);
            foreach (var season in seasons)
            {
                if (entries.Any(e => (string?)e["type"] == "season" && (int?)e["tmdb"] == tmdb && (int?)e["season"] == season.Number))
                {
                    Warnings.Add($"{info.Title} season {season.Number} is already in this universe; skipped.");
                    continue;
                }

                var released = season.Released ?? (requested is null ? null : releasedOverride.Length > 0 ? releasedOverride : null);
                if (released is null)
                {
                    throw new RequestException($"No air date is known for {info.Title} season {season.Number}. Fill in \"Release date override\".");
                }

                added.Add(Entry(UniqueId(entries, $"{Slug.From(info.Title)}-s{season.Number}", released), "season", info.Title, tmdb, info.Imdb, info.Tvdb, season.Number, released, season.Poster, groups, flags, note));
            }

            if (added.Count == 0)
            {
                throw new RequestException("Every requested season is already in this universe.");
            }
        }

        var entryArray = (JsonArray)universe["entries"]!;
        foreach (var entry in added)
        {
            entryArray.Add(entry);
            Summary.Add($"Added `{entry["id"]}`: {Describe(entry)}");
        }

        var ids = added.Select(e => (string)e["id"]!).ToList();
        var placements = Placement.ParseAll(form.Optional("Placement"));
        Place(universe, ids, placements);
        if (placements.Count == 0 && Orders(universe).Any(o => o["items"] is not null))
        {
            Warnings.Add("No placement given: the title only appears in derived rows (release order, groups), not in explicit timelines.");
        }

        _changed[file] = universe;
    }

    private void Move(IssueForm form)
    {
        var (file, universe) = LoadUniverse(form.Required("Universe"));
        var ids = IssueForm.List(form.Required("Entry IDs"));
        var known = Entries(universe).Select(e => (string?)e["id"]).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids.Where(i => !known.Contains(i)))
        {
            throw new RequestException($"Entry \"{id}\" does not exist in {universe["id"]}.");
        }

        var placements = Placement.ParseAll(form.Required("Placement"));
        Place(universe, ids, placements);
        _changed[file] = universe;
    }

    private void Exclude(IssueForm form)
    {
        var (file, universe) = LoadUniverse(form.Required("Universe"));
        var reference = TitleReference.Parse(form.Required("Title"));
        var key = (reference.IsMovie ? "movie:" : "tv:") + reference.Tmdb.ToString(CultureInfo.InvariantCulture);
        var discover = universe["discover"] as JsonObject ?? [];
        universe["discover"] = discover;
        var exclude = discover["exclude"] as JsonArray ?? [];
        discover["exclude"] = exclude;
        if (!exclude.Any(e => (string?)e == key))
        {
            exclude.Add(key);
            Summary.Add($"Excluded `{key}` from automatic discovery.");
        }
        else
        {
            Warnings.Add($"`{key}` was already excluded.");
        }

        if (form.Checked("Options", "Remove it from the universe"))
        {
            var entries = (JsonArray)universe["entries"]!;
            var matching = entries.OfType<JsonObject>()
                .Where(e => (int?)e["tmdb"] == reference.Tmdb && ((string?)e["type"] == "movie") == reference.IsMovie)
                .ToList();
            var removed = matching.Select(e => (string)e["id"]!).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in matching)
            {
                entries.Remove(entry);
                Summary.Add($"Removed `{entry["id"]}` ({Describe(entry)}).");
            }

            foreach (var items in Orders(universe).Select(o => o["items"]).OfType<JsonArray>())
            {
                foreach (var item in items.Where(i => removed.Contains((string?)i ?? string.Empty)).ToList())
                {
                    items.Remove(item);
                }
            }
        }

        _changed[file] = universe;
    }

    private async Task NewUniverseAsync(IssueForm form)
    {
        var id = form.Required("Universe ID").Trim();
        if (Slug.From(id) != id)
        {
            throw new RequestException($"Universe ID \"{id}\" must be lowercase letters, digits and dashes (e.g. \"middle-earth\").");
        }

        var index = Index();
        if (((JsonArray)index["universes"]!).OfType<JsonObject>().Any(u => (string?)u["id"] == id))
        {
            throw new RequestException($"A universe with id \"{id}\" already exists.");
        }

        var name = form.Required("Name");
        var description = form.Optional("Description");
        var anchors = IssueForm.List(form.Optional("Wikidata franchise")).Select(a => a.ToUpperInvariant()).ToList();
        if (anchors.Any(a => !a.StartsWith('Q') || !int.TryParse(a[1..], CultureInfo.InvariantCulture, out _)))
        {
            throw new RequestException("Wikidata franchise ids look like Q642878.");
        }

        var collections = IssueForm.List(form.Optional("TMDB collections")).Select(ParseTmdb).ToList();
        var references = form.Optional("Timeline")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Trim('-', '*', ' '))
            .Where(l => l.Length > 0)
            .Select(TitleReference.Parse)
            .ToList();
        var includeWikidata = form.Checked("Options", "Add every other title");
        if (references.Count == 0 && (!includeWikidata || anchors.Count == 0))
        {
            throw new RequestException("Give a timeline, or a Wikidata franchise with \"Add every other title\" ticked.");
        }

        var entries = new JsonArray();
        var timeline = new List<string>();
        foreach (var reference in references)
        {
            foreach (var entry in await EntriesForAsync(entries, reference, []))
            {
                entries.Add(entry);
                timeline.Add((string)entry["id"]!);
            }
        }

        var groups = new JsonArray();
        if (includeWikidata && anchors.Count > 0)
        {
            var franchise = await _franchise(anchors) ?? throw new RequestException("Wikidata could not be reached.");
            var usedGroups = franchise.Groups
                .Where(g => franchise.Entries.Count(e => e.Groups.Contains(g.Id)) >= 2 && !string.Equals(g.Title, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var group in usedGroups)
            {
                groups.Add(new JsonObject { ["id"] = group.Id, ["title"] = group.Title });
            }

            var groupIds = usedGroups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var discovered in franchise.Entries.Where(e => e.ReleaseDate is null || e.ReleaseDate <= _today.AddYears(2)))
            {
                var isMovie = discovered.Type == EntryType.Movie;
                var existing = entries.OfType<JsonObject>().Where(e => (int?)e["tmdb"] == discovered.Tmdb && ((string?)e["type"] == "movie") == isMovie).ToList();
                var memberGroups = discovered.Groups.Where(groupIds.Contains).ToList();
                if (existing.Count > 0)
                {
                    foreach (var entry in existing.Where(_ => memberGroups.Count > 0))
                    {
                        entry["groups"] = new JsonArray(memberGroups.Select(g => (JsonNode)g!).ToArray());
                    }

                    continue;
                }

                try
                {
                    foreach (var entry in await EntriesForAsync(entries, new TitleReference(isMovie, discovered.Tmdb, null), memberGroups))
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception ex) when (ex is RequestException or HttpRequestException)
                {
                    Warnings.Add($"Skipped {discovered.Title} ({(isMovie ? "movie" : "tv")}:{discovered.Tmdb}): {ex.Message}");
                }
            }
        }

        var orders = new JsonArray();
        var rows = new JsonArray();
        if (timeline.Count > 0)
        {
            var timelineTitle = form.Optional("Timeline title") is { Length: > 0 } t ? t : "Timeline";
            orders.Add(new JsonObject
            {
                ["id"] = "timeline",
                ["title"] = timelineTitle,
                ["description"] = "In-universe chronological order.",
                ["items"] = new JsonArray(timeline.Select(i => (JsonNode)i!).ToArray())
            });
            rows.Add("timeline");
            var types = entries.OfType<JsonObject>().Where(e => timeline.Contains((string)e["id"]!)).Select(e => (string?)e["type"]).Distinct().Count();
            if (types > 1)
            {
                orders.Add(new JsonObject
                {
                    ["id"] = "movie-timeline",
                    ["title"] = "Movie Timeline",
                    ["description"] = "Just the films, in timeline order.",
                    ["derive"] = new JsonObject { ["from"] = "timeline", ["types"] = new JsonArray("movie") }
                });
                rows.Add("movie-timeline");
            }
        }

        foreach (var group in groups.OfType<JsonObject>())
        {
            var groupId = (string)group["id"]!;
            orders.Add(new JsonObject
            {
                ["id"] = groupId,
                ["title"] = (string)group["title"]!,
                ["derive"] = new JsonObject { ["groups"] = new JsonArray(groupId), ["sortBy"] = "released" }
            });
            rows.Add(groupId);
        }

        orders.Add(new JsonObject
        {
            ["id"] = "release",
            ["title"] = "Release Order",
            ["description"] = "Everything in the order it was released.",
            ["derive"] = new JsonObject { ["sortBy"] = "released" }
        });
        rows.Add("release");

        var attribution = new JsonArray();
        foreach (var anchor in anchors)
        {
            attribution.Add(new JsonObject { ["name"] = "Wikidata", ["url"] = "https://www.wikidata.org/wiki/" + anchor, ["license"] = "CC0 1.0" });
        }

        attribution.Add(new JsonObject { ["name"] = "TMDB", ["url"] = "https://www.themoviedb.org/" });
        var sources = form.Optional("Sources");
        var universe = new JsonObject
        {
            ["$schema"] = "../schema/universe.schema.json",
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["name"] = name,
            ["revision"] = NextRevision(null)
        };
        if (description.Length > 0)
        {
            universe["description"] = description;
        }

        universe["discover"] = new JsonObject
        {
            ["wikidata"] = new JsonArray(anchors.Select(a => (JsonNode)a!).ToArray()),
            ["tmdbCollections"] = new JsonArray(collections.Select(c => (JsonNode)c).ToArray())
        };
        universe["attribution"] = attribution;
        universe["flags"] = new JsonObject();
        universe["groups"] = groups;
        universe["entries"] = entries;
        universe["orders"] = orders;
        universe["hub"] = new JsonObject { ["rows"] = rows };
        Reorder(universe, ["$schema", "schemaVersion", "id", "name", "description", "revision", "discover", "attribution", "flags", "groups", "entries", "orders", "hub"]);

        var file = $"universes/{id}.json";
        _changed[file] = universe;
        var indexEntry = new JsonObject { ["id"] = id, ["name"] = name };
        if (description.Length > 0)
        {
            indexEntry["description"] = description;
        }

        indexEntry["file"] = file;
        ((JsonArray)index["universes"]!).Add(indexEntry);
        _indexChanged = true;
        Summary.Add($"Created universe `{id}` ({name}) with {entries.Count} entries ({timeline.Count} in the timeline).");
        if (sources.Length > 0)
        {
            Summary.Add("Sources: " + sources.Replace('\n', ' '));
        }
    }

    private async Task<IReadOnlyList<JsonObject>> EntriesForAsync(JsonArray existing, TitleReference reference, IReadOnlyList<string> groups)
    {
        var known = existing.OfType<JsonObject>().ToList();
        if (reference.IsMovie)
        {
            var info = await _metadata.MovieAsync(reference.Tmdb);
            if (info.Released is null)
            {
                throw new RequestException($"No release date is known for {info.Title} (movie:{reference.Tmdb}).");
            }

            return [Entry(UniqueId(known, Slug.From(info.Title), info.Released), "movie", info.Title, reference.Tmdb, info.Imdb, null, null, info.Released, info.Poster, groups, [], string.Empty)];
        }

        var series = await _metadata.SeriesAsync(reference.Tmdb);
        var result = new List<JsonObject>();
        foreach (var season in SelectSeasons(series, reference.Seasons))
        {
            if (season.Released is null)
            {
                continue;
            }

            var entry = Entry(UniqueId(known.Concat(result).ToList(), $"{Slug.From(series.Title)}-s{season.Number}", season.Released), "season", series.Title, reference.Tmdb, series.Imdb, series.Tvdb, season.Number, season.Released, season.Poster, groups, [], string.Empty);
            result.Add(entry);
        }

        if (result.Count == 0)
        {
            throw new RequestException($"No aired seasons found for {series.Title} (tv:{reference.Tmdb}).");
        }

        return result;
    }

    private IReadOnlyList<SeasonInfo> SelectSeasons(TitleInfo series, IReadOnlyList<int>? requested)
    {
        if (requested is null)
        {
            var horizon = _today.AddYears(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return series.Seasons.Where(s => s.Released is not null && string.CompareOrdinal(s.Released, horizon) <= 0).ToList();
        }

        return requested.Select(n => series.Seasons.FirstOrDefault(s => s.Number == n) ?? new SeasonInfo(n, null, null)).ToList();
    }

    private async Task<TitleInfo> MovieInfoAsync(int tmdb, string titleOverride, string releasedOverride)
    {
        TitleInfo info;
        try
        {
            info = await _metadata.MovieAsync(tmdb);
        }
        catch (RequestException) when (titleOverride.Length > 0 && releasedOverride.Length > 0)
        {
            info = new TitleInfo(titleOverride, releasedOverride, null, null, null, []);
        }

        info = info with
        {
            Title = titleOverride.Length > 0 ? titleOverride : info.Title,
            Released = releasedOverride.Length > 0 ? releasedOverride : info.Released
        };
        return info.Released is null ? throw new RequestException("No release date is known; fill in \"Release date override\".") : info;
    }

    private async Task<TitleInfo> SeriesInfoAsync(int tmdb, string titleOverride, string releasedOverride)
    {
        TitleInfo info;
        try
        {
            info = await _metadata.SeriesAsync(tmdb);
        }
        catch (RequestException) when (titleOverride.Length > 0 && releasedOverride.Length > 0)
        {
            info = new TitleInfo(titleOverride, releasedOverride, null, null, null, []);
        }

        return info with
        {
            Title = titleOverride.Length > 0 ? titleOverride : info.Title,
            Released = releasedOverride.Length > 0 ? releasedOverride : info.Released
        };
    }

    private void Place(JsonObject universe, IReadOnlyList<string> ids, IReadOnlyList<Placement> placements)
    {
        var orders = Orders(universe).ToList();
        foreach (var placement in placements)
        {
            var order = orders.FirstOrDefault(o => (string?)o["id"] == placement.OrderId)
                ?? throw new RequestException($"Order \"{placement.OrderId}\" does not exist. Orders: {string.Join(", ", orders.Select(o => (string?)o["id"]))}.");
            if (order["items"] is not JsonArray items)
            {
                throw new RequestException($"Order \"{placement.OrderId}\" is derived automatically and can't be placed into; only explicit orders ({string.Join(", ", orders.Where(o => o["items"] is not null).Select(o => (string?)o["id"]))}) can.");
            }

            foreach (var existing in items.Where(i => ids.Contains((string?)i ?? string.Empty)).ToList())
            {
                items.Remove(existing);
            }

            var position = placement.Kind switch
            {
                PlacementKind.First => 0,
                PlacementKind.Last => items.Count,
                _ => AnchorIndex(items, placement) + (placement.Kind == PlacementKind.After ? 1 : 0)
            };
            for (var i = 0; i < ids.Count; i++)
            {
                items.Insert(position + i, ids[i]);
            }

            var where = placement.Kind switch
            {
                PlacementKind.First => "at the start",
                PlacementKind.Last => "at the end",
                _ => $"{placement.Kind.ToString().ToLowerInvariant()} `{placement.Anchor}`"
            };
            Summary.Add($"Placed {string.Join(", ", ids.Select(i => $"`{i}`"))} in `{placement.OrderId}` {where}.");
        }
    }

    private static int AnchorIndex(JsonArray items, Placement placement)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if ((string?)items[i] == placement.Anchor)
            {
                return i;
            }
        }

        throw new RequestException($"Entry \"{placement.Anchor}\" is not in order \"{placement.OrderId}\".");
    }

    private (string File, JsonObject Universe) LoadUniverse(string id)
    {
        var entry = ((JsonArray)Index()["universes"]!).OfType<JsonObject>().FirstOrDefault(u => (string?)u["id"] == id.Trim())
            ?? throw new RequestException($"Unknown universe \"{id}\". Known: {string.Join(", ", ((JsonArray)Index()["universes"]!).OfType<JsonObject>().Select(u => (string?)u["id"]))}.");
        var file = (string)entry["file"]!;
        if (_changed.TryGetValue(file, out var loaded))
        {
            return (file, loaded);
        }

        return (file, (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_registry, file)))!);
    }

    private JsonObject Index()
    {
        _index ??= (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_registry, "index.json")))!;
        return _index;
    }

    private static void CheckDeclared(JsonObject universe, IReadOnlyList<string> groups, IReadOnlyList<string> flags)
    {
        var knownGroups = (universe["groups"] as JsonArray ?? []).OfType<JsonObject>().Select(g => (string?)g["id"]).ToHashSet(StringComparer.Ordinal);
        var knownFlags = (universe["flags"] as JsonObject ?? []).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var badGroups = groups.Where(g => !knownGroups.Contains(g)).ToList();
        var badFlags = flags.Where(f => !knownFlags.Contains(f)).ToList();
        if (badGroups.Count > 0)
        {
            throw new RequestException($"Unknown group(s) {string.Join(", ", badGroups)}. Known: {string.Join(", ", knownGroups)}.");
        }

        if (badFlags.Count > 0)
        {
            throw new RequestException($"Unknown flag(s) {string.Join(", ", badFlags)}. Known: {string.Join(", ", knownFlags)}.");
        }
    }

    private static List<JsonObject> Entries(JsonObject universe) => (universe["entries"] as JsonArray ?? []).OfType<JsonObject>().ToList();

    private static IEnumerable<JsonObject> Orders(JsonObject universe) => (universe["orders"] as JsonArray ?? []).OfType<JsonObject>();

    private static string UniqueId(IReadOnlyCollection<JsonObject> entries, string candidate, string? released)
    {
        var used = entries.Select(e => (string?)e["id"]).ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(candidate))
        {
            return candidate;
        }

        var withYear = released is { Length: >= 4 } ? $"{candidate}-{released[..4]}" : candidate;
        var unique = withYear;
        for (var n = 2; used.Contains(unique); n++)
        {
            unique = $"{withYear}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        return unique;
    }

    private static JsonObject Entry(string id, string type, string title, int tmdb, string? imdb, int? tvdb, int? season, string? released, string? poster, IReadOnlyList<string> groups, IReadOnlyList<string> flags, string note)
    {
        var entry = new JsonObject { ["id"] = id, ["type"] = type, ["title"] = title, ["tmdb"] = tmdb };
        if (imdb is not null)
        {
            entry["imdb"] = imdb;
        }

        if (tvdb is int tvdbId)
        {
            entry["tvdb"] = tvdbId;
        }

        if (season is int number)
        {
            entry["season"] = number;
        }

        if (released is not null)
        {
            entry["released"] = released;
        }

        if (poster is not null)
        {
            entry["poster"] = poster;
        }

        if (groups.Count > 0)
        {
            entry["groups"] = new JsonArray(groups.Select(g => (JsonNode)g!).ToArray());
        }

        if (flags.Count > 0)
        {
            entry["flags"] = new JsonArray(flags.Select(f => (JsonNode)f!).ToArray());
        }

        if (note.Length > 0)
        {
            entry["note"] = note;
        }

        return entry;
    }

    private static void Reorder(JsonObject node, IReadOnlyList<string> order)
    {
        var properties = node.ToList();
        node.Clear();
        foreach (var key in order)
        {
            var property = properties.FirstOrDefault(p => p.Key == key);
            if (property.Key is not null)
            {
                node[key] = property.Value;
            }
        }
    }

    private static string Describe(JsonObject entry)
    {
        var season = entry["season"] is null ? string.Empty : $" season {entry["season"]}";
        var kind = (string?)entry["type"] == "movie" ? "movie" : "tv";
        return $"{entry["title"]}{season} ({entry["released"]}, [TMDB {kind}/{entry["tmdb"]}](https://www.themoviedb.org/{kind}/{entry["tmdb"]}))";
    }

    private static int ParseTmdb(string value)
    {
        var text = value.Trim();
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            text = text[(slash + 1)..];
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
        {
            text = text[..dash];
        }

        return int.TryParse(text, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : throw new RequestException($"\"{value}\" is not a TMDB id (a number, or a themoviedb.org URL).");
    }
}
