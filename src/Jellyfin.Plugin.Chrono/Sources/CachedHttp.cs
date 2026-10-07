using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Sources;

public sealed class CachedHttp
{
    public const string UserAgent = "JellyfinChrono/0.1 (+https://github.com/JiiimmyyN/jellyfin-chrono)";

    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(30);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _failures = new(StringComparer.Ordinal);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CachedHttp> _logger;

    public CachedHttp(IHttpClientFactory httpClientFactory, ILogger<CachedHttp> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("Chrono");
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    public async Task<string?> GetStringAsync(
        string cacheKey,
        Func<HttpRequestMessage> requestFactory,
        TimeSpan maxAge,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var path = CachePath(cacheKey);
        var cached = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (!forceRefresh && cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < maxAge)
        {
            return cached.Body;
        }

        if (!forceRefresh && _failures.TryGetValue(cacheKey, out var failedAt) && DateTimeOffset.UtcNow - failedAt < FailureBackoff)
        {
            return cached?.Body;
        }

        try
        {
            using var request = requestFactory();
            if (cached?.ETag is not null && EntityTagHeaderValue.TryParse(cached.ETag, out var cachedETag))
            {
                request.Headers.IfNoneMatch.Add(cachedETag);
            }

            using var client = CreateClient();
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                await WriteAsync(path, cached with { FetchedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
                return cached.Body;
            }

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _failures.TryRemove(cacheKey, out _);
            await WriteAsync(path, new CacheRecord(body, response.Headers.ETag?.ToString(), DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            return body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _failures[cacheKey] = DateTimeOffset.UtcNow;
            _logger.LogWarning("Chrono: fetching {Key} failed ({Message}); using cached copy: {HasCache}", cacheKey, ex.Message, cached is not null);
            return cached?.Body;
        }
    }

    private static string CachePath(string key)
    {
        var directory = Plugin.Instance?.CacheDirectory ?? Path.Combine(Path.GetTempPath(), "jellyfin-chrono-cache");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
        return Path.Combine(directory, hash + ".json");
    }

    private static async Task<CacheRecord?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CacheRecord>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteAsync(string path, CacheRecord record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, record, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, true);
    }

    private sealed record CacheRecord(string Body, string? ETag, DateTimeOffset FetchedAt);
}
