using Avalonia;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class TopRail
{
    private TopRailFinalScene? _havenOwnedScene;
    private IReadOnlyList<TopRailTab> _havenTabs = [];
    private bool _havenAnchorSubscriptionsWired;
    private TaskbarShelfState _shelfState = TaskbarShelfState.Standard;

    internal HavenSceneControl SceneHost => HavenScene;
    internal TopRailFinalScene? HavenOwnedScene => _havenOwnedScene;
    internal TaskbarShelfState ShelfState => _shelfState;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureHavenOwnedScene();
        UpdateTaskbarShelfState(Bounds.Width);
    }

    private void EnsureHavenOwnedScene()
    {
        if (_havenOwnedScene is null)
        {
            _havenOwnedScene = new TopRailFinalScene();
            TaskbarShelfLayout.Apply(_havenOwnedScene, _shelfState);
            HavenScene.Root = _havenOwnedScene.Root;
            _havenOwnedScene.HomeRequested += (_, _) => InvokeHomeAction();
            _havenOwnedScene.NewTabRequested += (_, _) => InvokeNewTabAction();
            _havenOwnedScene.TabOverviewRequested += (_, _) => InvokeTabOverviewAction();
            _havenOwnedScene.BackRequested += (_, _) => InvokeBackAction();
            _havenOwnedScene.ForwardRequested += (_, _) => InvokeForwardAction();
            _havenOwnedScene.AppsRequested += (_, _) => InvokeAppsAction();
            _havenOwnedScene.ActionsRequested += (_, _) => InvokeActionsAction();
            _havenOwnedScene.ModelRequested += (_, _) => InvokeModelAction();
            _havenOwnedScene.NotificationsRequested += (_, _) => InvokeNotificationsAction();
            _havenOwnedScene.SearchRequested += (_, _) => InvokeSearchAction();
            _havenOwnedScene.TabSelected += (_, key) => InvokeTabSelection(key);
            _havenOwnedScene.TabRenameRequested += (_, tab) => ShowRenameFlyout(tab);
            _havenOwnedScene.TabCommandRequested += (_, request) => TabCommandRequested?.Invoke(this, request);
            _havenOwnedScene.TabCloseRequested += (_, key) =>
            {
                Fire("TopRail.Tabs.CloseTab");
                TabCloseRequested?.Invoke(this, key);
            };
            SizeChanged += (_, _) => UpdateTaskbarShelfState(Bounds.Width);
        }

        WireHavenAnchorSubscriptions();
        SyncHavenSceneFromAnchors();
        _havenOwnedScene.SetTabs(_havenTabs);
    }

    private void UpdateTaskbarShelfState(double width)
    {
        if (_havenOwnedScene is null || width <= 0d) return;
        var next = TaskbarShelfLayout.Resolve(width);
        if (next == _shelfState) return;
        _shelfState = next;
        TaskbarShelfLayout.Apply(_havenOwnedScene, next);
    }

    private void SyncHavenTabs(IReadOnlyList<TopRailTab> tabs)
    {
        _havenTabs = tabs.ToArray();
        _havenOwnedScene?.SetTabs(_havenTabs);
    }

    private void WireHavenAnchorSubscriptions()
    {
        if (_havenAnchorSubscriptionsWired) return;
        _havenAnchorSubscriptionsWired = true;
        BackButton.PropertyChanged += OnHavenAnchorPropertyChanged;
        ForwardButton.PropertyChanged += OnHavenAnchorPropertyChanged;
        UniversalModelButton.PropertyChanged += OnHavenAnchorPropertyChanged;
        UniversalModelName.PropertyChanged += OnHavenAnchorPropertyChanged;
        UniversalReasoningValue.PropertyChanged += OnHavenAnchorPropertyChanged;
        NotificationBadge.PropertyChanged += OnHavenAnchorPropertyChanged;
        NotificationBadgeText.PropertyChanged += OnHavenAnchorPropertyChanged;
    }

    private void OnHavenAnchorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) => SyncHavenSceneFromAnchors();

    private void SyncHavenSceneFromAnchors()
    {
        var scene = _havenOwnedScene;
        if (scene is null) return;
        scene.SetNavigationAvailability(BackButton.IsVisible && BackButton.IsEnabled, ForwardButton.IsVisible && ForwardButton.IsEnabled);
        scene.SetModelSummary(UniversalModelName.Text, ParseEffort(UniversalReasoningValue.Text));
        scene.SetModelSelectorEnabled(UniversalModelButton.IsEnabled);
        var unread = 0;
        if (NotificationBadge.IsVisible)
            int.TryParse(NotificationBadgeText.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out unread);
        scene.SetNotificationCount(unread);
    }

    private static int ParseEffort(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return int.TryParse(text.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 100)
            : 0;
    }

}
