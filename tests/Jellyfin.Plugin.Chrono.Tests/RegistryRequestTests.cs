using Chrono.RegistryTool.Metadata;
using Chrono.RegistryTool.Requests;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Sources;

namespace Jellyfin.Plugin.Chrono.Tests;

public sealed class RegistryRequestTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private readonly string _registry;

    public RegistryRequestTests()
    {
        _registry = Path.Combine(Path.GetTempPath(), "chrono-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_registry, "universes"));
        File.Copy(Path.Combine(TestPaths.RegistryRoot, "index.json"), Path.Combine(_registry, "index.json"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TestPaths.RegistryRoot, "universes")))
        {
            File.Copy(file, Path.Combine(_registry, "universes", Path.GetFileName(file)));
        }
    }

    public void Dispose() => Directory.Delete(_registry, true);

    [Fact]
    public void IssueFormsAreParsed()
    {
        var form = IssueForm.Parse("### Universe\n\nmcu\n\n### Groups\n\n_No response_\n\n### Options\n\n- [X] Remove it from the universe if it is already listed\n- [ ] Something else\n");

        Assert.Equal("mcu", form.Required("Universe"));
        Assert.Equal(string.Empty, form.Optional("Groups"));
        Assert.True(form.Checked("Options", "Remove it from the universe"));
        Assert.False(form.Checked("Options", "Something else"));
        Assert.Throws<RequestException>(() => form.Required("Groups"));
    }

    [Fact]
    public void CodeFencedFieldsAreUnwrapped()
    {
        var form = IssueForm.Parse("### Placement\n\n```text\ntimeline after loki-s1\nofficial-timeline last\n```\n\n### Note\n\nx\n");

        Assert.Equal("timeline after loki-s1\nofficial-timeline last", form.Required("Placement"));
        Assert.Equal(2, Placement.ParseAll(form.Required("Placement")).Count);
    }

    [Theory]
    [InlineData("movie:1726", true, 1726, null)]
    [InlineData("tv:84958", false, 84958, null)]
    [InlineData("tv:84958:s2", false, 84958, new[] { 2 })]
    [InlineData("tv:1403:s1-3,s5", false, 1403, new[] { 1, 2, 3, 5 })]
    public void TitleReferencesAreParsed(string text, bool isMovie, int tmdb, int[]? seasons)
    {
        var reference = TitleReference.Parse(text);

        Assert.Equal(isMovie, reference.IsMovie);
        Assert.Equal(tmdb, reference.Tmdb);
        Assert.Equal(seasons, reference.Seasons);
    }

    [Fact]
    public async Task AddMovieIsPlacedInExplicitOrders()
    {
        var editor = Editor();
        var form = IssueForm.Parse("""
            ### Universe

            mcu

            ### Title type

            Movie

            ### TMDB ID

            https://www.themoviedb.org/movie/900001-blade

            ### Placement

            timeline after loki-s1
            official-timeline last

            ### Groups

            phase-6

            ### Flags

            _No response_
            """);

        await editor.ApplyAsync(RegistryEditor.AddTitle, form);
        var files = editor.Save();

        Assert.Equal(["universes/mcu.json"], files);
        var universe = Load("mcu");
        var entry = Assert.Single(universe.Entries, e => e.Tmdb == 900001);
        Assert.Equal("blade", entry.Id);
        Assert.Equal("2027-11-03", entry.Released);
        Assert.Equal(["phase-6"], entry.Groups);
        var orders = OrderEngine.Evaluate(universe, []);
        var timeline = orders["timeline"].ToList();
        Assert.Equal(timeline.IndexOf("loki-s1") + 1, timeline.IndexOf("blade"));
        Assert.Equal("blade", orders["official-timeline"][^1]);
        Assert.Contains("blade", orders["phase-6"]);
        Assert.Equal("2026.10.07.1", universe.Revision);
    }

    [Fact]
    public async Task AddSeasonsCreatesOneEntryPerSeasonInOrder()
    {
        var editor = Editor();
        await editor.ApplyAsync(RegistryEditor.AddTitle, IssueForm.Parse("""
            ### Universe

            star-wars

            ### Title type

            Seasons of a series

            ### TMDB ID

            900002

            ### Seasons

            1-2

            ### Placement

            timeline before a-new-hope
            """));
        editor.Save();

        var universe = Load("star-wars");
        var timeline = OrderEngine.Evaluate(universe, [])["timeline"].ToList();
        var newHope = timeline.IndexOf("a-new-hope");
        Assert.Equal(["test-show-s1", "test-show-s2"], timeline.Skip(newHope - 2).Take(2));
    }

    [Fact]
    public async Task MoveRepositionsExistingEntries()
    {
        var editor = Editor();
        await editor.ApplyAsync(RegistryEditor.MoveTitle, IssueForm.Parse("""
            ### Universe

            mcu

            ### Entry IDs

            captain-marvel

            ### Placement

            timeline first
            """));
        editor.Save();

        Assert.Equal("captain-marvel", OrderEngine.Evaluate(Load("mcu"), [])["timeline"][0]);
    }

    [Fact]
    public async Task PlacingIntoDerivedOrdersIsRejected()
    {
        var editor = Editor();

        var error = await Assert.ThrowsAsync<RequestException>(() => editor.ApplyAsync(RegistryEditor.MoveTitle, IssueForm.Parse("### Universe\n\nmcu\n\n### Entry IDs\n\niron-man\n\n### Placement\n\nrelease first\n")));
        Assert.Contains("derived", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExcludeCanRemoveCuratedEntries()
    {
        var editor = Editor();
        await editor.ApplyAsync(RegistryEditor.ExcludeTitle, IssueForm.Parse("""
            ### Universe

            mcu

            ### Title

            movie:1726

            ### Options

            - [x] Remove it from the universe if it is already listed
            """));
        editor.Save();

        var universe = Load("mcu");
        Assert.DoesNotContain(universe.Entries, e => e.Id == "iron-man");
        Assert.Contains("movie:1726", universe.Discover!.Exclude);
        Assert.DoesNotContain("iron-man", OrderEngine.Evaluate(universe, [])["timeline"]);
    }

    [Fact]
    public async Task NewUniverseCombinesTimelineAndWikidata()
    {
        var franchise = new SourceResult();
        franchise.Entries.Add(new EntryDefinition { Id = "movie-900001", Type = EntryType.Movie, Tmdb = 900001, Title = "Blade", Released = "2027-11-03", Groups = ["trilogy"] });
        franchise.Entries.Add(new EntryDefinition { Id = "movie-900003", Type = EntryType.Movie, Tmdb = 900003, Title = "Prequel", Released = "2001-01-01", Groups = ["trilogy"] });
        franchise.Groups.Add(new GroupDefinition { Id = "trilogy", Title = "The Trilogy" });
        var editor = Editor(franchise);
        await editor.ApplyAsync(RegistryEditor.NewUniverse, IssueForm.Parse("""
            ### Universe ID

            test-verse

            ### Name

            Test Verse

            ### Wikidata franchise

            Q123

            ### Timeline

            movie:900003
            tv:900002:s2

            ### Options

            - [x] Add every other title Wikidata links to the franchise
            """));
        var files = editor.Save();

        Assert.Contains("index.json", files);
        var index = DefinitionJson.ParseIndex(File.ReadAllText(Path.Combine(_registry, "index.json")));
        Assert.Contains(index.Universes, u => u.Id == "test-verse" && u.File == "universes/test-verse.json");
        var universe = Load("test-verse");
        Assert.Empty(DefinitionValidator.Validate(universe));
        var orders = OrderEngine.Evaluate(universe, []);
        Assert.Equal(["prequel", "test-show-s2"], orders["timeline"]);
        Assert.Equal(["prequel", "blade"], orders["trilogy"]);
        Assert.Equal(["prequel", "test-show-s2", "blade"], orders["release"]);
        Assert.Equal(["Q123"], universe.Discover!.Wikidata);
    }

    [Fact]
    public void TmdbSeriesResponsesAreParsed()
    {
        const string json = """
            {"name": "Loki", "first_air_date": "2021-06-09", "poster_path": "/loki.jpg",
             "external_ids": {"imdb_id": "tt9140554", "tvdb_id": 362472},
             "seasons": [
               {"season_number": 0, "air_date": "2021-01-01", "poster_path": "/specials.jpg"},
               {"season_number": 2, "air_date": "2023-10-05", "poster_path": "/s2.jpg"},
               {"season_number": 1, "air_date": "2021-06-09", "poster_path": null}
             ]}
            """;

        var info = MetadataResolver.ParseTmdb(false, json);

        Assert.Equal("Loki", info.Title);
        Assert.Equal("tt9140554", info.Imdb);
        Assert.Equal(362472, info.Tvdb);
        Assert.Equal([1, 2], info.Seasons.Select(s => s.Number));
        Assert.Equal("/s2.jpg", info.Seasons[1].Poster);
        Assert.Null(info.Seasons[0].Poster);
    }

    [Fact]
    public void RevisionsIncrementWithinADay()
    {
        var editor = Editor();

        Assert.Equal("2026.10.07", editor.NextRevision("2026.09.30"));
        Assert.Equal("2026.10.07.1", editor.NextRevision("2026.10.07"));
        Assert.Equal("2026.10.07.3", editor.NextRevision("2026.10.07.2"));
    }

    private RegistryEditor Editor(SourceResult? franchise = null)
        => new(_registry, new FakeMetadata(), _ => Task.FromResult(franchise), Today);

    private UniverseDefinition Load(string id)
        => DefinitionJson.ParseUniverse(File.ReadAllText(Path.Combine(_registry, "universes", id + ".json")));

    private sealed class FakeMetadata : IMetadataResolver
    {
        public Task<TitleInfo> MovieAsync(int tmdb) => Task.FromResult(tmdb switch
        {
            900001 => new TitleInfo("Blade", "2027-11-03", "tt0000001", null, "/blade.jpg", []),
            900003 => new TitleInfo("Prequel", "2001-01-01", null, null, null, []),
            _ => throw new RequestException("unknown movie")
        });

        public Task<TitleInfo> SeriesAsync(int tmdb) => Task.FromResult(new TitleInfo(
            "Test Show",
            "2020-01-01",
            "tt0000002",
            12345,
            null,
            [new SeasonInfo(1, "2020-01-01", "/s1.jpg"), new SeasonInfo(2, "2021-01-01", null), new SeasonInfo(3, null, null)]));
    }
}
