using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Hub;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Output;

public sealed class OutputSync
{
    public const string ProviderKey = "Chrono";
    private const int MaxPlaylistLength = 2000;

    private readonly UniverseService _universes;
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<OutputSync> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OutputSync(
        UniverseService universes,
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        ICollectionManager collectionManager,
        IUserManager userManager,
        ILogger<OutputSync> logger)
    {
        _universes = universes;
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _collectionManager = collectionManager;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var universe in _universes.Universes.Values)
            {
                try
                {
                    if (universe.Settings.CreatePlaylists)
                    {
                        await SyncPlaylistsAsync(universe, cancellationToken).ConfigureAwait(false);
                    }

                    if (universe.Settings.CreateCollections)
                    {
                        await SyncCollectionsAsync(universe, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Chrono: syncing playlists/collections for {Universe} failed", universe.Definition.Id);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public static string PlaylistKey(string universeId, string rowId) => $"{universeId}/{rowId}";

    public Playlist? FindPlaylist(string universeId, string rowId) => FindByKey<Playlist>(BaseItemKind.Playlist, PlaylistKey(universeId, rowId));

    private async Task SyncPlaylistsAsync(ResolvedUniverse universe, CancellationToken cancellationToken)
    {
        var owner = ResolveOwner();
        if (owner is null)
        {
            _logger.LogWarning("Chrono: no administrator found to own playlists");
            return;
        }

        var rows = universe.Settings.PlaylistRows.Length > 0 ? universe.Settings.PlaylistRows : [universe.PrimaryRowId];
        foreach (var rowId in rows.Distinct(StringComparer.Ordinal))
        {
            if (!universe.Orders.TryGetValue(rowId, out var row) || universe.Order(rowId) is not { } order)
            {
                continue;
            }

            var items = PlaybackService.BuildQueue(row, universe.Matches, null, new HashSet<Guid>(), MaxPlaylistLength);
            var key = PlaylistKey(universe.Definition.Id, rowId);
            var name = $"{universe.Definition.Name} · {order.Title}";
            var existing = FindByKey<Playlist>(BaseItemKind.Playlist, key);
            if (items.Count == 0)
            {
                continue;
            }

            if (existing is null)
            {
                var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
                {
                    Name = name,
                    ItemIdList = items,
                    MediaType = MediaType.Video,
                    UserId = owner.Id,
                    Public = true
                }).ConfigureAwait(false);
                if (_libraryManager.GetItemById(Guid.Parse(result.Id)) is Playlist created)
                {
                    created.SetProviderId(ProviderKey, key);
                    await created.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation("Chrono: created playlist {Name} with {Count} items", name, items.Count);
                continue;
            }

            var current = existing.LinkedChildren.Select(c => c.ItemId).OfType<Guid>().ToList();
            if (!current.SequenceEqual(items) || existing.Name != name || !existing.OpenAccess)
            {
                await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
                {
                    Id = existing.Id,
                    UserId = existing.OwnerUserId,
                    Name = name,
                    Ids = current.SequenceEqual(items) ? null : items,
                    Public = true
                }).ConfigureAwait(false);
                _logger.LogInformation("Chrono: updated playlist {Name} ({Count} items)", name, items.Count);
            }
        }
    }

    private async Task SyncCollectionsAsync(ResolvedUniverse universe, CancellationToken cancellationToken)
    {
        var rowCollections = new List<BaseItem>();
        foreach (var rowId in universe.Rows.Concat(universe.Pages.SelectMany(p => p.Rows)).Distinct(StringComparer.Ordinal))
        {
            if (!universe.Orders.TryGetValue(rowId, out var row) || universe.Order(rowId) is not { } order)
            {
                continue;
            }

            var items = row
                .Select(id => universe.Matches.GetValueOrDefault(id)?.Item)
                .OfType<BaseItem>()
                .DistinctBy(i => i.Id)
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var collection = await EnsureCollectionAsync($"{universe.Definition.Id}/{rowId}", $"{universe.Definition.Name} · {order.Title}", items, cancellationToken).ConfigureAwait(false);
            rowCollections.Add(collection);
        }

        if (rowCollections.Count > 0)
        {
            await EnsureCollectionAsync(universe.Definition.Id, universe.Definition.Name + " (Chrono)", rowCollections, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<BoxSet> EnsureCollectionAsync(string key, string name, IReadOnlyList<BaseItem> children, CancellationToken cancellationToken)
    {
        var collection = FindByKey<BoxSet>(BaseItemKind.BoxSet, key);
        if (collection is null)
        {
            collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = name,
                IsLocked = true,
                ProviderIds = new Dictionary<string, string> { [ProviderKey] = key }
            }).ConfigureAwait(false);
        }

        var desired = children.Select(c => c.Id).ToList();
        var current = collection.LinkedChildren.Select(c => c.ItemId).OfType<Guid>().ToList();
        if (desired.SequenceEqual(current) && collection.Name == name && collection.DisplayOrder == "Default")
        {
            return collection;
        }

        collection.Name = name;
        collection.DisplayOrder = "Default";
        collection.IsLocked = true;
        collection.LinkedChildren = children.Select(LinkedChild.Create).ToArray();
        RemoveForeignProviderIds(collection, key);
        await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Chrono: updated collection {Name} ({Count} items)", name, desired.Count);
        return collection;
    }

    private static void RemoveForeignProviderIds(BaseItem item, string key)
    {
        item.ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderKey] = key };
    }

    private T? FindByKey<T>(BaseItemKind kind, string key)
        where T : BaseItem
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [kind],
            Recursive = true,
            HasAnyProviderId = new Dictionary<string, string> { [ProviderKey] = key }
        }).OfType<T>().FirstOrDefault();
    }

    private User? ResolveOwner()
    {
        var configured = Plugin.Instance?.Configuration.PlaylistOwnerUserId;
        if (Guid.TryParse(configured, out var id) && _userManager.GetUserById(id) is { } user)
        {
            return user;
        }

        return _userManager.GetUsers().FirstOrDefault(u => u.HasPermission(PermissionKind.IsAdministrator));
    }
}
