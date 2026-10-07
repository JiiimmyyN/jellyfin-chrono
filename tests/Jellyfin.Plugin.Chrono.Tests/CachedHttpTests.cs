using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.Chrono.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Chrono.Tests;

public class CachedHttpTests
{
    [Theory]
    [InlineData("\"908389b9f94ab59c54cc80afd01498a374229c8cb8863ba4c625dfe673d2b448\"")]
    [InlineData("W/\"908389b9f94ab59c\"")]
    public async Task RevalidatesWithTheETagTheServerSent(string etag)
    {
        var handler = new ETagHandler(etag);
        var http = new CachedHttp(new SingleHandlerFactory(handler), NullLogger<CachedHttp>.Instance);
        var key = "test:" + Guid.NewGuid();

        var first = await Fetch(http, key);
        var second = await Fetch(http, key);

        Assert.Equal("body", first);
        Assert.Equal("body", second);
        Assert.Equal([null, etag], handler.IfNoneMatch);
    }

    private static Task<string?> Fetch(CachedHttp http, string key) =>
        http.GetStringAsync(
            key,
            () => new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/registry.json"),
            TimeSpan.Zero,
            false,
            CancellationToken.None);

    private sealed class ETagHandler(string etag) : HttpMessageHandler
    {
        public List<string?> IfNoneMatch { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            IfNoneMatch.Add(sent);
            var response = sent == etag
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("body") };
            response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
            return Task.FromResult(response);
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
