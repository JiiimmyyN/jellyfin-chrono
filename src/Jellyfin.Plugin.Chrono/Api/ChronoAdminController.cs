using System.Globalization;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Configuration;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Library;
using Jellyfin.Plugin.Chrono.Seerr;
using Jellyfin.Plugin.Chrono.Services;
using Jellyfin.Plugin.Chrono.Sources;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Chrono.Api;

[ApiController]
[Route("Chrono/Admin")]
[Authorize(Policy = "RequiresElevation")]
public sealed class ChronoAdminController : ControllerBase
{
    private readonly UniverseService _universes;
    private readonly RefreshCoordinator _coordinator;
    private readonly WikidataClient _wikidata;
    private readonly SeerrClient _seerr;
    private readonly LibraryResolver _resolver;

    public ChronoAdminController(UniverseService universes, RefreshCoordinator coordinator, WikidataClient wikidata, SeerrClient seerr, LibraryResolver resolver)
    {
        _universes = universes;
        _coordinator = coordinator;
        _wikidata = wikidata;
        _seerr = seerr;
        _resolver = resolver;
    }

    [HttpGet("Status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        if (!_universes.HasLoaded)
        {
            await _universes.RefreshAsync(false, cancellationToken).ConfigureAwait(false);
        }

        var universes = _universes.Universes.Values.Select(u =>
        {
            var shown = u.Rows.Concat(u.Pages.SelectMany(p => p.Rows)).SelectMany(r => u.Orders.GetValueOrDefault(r) ?? []).ToHashSet(StringComparer.Ordinal);
            var missing = u.Definition.Entries
                .Where(e => shown.Contains(e.Id) && !u.Matches.ContainsKey(e.Id))
                .Select(e => new { e.Id, e.Title, Season = e.Season, e.Released, e.Tmdb, Type = e.Type.ToString().ToLowerInvariant() })
                .ToList();
            return new
            {
                u.Definition.Id,
                u.Definition.Name,
                u.Definition.Revision,
                u.Origin,
                EntryCount = shown.Count,
                OwnedCount = shown.Count(u.Matches.ContainsKey),
                Rows = u.Rows.Concat(u.Pages.SelectMany(p => p.Rows)).Distinct().Select(r => new { Id = r, Title = u.Order(r)?.Title ?? r, Count = u.Orders.GetValueOrDefault(r)?.Count ?? 0 }),
                Flags = u.Definition.Flags.Select(f => new { Id = f.Key, Label = f.Value }),
                u.PrimaryRowId,
                u.Warnings,
                Missing = missing,
                u.BuiltAt
            };
        });

        return ChronoController.JsonContent(new
        {
            Universes = universes,
            Errors = _universes.Errors,
            Available = _universes.Available,
            _coordinator.LastRefresh,
            _coordinator.LastError
        });
    }

    [HttpPost("Refresh")]
    public async Task<IActionResult> Refresh([FromQuery] bool force = true, CancellationToken cancellationToken = default)
    {
        await _coordinator.RunAsync(force, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("Available")]
    public async Task<IActionResult> GetAvailable(CancellationToken cancellationToken)
    {
        if (!_universes.HasLoaded)
        {
            await _universes.RefreshAsync(false, cancellationToken).ConfigureAwait(false);
        }

        return ChronoController.JsonContent(_universes.Available);
    }

    [HttpGet("Wikidata/Search")]
    public async Task<IActionResult> SearchWikidata([FromQuery] string q, CancellationToken cancellationToken)
    {
        return ChronoController.JsonContent(await _wikidata.SearchAsync(q, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("Suggestions")]
    public async Task<IActionResult> GetSuggestions(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var configuredAnchors = config.CustomUniverses
            .SelectMany(c => c.Sources)
            .Where(s => s.Type == SourceType.Wikidata)
            .SelectMany(s => s.Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        var configuredCollections = config.CustomUniverses
            .SelectMany(c => c.Sources)
            .Where(s => s.Type == SourceType.TmdbCollection)
            .Select(s => s.Value.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (!_universes.HasLoaded)
        {
            await _universes.RefreshAsync(false, cancellationToken).ConfigureAwait(false);
        }

        foreach (var universe in _universes.Universes.Values)
        {
            configuredAnchors.UnionWith(universe.Definition.Discover?.Wikidata ?? []);
            configuredCollections.UnionWith((universe.Definition.Discover?.TmdbCollections ?? []).Select(c => c.ToString(CultureInfo.InvariantCulture)));
        }

        var covered = _universes.Universes.Values
            .SelectMany(u => u.Matches.Values)
            .SelectMany(m => m.Series is null ? [m.Item.Id] : new[] { m.Item.Id, m.Series.Id })
            .ToHashSet();
        var movies = _resolver.AllWithTmdb(BaseItemKind.Movie).Where(m => !covered.Contains(m.Id)).ToList();
        var series = _resolver.AllWithTmdb(BaseItemKind.Series).Where(s => !covered.Contains(s.Id)).ToList();

        var collections = movies.OfType<Movie>()
            .Select(m => (Id: m.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.TmdbCollection), Name: m.CollectionName))
            .Where(c => !string.IsNullOrEmpty(c.Id) && !configuredCollections.Contains(c.Id!))
            .GroupBy(c => c.Id!)
            .Where(g => g.Count() >= 2)
            .Select(g => new Suggestion("tmdbCollection", g.Key, g.Select(c => c.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "TMDB collection " + g.Key, g.Count()))
            .ToList();

        var franchises = new List<Suggestion>();
        try
        {
            var movieIds = movies.Select(m => ParseId(m.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tmdb))).OfType<int>().ToList();
            var seriesIds = series.Select(s => ParseId(s.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tmdb))).OfType<int>().ToList();
            franchises = (await _wikidata.SuggestFranchisesAsync(movieIds, seriesIds, cancellationToken).ConfigureAwait(false))
                .Where(f => f.OwnedCount >= 3 && !configuredAnchors.Contains(f.Id))
                .Take(25)
                .Select(f => new Suggestion("wikidata", f.Id, f.Label, f.OwnedCount))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        return ChronoController.JsonContent(franchises.Concat(collections.OrderByDescending(c => c.OwnedCount)).ToList());
    }

    [HttpPost("Seerr/Test")]
    public async Task<IActionResult> TestSeerr(CancellationToken cancellationToken)
    {
        var body = await JsonSerializer.DeserializeAsync<SeerrTestRequest>(Request.Body, DefinitionJson.Options, cancellationToken).ConfigureAwait(false);
        if (body is null || string.IsNullOrWhiteSpace(body.Url) || string.IsNullOrWhiteSpace(body.ApiKey))
        {
            return ChronoController.JsonContent(new { Ok = false, Message = "URL and API key are required." });
        }

        var (ok, message) = await _seerr.TestAsync(body.Url, body.ApiKey, cancellationToken).ConfigureAwait(false);
        return ChronoController.JsonContent(new { Ok = ok, Message = message });
    }

    private static int? ParseId(string? value) => int.TryParse(value, CultureInfo.InvariantCulture, out var id) ? id : null;

    private sealed record Suggestion(string Type, string Value, string Name, int OwnedCount);

    private sealed record SeerrTestRequest(string Url, string ApiKey);
}
