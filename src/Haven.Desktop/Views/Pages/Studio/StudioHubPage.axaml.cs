using Avalonia.Controls;

namespace Haven.Desktop.Views.Pages.Studio;

public sealed partial class StudioHubPage : UserControl, IDisposable
{
    private readonly Func<StudioCreationIntent, Task> _navigate;
    private readonly StudioHubHavenScene _route;
    private bool _disposed;

    public StudioHubPage(Func<StudioCreationIntent, Task> navigate)
    {
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        InitializeComponent();
        _route = new StudioHubHavenScene();
        Scene.Root = _route.Root;
        _route.CreationRequested += OnCreationRequested;
    }

    internal StudioHubHavenScene Route => _route;

    private async void OnCreationRequested(StudioCreationIntent intent)
    {
        if (_disposed) return;
        _route.SetBusy(intent.Id, true);
        _route.SetStatus($"Opening {intent.Name}…");
        try
        {
            await _navigate(intent);
            if (!_disposed) _route.SetStatus($"Opened {intent.Name}.");
        }
        catch (Exception exception)
        {
            if (!_disposed) _route.SetStatus($"{intent.Name} is unavailable: {exception.Message}");
        }
        finally
        {
            if (!_disposed) _route.SetBusy(intent.Id, false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _route.CreationRequested -= OnCreationRequested;
        _route.Dispose();
    }
}
