using System.Text.Json;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Seerr;
using Jellyfin.Plugin.Chrono.Sources;
using Jellyfin.Plugin.Chrono.Web;

namespace Jellyfin.Plugin.Chrono.Tests;

public class ParserTests
{
    [Fact]
    public void MdbListItemsAreRankedAndTyped()
    {
        const string json = """
            [
              {"id": 1726, "rank": 2, "title": "Iron Man", "imdb_id": "tt0371746", "mediatype": "movie", "release_year": 2008},
              {"id": 84958, "rank": 1, "title": "Loki", "imdb_id": "tt9140554", "tvdbid": 362472, "mediatype": "show", "release_year": 2021},
              {"id": 0, "rank": 3, "title": "Broken", "mediatype": "movie"}
            ]
            """;

        var result = MdbListClient.Parse(json);

        Assert.Equal(["tv-84958", "movie-1726"], result.RankedEntryIds);
        var loki = result.Entries.Single(e => e.Id == "tv-84958");
        Assert.Equal(EntryType.Series, loki.Type);
        Assert.Equal(362472, loki.Tvdb);
        Assert.Equal("2021-01-01", loki.Released);
    }

    [Fact]
    public void TraktSeasonsAndEpisodesBecomeSeasonEntries()
    {
        const string json = """
            [
              {"rank": 3, "type": "episode", "show": {"title": "Loki", "ids": {"tmdb": 84958, "tvdb": 362472, "imdb": "tt9140554"}}, "episode": {"season": 1, "number": 2}},
              {"rank": 1, "type": "movie", "movie": {"title": "Iron Man", "released": "2008-05-02", "ids": {"tmdb": 1726, "imdb": "tt0371746"}}},
              {"rank": 2, "type": "season", "show": {"title": "Loki", "first_aired": "2021-06-09T00:00:00.000Z", "ids": {"tmdb": 84958}}, "season": {"number": 1, "first_aired": "2021-06-09T01:00:00.000Z"}},
              {"rank": 4, "type": "person", "person": {"name": "Someone"}}
            ]
            """;

        var result = TraktClient.Parse(json);

        Assert.Equal(["movie-1726", "tv-84958-s1"], result.RankedEntryIds);
        var season = result.Entries.Single(e => e.Type == EntryType.Season);
        Assert.Equal(1, season.Season);
        Assert.Equal("2021-06-09", season.Released);
    }

    [Fact]
    public void WikidataFranchiseResultsBecomeEntriesAndGroups()
    {
        const string json = """
            {"results": {"bindings": [
              {"item": {"value": "http://www.wikidata.org/entity/Q17738"}, "itemLabel": {"value": "A New Hope"}, "tmdbMovie": {"value": "11"}, "imdb": {"value": "tt0076759"}, "date": {"value": "1977-10-19T00:00:00Z"}, "series": {"value": "http://www.wikidata.org/entity/Q1"}, "seriesLabel": {"value": "Original trilogy"}},
              {"item": {"value": "http://www.wikidata.org/entity/Q17738"}, "itemLabel": {"value": "A New Hope"}, "tmdbMovie": {"value": "11"}, "date": {"value": "1977-05-25T00:00:00Z"}},
              {"item": {"value": "http://www.wikidata.org/entity/Q181795"}, "itemLabel": {"value": "The Empire Strikes Back"}, "tmdbMovie": {"value": "1891"}, "date": {"value": "1980-05-17T00:00:00Z"}, "series": {"value": "http://www.wikidata.org/entity/Q1"}, "seriesLabel": {"value": "Original trilogy"}},
              {"item": {"value": "http://www.wikidata.org/entity/Q1"}, "itemLabel": {"value": "Q999"}, "tmdbMovie": {"value": "5"}},
              {"item": {"value": "http://www.wikidata.org/entity/Q3"}, "itemLabel": {"value": "The Mandalorian"}, "tmdbTv": {"value": "82856"}, "tvdb": {"value": "361753"}, "start": {"value": "2019-11-12T00:00:00Z"}, "series": {"value": "http://www.wikidata.org/entity/Q462"}}
            ]}}
            """;

        var result = WikidataClient.ParseFranchise(json, ["Q462"]);

        Assert.Equal(["movie-11", "movie-1891", "tv-82856"], result.Entries.Select(e => e.Id).ToList());
        Assert.Equal("1977-05-25", result.Entries[0].Released);
        Assert.Equal(EntryType.Series, result.Entries[2].Type);
        var group = Assert.Single(result.Groups);
        Assert.Equal("original-trilogy", group.Id);
        Assert.Equal(["original-trilogy"], result.Entries[0].Groups);
    }

    [Fact]
    public void SeerrMediaStatusIsMappedPerSeason()
    {
        const string json = """
            {"name": "Loki", "posterPath": "/show.jpg", "overview": "Trickster",
             "seasons": [{"seasonNumber": 1, "posterPath": "/s1.jpg"}, {"seasonNumber": 2, "posterPath": "/s2.jpg"}],
             "mediaInfo": {"status": 4, "seasons": [{"seasonNumber": 1, "status": 5}],
               "requests": [{"status": 1, "seasons": [{"seasonNumber": 2}]}]}}
            """;
        using var document = JsonDocument.Parse(json);

        var media = SeerrClient.ParseMedia(document.RootElement);

        Assert.Equal(RequestStatus.Partial, media.Status);
        Assert.Equal(RequestStatus.Available, media.Seasons[1]);
        Assert.Equal(RequestStatus.Pending, media.Seasons[2]);
        Assert.Equal("/s2.jpg", media.SeasonPosters[2]);
        var season3 = new EntryDefinition { Type = EntryType.Season, Season = 3, Tmdb = 1 };
        Assert.Equal(RequestStatus.None, SeerrClient.StatusFor(season3, media));
    }

    [Fact]
    public void ScriptIsInjectedOnce()
    {
        const string html = "<html><body><div id=\"app\"></div></body></html>";

        var once = WebInjectionStartupFilter.InjectScript(html, "1.0");
        var twice = WebInjectionStartupFilter.InjectScript(once, "1.0");

        Assert.Contains("../Chrono/Client/chrono.js?v=1.0", once, StringComparison.Ordinal);
        Assert.Equal(once, twice);
        Assert.EndsWith("</body></html>", once, StringComparison.Ordinal);
    }

    [Fact]
    public void MenuLinkIsAddedOnce()
    {
        const string json = """{"menuLinks": [{"name": "Docs", "url": "https://jellyfin.org"}], "themes": []}""";

        var once = WebInjectionStartupFilter.InjectMenuLink(json);
        var twice = WebInjectionStartupFilter.InjectMenuLink(once);

        using var document = JsonDocument.Parse(twice);
        var links = document.RootElement.GetProperty("menuLinks").EnumerateArray().ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal("#/chrono", links[0].GetProperty("url").GetString());
    }

    [Fact]
    public void MenuLinksArrayIsCreatedWhenMissing()
    {
        using var document = JsonDocument.Parse(WebInjectionStartupFilter.InjectMenuLink("{\"themes\": []}"));

        Assert.Single(document.RootElement.GetProperty("menuLinks").EnumerateArray());
    }
}
