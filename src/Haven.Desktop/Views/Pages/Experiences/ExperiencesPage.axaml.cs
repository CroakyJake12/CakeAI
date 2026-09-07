using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.HavenUI.GenerativeUi;

namespace Haven.Desktop.Views.Pages.Experiences;

public sealed partial class ExperiencesPage : UserControl, IDisposable
{
    private readonly IGenUiAppRepository _apps;
    private readonly GenUiAppSessionService _sessions;
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly Func<string, Task> _create;
    private readonly ExperiencesHavenScene _route;
    private HavenGenUiSceneSurface? _surface;
    private Guid? _openInstanceId;
    private bool _disposed;

    public ExperiencesPage(
        IGenUiAppRepository apps,
        GenUiAppSessionService sessions,
        GenerativeUiEventRouter router,
        GenUiInstanceStore instances,
        Func<string, Task> create)
    {
        _apps = apps ?? throw new ArgumentNullException(nameof(apps));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _create = create ?? throw new ArgumentNullException(nameof(create));
        InitializeComponent();
        _route = new ExperiencesHavenScene();
        Scene.Root = _route.Root;
        _route.NewRequested += OnNewRequested;
        _route.RefreshRequested += OnRefreshRequested;
        _route.OpenRequested += OnOpenRequested;
        _route.PinRequested += OnPinRequested;
        _ = RefreshAsync();
    }

    internal ExperiencesHavenScene Route => _route;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        try
        {
            var pinnedTask = _apps.GetPinnedAsync(12, cancellationToken);
            var recentTask = _apps.GetRecentAsync(24, cancellationToken);
            await Task.WhenAll(pinnedTask, recentTask);
            if (_disposed) return;
            var pinned = await pinnedTask;
            var recent = await recentTask;
            _route.SetItems(pinned, recent);
            _route.SetStatus(recent.Count == 0 ? "No saved experiences yet." : $"{recent.Count} saved experience{(recent.Count == 1 ? string.Empty : "s")}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            if (!_disposed) _route.SetStatus("Could not load experiences: " + exception.Message);
        }
    }

    public async Task OpenAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        try
        {
            if (_openInstanceId is Guid previous && previous != instanceId)
                await _sessions.PersistCurrentStateAsync(previous, cancellationToken);
            var definition = await _sessions.OpenAsync(instanceId, cancellationToken);
            if (_disposed) return;
            _surface?.Dispose();
            _surface = new HavenGenUiSceneSurface(_router, _instances);
            _surface.PresentExisting(definition.Document);
            _openInstanceId = instanceId;
            _route.ShowExperience(definition.Document.Title, _surface.Root);
            _route.SetStatus($"Opened {definition.Document.Title}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IOException)
        {
            if (!_disposed) _route.SetStatus("Could not open that experience: " + exception.Message);
        }
    }

    private async void OnNewRequested(object? sender, EventArgs e)
    {
        if (_disposed) return;
        try
        {
            _route.SetStatus("Opening the Experience builder…");
            await _create("Create a standalone interactive Haven experience that can be saved and reopened from Experiences.");
        }
        catch (Exception exception)
        {
            if (!_disposed) _route.SetStatus("Experience creation is unavailable: " + exception.Message);
        }
    }

    private async void OnRefreshRequested(object? sender, EventArgs e) => await RefreshAsync();
    private async void OnOpenRequested(Guid instanceId) => await OpenAsync(instanceId);

    private async void OnPinRequested(Guid instanceId, bool pinned)
    {
        if (_disposed) return;
        try
        {
            await _apps.SetPinnedAsync(instanceId, pinned, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            if (!_disposed) _route.SetStatus("Could not update the pin: " + exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _route.NewRequested -= OnNewRequested;
        _route.RefreshRequested -= OnRefreshRequested;
        _route.OpenRequested -= OnOpenRequested;
        _route.PinRequested -= OnPinRequested;
        if (_openInstanceId is Guid instanceId) _ = _sessions.PersistCurrentStateAsync(instanceId, CancellationToken.None);
        _surface?.Dispose();
        _surface = null;
        _route.Dispose();
    }
}
