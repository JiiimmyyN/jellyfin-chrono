using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Chrono.Definitions;

namespace Jellyfin.Plugin.Chrono.Sources;

public sealed partial class WikidataClient
{
    private const string SparqlEndpoint = "https://query.wikidata.org/sparql";
    private const int MaxGroups = 8;

    private readonly CachedHttp _http;

    public WikidataClient(CachedHttp http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<WikidataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = "https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&language=en&uselang=en&type=item&limit=15&search="
            + Uri.EscapeDataString(query.Trim());
        var body = await _http.GetStringAsync("wd-search:" + query.Trim().ToLowerInvariant(), () => new HttpRequestMessage(HttpMethod.Get, url), TimeSpan.FromDays(1), false, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("search", out var search))
        {
            return [];
        }

        return search.EnumerateArray()
            .Select(s => new WikidataSearchResult(
                s.GetProperty("id").GetString() ?? string.Empty,
                s.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                s.TryGetProperty("description", out var description) ? description.GetString() : null))
            .Where(r => QidRegex().IsMatch(r.Id))
            .ToList();
    }

    public async Task<SourceResult?> GetFranchiseAsync(IReadOnlyCollection<string> anchors, TimeSpan maxAge, bool forceRefresh, CancellationToken cancellationToken)
    {
        var valid = anchors.Where(a => QidRegex().IsMatch(a)).Distinct().OrderBy(a => a, StringComparer.Ordinal).ToList();
        if (valid.Count == 0)
        {
            return null;
        }

        var query = BuildFranchiseQuery(valid);
        var body = await _http.GetStringAsync(
            "wd-franchise:" + string.Join(',', valid),
            () => SparqlRequest(query),
            maxAge,
            forceRefresh,
            cancellationToken).ConfigureAwait(false);
        return body is null ? null : ParseFranchise(body, valid);
    }

    public async Task<IReadOnlyList<FranchiseSuggestion>> SuggestFranchisesAsync(IReadOnlyCollection<int> tmdbMovieIds, IReadOnlyCollection<int> tmdbTvIds, CancellationToken cancellationToken)
    {
        var totals = new Dictionary<string, FranchiseSuggestion>(StringComparer.Ordinal);
        foreach (var (property, ids) in new[] { ("P4947", tmdbMovieIds), ("P4983", tmdbTvIds) })
        {
            foreach (var chunk in ids.Distinct().Order().Chunk(400))
            {
                var values = string.Join(' ', chunk.Select(id => "\"" + id.ToString(CultureInfo.InvariantCulture) + "\""));
                var query = $$"""
                    SELECT ?franchise ?franchiseLabel (COUNT(DISTINCT ?item) AS ?n) WHERE {
                      VALUES ?tmdb { {{values}} }
                      ?item wdt:{{property}} ?tmdb .
                      { ?item wdt:P8345 ?franchise } UNION { ?item wdt:P1434 ?franchise }
                      SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
                    } GROUP BY ?franchise ?franchiseLabel
                    """;
                var key = "wd-suggest:" + property + ":" + string.Join(',', chunk);
                var body = await _http.GetStringAsync(key, () => SparqlRequest(query), TimeSpan.FromDays(7), false, cancellationToken).ConfigureAwait(false);
                if (body is null)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(body);
                foreach (var row in Bindings(document))
                {
                    var qid = EntityId(Value(row, "franchise"));
                    if (qid is null || !int.TryParse(Value(row, "n"), CultureInfo.InvariantCulture, out var count))
                    {
                        continue;
                    }

                    var label = Value(row, "franchiseLabel") ?? qid;
                    totals[qid] = totals.TryGetValue(qid, out var existing)
                        ? existing with { OwnedCount = existing.OwnedCount + count }
                        : new FranchiseSuggestion(qid, label, count);
                }
            }
        }

        return totals.Values.OrderByDescending(s => s.OwnedCount).ToList();
    }

    internal static string BuildFranchiseQuery(IReadOnlyCollection<string> anchors)
    {
        var values = string.Join(' ', anchors.Select(a => "wd:" + a));
        return $$"""
            SELECT ?item ?itemLabel ?tmdbMovie ?tmdbTv ?imdb ?tvdb ?date ?start ?series ?seriesLabel WHERE {
              VALUES ?anchor { {{values}} }
              { ?item wdt:P1434 ?anchor } UNION { ?item wdt:P8345 ?anchor } UNION { ?item wdt:P179/wdt:P361* ?anchor } UNION { ?item wdt:P179/wdt:P179 ?anchor } UNION { ?item wdt:P361 ?anchor }
              { ?item wdt:P4947 ?tmdbMovie } UNION { ?item wdt:P4983 ?tmdbTv }
              FILTER NOT EXISTS { ?item wdt:P31 wd:Q93204 }
              FILTER NOT EXISTS { ?item wdt:P31 wd:Q21191270 }
              OPTIONAL { ?item wdt:P345 ?imdb }
              OPTIONAL { ?item wdt:P4835 ?tvdb }
              OPTIONAL { ?item wdt:P577 ?date }
              OPTIONAL { ?item wdt:P580 ?start }
              OPTIONAL { ?item wdt:P179 ?series }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            """;
    }

    internal static SourceResult ParseFranchise(string json, IReadOnlyCollection<string> anchors)
    {
        using var document = JsonDocument.Parse(json);
        var items = new Dictionary<string, FranchiseItem>(StringComparer.Ordinal);
        var seriesLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Bindings(document))
        {
            var qid = EntityId(Value(row, "item"));
            if (qid is null)
            {
                continue;
            }

            if (!items.TryGetValue(qid, out var item))
            {
                item = new FranchiseItem { Label = Value(row, "itemLabel") ?? qid };
                items[qid] = item;
            }

            item.TmdbMovie ??= ParseInt(Value(row, "tmdbMovie"));
            item.TmdbTv ??= ParseInt(Value(row, "tmdbTv"));
            item.Imdb ??= Value(row, "imdb");
            item.Tvdb ??= ParseInt(Value(row, "tvdb"));
            foreach (var dateText in new[] { Value(row, "date"), Value(row, "start") })
            {
                if (dateText is not null && DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                {
                    var day = DateOnly.FromDateTime(date.UtcDateTime);
                    if (item.Date is null || day < item.Date)
                    {
                        item.Date = day;
                    }
                }
            }

            var series = EntityId(Value(row, "series"));
            if (series is not null && !anchors.Contains(series))
            {
                item.Series.Add(series);
                seriesLabels.TryAdd(series, Value(row, "seriesLabel") ?? series);
            }
        }

        var result = new SourceResult();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var groupMembers = new Dictionary<string, List<EntryDefinition>>(StringComparer.Ordinal);
        foreach (var item in items.Values.OrderBy(i => i.Date ?? DateOnly.MaxValue))
        {
            if (item.Label.StartsWith('Q') && QidRegex().IsMatch(item.Label))
            {
                continue;
            }

            EntryDefinition entry;
            if (item.TmdbMovie is int movie)
            {
                entry = new EntryDefinition { Id = SourceResult.MovieEntryId(movie), Type = EntryType.Movie, Tmdb = movie };
            }
            else if (item.TmdbTv is int tv)
            {
                entry = new EntryDefinition { Id = SourceResult.SeriesEntryId(tv), Type = EntryType.Series, Tmdb = tv, Tvdb = item.Tvdb };
            }
            else
            {
                continue;
            }

            if (!seen.Add(entry.Id))
            {
                continue;
            }

            entry.Title = item.Label;
            entry.Imdb = item.Imdb is not null && ImdbRegex().IsMatch(item.Imdb) ? item.Imdb : null;
            entry.Released = item.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            result.Entries.Add(entry);
            foreach (var series in item.Series)
            {
                if (!groupMembers.TryGetValue(series, out var members))
                {
                    members = [];
                    groupMembers[series] = members;
                }

                members.Add(entry);
            }
        }

        var usedGroupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (series, members) in groupMembers
            .Where(g => g.Value.Count >= 2)
            .OrderByDescending(g => g.Value.Count)
            .Take(MaxGroups))
        {
            var groupId = Slug.From(seriesLabels[series]);
            if (string.IsNullOrEmpty(groupId) || !usedGroupIds.Add(groupId))
            {
                groupId = series.ToLowerInvariant();
                usedGroupIds.Add(groupId);
            }

            result.Groups.Add(new GroupDefinition { Id = groupId, Title = seriesLabels[series] });
            foreach (var member in members)
            {
                member.Groups.Add(groupId);
            }
        }

        return result;
    }

    private static HttpRequestMessage SparqlRequest(string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SparqlEndpoint)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", query)])
        };
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        return request;
    }

    private static IEnumerable<JsonElement> Bindings(JsonDocument document)
    {
        return document.RootElement.TryGetProperty("results", out var results) && results.TryGetProperty("bindings", out var bindings)
            ? bindings.EnumerateArray()
            : [];
    }

    private static string? Value(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out var cell) && cell.TryGetProperty("value", out var value) ? value.GetString() : null;
    }

    private static string? EntityId(string? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var id = uri[(uri.LastIndexOf('/') + 1)..];
        return QidRegex().IsMatch(id) ? id : null;
    }

    private static int? ParseInt(string? value) => int.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : null;

    [GeneratedRegex("^Q\\d+$")]
    private static partial Regex QidRegex();

    [GeneratedRegex("^tt\\d+$")]
    private static partial Regex ImdbRegex();

    private sealed class FranchiseItem
    {
        public string Label { get; set; } = string.Empty;

        public int? TmdbMovie { get; set; }

        public int? TmdbTv { get; set; }

        public string? Imdb { get; set; }

        public int? Tvdb { get; set; }

        public DateOnly? Date { get; set; }

        public HashSet<string> Series { get; } = new(StringComparer.Ordinal);
    }
}

public sealed record WikidataSearchResult(string Id, string Label, string? Description);

public sealed record FranchiseSuggestion(string Id, string Label, int OwnedCount);
