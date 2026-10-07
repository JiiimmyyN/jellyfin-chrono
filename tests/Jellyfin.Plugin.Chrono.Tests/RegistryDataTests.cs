using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Sources;

namespace Jellyfin.Plugin.Chrono.Tests;

public class RegistryDataTests
{
    public static TheoryData<string> UniverseFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TestPaths.RegistryRoot, "universes"), "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UniverseFiles))]
    public void UniverseFileIsValid(string file)
    {
        var universe = DefinitionJson.ParseUniverse(File.ReadAllText(Path.Combine(TestPaths.RegistryRoot, "universes", file)));

        Assert.Empty(DefinitionValidator.Validate(universe));
        Assert.All(universe.Entries, e => Assert.NotNull(e.ReleaseDate));
    }

    [Theory]
    [MemberData(nameof(UniverseFiles))]
    public void EveryHubRowHasEntries(string file)
    {
        var universe = DefinitionJson.ParseUniverse(File.ReadAllText(Path.Combine(TestPaths.RegistryRoot, "universes", file)));
        var orders = OrderEngine.Evaluate(universe, []);
        var (rows, pages) = UniverseComposer.HubLayout(universe);

        Assert.NotEmpty(rows);
        Assert.All(rows.Concat(pages.SelectMany(p => p.Rows)), r => Assert.NotEmpty(orders[r]));
    }

    [Fact]
    public void IndexListsEveryUniverseFile()
    {
        var index = DefinitionJson.ParseIndex(File.ReadAllText(Path.Combine(TestPaths.RegistryRoot, "index.json")));
        var files = Directory.EnumerateFiles(Path.Combine(TestPaths.RegistryRoot, "universes"), "*.json")
            .Select(f => "universes/" + Path.GetFileName(f))
            .Order()
            .ToList();

        Assert.Equal(files, index.Universes.Select(u => u.File).Order().ToList());
        foreach (var entry in index.Universes)
        {
            var universe = DefinitionJson.ParseUniverse(File.ReadAllText(Path.Combine(TestPaths.RegistryRoot, entry.File)));
            Assert.Equal(entry.Id, universe.Id);
        }
    }

    [Fact]
    public void BundledRegistryIsEmbedded()
    {
        var bundled = RegistryClient.LoadBundled();

        Assert.Contains(bundled, u => u.Definition.Id == "mcu");
    }

    [Fact]
    public void McuTimelineContainsOfficialTimelineInSameRelativeOrder()
    {
        var universe = DefinitionJson.ParseUniverse(File.ReadAllText(Path.Combine(TestPaths.RegistryRoot, "universes", "mcu.json")));
        var orders = OrderEngine.Evaluate(universe, []);
        var official = orders["official-timeline"];
        var timeline = orders["timeline"];

        Assert.Equal(official, timeline.Where(official.Contains).ToList());
    }
}
