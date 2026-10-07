using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Sources;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Seerr;

public enum RequestStatus
{
    Unknown,
    None,
    Pending,
    Processing,
    Partial,
    Available
}

public sealed record SeerrMedia(RequestStatus Status, IReadOnlyDictionary<int, RequestStatus> Seasons, string? PosterPath, string? Overview, IReadOnlyDictionary<int, string> SeasonPosters);

public sealed class SeerrClient
{
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromMinutes(5);

    private readonly CachedHttp _http;
    private readonly ILogger<SeerrClient> _logger;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, SeerrMedia? Media)> _mediaCache = new(StringComparer.Ordinal);
    private (DateTimeOffset At, Dictionary<string, int> Users)? _userCache;

    public SeerrClient(CachedHttp http, ILogger<SeerrClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static bool IsConfigured(Configuration.PluginConfiguration config)
        => config.SeerrEnabled && Uri.TryCreate(config.SeerrUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(config.SeerrApiKey);

    public async Task<(bool Ok, string Message)> TestAsync(string url, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(url, apiKey);
            using var response = await client.GetAsync("api/v1/auth/me", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Seerr answered {(int)response.StatusCode} {response.ReasonPhrase}. Check the URL and API key.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var name = document.RootElement.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : null;
            return (true, "Connected" + (name is null ? "." : $" as {name}."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return (false, ex.Message);
        }
    }

    public async Task<SeerrMedia?> GetMediaAsync(Configuration.PluginConfiguration config, bool isTv, int tmdbId, CancellationToken cancellationToken)
    {
        var key = (isTv ? "tv:" : "movie:") + tmdbId.ToString(CultureInfo.InvariantCulture);
        if (_mediaCache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < StatusCacheDuration)
        {
            return cached.Media;
        }

        SeerrMedia? media = null;
        try
        {
            using var client = CreateClient(config.SeerrUrl, config.SeerrApiKey);
            using var response = await client.GetAsync($"api/v1/{(isTv ? "tv" : "movie")}/{tmdbId}", cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                media = ParseMedia(document.RootElement);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogDebug(ex, "Chrono: Seerr lookup for {Key} failed", key);
        }

        _mediaCache[key] = (DateTimeOffset.UtcNow, media);
        return media;
    }

    public async Task<RequestStatus> RequestAsync(Configuration.PluginConfiguration config, EntryDefinition entry, Guid jellyfinUserId, CancellationToken cancellationToken)
    {
        using var client = CreateClient(config.SeerrUrl, config.SeerrApiKey);
        var seerrUserId = await FindUserAsync(client, jellyfinUserId, cancellationToken).ConfigureAwait(false);
        var body = new Dictionary<string, object>
        {
            ["mediaType"] = entry.IsTv ? "tv" : "movie",
            ["mediaId"] = entry.Tmdb
        };
        if (entry.Type == EntryType.Season && entry.Season is int season)
        {
            body["seasons"] = new[] { season };
        }
        else if (entry.Type == EntryType.Series)
        {
            body["seasons"] = "all";
        }

        if (seerrUserId is int userId)
        {
            body["userId"] = userId;
        }

        using var response = await client.PostAsJsonAsync("api/v1/request", body, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Seerr rejected the request ({(int)response.StatusCode}): {Truncate(text)}");
        }

        _mediaCache.TryRemove((entry.IsTv ? "tv:" : "movie:") + entry.Tmdb.ToString(CultureInfo.InvariantCulture), out _);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var status = document.RootElement.TryGetProperty("status", out var statusElement) && statusElement.TryGetInt32(out var requestStatus) ? requestStatus : 1;
        return status == 2 ? RequestStatus.Processing : RequestStatus.Pending;
    }

    public static RequestStatus StatusFor(EntryDefinition entry, SeerrMedia? media)
    {
        if (media is null)
        {
            return RequestStatus.Unknown;
        }

        if (entry.Type == EntryType.Season && entry.Season is int season)
        {
            return media.Seasons.TryGetValue(season, out var seasonStatus) ? seasonStatus : media.Status == RequestStatus.Available ? RequestStatus.Available : RequestStatus.None;
        }

        return media.Status;
    }

    internal static SeerrMedia ParseMedia(JsonElement root)
    {
        var status = RequestStatus.None;
        var seasons = new Dictionary<int, RequestStatus>();
        if (root.TryGetProperty("mediaInfo", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            status = MapStatus(info);
            if (info.TryGetProperty("seasons", out var infoSeasons) && infoSeasons.ValueKind == JsonValueKind.Array)
            {
                foreach (var season in infoSeasons.EnumerateArray())
                {
                    if (season.TryGetProperty("seasonNumber", out var number) && number.TryGetInt32(out var n))
                    {
                        seasons[n] = MapStatus(season);
                    }
                }
            }

            if (info.TryGetProperty("requests", out var requests) && requests.ValueKind == JsonValueKind.Array)
            {
                foreach (var request in requests.EnumerateArray())
                {
                    var pending = request.TryGetProperty("status", out var requestStatus) && requestStatus.TryGetInt32(out var rs) && rs == 1;
                    if (!pending || !request.TryGetProperty("seasons", out var requestedSeasons) || requestedSeasons.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var requested in requestedSeasons.EnumerateArray())
                    {
                        if (requested.TryGetProperty("seasonNumber", out var number) && number.TryGetInt32(out var n) && (!seasons.TryGetValue(n, out var current) || current == RequestStatus.None))
                        {
                            seasons[n] = RequestStatus.Pending;
                        }
                    }
                }
            }
        }

        var seasonPosters = new Dictionary<int, string>();
        if (root.TryGetProperty("seasons", out var tvSeasons) && tvSeasons.ValueKind == JsonValueKind.Array)
        {
            foreach (var season in tvSeasons.EnumerateArray())
            {
                if (season.TryGetProperty("seasonNumber", out var number) && number.TryGetInt32(out var n)
                    && season.TryGetProperty("posterPath", out var poster) && poster.ValueKind == JsonValueKind.String)
                {
                    seasonPosters[n] = poster.GetString()!;
                }
            }
        }

        return new SeerrMedia(
            status,
            seasons,
            root.TryGetProperty("posterPath", out var posterPath) && posterPath.ValueKind == JsonValueKind.String ? posterPath.GetString() : null,
            root.TryGetProperty("overview", out var overview) && overview.ValueKind == JsonValueKind.String ? overview.GetString() : null,
            seasonPosters);
    }

    private static RequestStatus MapStatus(JsonElement element)
    {
        var value = element.TryGetProperty("status", out var status) && status.TryGetInt32(out var number) ? number : 1;
        return value switch
        {
            2 => RequestStatus.Pending,
            3 => RequestStatus.Processing,
            4 => RequestStatus.Partial,
            5 => RequestStatus.Available,
            _ => RequestStatus.None
        };
    }

    private async Task<int?> FindUserAsync(HttpClient client, Guid jellyfinUserId, CancellationToken cancellationToken)
    {
        if (_userCache is not { } cache || DateTimeOffset.UtcNow - cache.At > TimeSpan.FromMinutes(30))
        {
            var users = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var response = await client.GetAsync("api/v1/user?take=1000", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("results", out var results))
                    {
                        foreach (var user in results.EnumerateArray())
                        {
                            if (user.TryGetProperty("jellyfinUserId", out var jf) && jf.ValueKind == JsonValueKind.String
                                && user.TryGetProperty("id", out var id) && id.TryGetInt32(out var seerrId))
                            {
                                users[jf.GetString()!.Replace("-", string.Empty, StringComparison.Ordinal)] = seerrId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                _logger.LogDebug(ex, "Chrono: could not list Seerr users");
            }

            cache = (DateTimeOffset.UtcNow, users);
            _userCache = cache;
        }

        return cache.Users.TryGetValue(jellyfinUserId.ToString("N"), out var match) ? match : null;
    }

    private HttpClient CreateClient(string url, string apiKey)
    {
        var client = _http.CreateClient();
        client.BaseAddress = new Uri(url.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey.Trim());
        return client;
    }

    private static string Truncate(string text) => text.Length > 300 ? text[..300] : text;
}
