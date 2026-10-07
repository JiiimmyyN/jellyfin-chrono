using System.Collections.Concurrent;
using Jellyfin.Plugin.Chrono.Configuration;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Library;
using Jellyfin.Plugin.Chrono.Seerr;
using Jellyfin.Plugin.Chrono.Sources;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Composition;

public sealed class ResolvedUniverse
{
    public required UniverseDefinition Definition { get; init; }

    public required UniverseSettings Settings { get; init; }

    public required string Origin { get; init; }

    public required IReadOnlyDictionary<string, EntryDefinition> Entries { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Orders { get; init; }

    public required IReadOnlyDictionary<string, LibraryMatch> Matches { get; init; }

    public required IReadOnlyList<string> Rows { get; init; }

    public required IReadOnlyList<HubPageDefinition> Pages { get; init; }

    public required string PrimaryRowId { get; init; }

    public IReadOnlyDictionary<string, SeerrMedia> SeerrDetails { get; init; } = new Dictionary<string, SeerrMedia>();

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DateTimeOffset BuiltAt { get; init; } = DateTimeOffset.UtcNow;

    public OrderDefinition? Order(string id) => Definition.Orders.FirstOrDefault(o => o.Id == id);
}

public sealed record AvailableUniverse(string Id, string Name, string? Description, string? Revision, string Origin, int EntryCount, bool Custom);

public sealed class UniverseService
{
    private readonly RegistryClient _registry;
    private readonly WikidataClient _wikidata;
    private readonly MdbListClient _mdbList;
    private readonly TraktClient _trakt;
    private readonly SeerrClient _seerr;
    private readonly LibraryResolver _resolver;
    private readonly ILogger<UniverseService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile IReadOnlyDictionary<string, ResolvedUniverse> _universes = new Dictionary<string, ResolvedUniverse>();
    private volatile IReadOnlyList<AvailableUniverse> _available = [];
    private readonly ConcurrentDictionary<string, string> _errors = new(StringComparer.Ordinal);

    public UniverseService(
        RegistryClient registry,
        WikidataClient wikidata,
        MdbListClient mdbList,
        TraktClient trakt,
        SeerrClient seerr,
        LibraryResolver resolver,
        ILogger<UniverseService> logger)
    {
        _registry = registry;
        _wikidata = wikidata;
        _mdbList = mdbList;
        _trakt = trakt;
        _seerr = seerr;
        _resolver = resolver;
        _logger = logger;
    }

    public event EventHandler? Refreshed;

    public IReadOnlyDictionary<string, ResolvedUniverse> Universes => _universes;

    public IReadOnlyList<AvailableUniverse> Available => _available;

    public IReadOnlyDictionary<string, string> Errors => _errors;

    public bool HasLoaded { get; private set; }

    public async Task RefreshAsync(bool forceSources, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var definitions = await LoadDefinitionsAsync(config, forceSources, cancellationToken).ConfigureAwait(false);
            _available = definitions.Values
                .Select(d => new AvailableUniverse(d.Definition.Id, d.Definition.Name, d.Definition.Description, d.Definition.Revision, d.Origin, d.Definition.Entries.Count, d.Origin == "custom"))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolved = new Dictionary<string, ResolvedUniverse>(StringComparer.Ordinal);
            foreach (var settings in config.Universes.Where(u => u.Enabled))
            {
                if (!definitions.TryGetValue(settings.Id, out var loaded))
                {
                    _errors[settings.Id] = "Universe definition not found in any registry, local file or custom universe.";
                    continue;
                }

                try
                {
                    resolved[settings.Id] = await ResolveAsync(loaded, settings, config, forceSources, cancellationToken).ConfigureAwait(false);
                    _errors.TryRemove(settings.Id, out _);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Chrono: failed to build universe {Id}", settings.Id);
                    _errors[settings.Id] = ex.Message;
                    if (_universes.TryGetValue(settings.Id, out var previous))
                    {
                        resolved[settings.Id] = previous;
                    }
                }
            }

            _universes = resolved;
            HasLoaded = true;
        }
        finally
        {
            _refreshLock.Release();
        }

        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<Dictionary<string, LoadedUniverse>> LoadDefinitionsAsync(PluginConfiguration config, bool forceSources, CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<string, LoadedUniverse>(StringComparer.Ordinal);
        var registryUniverses = await _registry.LoadAsync(config.RegistryUrls, TimeSpan.FromHours(Math.Max(1, config.RegistryRefreshHours)), forceSources, cancellationToken).ConfigureAwait(false);
        foreach (var universe in registryUniverses)
        {
            definitions[universe.Definition.Id] = universe;
        }

        foreach (var local in LoadLocal())
        {
            definitions[local.Definition.Id] = local;
        }

        foreach (var custom in config.CustomUniverses.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
        {
            try
            {
                var definition = await BuildCustomAsync(custom, config, forceSources, cancellationToken).ConfigureAwait(false);
                definitions[definition.Id] = new LoadedUniverse(definition, "custom");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Chrono: custom universe {Id} could not be built", custom.Id);
                _errors[custom.Id] = ex.Message;
            }
        }

        return definitions;
    }

    private IEnumerable<LoadedUniverse> LoadLocal()
    {
        var directory = Plugin.Instance?.LocalUniversesDirectory;
        if (directory is null || !Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            UniverseDefinition? definition = null;
            try
            {
                definition = DefinitionJson.ParseUniverse(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException)
            {
                _logger.LogWarning(ex, "Chrono: local universe file {File} is invalid", file);
                _errors[Path.GetFileNameWithoutExtension(file)] = $"{Path.GetFileName(file)}: {ex.Message}";
            }

            if (definition is null)
            {
                continue;
            }

            var errors = DefinitionValidator.Validate(definition);
            if (errors.Count > 0)
            {
                _errors[definition.Id] = $"{Path.GetFileName(file)}: {string.Join("; ", errors.Take(5))}";
                continue;
            }

            yield return new LoadedUniverse(definition, "local:" + Path.GetFileName(file));
        }
    }

    private async Task<UniverseDefinition> BuildCustomAsync(CustomUniverse custom, PluginConfiguration config, bool forceSources, CancellationToken cancellationToken)
    {
        var maxAge = TimeSpan.FromHours(Math.Max(1, config.DiscoveryRefreshHours));
        var listMaxAge = TimeSpan.FromHours(Math.Max(1, config.RegistryRefreshHours));
        var results = new List<(SourceResult, string)>();
        foreach (var source in custom.Sources)
        {
            SourceResult? result = source.Type switch
            {
                SourceType.Wikidata => await _wikidata.GetFranchiseAsync(source.Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries), maxAge, forceSources, cancellationToken).ConfigureAwait(false),
                SourceType.MdbList => await _mdbList.GetListAsync(source.Value, listMaxAge, forceSources, cancellationToken).ConfigureAwait(false),
                SourceType.Trakt => await _trakt.GetListAsync(source.Value, config.TraktClientId, listMaxAge, forceSources, cancellationToken).ConfigureAwait(false),
                SourceType.TmdbCollection => TmdbCollection(source.Value),
                _ => null
            };
            if (result is not null)
            {
                results.Add((result, source.Title));
            }
        }

        if (results.Count == 0 || results.All(r => r.Item1.Entries.Count == 0))
        {
            throw new InvalidDataException("None of the sources returned any titles.");
        }

        return UniverseComposer.FromSources(custom.Id, custom.Name, custom.Description, results);
    }

    private SourceResult TmdbCollection(string collectionId)
    {
        var result = new SourceResult();
        foreach (var movie in _resolver.MoviesInTmdbCollection(collectionId.Trim()))
        {
            if (!int.TryParse(movie.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tmdb), System.Globalization.CultureInfo.InvariantCulture, out var tmdb))
            {
                continue;
            }

            result.SuggestedName ??= (movie as MediaBrowser.Controller.Entities.Movies.Movie)?.CollectionName;
            result.Entries.Add(new EntryDefinition
            {
                Id = SourceResult.MovieEntryId(tmdb),
                Type = EntryType.Movie,
                Tmdb = tmdb,
                Imdb = movie.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Imdb),
                Title = movie.Name,
                Released = movie.PremiereDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                    ?? (movie.ProductionYear is int year ? $"{year:D4}-01-01" : null)
            });
        }

        return result;
    }

    private async Task<ResolvedUniverse> ResolveAsync(LoadedUniverse loaded, UniverseSettings settings, PluginConfiguration config, bool forceSources, CancellationToken cancellationToken)
    {
        var definition = loaded.Definition;
        var warnings = new List<string>();
        if (settings.IncludeDiscovered && definition.Discover?.Wikidata is { Count: > 0 } anchors && loaded.Origin != "custom")
        {
            var discovered = await _wikidata.GetFranchiseAsync(anchors, TimeSpan.FromHours(Math.Max(1, config.DiscoveryRefreshHours)), forceSources, cancellationToken).ConfigureAwait(false);
            if (discovered is not null)
            {
                definition = UniverseComposer.AddDiscovered(definition, discovered);
            }
            else
            {
                warnings.Add("Wikidata discovery is unavailable; showing curated titles only.");
            }
        }

        var snapshot = _resolver.Snapshot(definition.Entries);
        definition = UniverseComposer.DropUnowned(
            definition,
            e => snapshot.Match(e) is not null,
            e => e.Flags.Contains(UniverseComposer.DiscoveredFlag) || (loaded.Origin == "custom" && !settings.ShowMissing));
        definition = UniverseComposer.SplitSeasons(definition, snapshot.OwnedSeasons);

        var matches = new Dictionary<string, LibraryMatch>(StringComparer.Ordinal);
        foreach (var entry in definition.Entries)
        {
            if (snapshot.Match(entry) is { } match)
            {
                matches[entry.Id] = match;
            }
        }

        var orders = OrderEngine.Evaluate(definition, settings.HiddenFlags);
        if (!settings.ShowMissing)
        {
            orders = orders.ToDictionary(o => o.Key, o => (IReadOnlyList<string>)o.Value.Where(matches.ContainsKey).ToList(), StringComparer.Ordinal);
        }

        var (rows, pages) = UniverseComposer.HubLayout(definition);
        var primary = UniverseComposer.PrimaryRow(definition, rows);
        var placed = orders.TryGetValue(primary, out var primaryItems) ? primaryItems.ToHashSet(StringComparer.Ordinal) : [];
        var notPlaced = definition.Entries.Count(e => matches.ContainsKey(e.Id)
            && !placed.Contains(e.Id)
            && !e.Flags.Any(settings.HiddenFlags.Contains)
            && (loaded.Origin == "custom" || e.Flags.Contains(UniverseComposer.DiscoveredFlag)));
        if (notPlaced > 0 && definition.Orders.Any(o => o.Id == primary && o.Items is not null))
        {
            warnings.Add($"{notPlaced} owned title(s) are not placed in '{definition.Orders.First(o => o.Id == primary).Title}' (they still appear in derived rows such as Release Order).");
        }

        var entries = definition.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var seerrDetails = await EnrichAsync(definition.Entries.Where(e => !matches.ContainsKey(e.Id)).ToList(), config, cancellationToken).ConfigureAwait(false);

        return new ResolvedUniverse
        {
            Definition = definition,
            Settings = settings,
            Origin = loaded.Origin,
            Entries = entries,
            Orders = orders,
            Matches = matches,
            Rows = rows,
            Pages = pages,
            PrimaryRowId = primary,
            SeerrDetails = seerrDetails,
            Warnings = warnings
        };
    }

    private async Task<IReadOnlyDictionary<string, SeerrMedia>> EnrichAsync(IReadOnlyList<EntryDefinition> missing, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var result = new ConcurrentDictionary<string, SeerrMedia>(StringComparer.Ordinal);
        if (!SeerrClient.IsConfigured(config) || missing.Count == 0)
        {
            return result;
        }

        await Parallel.ForEachAsync(
            missing,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (entry, ct) =>
            {
                if (await _seerr.GetMediaAsync(config, entry.IsTv, entry.Tmdb, ct).ConfigureAwait(false) is { } media)
                {
                    result[entry.Id] = media;
                }
            }).ConfigureAwait(false);
        return result;
    }
}
