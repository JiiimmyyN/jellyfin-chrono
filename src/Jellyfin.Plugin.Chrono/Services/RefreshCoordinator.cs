using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Output;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Services;

public sealed class RefreshCoordinator
{
    private readonly UniverseService _universes;
    private readonly OutputSync _output;
    private readonly ILogger<RefreshCoordinator> _logger;

    public RefreshCoordinator(UniverseService universes, OutputSync output, ILogger<RefreshCoordinator> logger)
    {
        _universes = universes;
        _output = output;
        _logger = logger;
    }

    public DateTimeOffset? LastRefresh { get; private set; }

    public string? LastError { get; private set; }

    public async Task RunAsync(bool forceSources, CancellationToken cancellationToken)
    {
        try
        {
            await _universes.RefreshAsync(forceSources, cancellationToken).ConfigureAwait(false);
            await _output.SyncAsync(cancellationToken).ConfigureAwait(false);
            LastRefresh = DateTimeOffset.UtcNow;
            LastError = null;
            _logger.LogInformation("Chrono: refreshed {Count} universe(s)", _universes.Universes.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            _logger.LogError(ex, "Chrono: refresh failed");
        }
    }
}
