using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Chrono.Services;

public sealed class RefreshUniversesTask : IScheduledTask
{
    private readonly RefreshCoordinator _coordinator;

    public RefreshUniversesTask(RefreshCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public string Name => "Refresh universes";

    public string Key => "ChronoRefreshUniverses";

    public string Description => "Downloads updated universe definitions, matches them against the library and rebuilds Chrono playlists and collections.";

    public string Category => "Chrono";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        await _coordinator.RunAsync(true, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(24).Ticks }
        ];
    }
}

public sealed class ChronoPostScanTask : ILibraryPostScanTask
{
    private readonly RefreshCoordinator _coordinator;

    public ChronoPostScanTask(RefreshCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _coordinator.RunAsync(false, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
