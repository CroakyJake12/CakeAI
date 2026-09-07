using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;
using System.Collections.Specialized;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Owns the compact product header. The shell supplies tab state and responds to
/// semantic events; this control never reaches into a shell view-model.
/// </summary>
public sealed partial class TopRail : UserControl, IDisposable
{
    private HavenEventBus? _eventBus;
    private NotificationCentre? _notificationCentre;
    private Flyout? _notificationFlyout;
    private Flyout? _appLauncherFlyout;
    private AppLauncherControl? _appLauncherControl;
    private Flyout? _searchFlyout;
    private UniversalSearchControl? _searchControl;
    private NotificationService? _notificationService;
    private bool _eventsWired;
    private bool _disposed;

    public TopRail()
    {
        InitializeComponent();
        WireControlEvents();
    }

    public TopRail(HavenEventBus eventBus) : this() => AttachEventBus(eventBus);

    public event EventHandler? HomeRequested;
    public event EventHandler? NewTabRequested;
    public event EventHandler? TabOverviewRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? ForwardRequested;
    public event EventHandler? AppsRequested;
    public event EventHandler? ActionsRequested;
    public event EventHandler? ModelRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler<string>? TabSelected;
    public event EventHandler<string>? TabCloseRequested;
    public event EventHandler<TabRenameRequestedEventArgs>? TabRenameRequested;
    public event EventHandler<TabCommandRequestedEventArgs>? TabCommandRequested;
    public event EventHandler<HavenNavigationTarget>? NotificationOpenRequested;
    public event EventHandler? NotificationSettingsRequested;

    /// <summary>Attaches the one application event bus used by the entire shell.</summary>
    public void AttachEventBus(HavenEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        if (ReferenceEquals(_eventBus, eventBus)) return;
        _eventBus = eventBus;

        Register("TopRail.Logo", LogoButton);
        Register("TopRail.Tabs.Add", AddTabButton);
        Register("TopRail.Tabs.Overview", TabViewButton);
        Register("TopRail.Actions.Back", BackButton);
        Register("TopRail.Actions.Forward", ForwardButton);
        Register("TopRail.Actions.Apps", AppsButton);
        Register("TopRail.Actions.Model", UniversalModelButton);
        Register("TopRail.Actions.Notifications", NotificationsButton);
        Register("TopRail.Actions.Search", SearchButton);
    }

    /// <summary>Rebuilds the visual tab strip from the shell's current tab snapshot.</summary>
    public void SetTabs(IReadOnlyList<TopRailTab> tabs)
    {
        TabStrip.Children.Clear();
        foreach (var tab in tabs)
            TabStrip.Children.Add(BuildTab(tab));
        SyncHavenTabs(tabs);
        Dispatcher.UIThread.Post(UpdateTabScrollButtons);
    }

    /// <summary>Opens the searchable Actions menu, including for Ctrl+K.</summary>
    public void ShowActions() => ActionToolbar.ShowActionsFlyout();

    /// <summary>Replaces the Actions catalogue with the shell's current semantic commands.</summary>
    public void SetActions(IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions) =>
        ActionToolbar.SetActions(actions);

    public void SetEditActionsHandler(Action onExecute) => ActionToolbar.SetEditActionsHandler(onExecute);

    /// <summary>
    /// Shows history controls only when the selected tab can actually navigate.
    /// Collapsed buttons release their grid width so the tab strip grows naturally.
    /// </summary>
    public void SetNavigationAvailability(bool canGoBack, bool canGoForward)
    {
        BackButton.IsVisible = canGoBack;
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsVisible = canGoForward;
        ForwardButton.IsEnabled = canGoForward;
    }

    public void SetModelSummary(string? modelName, int reasoningPercent)
    {
        var clampedEffort = Math.Clamp(reasoningPercent, 0, 100);
        UniversalModelName.Text = ModelConfigurationControl.SimplifyModelName(modelName);
        UniversalReasoningValue.Text = $"{clampedEffort}%";
        UniversalModelButton.EffortPercentage = clampedEffort;
        UniversalReasoningValue.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>Connects header unread state to Haven's actual notification service.</summary>
    public void AttachNotifications(NotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        if (ReferenceEquals(_notificationService, notifications)) return;
        if (_notificationService is not null)
            _notificationService.Notifications.CollectionChanged -= OnNotificationsChanged;
        _notificationService = notifications;
        _notificationService.Notifications.CollectionChanged += OnNotificationsChanged;
        RefreshNotificationState();
    }

    public void SetModelSelectorEnabled(bool enabled) => UniversalModelButton.IsEnabled = enabled;

    public void ShowModelFlyout(Flyout flyout) => flyout.ShowAt(UniversalModelButton);

    public void ShowUniversalSearch(
        IReadOnlyList<UniversalSearchItem> items,
        Action viewAll,
        Action openSettings)
    {
        _searchFlyout?.Hide();
        _searchControl = new UniversalSearchControl
        {
            Width = Math.Min(690, Math.Max(320, Bounds.Width - 32))
        };
        _searchControl.SetItems(items);
        _searchControl.ItemInvoked += (_, _) => _searchFlyout?.Hide();
        _searchControl.CloseRequested += (_, _) => _searchFlyout?.Hide();
        _searchControl.ViewAllRequested += (_, _) =>
        {
            _searchFlyout?.Hide();
            viewAll();
        };
        _searchControl.SettingsRequested += (_, _) =>
        {
            _searchFlyout?.Hide();
            openSettings();
        };
        _searchFlyout = new HavenDropdown
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = Avalonia.Application.Current?.TryFindResource(
                "HavenFloatingFlyoutPresenterTheme", out var theme) == true
                    ? theme as Avalonia.Styling.ControlTheme
                    : null,
            Content = _searchControl
        };
        _searchFlyout.ShowAt(SearchButton);
        _searchControl.FocusSearch();
    }

    public void UpdateUniversalSearchItems(IReadOnlyList<UniversalSearchItem> items)
    {
        _searchControl?.SetItems(items);
    }

    public void ShowNotifications()
    {
        _notificationCentre ??= CreateNotificationCentre();
        _notificationFlyout ??= new HavenDropdown
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = FloatingPresenterTheme(),
            Content = _notificationCentre
        };
        if (_notificationService is not null)
            _notificationCentre.SetNotifications(_notificationService.Notifications);
        _notificationCentre.Open();
        _notificationFlyout.ShowAt(NotificationsButton);
    }

    public void ShowAppLauncher(
        IReadOnlyList<ModeDefinition> apps,
        IReadOnlySet<Guid> pinnedIds,
        bool openInNewTab,
        Action<ModeDefinition, bool> launch,
        Action manage)
    {
        _appLauncherFlyout?.Hide();
        _appLauncherFlyout?.Content = null;
        _appLauncherControl = new AppLauncherControl();
        _appLauncherControl.Configure(
            apps,
            pinnedIds,
            openInNewTab,
            (app, newTab) =>
            {
                _appLauncherFlyout?.Hide();
                launch(app, newTab);
            },
            () =>
            {
                _appLauncherFlyout?.Hide();
                manage();
            });
        _appLauncherFlyout = new HavenDropdown
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = FloatingPresenterTheme(),
            Content = _appLauncherControl
        };
        // The platform popup still owns placement; the visible launcher body and focus are Haven.UI-owned.
        _appLauncherFlyout.ShowAt(AppsButton);
        _appLauncherControl.FocusSearch();
    }

    private void WireControlEvents()
    {
        if (_eventsWired) return;
        _eventsWired = true;

        LogoButton.Click += OnLogoClicked;
        AddTabButton.Click += OnAddTabClicked;
        TabViewButton.Click += OnTabOverviewClicked;
        BackButton.Click += OnBackClicked;
        ForwardButton.Click += OnForwardClicked;
        AppsButton.Click += OnAppsClicked;
        UniversalModelButton.Click += OnModelClicked;
        NotificationsButton.Click += OnNotificationsClicked;
        TabScrollLeftButton.Click += (_, _) => ScrollTabs(-240);
        TabScrollRightButton.Click += (_, _) => ScrollTabs(240);
        TabScroller.ScrollChanged += (_, _) => UpdateTabScrollButtons();
        SearchButton.Click += OnSearchClicked;
        ActionToolbar.ActionsClicked += OnActionsClicked;
    }

    private Control BuildTab(TopRailTab tab)
    {
        var title = new TextBlock
        {
            Text = tab.Title,
            FontSize = 14,
            FontWeight = tab.IsSelected ? FontWeight.ExtraBold : FontWeight.Bold,
            Foreground = tab.IsSelected ? ResourceBrush("HavenAccentBrush", Colors.Black) : ResourceBrush("HavenTextSecondaryBrush", Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };

        var underline = new HavenAdaptiveSurface
        {
            Height = 3,
            Width = Math.Clamp((tab.Title.Length * 7.2) + 12, 30, 170),
            Margin = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(2),
            Background = tab.IsSelected
                ? ResourceBrush("HavenAccentBrush", Colors.Black)
                : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var titleAndUnderline = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { title, underline }
        };

        var button = new HavenTabButton
        {
            Content = titleAndUnderline,
            IsSelected = tab.IsSelected,
            MinWidth = 72,
            MaxWidth = 230,
            Height = 48,
            Padding = new Thickness(12, 5, 12, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Tag = tab.Key
        };
        ToolTip.SetTip(button, tab.Title);
        button.Click += (_, _) => InvokeTabSelection(tab.Key);
        button.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
                return;
            args.Handled = true;
            BuildTabMenu(tab).ShowAt(button);
        };
        return button;
    }

    private Flyout BuildTabMenu(TopRailTab tab)
    {
        var rename = new HavenDropdownItemButton
        {
            Content = BuildMenuContent("edit", "Rename tab"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var close = new HavenDropdownItemButton
        {
            Content = BuildMenuContent("close", "Close tab"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = tab.IsCloseable
        };
        var menu = new HavenDropdown
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new HavenDropdownCard
            {
                Width = 286,
                MinWidth = 286,
                Padding = new Thickness(8),
                Child = new StackPanel { Spacing = 5, Children = { rename, close } }
            }
        };
        rename.Click += (_, _) =>
        {
            menu.Hide();
            ShowRenameFlyout(tab);
        };
        close.Click += (_, _) =>
        {
            menu.Hide();
            Fire("TopRail.Tabs.CloseTab");
            TabCloseRequested?.Invoke(this, tab.Key);
        };
        return menu;
    }

    private void ShowRenameFlyout(TopRailTab tab)
    {
        var input = new HavenTextInput
        {
            Text = tab.Title,
            MinWidth = 240,
            SelectionStart = 0,
            SelectionEnd = tab.Title.Length
        };
        var save = new HavenPrimaryButton { Content = "Rename", HorizontalAlignment = HorizontalAlignment.Stretch };
        var flyout = new HavenDropdown
        {
            Placement = PlacementMode.Bottom,
            Content = new HavenDropdownCard
            {
                Width = 310,
                MinWidth = 310,
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Rename tab", FontSize = 20, FontWeight = FontWeight.ExtraBold },
                        input,
                        save
                    }
                }
            }
        };
        save.Click += (_, _) =>
        {
            var title = input.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title)) return;
            flyout.Hide();
            TabRenameRequested?.Invoke(this, new TabRenameRequestedEventArgs(tab.Key, title));
        };
        input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            args.Handled = true;
        };
        flyout.ShowAt(this);
        input.Focus();
    }

    private static Control BuildMenuContent(string icon, string label) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 9,
        Children =
        {
            new HavenIcon { IconKey = icon, Width = 15, Height = 15, Opacity = 0.72 },
            new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }
        }
    };

    private static Avalonia.Styling.ControlTheme? FloatingPresenterTheme() =>
        Avalonia.Application.Current?.TryFindResource(
            "HavenFloatingFlyoutPresenterTheme", out var theme) == true
            ? theme as Avalonia.Styling.ControlTheme
            : null;

    private void OnLogoClicked(object? sender, RoutedEventArgs e) => InvokeHomeAction();
    private void OnAddTabClicked(object? sender, RoutedEventArgs e) => InvokeNewTabAction();
    private void OnTabOverviewClicked(object? sender, RoutedEventArgs e) => InvokeTabOverviewAction();
    private void OnBackClicked(object? sender, RoutedEventArgs e) => InvokeBackAction();
    private void OnForwardClicked(object? sender, RoutedEventArgs e) => InvokeForwardAction();
    private void OnAppsClicked(object? sender, RoutedEventArgs e) => InvokeAppsAction();
    private void OnModelClicked(object? sender, RoutedEventArgs e) => InvokeModelAction();
    private void OnSearchClicked(object? sender, RoutedEventArgs e) => InvokeSearchAction();
    private void OnNotificationsClicked(object? sender, RoutedEventArgs e) => InvokeNotificationsAction();
    private NotificationCentre CreateNotificationCentre()
    {
        var centre = new NotificationCentre { Height = 520 };
        centre.CloseRequested += (_, _) => _notificationFlyout?.Hide();
        centre.SettingsClicked += (_, _) =>
        {
            _notificationFlyout?.Hide();
            NotificationSettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        centre.DismissRequested += (_, id) => _notificationService?.Dismiss(id);
        centre.OpenRequested += (_, target) =>
        {
            _notificationFlyout?.Hide();
            NotificationOpenRequested?.Invoke(this, target);
        };
        return centre;
    }

    private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshNotificationState();

    private void RefreshNotificationState()
    {
        var count = _notificationService?.Notifications.Count ?? 0;
        var colour = NotificationUrgencyColour(count);
        var brush = new SolidColorBrush(colour);
        NotificationBadge.IsVisible = count > 0;
        NotificationBadge.Background = brush;
        NotificationBadgeText.Text = Math.Min(count, 30).ToString(System.Globalization.CultureInfo.InvariantCulture);
        NotificationsButton.Foreground = count > 0
            ? brush
            : ResourceBrush("HavenTextPrimaryBrush", Colors.Black);
        if (_notificationCentre is not null && _notificationService is not null)
            _notificationCentre.SetNotifications(_notificationService.Notifications);
    }

    private void ScrollTabs(double delta)
    {
        var maximum = Math.Max(0, TabScroller.Extent.Width - TabScroller.Viewport.Width);
        TabScroller.Offset = new Vector(Math.Clamp(TabScroller.Offset.X + delta, 0, maximum), 0);
        UpdateTabScrollButtons();
    }

    private void UpdateTabScrollButtons()
    {
        var availability = GetTabScrollAvailability(TabScroller.Offset.X, TabScroller.Extent.Width, TabScroller.Viewport.Width);
        TabScrollLeftButton.IsVisible = availability.CanScrollLeft;
        TabScrollRightButton.IsVisible = availability.CanScrollRight;
    }

    internal static (bool CanScrollLeft, bool CanScrollRight) GetTabScrollAvailability(double offset, double extent, double viewport) =>
        (offset > 0.5, extent - viewport - offset > 0.5);

    internal static Color EffortColour(int reasoningPercent)
    {
        var t = Math.Clamp((reasoningPercent - 20) / 80d, 0d, 1d);
        return Lerp(Color.Parse("#FFFBC02D"), Color.Parse("#FFFF6D00"), t);
    }

    internal static Color NotificationUrgencyColour(int unreadCount)
    {
        var t = Math.Clamp(unreadCount / 30d, 0d, 1d);
        return Lerp(Color.Parse("#FFFFD54F"), Color.Parse("#FFFF1744"), t);
    }

    private static Color Lerp(Color from, Color to, double amount) => Color.FromArgb(
        255,
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    private void OnActionsClicked(object? sender, EventArgs e) => InvokeActionsAction();
    private void Register(string name, Control control)
    {
        _eventBus?.RegisterElement(name, control);
        _eventBus?.WirePointerEvents(name, control);
    }

    private void Fire(string name) => _eventBus?.Fire(name);

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notificationCentre?.Dispose();
        if (_notificationService is not null)
            _notificationService.Notifications.CollectionChanged -= OnNotificationsChanged;
        _notificationService = null;
        _appLauncherFlyout?.Hide();
        _searchFlyout?.Hide();
        ActionToolbar.Dispose();
    }
}

public sealed record TopRailTab(
    string Key,
    string Title,
    string IconKey,
    bool IsSelected,
    bool IsCloseable,
    Guid? GroupId = null,
    string? GroupName = null,
    bool IsGroupCollapsed = false);

public sealed class TabRenameRequestedEventArgs(string key, string title) : EventArgs
{
    public string Key { get; } = key;
    public string Title { get; } = title;
}

public sealed class TabCommandRequestedEventArgs(string key, string command) : EventArgs
{
    public string Key { get; } = key;
    public string Command { get; } = command;
}
