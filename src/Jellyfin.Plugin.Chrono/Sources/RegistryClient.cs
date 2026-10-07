using System.Reflection;
using Jellyfin.Plugin.Chrono.Definitions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Sources;

public sealed class RegistryClient
{
    private const string BundledPrefix = "Jellyfin.Plugin.Chrono.Registry.";

    private readonly CachedHttp _http;
    private readonly ILogger<RegistryClient> _logger;

    public RegistryClient(CachedHttp http, ILogger<RegistryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LoadedUniverse>> LoadAsync(IReadOnlyCollection<string> registryUrls, TimeSpan maxAge, bool forceRefresh, CancellationToken cancellationToken)
    {
        var universes = new Dictionary<string, LoadedUniverse>(StringComparer.Ordinal);
        foreach (var bundled in LoadBundled())
        {
            universes[bundled.Definition.Id] = bundled;
        }

        foreach (var registryUrl in registryUrls.Where(u => Uri.TryCreate(u, UriKind.Absolute, out _)))
        {
            foreach (var remote in await LoadRemoteAsync(registryUrl, maxAge, forceRefresh, cancellationToken).ConfigureAwait(false))
            {
                if (!universes.TryGetValue(remote.Definition.Id, out var existing)
                    || CompareRevisions(remote.Definition.Revision, existing.Definition.Revision) > 0)
                {
                    universes[remote.Definition.Id] = remote;
                }
            }
        }

        return universes.Values.ToList();
    }

    public static IReadOnlyList<LoadedUniverse> LoadBundled()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var result = new List<LoadedUniverse>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(BundledPrefix + "universes.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var definition = DefinitionJson.ParseUniverse(reader.ReadToEnd());
            if (DefinitionValidator.Validate(definition).Count == 0)
            {
                result.Add(new LoadedUniverse(definition, "bundled"));
            }
        }

        return result;
    }

    public static int CompareRevisions(string? left, string? right)
    {
        static long[] Parts(string? revision) => (revision ?? string.Empty)
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => long.TryParse(p, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0)
            .ToArray();

        var a = Parts(left);
        var b = Parts(right);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }

        return 0;
    }

    private async Task<IReadOnlyList<LoadedUniverse>> LoadRemoteAsync(string registryUrl, TimeSpan maxAge, bool forceRefresh, CancellationToken cancellationToken)
    {
        var result = new List<LoadedUniverse>();
        try
        {
            var indexBody = await _http.GetStringAsync("registry:" + registryUrl, () => new HttpRequestMessage(HttpMethod.Get, registryUrl), maxAge, forceRefresh, cancellationToken).ConfigureAwait(false);
            if (indexBody is null)
            {
                return result;
            }

            var index = DefinitionJson.ParseIndex(indexBody);
            var baseUri = new Uri(registryUrl);
            foreach (var entry in index.Universes)
            {
                var url = new Uri(baseUri, entry.File).ToString();
                var body = await _http.GetStringAsync("registry:" + url, () => new HttpRequestMessage(HttpMethod.Get, url), maxAge, forceRefresh, cancellationToken).ConfigureAwait(false);
                if (body is null)
                {
                    continue;
                }

                var definition = DefinitionJson.ParseUniverse(body);
                var errors = DefinitionValidator.Validate(definition);
                if (errors.Count > 0)
                {
                    _logger.LogWarning("Chrono: ignoring universe {Url}: {Errors}", url, string.Join("; ", errors.Take(5)));
                    continue;
                }

                result.Add(new LoadedUniverse(definition, registryUrl));
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or UriFormatException)
        {
            _logger.LogWarning(ex, "Chrono: registry {Url} could not be read", registryUrl);
        }

        return result;
    }
}

public sealed record LoadedUniverse(UniverseDefinition Definition, string Origin);
