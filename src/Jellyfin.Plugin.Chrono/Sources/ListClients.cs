using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Chrono.Definitions;

namespace Jellyfin.Plugin.Chrono.Sources;

public sealed partial class MdbListClient
{
    private readonly CachedHttp _http;

    public MdbListClient(CachedHttp http)
    {
        _http = http;
    }

    public async Task<SourceResult?> GetListAsync(string listUrl, TimeSpan maxAge, bool forceRefresh, CancellationToken cancellationToken)
    {
        var match = ListUrlRegex().Match(listUrl.Trim());
        if (!match.Success)
        {
            throw new ArgumentException("Not an MDBList list URL: " + listUrl);
        }

        var url = $"https://mdblist.com/lists/{match.Groups["user"].Value}/{match.Groups["slug"].Value}/json";
        var body = await _http.GetStringAsync("mdblist:" + url, () => new HttpRequestMessage(HttpMethod.Get, url), maxAge, forceRefresh, cancellationToken).ConfigureAwait(false);
        return body is null ? null : Parse(body);
    }

    internal static SourceResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new SourceResult { RankedEntryIds = [] };
        var rows = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [];
        foreach (var row in rows.OrderBy(r => r.TryGetProperty("rank", out var rank) && rank.TryGetInt32(out var value) ? value : int.MaxValue))
        {
            if (!row.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var tmdb) || tmdb <= 0)
            {
                continue;
            }

            var isShow = string.Equals(StringValue(row, "mediatype"), "show", StringComparison.OrdinalIgnoreCase);
            var entry = new EntryDefinition
            {
                Id = isShow ? SourceResult.SeriesEntryId(tmdb) : SourceResult.MovieEntryId(tmdb),
                Type = isShow ? EntryType.Series : EntryType.Movie,
                Tmdb = tmdb,
                Title = StringValue(row, "title") ?? tmdb.ToString(CultureInfo.InvariantCulture),
                Imdb = StringValue(row, "imdb_id") is { } imdb && imdb.StartsWith("tt", StringComparison.Ordinal) ? imdb : null,
                Tvdb = isShow && row.TryGetProperty("tvdbid", out var tvdb) && tvdb.TryGetInt32(out var tvdbId) && tvdbId > 0 ? tvdbId : null,
                Released = row.TryGetProperty("release_year", out var year) && year.TryGetInt32(out var y) && y > 1800 ? $"{y:D4}-01-01" : null
            };
            if (result.RankedEntryIds.Contains(entry.Id))
            {
                continue;
            }

            result.Entries.Add(entry);
            result.RankedEntryIds.Add(entry.Id);
        }

        return result;
    }

    private static string? StringValue(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    [GeneratedRegex(@"mdblist\.com/lists/(?<user>[^/\s]+)/(?<slug>[^/\s?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ListUrlRegex();
}

public sealed partial class TraktClient
{
    private readonly CachedHttp _http;

    public TraktClient(CachedHttp http)
    {
        _http = http;
    }

    public async Task<SourceResult?> GetListAsync(string listUrl, string clientId, TimeSpan maxAge, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("A Trakt client id is required for Trakt lists.");
        }

        var match = ListUrlRegex().Match(listUrl.Trim());
        if (!match.Success)
        {
            throw new ArgumentException("Not a Trakt list URL: " + listUrl);
        }

        var url = $"https://api.trakt.tv/users/{match.Groups["user"].Value}/lists/{match.Groups["slug"].Value}/items?extended=full";
        var body = await _http.GetStringAsync(
            "trakt:" + url,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("trakt-api-version", "2");
                request.Headers.Add("trakt-api-key", clientId.Trim());
                return request;
            },
            maxAge,
            forceRefresh,
            cancellationToken).ConfigureAwait(false);
        return body is null ? null : Parse(body);
    }

    internal static SourceResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new SourceResult { RankedEntryIds = [] };
        var rows = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [];
        foreach (var row in rows.OrderBy(r => r.TryGetProperty("rank", out var rank) && rank.TryGetInt32(out var value) ? value : int.MaxValue))
        {
            var type = row.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            EntryDefinition? entry = type switch
            {
                "movie" when row.TryGetProperty("movie", out var movie) => MovieEntry(movie),
                "show" when row.TryGetProperty("show", out var show) => ShowEntry(show, null, null),
                "season" when row.TryGetProperty("show", out var show) && row.TryGetProperty("season", out var season) => ShowEntry(show, Int(season, "number"), String(season, "first_aired")),
                "episode" when row.TryGetProperty("show", out var show) && row.TryGetProperty("episode", out var episode) => ShowEntry(show, Int(episode, "season"), null),
                _ => null
            };
            if (entry is null || result.RankedEntryIds.Contains(entry.Id))
            {
                continue;
            }

            result.Entries.Add(entry);
            result.RankedEntryIds.Add(entry.Id);
        }

        return result;
    }

    private static EntryDefinition? MovieEntry(JsonElement movie)
    {
        var ids = movie.TryGetProperty("ids", out var idElement) ? idElement : default;
        if (ids.ValueKind != JsonValueKind.Object || Int(ids, "tmdb") is not int tmdb)
        {
            return null;
        }

        return new EntryDefinition
        {
            Id = SourceResult.MovieEntryId(tmdb),
            Type = EntryType.Movie,
            Tmdb = tmdb,
            Title = String(movie, "title") ?? tmdb.ToString(CultureInfo.InvariantCulture),
            Imdb = String(ids, "imdb"),
            Released = NormalizeDate(String(movie, "released"))
        };
    }

    private static EntryDefinition? ShowEntry(JsonElement show, int? season, string? seasonAired)
    {
        var ids = show.TryGetProperty("ids", out var idElement) ? idElement : default;
        if (ids.ValueKind != JsonValueKind.Object || Int(ids, "tmdb") is not int tmdb)
        {
            return null;
        }

        return new EntryDefinition
        {
            Id = season is int s ? SourceResult.SeasonEntryId(tmdb, s) : SourceResult.SeriesEntryId(tmdb),
            Type = season is null ? EntryType.Series : EntryType.Season,
            Season = season,
            Tmdb = tmdb,
            Tvdb = Int(ids, "tvdb"),
            Imdb = String(ids, "imdb"),
            Title = String(show, "title") ?? tmdb.ToString(CultureInfo.InvariantCulture),
            Released = NormalizeDate(seasonAired ?? String(show, "first_aired"))
        };
    }

    private static string? NormalizeDate(string? value)
    {
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    private static int? Int(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
    }

    private static string? String(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    [GeneratedRegex(@"trakt\.tv/users/(?<user>[^/\s]+)/lists/(?<slug>[^/\s?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ListUrlRegex();
}
