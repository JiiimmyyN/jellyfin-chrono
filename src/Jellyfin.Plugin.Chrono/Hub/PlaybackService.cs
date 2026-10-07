using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Chrono.Hub;

public sealed class PlaybackService
{
    public const int MaxQueueLength = 250;

    private readonly UniverseService _universes;
    private readonly HubService _hub;
    private readonly ISessionManager _sessionManager;

    public PlaybackService(UniverseService universes, HubService hub, ISessionManager sessionManager)
    {
        _universes = universes;
        _hub = hub;
        _sessionManager = sessionManager;
    }

    public static IReadOnlyList<Guid> BuildQueue(
        IReadOnlyList<string> rowEntryIds,
        IReadOnlyDictionary<string, LibraryMatch> matches,
        string? fromEntryId,
        IReadOnlySet<Guid> playedEpisodes,
        int maxLength)
    {
        var start = fromEntryId is null ? 0 : Math.Max(0, rowEntryIds.ToList().IndexOf(fromEntryId));
        var queue = new List<Guid>();
        var seen = new HashSet<Guid>();
        for (var i = start; i < rowEntryIds.Count && queue.Count < maxLength; i++)
        {
            if (!matches.TryGetValue(rowEntryIds[i], out var match))
            {
                continue;
            }

            IEnumerable<Guid> items;
            if (match.Episodes.Count > 0)
            {
                var episodes = match.Episodes.Select(e => e.Id).ToList();
                if (i == start && fromEntryId is not null)
                {
                    var watched = episodes.Count(playedEpisodes.Contains);
                    if (watched > 0 && watched < episodes.Count)
                    {
                        var firstUnplayed = episodes.FindIndex(e => !playedEpisodes.Contains(e));
                        episodes = episodes.Skip(firstUnplayed).ToList();
                    }
                }

                items = episodes;
            }
            else if (match.IsPlayable)
            {
                items = [match.Item.Id];
            }
            else
            {
                continue;
            }

            foreach (var id in items)
            {
                if (queue.Count >= maxLength)
                {
                    break;
                }

                if (seen.Add(id))
                {
                    queue.Add(id);
                }
            }
        }

        return queue;
    }

    public static (IReadOnlyList<string> Entries, string? FromEntryId) ApplyFilters(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, Definitions.EntryDefinition> entries,
        IReadOnlyCollection<string> excludeFlags,
        string? fromEntryId)
    {
        var start = fromEntryId is null ? 0 : row.ToList().IndexOf(fromEntryId);
        if (start < 0)
        {
            start = 0;
            fromEntryId = null;
        }

        bool Included(string id) => excludeFlags.Count == 0 || !entries[id].Flags.Any(excludeFlags.Contains);
        var filtered = row.Where(Included).ToList();
        if (fromEntryId is not null && !Included(fromEntryId))
        {
            fromEntryId = row.Skip(start).FirstOrDefault(Included);
            if (fromEntryId is null)
            {
                return ([], null);
            }
        }

        return (filtered, fromEntryId);
    }

    public async Task<PlayOutcome> PlayAsync(string universeId, PlayRequestDto request, User user, string? deviceId, CancellationToken cancellationToken)
    {
        if (!_universes.Universes.TryGetValue(universeId, out var universe) || !universe.Orders.TryGetValue(request.RowId, out var row))
        {
            return PlayOutcome.NotFound;
        }

        var session = _sessionManager.Sessions
            .Where(s => s.UserId.Equals(user.Id) && string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal))
            .OrderByDescending(s => s.LastActivityDate)
            .FirstOrDefault();
        if (session is null)
        {
            return PlayOutcome.NoSession;
        }

        var visible = _hub.VisibleMatches(universe, user);
        var played = _hub.PlayedEpisodeIds(visible.Values, user);
        var (entries, fromEntryId) = ApplyFilters(row, universe.Entries, request.ExcludeFlags, request.FromEntryId);
        var queue = BuildQueue(entries, visible, fromEntryId, played, MaxQueueLength);
        if (queue.Count == 0)
        {
            return PlayOutcome.NothingToPlay;
        }

        await _sessionManager.SendPlayCommand(
            session.Id,
            session.Id,
            new PlayRequest { ItemIds = [.. queue], PlayCommand = PlayCommand.PlayNow, ControllingUserId = user.Id },
            cancellationToken).ConfigureAwait(false);
        return PlayOutcome.Started;
    }
}

public enum PlayOutcome
{
    Started,
    NotFound,
    NoSession,
    NothingToPlay
}
