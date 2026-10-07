using System.Collections.Concurrent;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Seerr;

namespace Jellyfin.Plugin.Chrono.Hub;

public sealed class RequestService
{
    private readonly UniverseService _universes;
    private readonly HubService _hub;
    private readonly SeerrClient _seerr;

    public RequestService(UniverseService universes, HubService hub, SeerrClient seerr)
    {
        _universes = universes;
        _hub = hub;
        _seerr = seerr;
    }

    public static string ToApi(RequestStatus status) => status.ToString().ToLowerInvariant();

    public async Task<Dictionary<string, string>> GetStatusesAsync(string universeId, User user, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !SeerrClient.IsConfigured(config) || !_universes.Universes.TryGetValue(universeId, out var universe))
        {
            return [];
        }

        var visible = _hub.VisibleMatches(universe, user);
        var shown = universe.Rows.Concat(universe.Pages.SelectMany(p => p.Rows))
            .SelectMany(r => universe.Orders.GetValueOrDefault(r) ?? [])
            .Where(id => !visible.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => universe.Entries[id])
            .ToList();

        var statuses = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(
            shown,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (entry, ct) =>
            {
                var media = await _seerr.GetMediaAsync(config, entry.IsTv, entry.Tmdb, ct).ConfigureAwait(false);
                statuses[entry.Id] = ToApi(SeerrClient.StatusFor(entry, media));
            }).ConfigureAwait(false);
        return new Dictionary<string, string>(statuses, StringComparer.Ordinal);
    }

    public async Task<RequestOutcome> RequestAsync(string universeId, string entryId, User user, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !SeerrClient.IsConfigured(config) || !config.SeerrAllowRequests)
        {
            return new RequestOutcome(false, "Requests are not enabled.", null);
        }

        if (!_universes.Universes.TryGetValue(universeId, out var universe) || !universe.Entries.TryGetValue(entryId, out var entry))
        {
            return new RequestOutcome(false, "Unknown title.", null);
        }

        if (_hub.VisibleMatches(universe, user).ContainsKey(entryId))
        {
            return new RequestOutcome(false, "This title is already in the library.", null);
        }

        var status = await _seerr.RequestAsync(config, entry, user.Id, cancellationToken).ConfigureAwait(false);
        return new RequestOutcome(true, null, ToApi(status));
    }
}

public sealed record RequestOutcome(bool Ok, string? Error, string? Status);
