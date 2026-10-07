using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Library;
using Jellyfin.Plugin.Chrono.Seerr;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Chrono.Hub;

public sealed class HubService
{
    private const string TmdbPosterBase = "https://image.tmdb.org/t/p/w342";

    private readonly UniverseService _universes;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IImageProcessor _imageProcessor;

    public HubService(UniverseService universes, ILibraryManager libraryManager, IUserDataManager userDataManager, IImageProcessor imageProcessor)
    {
        _universes = universes;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _imageProcessor = imageProcessor;
    }

    public IReadOnlyList<UniverseSummaryDto> GetSummaries(User user)
    {
        return _universes.Universes.Values
            .OrderBy(u => u.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(universe =>
            {
                var visible = VisibleMatches(universe, user);
                var primary = universe.Orders.TryGetValue(universe.PrimaryRowId, out var ids) ? ids : [];
                var displayed = universe.Orders.Values.SelectMany(v => v).ToHashSet(StringComparer.Ordinal);
                var posters = primary
                    .OrderBy(id => visible.ContainsKey(id) ? 0 : 1)
                    .Select(id => PosterUrl(universe, universe.Entries[id], visible.GetValueOrDefault(id)))
                    .OfType<string>()
                    .Take(6)
                    .ToList();
                return new UniverseSummaryDto
                {
                    Id = universe.Definition.Id,
                    Name = universe.Definition.Name,
                    Description = universe.Definition.Description,
                    EntryCount = displayed.Count,
                    OwnedCount = displayed.Count(visible.ContainsKey),
                    PosterUrls = posters,
                    BackdropUrl = primary.Select(id => visible.GetValueOrDefault(id)).OfType<LibraryMatch>().Select(BackdropUrl).FirstOrDefault(u => u is not null)
                };
            })
            .ToList();
    }

    public UniverseHubDto? GetHub(string universeId, User user)
    {
        if (!_universes.Universes.TryGetValue(universeId, out var universe))
        {
            return null;
        }

        var visible = VisibleMatches(universe, user);
        var played = PlayedEpisodeIds(visible.Values, user);
        var rowIds = universe.Rows.Concat(universe.Pages.SelectMany(p => p.Rows)).Distinct(StringComparer.Ordinal).ToList();
        var shownEntryIds = rowIds.SelectMany(r => universe.Orders.GetValueOrDefault(r) ?? []).ToHashSet(StringComparer.Ordinal);

        var dto = new UniverseHubDto
        {
            Id = universe.Definition.Id,
            Name = universe.Definition.Name,
            Description = universe.Definition.Description,
            Revision = universe.Definition.Revision,
            Attribution = universe.Definition.Attribution.Select(a => new AttributionDto { Name = a.Name, Url = a.Url, License = a.License }).ToList(),
            FlagLabels = universe.Definition.Flags
                .Where(f => !universe.Settings.HiddenFlags.Contains(f.Key) && universe.Definition.Entries.Any(e => shownEntryIds.Contains(e.Id) && e.Flags.Contains(f.Key)))
                .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal),
            PrimaryRowId = universe.PrimaryRowId,
            RequestsEnabled = Plugin.Instance?.Configuration is { } config && SeerrClient.IsConfigured(config) && config.SeerrAllowRequests
        };

        foreach (var id in shownEntryIds)
        {
            dto.Entries[id] = BuildEntry(universe, universe.Entries[id], visible.GetValueOrDefault(id), user, played);
        }

        dto.Rows = universe.Rows.Select(r => BuildRow(universe, r, dto.Entries)).OfType<HubRowDto>().ToList();
        dto.Pages = universe.Pages
            .Select(p => new HubPageDto { Id = p.Id, Title = p.Title, Rows = p.Rows.Select(r => BuildRow(universe, r, dto.Entries)).OfType<HubRowDto>().ToList() })
            .Where(p => p.Rows.Count > 0)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var primaryIds = universe.Orders.GetValueOrDefault(universe.PrimaryRowId) ?? [];
        dto.NextEntryId = primaryIds.FirstOrDefault(id => dto.Entries.TryGetValue(id, out var e) && e.Owned && !e.Played && visible[id].IsPlayable);
        dto.BackdropUrl = (dto.NextEntryId is not null ? dto.Entries[dto.NextEntryId].BackdropUrl : null)
            ?? primaryIds.Select(id => dto.Entries.GetValueOrDefault(id)?.BackdropUrl).FirstOrDefault(u => u is not null);
        return dto;
    }

    public Dictionary<string, LibraryMatch> VisibleMatches(ResolvedUniverse universe, User user)
    {
        return universe.Matches
            .Where(m => m.Value.Item.IsVisible(user))
            .ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
    }

    public HashSet<Guid> PlayedEpisodeIds(IEnumerable<LibraryMatch> matches, User user)
    {
        var episodeIds = matches.SelectMany(m => m.Episodes).Select(e => e.Id).Distinct().ToArray();
        if (episodeIds.Length == 0)
        {
            return [];
        }

        return _libraryManager.GetItemIds(new InternalItemsQuery(user)
        {
            ItemIds = episodeIds,
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Episode]
        }).ToHashSet();
    }

    private static HubRowDto? BuildRow(ResolvedUniverse universe, string orderId, Dictionary<string, HubEntryDto> entries)
    {
        var order = universe.Order(orderId);
        if (order is null || !universe.Orders.TryGetValue(orderId, out var ids) || ids.Count == 0)
        {
            return null;
        }

        return new HubRowDto
        {
            Id = order.Id,
            Title = order.Title,
            Description = order.Description,
            Basis = order.Basis,
            EntryIds = [.. ids],
            Playable = ids.Any(id => entries.TryGetValue(id, out var e) && e.Owned)
        };
    }

    private HubEntryDto BuildEntry(ResolvedUniverse universe, EntryDefinition entry, LibraryMatch? match, User user, HashSet<Guid> playedEpisodes)
    {
        var release = entry.ReleaseDate;
        var dto = new HubEntryDto
        {
            Id = entry.Id,
            Type = entry.Type.ToString().ToLowerInvariant(),
            Title = entry.Title,
            Subtitle = entry.Type == EntryType.Season && entry.Season is int number ? SeasonLabel(number) : null,
            SeasonNumber = entry.Season,
            Released = entry.Released,
            Year = release?.Year,
            Flags = entry.Flags,
            Groups = entry.Groups,
            TmdbId = entry.Tmdb,
            MediaType = entry.IsTv ? "tv" : "movie",
            Owned = match is not null,
            ItemId = match?.Item.Id.ToString("N", CultureInfo.InvariantCulture),
            PosterUrl = PosterUrl(universe, entry, match),
            BackdropUrl = match is null ? null : BackdropUrl(match)
        };

        if (match is null)
        {
            dto.Overview = universe.SeerrDetails.GetValueOrDefault(entry.Id)?.Overview;
            return dto;
        }

        if (match.Item is MediaBrowser.Controller.Entities.Movies.Movie or Video && match.Episodes.Count == 0)
        {
            var userData = _userDataManager.GetUserData(user, match.Item);
            dto.Played = userData?.Played ?? false;
            if (!dto.Played && userData is { PlaybackPositionTicks: > 0 } && match.Item.RunTimeTicks is > 0)
            {
                dto.Progress = Math.Clamp((double)userData.PlaybackPositionTicks / match.Item.RunTimeTicks.Value, 0, 1);
            }

            dto.RuntimeMinutes = match.Item.RunTimeTicks is long ticks ? (int)TimeSpan.FromTicks(ticks).TotalMinutes : null;
            dto.Overview = match.Item.Overview;
        }
        else
        {
            var total = match.Episodes.Count;
            var watched = match.Episodes.Count(e => playedEpisodes.Contains(e.Id));
            dto.EpisodeCount = total;
            dto.UnplayedCount = total - watched;
            dto.Played = total > 0 && watched == total;
            dto.Progress = total > 0 && watched > 0 && watched < total ? (double)watched / total : null;
            dto.Overview = string.IsNullOrWhiteSpace(match.Item.Overview) ? match.Series?.Overview : match.Item.Overview;
        }

        return dto;
    }

    private string? PosterUrl(ResolvedUniverse universe, EntryDefinition entry, LibraryMatch? match)
    {
        if (match is not null)
        {
            var url = ImageUrl(match.Item, ImageType.Primary, 480) ?? (match.Series is not null ? ImageUrl(match.Series, ImageType.Primary, 480) : null);
            if (url is not null && (match.Item is not MediaBrowser.Controller.Entities.TV.Season || match.Item.HasImage(ImageType.Primary, 0) || entry.Poster is null))
            {
                return url;
            }
        }

        if (entry.Poster is not null)
        {
            return TmdbPosterBase + entry.Poster;
        }

        if (universe.SeerrDetails.GetValueOrDefault(entry.Id) is { } media)
        {
            var path = entry.Season is int season && media.SeasonPosters.TryGetValue(season, out var seasonPoster) ? seasonPoster : media.PosterPath;
            if (path is not null)
            {
                return TmdbPosterBase + path;
            }
        }

        return null;
    }

    private string? BackdropUrl(LibraryMatch match)
    {
        return ImageUrl(match.Item, ImageType.Backdrop, 1080) ?? (match.Series is not null ? ImageUrl(match.Series, ImageType.Backdrop, 1080) : null);
    }

    private string? ImageUrl(BaseItem item, ImageType type, int maxHeight)
    {
        if (!item.HasImage(type, 0))
        {
            return null;
        }

        var info = item.GetImageInfo(type, 0);
        var tag = info is null ? null : _imageProcessor.GetImageCacheTag(item, info);
        var path = type == ImageType.Backdrop ? "Backdrop/0" : type.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"Items/{item.Id:N}/Images/{path}?maxHeight={maxHeight}&quality=90{(tag is null ? string.Empty : "&tag=" + tag)}");
    }

    private static string SeasonLabel(int number) => number == 0 ? "Specials" : "Season " + number.ToString(CultureInfo.InvariantCulture);
}
