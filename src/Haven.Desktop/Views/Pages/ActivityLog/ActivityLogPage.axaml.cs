using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.ActivityLog;

/// <summary>
/// Desktop adapter that keeps Activity Log data/event behavior while rendering the
/// surface with the canonical Haven.UI scene backend.
/// </summary>
public sealed partial class ActivityLogPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IConversationRepository _conversations;
    private readonly ActivityLogHavenScene _scene;
    private readonly HashSet<string> _registeredItemNames = new(StringComparer.Ordinal);
    private IReadOnlyList<Conversation> _allItems = [];
    private string _searchQuery = string.Empty;
    private bool _disposed;

    public ActivityLogPage(HavenEventBus bus, IConversationRepository conversations)
    {
        _bus = bus;
        _conversations = conversations;

        InitializeComponent();
        _scene = new ActivityLogHavenScene();
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "ActivityLogPage");
        AutomationProperties.SetName(this, "Activity Log");
        AutomationProperties.SetAutomationId(Scene, "ActivityLogScene");
        AutomationProperties.SetName(Scene, "Activity Log conversations");
        Content = Scene;

        Loaded += OnLoaded;
        Scene.PointerMoved += (_, args) => FirePointerEventAt(args.GetPosition(Scene), "Move");
        Scene.PointerWheelChanged += (_, args) => FirePointerEventAt(args.GetPosition(Scene), "Wheel");
        _scene.RefreshRequested += OnRefreshRequested;
        _scene.SearchChanged += OnSearchChanged;
        _scene.ItemInvoked += OnItemInvoked;
        _scene.PointerEventRequested += OnPointerEventRequested;
        _bus.RegisterElement("ActivityLog.Actions.Refresh", Scene);
    }

    public HavenSceneControl Scene { get; }

    internal ActivityLogHavenScene HavenScene => _scene;

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = RefreshAsync();

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        _bus.Fire("ActivityLog.Actions.Refresh");
        await RefreshAsync();
    }

    private void OnSearchChanged(object? sender, string query)
    {
        _searchQuery = query;
        _bus.Fire("ActivityLog.Search.QueryChanged");
        FilterAndDisplay();
    }

    private void OnItemInvoked(object? sender, string qualifiedName) => _bus.Fire($"{qualifiedName}.Click");

    private void OnPointerEventRequested(object? sender, string eventName) => _bus.Fire(eventName);

    private void FirePointerEventAt(Avalonia.Point point, string suffix)
    {
        var qualifiedName = _scene.GetQualifiedActionAt(point.X, point.Y);
        if (qualifiedName is not null) _bus.Fire($"{qualifiedName}.{suffix}");
    }

    private async Task RefreshAsync()
    {
        _scene.SetStatus("Loading…");
        try
        {
            _allItems = await _conversations.GetRecentAsync(null, 50, CancellationToken.None);
            FilterAndDisplay();
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"Failed to load: {ex.Message}");
        }
    }

    private void FilterAndDisplay()
    {
        var items = string.IsNullOrWhiteSpace(_searchQuery)
            ? _allItems
            : _allItems.Where(c => c.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        var rows = items
            .Select(conversation => new ActivityLogRow(conversation.Title, conversation.Mode.ToString(), conversation.UpdatedAt))
            .ToArray();
        _scene.SetItems(rows);
        RegisterItemNames(rows.Length);
        _scene.SetStatus($"{rows.Length} conversation{(rows.Length == 1 ? "" : "s")}");
    }

    private void RegisterItemNames(int count)
    {
        foreach (var qualifiedName in _registeredItemNames) _bus.UnregisterElement(qualifiedName);
        _registeredItemNames.Clear();

        for (var index = 0; index < count; index++)
        {
            var qualifiedName = $"ActivityLog.List.Item{index}";
            _registeredItemNames.Add(qualifiedName);
            _bus.RegisterElement(qualifiedName, Scene);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= OnLoaded;
        _scene.RefreshRequested -= OnRefreshRequested;
        _scene.SearchChanged -= OnSearchChanged;
        _scene.ItemInvoked -= OnItemInvoked;
        _scene.PointerEventRequested -= OnPointerEventRequested;
        _bus.UnregisterElement("ActivityLog.Actions.Refresh");
        foreach (var qualifiedName in _registeredItemNames) _bus.UnregisterElement(qualifiedName);
        _registeredItemNames.Clear();
        _scene.Dispose();
        Scene.Root = null;
    }
}
