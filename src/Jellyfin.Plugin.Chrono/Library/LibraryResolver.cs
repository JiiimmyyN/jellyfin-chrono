using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Chrono.Definitions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Chrono.Library;

public sealed class LibraryMatch
{
    public required BaseItem Item { get; init; }

    public Series? Series { get; init; }

    public IReadOnlyList<BaseItem> Episodes { get; init; } = [];

    public bool IsPlayable => Item is not Folder || Episodes.Count > 0;
}

public sealed record OwnedSeason(int Number, DateOnly? Premiere, int EpisodeCount);

public sealed class LibrarySnapshot
{
    private readonly Dictionary<string, BaseItem> _moviesByTmdb = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BaseItem> _moviesByImdb = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _seriesByTmdb = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _seriesByTvdb = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _seriesByImdb = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Dictionary<int, SeasonData>> _seasons = [];

    internal LibrarySnapshot()
    {
    }

    public LibraryMatch? Match(EntryDefinition entry)
    {
        switch (entry.Type)
        {
            case EntryType.Movie:
                var movie = Find(_moviesByTmdb, Key(entry.Tmdb)) ?? Find(_moviesByImdb, entry.Imdb);
                return movie is null ? null : new LibraryMatch { Item = movie };
            case EntryType.Series:
                var series = FindSeries(entry);
                if (series is null)
                {
                    return null;
                }

                var episodes = _seasons.TryGetValue(series.Id, out var all)
                    ? all.Where(s => s.Key > 0).OrderBy(s => s.Key).SelectMany(s => s.Value.Episodes).ToList()
                    : [];
                return new LibraryMatch { Item = series, Series = series, Episodes = episodes };
            case EntryType.Season:
                var parent = FindSeries(entry);
                if (parent is null || entry.Season is not int number
                    || !_seasons.TryGetValue(parent.Id, out var seasons)
                    || !seasons.TryGetValue(number, out var season)
                    || season.Episodes.Count == 0)
                {
                    return null;
                }

                return new LibraryMatch { Item = (BaseItem?)season.Season ?? parent, Series = parent, Episodes = season.Episodes };
            default:
                return null;
        }
    }

    public IReadOnlyList<OwnedSeason> OwnedSeasons(EntryDefinition seriesEntry)
    {
        var series = FindSeries(seriesEntry);
        if (series is null || !_seasons.TryGetValue(series.Id, out var seasons))
        {
            return [];
        }

        return seasons
            .Where(s => s.Key > 0 && s.Value.Episodes.Count > 0)
            .OrderBy(s => s.Key)
            .Select(s => new OwnedSeason(
                s.Key,
                ToDate(s.Value.Season?.PremiereDate) ?? ToDate(s.Value.Episodes.Select(e => e.PremiereDate).Where(d => d.HasValue).Min()),
                s.Value.Episodes.Count))
            .ToList();
    }

    internal void AddMovie(BaseItem movie)
    {
        AddKey(_moviesByTmdb, movie.GetProviderId(MetadataProvider.Tmdb), movie);
        AddKey(_moviesByImdb, movie.GetProviderId(MetadataProvider.Imdb), movie);
    }

    internal void AddSeries(Series series)
    {
        AddKey(_seriesByTmdb, series.GetProviderId(MetadataProvider.Tmdb), series);
        AddKey(_seriesByTvdb, series.GetProviderId(MetadataProvider.Tvdb), series);
        AddKey(_seriesByImdb, series.GetProviderId(MetadataProvider.Imdb), series);
    }

    internal void AddSeason(Guid seriesId, Season season)
    {
        if (season.IndexNumber is int number)
        {
            Seasons(seriesId, number).Season ??= season;
        }
    }

    internal void AddEpisode(Guid seriesId, int seasonNumber, BaseItem episode)
    {
        Seasons(seriesId, seasonNumber).Episodes.Add(episode);
    }

    internal void SortEpisodes()
    {
        foreach (var season in _seasons.Values.SelectMany(s => s.Values))
        {
            season.Episodes.Sort((a, b) => (a.IndexNumber ?? int.MaxValue).CompareTo(b.IndexNumber ?? int.MaxValue));
        }
    }

    internal IEnumerable<Series> AllSeries => _seriesByTmdb.Values.Concat(_seriesByTvdb.Values).Concat(_seriesByImdb.Values).DistinctBy(s => s.Id);

    private SeasonData Seasons(Guid seriesId, int number)
    {
        if (!_seasons.TryGetValue(seriesId, out var seasons))
        {
            seasons = [];
            _seasons[seriesId] = seasons;
        }

        if (!seasons.TryGetValue(number, out var data))
        {
            data = new SeasonData();
            seasons[number] = data;
        }

        return data;
    }

    private Series? FindSeries(EntryDefinition entry)
    {
        return Find(_seriesByTmdb, Key(entry.Tmdb))
            ?? Find(_seriesByTvdb, entry.Tvdb is int tvdb ? Key(tvdb) : null)
            ?? Find(_seriesByImdb, entry.Imdb);
    }

    private static T? Find<T>(Dictionary<string, T> map, string? key)
        where T : class
        => key is not null && map.TryGetValue(key, out var value) ? value : null;

    private static void AddKey<T>(Dictionary<string, T> map, string? key, T item)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            map.TryAdd(key.Trim(), item);
        }
    }

    private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static DateOnly? ToDate(DateTime? date) => date is DateTime value ? DateOnly.FromDateTime(value) : null;

    private sealed class SeasonData
    {
        public Season? Season { get; set; }

        public List<BaseItem> Episodes { get; } = [];
    }
}

public sealed class LibraryResolver
{
    private readonly ILibraryManager _libraryManager;

    public LibraryResolver(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public LibrarySnapshot Snapshot(IEnumerable<EntryDefinition> entries)
    {
        var list = entries.ToList();
        var snapshot = new LibrarySnapshot();

        var movies = list.Where(e => e.Type == EntryType.Movie).ToList();
        if (movies.Count > 0)
        {
            foreach (var movie in _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true,
                IsVirtualItem = false,
                HasAnyProviderIds = ProviderIds(movies, includeTvdb: false)
            }))
            {
                snapshot.AddMovie(movie);
            }
        }

        var shows = list.Where(e => e.IsTv).ToList();
        if (shows.Count > 0)
        {
            foreach (var series in _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Series],
                Recursive = true,
                HasAnyProviderIds = ProviderIds(shows, includeTvdb: true)
            }).OfType<Series>())
            {
                snapshot.AddSeries(series);
            }

            var seriesIds = snapshot.AllSeries.Select(s => s.Id).ToArray();
            if (seriesIds.Length > 0)
            {
                foreach (var season in _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Season],
                    Recursive = true,
                    AncestorIds = seriesIds
                }).OfType<Season>())
                {
                    snapshot.AddSeason(season.SeriesId, season);
                }

                foreach (var episode in _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Episode],
                    Recursive = true,
                    IsVirtualItem = false,
                    AncestorIds = seriesIds
                }).OfType<Episode>())
                {
                    var seasonNumber = episode.ParentIndexNumber ?? episode.Season?.IndexNumber;
                    if (seasonNumber is int number)
                    {
                        snapshot.AddEpisode(episode.SeriesId, number, episode);
                    }
                }

                snapshot.SortEpisodes();
            }
        }

        return snapshot;
    }

    public IReadOnlyList<BaseItem> MoviesInTmdbCollection(string collectionId)
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsVirtualItem = false,
            HasAnyProviderId = new Dictionary<string, string> { [MetadataProvider.TmdbCollection.ToString()] = collectionId }
        });
    }

    public IReadOnlyList<BaseItem> AllWithTmdb(BaseItemKind kind)
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [kind],
            Recursive = true,
            IsVirtualItem = false,
            HasTmdbId = true
        });
    }

    private static Dictionary<string, string[]> ProviderIds(IReadOnlyCollection<EntryDefinition> entries, bool includeTvdb)
    {
        var ids = new Dictionary<string, string[]>
        {
            [MetadataProvider.Tmdb.ToString()] = entries.Select(e => e.Tmdb.ToString(CultureInfo.InvariantCulture)).Distinct().ToArray()
        };
        var imdb = entries.Select(e => e.Imdb).OfType<string>().Distinct().ToArray();
        if (imdb.Length > 0)
        {
            ids[MetadataProvider.Imdb.ToString()] = imdb;
        }

        if (includeTvdb)
        {
            var tvdb = entries.Select(e => e.Tvdb).OfType<int>().Select(t => t.ToString(CultureInfo.InvariantCulture)).Distinct().ToArray();
            if (tvdb.Length > 0)
            {
                ids[MetadataProvider.Tvdb.ToString()] = tvdb;
            }
        }

        return ids;
    }
}
