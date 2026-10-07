using System.Reflection;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Hub;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Api;

[ApiController]
[Route("Chrono")]
public sealed class ChronoController : ControllerBase
{
    private readonly UniverseService _universes;
    private readonly HubService _hub;
    private readonly PlaybackService _playback;
    private readonly RequestService _requests;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly ILogger<ChronoController> _logger;

    public ChronoController(
        UniverseService universes,
        HubService hub,
        PlaybackService playback,
        RequestService requests,
        IAuthorizationContext authorizationContext,
        ILogger<ChronoController> logger)
    {
        _universes = universes;
        _hub = hub;
        _playback = playback;
        _requests = requests;
        _authorizationContext = authorizationContext;
        _logger = logger;
    }

    [HttpGet("Universes")]
    [Authorize]
    public async Task<IActionResult> GetUniverses(CancellationToken cancellationToken)
    {
        var (user, _) = await CurrentAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return Json(_hub.GetSummaries(user));
    }

    [HttpGet("Universes/{id}")]
    [Authorize]
    public async Task<IActionResult> GetUniverse([FromRoute] string id, CancellationToken cancellationToken)
    {
        var (user, _) = await CurrentAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var hub = _hub.GetHub(id, user);
        return hub is null ? Error(StatusCodes.Status404NotFound, "Universe not found.") : Json(hub);
    }

    [HttpGet("Universes/{id}/RequestStatus")]
    [Authorize]
    public async Task<IActionResult> GetRequestStatus([FromRoute] string id, CancellationToken cancellationToken)
    {
        var (user, _) = await CurrentAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        return Json(await _requests.GetStatusesAsync(id, user, cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("Universes/{id}/Entries/{entryId}/Request")]
    [Authorize]
    public async Task<IActionResult> RequestEntry([FromRoute] string id, [FromRoute] string entryId, CancellationToken cancellationToken)
    {
        var (user, _) = await CurrentAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var outcome = await _requests.RequestAsync(id, entryId, user, cancellationToken).ConfigureAwait(false);
            return outcome.Ok ? Json(new { status = outcome.Status }) : Error(StatusCodes.Status400BadRequest, outcome.Error);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Chrono: Seerr request failed");
            return Error(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpPost("Universes/{id}/Play")]
    [Authorize]
    public async Task<IActionResult> Play([FromRoute] string id, CancellationToken cancellationToken)
    {
        var (user, deviceId) = await CurrentAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        var request = await ReadBodyAsync<PlayRequestDto>(cancellationToken).ConfigureAwait(false);
        if (request is null || string.IsNullOrEmpty(request.RowId))
        {
            return Error(StatusCodes.Status400BadRequest, "rowId is required.");
        }

        return await _playback.PlayAsync(id, request, user, deviceId, cancellationToken).ConfigureAwait(false) switch
        {
            PlayOutcome.Started => NoContent(),
            PlayOutcome.NoSession => Error(StatusCodes.Status404NotFound, "No active session found for this device. Reload the page and try again."),
            PlayOutcome.NothingToPlay => Error(StatusCodes.Status400BadRequest, "Nothing in this row is in the library."),
            _ => Error(StatusCodes.Status404NotFound, "Unknown universe or row.")
        };
    }

    [HttpGet("Client/{file}")]
    [AllowAnonymous]
    public IActionResult GetClientFile([FromRoute] string file)
    {
        var contentType = Path.GetExtension(file) switch
        {
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => null
        };
        if (contentType is null || file.Contains('/', StringComparison.Ordinal) || file.Contains('\\', StringComparison.Ordinal))
        {
            return NotFound();
        }

        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Jellyfin.Plugin.Chrono.Client." + file);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=300";
        return File(stream, contentType);
    }

    internal static ContentResult JsonContent(object value) => new()
    {
        Content = JsonSerializer.Serialize(value, value.GetType(), DefinitionJson.Options),
        ContentType = "application/json; charset=utf-8",
        StatusCode = StatusCodes.Status200OK
    };

    private ContentResult Json(object value) => JsonContent(value);

    private static ContentResult Error(int statusCode, string? detail)
    {
        var result = JsonContent(new { Title = "Chrono", Detail = detail ?? "Request failed.", Status = statusCode });
        result.StatusCode = statusCode;
        result.ContentType = "application/problem+json; charset=utf-8";
        return result;
    }

    private async Task<T?> ReadBodyAsync<T>(CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(Request.Body, DefinitionJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private async Task<(User? User, string? DeviceId)> CurrentAsync()
    {
        var info = await _authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        return (info.User, info.DeviceId);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (!_universes.HasLoaded)
        {
            await _universes.RefreshAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }
}
