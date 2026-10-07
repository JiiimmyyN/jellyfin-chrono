using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Web;

public sealed class WebInjectionStartupFilter : IStartupFilter
{
    public const string ScriptMarker = "data-chrono-client";
    public const string MenuUrl = "#/chrono";

    private static readonly Lazy<string> ClientVersion = new(ComputeClientVersion);

    private readonly ILogger<WebInjectionStartupFilter> _logger;

    public WebInjectionStartupFilter(ILogger<WebInjectionStartupFilter> logger)
    {
        _logger = logger;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(InvokeAsync);
            next(app);
        };
    }

    public static string InjectScript(string html, string version)
    {
        if (html.Contains(ScriptMarker, StringComparison.Ordinal))
        {
            return html;
        }

        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClose < 0)
        {
            return html;
        }

        var tag = $"<script src=\"../Chrono/Client/chrono.js?v={version}\" defer {ScriptMarker}></script>";
        return string.Concat(html.AsSpan(0, bodyClose), tag, "\n", html.AsSpan(bodyClose));
    }

    public static string InjectMenuLink(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root is null)
        {
            return json;
        }

        if (root["menuLinks"] is not JsonArray links)
        {
            links = [];
            root["menuLinks"] = links;
        }

        if (links.OfType<JsonObject>().Any(l => (string?)l["url"] == MenuUrl))
        {
            return json;
        }

        links.Insert(0, new JsonObject { ["name"] = "Universes", ["icon"] = "movie_filter", ["url"] = MenuUrl });
        return root.ToJsonString();
    }

    private static string ComputeClientVersion()
    {
        using var stream = typeof(WebInjectionStartupFilter).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Chrono.Client.chrono.js");
        if (stream is null)
        {
            return Plugin.Instance?.Version.ToString() ?? "0";
        }

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream))[..12];
    }

    private static Target? Classify(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web", StringComparison.OrdinalIgnoreCase))
        {
            return Target.Index;
        }

        return path.EndsWith("/web/config.json", StringComparison.OrdinalIgnoreCase) ? Target.Config : null;
    }

    private async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var config = Plugin.Instance?.Configuration;
        var target = Classify(context.Request.Path.Value);
        if (target is null || !HttpMethods.IsGet(context.Request.Method) || config is null
            || (target == Target.Index && !config.InjectWebClient)
            || (target == Target.Config && !(config.InjectWebClient && config.AddMenuLink)))
        {
            await next().ConfigureAwait(false);
            return;
        }

        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("Range");
        context.Request.Headers.Remove("If-Range");
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next().ConfigureAwait(false);
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);
        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        var text = Encoding.UTF8.GetString(buffer.ToArray());
        try
        {
            text = target == Target.Index
                ? InjectScript(text, ClientVersion.Value)
                : InjectMenuLink(text);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            _logger.LogWarning(ex, "Chrono: could not modify {Path}; serving it unchanged", context.Request.Path);
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers.Remove("Accept-Ranges");
        context.Response.Headers.CacheControl = "no-cache";
        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
    }

    private enum Target
    {
        Index,
        Config
    }
}
