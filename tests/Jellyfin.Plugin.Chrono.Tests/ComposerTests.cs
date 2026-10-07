using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Library;
using Jellyfin.Plugin.Chrono.Sources;

namespace Jellyfin.Plugin.Chrono.Tests;

public class ComposerTests
{
    private static EntryDefinition Movie(int tmdb, string released, params string[] groups) => new()
    {
        Id = SourceResult.MovieEntryId(tmdb), Type = EntryType.Movie, Tmdb = tmdb, Title = "Movie " + tmdb, Released = released, Groups = [.. groups]
    };

    private static EntryDefinition Series(int tmdb, string released) => new()
    {
        Id = SourceResult.SeriesEntryId(tmdb), Type = EntryType.Series, Tmdb = tmdb, Title = "Show " + tmdb, Released = released
    };

    [Fact]
    public void FromSourcesBuildsRankedReleaseAndGroupRows()
    {
        var wikidata = new SourceResult();
        wikidata.Entries.Add(Movie(1, "2001-01-01", "trilogy"));
        wikidata.Entries.Add(Movie(2, "2002-01-01", "trilogy"));
        wikidata.Entries.Add(Series(10, "2005-01-01"));
        wikidata.Groups.Add(new GroupDefinition { Id = "trilogy", Title = "Trilogy" });
        var list = new SourceResult { RankedEntryIds = [SourceResult.MovieEntryId(2), SourceResult.SeriesEntryId(10), SourceResult.MovieEntryId(1)] };
        list.Entries.Add(Movie(2, "2002-01-01"));
        list.Entries.Add(Series(10, "2005-01-01"));
        list.Entries.Add(Movie(1, "2001-01-01"));

        var universe = UniverseComposer.FromSources("test", "Test", null, [(wikidata, ""), (list, "Chronological")]);

        Assert.Empty(DefinitionValidator.Validate(universe));
        Assert.Equal(["chronological", "release", "trilogy"], universe.Hub!.Rows);
        var orders = OrderEngine.Evaluate(universe, []);
        Assert.Equal(["movie-2", "tv-10", "movie-1"], orders["chronological"]);
        Assert.Equal(["movie-1", "movie-2", "tv-10"], orders["release"]);
        Assert.Equal(["movie-1", "movie-2"], orders["trilogy"]);
        Assert.Equal("chronological", UniverseComposer.PrimaryRow(universe, universe.Hub.Rows));
    }

    [Fact]
    public void SeasonEntriesReplaceTheirSeriesInRankedLists()
    {
        var shows = new SourceResult();
        shows.Entries.Add(Series(10, "2005-01-01"));
        var seasons = new SourceResult { RankedEntryIds = [SourceResult.SeasonEntryId(10, 2), SourceResult.MovieEntryId(1)] };
        seasons.Entries.Add(new EntryDefinition { Id = SourceResult.SeasonEntryId(10, 2), Type = EntryType.Season, Tmdb = 10, Season = 2, Title = "Show", Released = "2006-01-01" });
        seasons.Entries.Add(Movie(1, "2001-01-01"));
        var series = new SourceResult { RankedEntryIds = [SourceResult.SeriesEntryId(10)] };
        series.Entries.Add(Series(10, "2005-01-01"));

        var universe = UniverseComposer.FromSources("test", "Test", null, [(shows, ""), (seasons, "Timeline"), (series, "Other")]);

        Assert.DoesNotContain(universe.Entries, e => e.Id == "tv-10");
        Assert.Equal(["tv-10-s2"], universe.Orders.Single(o => o.Id == "other").Items);
    }

    [Fact]
    public void DiscoveredEntriesSkipCuratedTitles()
    {
        var curated = new UniverseDefinition
        {
            Id = "u",
            Name = "U",
            Entries = [new EntryDefinition { Id = "loki-s1", Type = EntryType.Season, Tmdb = 84958, Season = 1, Title = "Loki", Released = "2021-06-09" }],
            Orders = [new OrderDefinition { Id = "release", Title = "Release", Derive = new DeriveDefinition { SortBy = OrderSort.Released } }]
        };
        var discovered = new SourceResult();
        discovered.Entries.Add(Series(84958, "2021-06-09"));
        discovered.Entries.Add(Movie(5, "2030-01-01", "something"));

        var result = UniverseComposer.AddDiscovered(curated, discovered);

        var added = Assert.Single(result.Entries, e => e.Id == "movie-5");
        Assert.Equal([UniverseComposer.DiscoveredFlag], added.Flags);
        Assert.Empty(added.Groups);
        Assert.True(result.Flags.ContainsKey(UniverseComposer.DiscoveredFlag));
        Assert.Single(curated.Entries);
    }

    [Fact]
    public void ExcludedTitlesAreNeverDiscovered()
    {
        var curated = new UniverseDefinition
        {
            Id = "u",
            Name = "U",
            Discover = new DiscoverDefinition { Exclude = ["movie:5", "tv:7"] },
            Orders = [new OrderDefinition { Id = "release", Title = "Release", Derive = new DeriveDefinition { SortBy = OrderSort.Released } }]
        };
        var discovered = new SourceResult();
        discovered.Entries.Add(Movie(5, "2001-01-01"));
        discovered.Entries.Add(Series(7, "2002-01-01"));
        discovered.Entries.Add(Movie(7, "2003-01-01"));

        var result = UniverseComposer.AddDiscovered(curated, discovered);

        Assert.Equal(["movie-7"], result.Entries.Select(e => e.Id));
    }

    [Fact]
    public void SplitSeasonsReplacesOwnedMultiSeasonSeries()
    {
        var universe = new UniverseDefinition
        {
            Id = "u",
            Name = "U",
            Entries = [Movie(1, "2001-01-01"), Series(10, "2005-01-01"), Series(11, "2007-01-01")],
            Orders =
            [
                new OrderDefinition { Id = "timeline", Title = "Timeline", Items = ["tv-10", "movie-1", "tv-11"] },
                new OrderDefinition { Id = "release", Title = "Release", Derive = new DeriveDefinition { SortBy = OrderSort.Released } }
            ]
        };

        var split = UniverseComposer.SplitSeasons(universe, e => e.Tmdb == 10
            ? [new OwnedSeason(1, new DateOnly(2005, 1, 1), 10), new OwnedSeason(2, new DateOnly(2008, 1, 1), 10)]
            : [new OwnedSeason(1, null, 3)]);

        Assert.Empty(DefinitionValidator.Validate(split));
        var orders = OrderEngine.Evaluate(split, []);
        Assert.Equal(["tv-10-s1", "tv-10-s2", "movie-1", "tv-11"], orders["timeline"]);
        Assert.Equal(["movie-1", "tv-10-s1", "tv-11", "tv-10-s2"], orders["release"]);
    }

    [Fact]
    public void DropUnownedRemovesOnlyMatchingEntries()
    {
        var universe = new UniverseDefinition
        {
            Id = "u",
            Name = "U",
            Entries = [Movie(1, "2001-01-01"), Movie(2, "2002-01-01")],
            Orders = [new OrderDefinition { Id = "t", Title = "T", Items = ["movie-1", "movie-2"] }]
        };

        var result = UniverseComposer.DropUnowned(universe, e => e.Tmdb == 1, _ => true);

        Assert.Equal(["movie-1"], result.Orders[0].Items);
        Assert.Single(result.Entries);
    }

    [Theory]
    [InlineData("Star Wars: The Clone Wars", "star-wars-the-clone-wars")]
    [InlineData("Marvel's Agents of S.H.I.E.L.D.", "marvels-agents-of-s-h-i-e-l-d")]
    [InlineData("Pokémon — The Movie", "pokemon-the-movie")]
    public void SlugIsUrlSafe(string text, string expected)
    {
        Assert.Equal(expected, Slug.From(text));
    }

    [Theory]
    [InlineData("2026.10.07", "2026.10.06", 1)]
    [InlineData("2026.10.07", "2026.10.07.1", -1)]
    [InlineData("2026.10.07", "2026.10.07", 0)]
    public void RevisionsCompareNumerically(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(RegistryClient.CompareRevisions(left, right)));
    }
}
