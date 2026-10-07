using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Definitions;

namespace Jellyfin.Plugin.Chrono.Tests;

public class OrderEngineTests
{
    private static UniverseDefinition Sample() => new()
    {
        Id = "test",
        Name = "Test",
        Flags = new() { ["tv"] = "TV", ["special"] = "Special" },
        Groups =
        [
            new GroupDefinition { Id = "saga", Title = "Saga" },
            new GroupDefinition { Id = "phase-1", Title = "Phase 1", Parent = "saga" },
            new GroupDefinition { Id = "phase-2", Title = "Phase 2", Parent = "saga" }
        ],
        Entries =
        [
            new EntryDefinition { Id = "b", Type = EntryType.Movie, Title = "B", Tmdb = 2, Released = "2010-01-01", Groups = ["phase-1"] },
            new EntryDefinition { Id = "a", Type = EntryType.Movie, Title = "A", Tmdb = 1, Released = "2008-01-01", Groups = ["phase-1"] },
            new EntryDefinition { Id = "s1", Type = EntryType.Season, Title = "Show", Tmdb = 9, Season = 1, Released = "2012-01-01", Groups = ["phase-2"], Flags = ["tv"] },
            new EntryDefinition { Id = "x", Type = EntryType.Movie, Title = "X", Tmdb = 3, Released = "2012-01-01", Flags = ["special"] }
        ],
        Orders =
        [
            new OrderDefinition { Id = "timeline", Title = "Timeline", Items = ["s1", "b", "a", "x"] },
            new OrderDefinition { Id = "release", Title = "Release", Derive = new DeriveDefinition { SortBy = OrderSort.Released } },
            new OrderDefinition { Id = "movies", Title = "Movies", Derive = new DeriveDefinition { From = "timeline", Types = [EntryType.Movie], ExcludeFlags = ["special"] } },
            new OrderDefinition { Id = "saga", Title = "Saga", Derive = new DeriveDefinition { Groups = ["saga"], SortBy = OrderSort.Released } },
            new OrderDefinition { Id = "tv", Title = "TV", Derive = new DeriveDefinition { Flags = ["tv"] } }
        ]
    };

    [Fact]
    public void ExplicitOrderIsKept()
    {
        Assert.Equal(["s1", "b", "a", "x"], OrderEngine.Evaluate(Sample(), [])["timeline"]);
    }

    [Fact]
    public void ReleaseSortIsStableForTies()
    {
        Assert.Equal(["a", "b", "s1", "x"], OrderEngine.Evaluate(Sample(), [])["release"]);
    }

    [Fact]
    public void DeriveFromAnotherOrderKeepsItsOrder()
    {
        Assert.Equal(["b", "a"], OrderEngine.Evaluate(Sample(), [])["movies"]);
    }

    [Fact]
    public void GroupFilterIncludesChildGroups()
    {
        Assert.Equal(["a", "b", "s1"], OrderEngine.Evaluate(Sample(), [])["saga"]);
    }

    [Fact]
    public void FlagFilterSelectsFlaggedEntries()
    {
        Assert.Equal(["s1"], OrderEngine.Evaluate(Sample(), [])["tv"]);
    }

    [Fact]
    public void HiddenFlagsAreRemovedEverywhere()
    {
        var orders = OrderEngine.Evaluate(Sample(), ["tv"]);

        Assert.Equal(["b", "a", "x"], orders["timeline"]);
        Assert.Empty(orders["tv"]);
    }

    [Fact]
    public void ValidatorAcceptsSample()
    {
        Assert.Empty(DefinitionValidator.Validate(Sample()));
    }

    [Fact]
    public void ValidatorReportsBrokenReferences()
    {
        var universe = Sample();
        universe.Orders.Add(new OrderDefinition { Id = "broken", Title = "Broken", Items = ["missing", "a", "a"] });
        universe.Entries.Add(new EntryDefinition { Id = "a", Type = EntryType.Season, Title = "Dup", Tmdb = 5 });

        var errors = DefinitionValidator.Validate(universe);

        Assert.Contains(errors, e => e.Contains("unknown entry 'missing'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("more than once", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Duplicate entry id 'a'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("no season number", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorDetectsDeriveCycles()
    {
        var universe = Sample();
        universe.Orders.Add(new OrderDefinition { Id = "c1", Title = "C1", Derive = new DeriveDefinition { From = "c2" } });
        universe.Orders.Add(new OrderDefinition { Id = "c2", Title = "C2", Derive = new DeriveDefinition { From = "c1" } });

        Assert.Contains(DefinitionValidator.Validate(universe), e => e.Contains("cycle", StringComparison.Ordinal));
    }
}
