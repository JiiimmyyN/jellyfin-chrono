using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Chrono.Services;

public sealed class LibraryWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(30);
    private static readonly HashSet<BaseItemKind> RelevantKinds = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode];

    private readonly ILibraryManager _libraryManager;
    private readonly RefreshCoordinator _coordinator;
    private readonly ILogger<LibraryWatcher> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _timerLock = new();
    private Timer? _timer;

    public LibraryWatcher(ILibraryManager libraryManager, RefreshCoordinator coordinator, ILogger<LibraryWatcher> logger)
    {
        _libraryManager = libraryManager;
        _coordinator = coordinator;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
        }

        Schedule(TimeSpan.FromSeconds(15), forceSources: false);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
        }

        _stopping.Cancel();
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _stopping.Dispose();
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (RelevantKinds.Contains(e.Item.GetBaseItemKind()))
        {
            Schedule(Debounce, forceSources: false);
        }
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        Schedule(TimeSpan.FromSeconds(1), forceSources: false);
    }

    private void Schedule(TimeSpan delay, bool forceSources)
    {
        lock (_timerLock)
        {
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            _timer?.Dispose();
            _timer = new Timer(_ => _ = RunAsync(forceSources), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunAsync(bool forceSources)
    {
        try
        {
            await _coordinator.RunAsync(forceSources, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Chrono: refresh cancelled during shutdown");
        }
    }
}
