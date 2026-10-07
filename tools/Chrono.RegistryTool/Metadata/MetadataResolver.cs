using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Chrono.RegistryTool.Requests;
using Jellyfin.Plugin.Chrono.Sources;

namespace Chrono.RegistryTool.Metadata;

public sealed record SeasonInfo(int Number, string? Released, string? Poster);

public sealed record TitleInfo(string Title, string? Released, string? Imdb, int? Tvdb, string? Poster, IReadOnlyList<SeasonInfo> Seasons);

public interface IMetadataResolver
{
    Task<TitleInfo> MovieAsync(int tmdb);

    Task<TitleInfo> SeriesAsync(int tmdb);
}

public sealed class MetadataResolver : IMetadataResolver
{
    private readonly HttpClient _http;
    private readonly string? _tmdbKey;

    public MetadataResolver(HttpClient http, string? tmdbKey)
    {
        _http = http;
        _tmdbKey = string.IsNullOrWhiteSpace(tmdbKey) ? null : tmdbKey.Trim();
    }

    public static MetadataResolver Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(CachedHttp.UserAgent);
        return new MetadataResolver(http, Environment.GetEnvironmentVariable("TMDB_API_KEY"));
    }

    public Task<TitleInfo> MovieAsync(int tmdb) => _tmdbKey is null ? FromOpenSourcesAsync(true, tmdb) : FromTmdbAsync(true, tmdb);

    public Task<TitleInfo> SeriesAsync(int tmdb) => _tmdbKey is null ? FromOpenSourcesAsync(false, tmdb) : FromTmdbAsync(false, tmdb);

    public static TitleInfo ParseTmdb(bool isMovie, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var external = root.TryGetProperty("external_ids", out var ids) ? ids : default;
        var seasons = new List<SeasonInfo>();
        if (!isMovie && root.TryGetProperty("seasons", out var seasonList))
        {
            foreach (var season in seasonList.EnumerateArray())
            {
                if (season.TryGetProperty("season_number", out var number) && number.GetInt32() > 0)
                {
                    seasons.Add(new SeasonInfo(number.GetInt32(), Date(String(season, "air_date")), String(season, "poster_path")));
                }
            }
        }

        var imdb = String(root, "imdb_id") ?? (external.ValueKind == JsonValueKind.Object ? String(external, "imdb_id") : null);
        var tvdb = external.ValueKind == JsonValueKind.Object && external.TryGetProperty("tvdb_id", out var tvdbId) && tvdbId.ValueKind == JsonValueKind.Number ? tvdbId.GetInt32() : (int?)null;
        return new TitleInfo(
            String(root, isMovie ? "title" : "name") ?? throw new RequestException("TMDB returned no title."),
            Date(String(root, isMovie ? "release_date" : "first_air_date")),
            string.IsNullOrEmpty(imdb) ? null : imdb,
            tvdb,
            String(root, "poster_path"),
            seasons.OrderBy(s => s.Number).ToList());
    }

    private async Task<TitleInfo> FromTmdbAsync(bool isMovie, int tmdb)
    {
        var bearer = _tmdbKey!.Length > 40;
        var url = $"https://api.themoviedb.org/3/{(isMovie ? "movie" : "tv")}/{tmdb}?append_to_response=external_ids&language=en-US"
            + (bearer ? string.Empty : "&api_key=" + Uri.EscapeDataString(_tmdbKey));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (bearer)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tmdbKey);
        }

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new RequestException($"TMDB has no {(isMovie ? "movie" : "series")} with id {tmdb} ({(int)response.StatusCode}).");
        }

        return ParseTmdb(isMovie, await response.Content.ReadAsStringAsync());
    }

    private async Task<TitleInfo> FromOpenSourcesAsync(bool isMovie, int tmdb)
    {
        var property = isMovie ? "P4947" : "P4983";
        var query = $$"""
            SELECT ?item ?itemLabel ?imdb ?tvdb ?date ?start WHERE {
              ?item wdt:{{property}} "{{tmdb.ToString(CultureInfo.InvariantCulture)}}" .
              OPTIONAL { ?item wdt:P345 ?imdb }
              OPTIONAL { ?item wdt:P4835 ?tvdb }
              OPTIONAL { ?item wdt:P577 ?date }
              OPTIONAL { ?item wdt:P580 ?start }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://query.wikidata.org/sparql")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", query)])
        };
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            throw new RequestException($"Wikidata has no {(isMovie ? "movie" : "series")} with TMDB id {tmdb}. Fill in the title and release date fields, or configure a TMDB_API_KEY secret.");
        }

        string? Value(string name) => rows.Select(r => r.TryGetProperty(name, out var cell) ? cell.GetProperty("value").GetString() : null).FirstOrDefault(v => v is not null);
        var dates = rows
            .SelectMany(r => new[] { r.TryGetProperty("date", out var d) ? d.GetProperty("value").GetString() : null, r.TryGetProperty("start", out var s) ? s.GetProperty("value").GetString() : null })
            .OfType<string>()
            .Select(Date)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
        var imdb = Value("imdb");
        var tvdb = int.TryParse(Value("tvdb"), CultureInfo.InvariantCulture, out var tvdbId) ? tvdbId : (int?)null;
        var seasons = isMovie ? [] : await TvMazeSeasonsAsync(imdb, tvdb);
        return new TitleInfo(Value("itemLabel") ?? tmdb.ToString(CultureInfo.InvariantCulture), dates.FirstOrDefault(), imdb, tvdb, null, seasons);
    }

    private async Task<IReadOnlyList<SeasonInfo>> TvMazeSeasonsAsync(string? imdb, int? tvdb)
    {
        var lookup = imdb is not null ? $"imdb={imdb}" : tvdb is int id ? $"thetvdb={id}" : null;
        if (lookup is null)
        {
            return [];
        }

        using var show = await _http.GetAsync("https://api.tvmaze.com/lookup/shows?" + lookup);
        if (!show.IsSuccessStatusCode)
        {
            return [];
        }

        using var showDocument = JsonDocument.Parse(await show.Content.ReadAsStringAsync());
        var showId = showDocument.RootElement.GetProperty("id").GetInt32();
        using var seasons = JsonDocument.Parse(await _http.GetStringAsync($"https://api.tvmaze.com/shows/{showId}/seasons"));
        return seasons.RootElement.EnumerateArray()
            .Where(s => s.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number && n.GetInt32() > 0)
            .Select(s => new SeasonInfo(s.GetProperty("number").GetInt32(), Date(String(s, "premiereDate")), null))
            .ToList();
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Date(string? value)
        => value is { Length: >= 10 } && DateOnly.TryParse(value[..10], CultureInfo.InvariantCulture, out _) ? value[..10] : null;
}
