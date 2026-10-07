using Jellyfin.Plugin.Chrono.Hub;
using Jellyfin.Plugin.Chrono.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Chrono.Tests;

public class PlaybackQueueTests
{
    private readonly Movie _movieA = new() { Id = Guid.NewGuid() };
    private readonly Movie _movieB = new() { Id = Guid.NewGuid() };
    private readonly Episode[] _episodes = Enumerable.Range(1, 4).Select(_ => new Episode { Id = Guid.NewGuid() }).ToArray();

    private Dictionary<string, LibraryMatch> Matches()
    {
        var season = new Season { Id = Guid.NewGuid() };
        return new Dictionary<string, LibraryMatch>
        {
            ["a"] = new() { Item = _movieA },
            ["s1"] = new() { Item = season, Episodes = _episodes },
            ["b"] = new() { Item = _movieB }
        };
    }

    [Fact]
    public void QueueExpandsSeasonsInOrderAndSkipsMissingEntries()
    {
        var queue = PlaybackService.BuildQueue(["a", "missing", "s1", "b"], Matches(), null, new HashSet<Guid>(), 100);

        Assert.Equal([_movieA.Id, .. _episodes.Select(e => e.Id), _movieB.Id], queue);
    }

    [Fact]
    public void ContinuingInsideAPartlyWatchedSeasonStartsAtFirstUnwatchedEpisode()
    {
        var played = new HashSet<Guid> { _episodes[0].Id, _episodes[1].Id };

        var queue = PlaybackService.BuildQueue(["a", "s1", "b"], Matches(), "s1", played, 100);

        Assert.Equal([_episodes[2].Id, _episodes[3].Id, _movieB.Id], queue);
    }

    [Fact]
    public void FullyWatchedStartingSeasonIsReplayedFromTheStart()
    {
        var played = _episodes.Select(e => e.Id).ToHashSet();

        var queue = PlaybackService.BuildQueue(["s1", "b"], Matches(), "s1", played, 100);

        Assert.Equal(5, queue.Count);
    }

    [Fact]
    public void QueueIsCapped()
    {
        Assert.Equal(3, PlaybackService.BuildQueue(["a", "s1", "b"], Matches(), null, new HashSet<Guid>(), 3).Count);
    }
}

public class PlayFilterTests
{
    private static readonly Dictionary<string, Jellyfin.Plugin.Chrono.Definitions.EntryDefinition> Entries = new()
    {
        ["a"] = new() { Id = "a" },
        ["tv"] = new() { Id = "tv", Flags = ["network"] },
        ["b"] = new() { Id = "b" }
    };

    [Fact]
    public void HiddenStartEntryMovesToNextVisibleEntry()
    {
        var (entries, from) = PlaybackService.ApplyFilters(["a", "tv", "b"], Entries, ["network"], "tv");

        Assert.Equal(["a", "b"], entries);
        Assert.Equal("b", from);
    }

    [Fact]
    public void UnknownStartEntryPlaysFromTheBeginning()
    {
        var (entries, from) = PlaybackService.ApplyFilters(["a", "tv", "b"], Entries, [], "zzz");

        Assert.Equal(3, entries.Count);
        Assert.Null(from);
    }
}
