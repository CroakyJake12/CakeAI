using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Core;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Slide-in notification panel anchored to the right edge of the window.
/// Opens when the bell icon is clicked and displays priority and unread
/// notification sections with search and settings.
/// </summary>
public sealed class NotificationCentre : Grid, IDisposable
{
    private readonly Border _panel;
    private readonly TextBox _searchBox;
    private readonly StackPanel _prioritySection;
    private readonly StackPanel _unreadSection;
    private readonly StackPanel _priorityItems;
    private readonly StackPanel _unreadItems;
    private readonly TextBlock _emptyState;
    private readonly Button _settingsButton;
    private bool _isOpen;
    private bool _disposed;

    public NotificationCentre()
    {
        IsHitTestVisible = false;
        MinWidth = 400;

        _searchBox = new HavenTextInput { PlaceholderText = "Search notifications", MinWidth = 260 };
        _searchBox.TextChanged += OnSearchChanged;

        _priorityItems = new StackPanel { Spacing = 6 };
        _unreadItems = new StackPanel { Spacing = 6 };

        _prioritySection = BuildSection("Live", _priorityItems);
        _unreadSection = BuildSection("Recent", _unreadItems);

        _emptyState = new TextBlock
        {
            Text = "No notifications",
            Classes = { "muted" },
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0),
            IsVisible = false
        };

        _settingsButton = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = "settings", Width = 16, Height = 16, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Notification Settings", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        _settingsButton.Classes.Add("sidebar");
        _settingsButton.Click += (_, _) => SettingsClicked?.Invoke(this, EventArgs.Empty);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                new TextBlock
                {
                    Text = "Notifications",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
            }
        };

        var closeBtn = new HavenButton
        {
            Content = new HavenIcon { IconKey = "close", Width = 14, Height = 14 },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6)
        };
        closeBtn.Classes.Add("chrome");
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(closeBtn);

        var content = new StackPanel
        {
            Width = 380,
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                header,
                _searchBox,
                new ScrollViewer
                {
                    MaxHeight = 320,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Spacing = 14,
                        Children = { _prioritySection, _unreadSection, _emptyState }
                    }
                },
                new HavenAdaptiveSurface { Height = 1, Background = ResourceBrush("HavenLineBrush", Color.FromArgb(30, 0, 0, 0)) },
                _settingsButton
            }
        };

        _panel = new HavenAdaptiveSurface
        {
            Width = 400,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = ResourceBrush("HavenElevatedBrush", Colors.White),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(30, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Child = content,
            Opacity = 0,
            RenderTransform = new TranslateTransform(400, 0)
        };

        Children.Add(_panel);
    }

    /// <summary>
    /// Raised when the user clicks "Notification Settings".
    /// </summary>
    public event EventHandler? SettingsClicked;

    /// <summary>Raised when the panel's own close button is used.</summary>
    public event EventHandler? CloseRequested;
    public event EventHandler<Guid>? DismissRequested;
    public event EventHandler<HavenNavigationTarget>? OpenRequested;

    /// <summary>
    /// Returns whether the panel is currently open.
    /// </summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// Opens the notification centre with a slide-in animation.
    /// </summary>
    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;
        IsHitTestVisible = true;
        _panel.Opacity = 1;
        _panel.RenderTransform = new TranslateTransform(0, 0);
    }

    /// <summary>
    /// Closes the notification centre with a slide-out animation.
    /// </summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _panel.Opacity = 0;
        _panel.RenderTransform = new TranslateTransform(400, 0);
        IsHitTestVisible = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Toggles the panel open or closed.
    /// </summary>
    public void Toggle() { if (_isOpen) Close(); else Open(); }

    /// <summary>
    /// Adds a priority notification to the panel.
    /// </summary>
    public void AddPriorityNotification(string title, string source, string iconKey = "info")
    {
        _priorityItems.Children.Add(BuildNotificationItem(Guid.Empty, title, source, iconKey, isPriority: true));
        UpdateEmptyState();
    }

    /// <summary>
    /// Adds an unread notification to the panel.
    /// </summary>
    public void AddUnreadNotification(string title, string source, string iconKey = "info")
    {
        _unreadItems.Children.Add(BuildNotificationItem(Guid.Empty, title, source, iconKey, isPriority: false));
        UpdateEmptyState();
    }

    /// <summary>
    /// Clears all notifications.
    /// </summary>
    public void ClearAll()
    {
        _priorityItems.Children.Clear();
        _unreadItems.Children.Clear();
        UpdateEmptyState();
    }

    public void SetNotifications(IEnumerable<ToastNotification> notifications)
    {
        _priorityItems.Children.Clear();
        _unreadItems.Children.Clear();
        foreach (var notification in notifications.OrderByDescending(item => item.CreatedAt))
        {
            var priority = notification.IsLive;
            var icon = notification.Kind switch
            {
                ToastKind.Success => "check",
                ToastKind.Warning => "warning",
                ToastKind.Error => "warning",
                _ => "info"
            };
            var source = string.IsNullOrWhiteSpace(notification.SourceName)
                ? notification.Message
                : notification.SourceName + " · " + notification.Message;
            var item = BuildNotificationItem(notification.Id, notification.Title, source, icon, priority, notification.Target);
            (priority ? _priorityItems : _unreadItems).Children.Add(item);
        }
        UpdateEmptyState();
    }

    private static StackPanel BuildSection(string title, StackPanel items)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = title.ToUpperInvariant(),
                    Classes = { "eyebrow" },
                    FontSize = 10,
                    Margin = new Thickness(2, 0, 0, 4)
                },
                items
            }
        };
    }

    private Border BuildNotificationItem(Guid id, string title, string source, string iconKey, bool isPriority, HavenNavigationTarget? target = null)
    {
        var icon = new HavenIcon
        {
            IconKey = iconKey,
            Width = 18,
            Height = 18,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dismissBtn = new HavenButton
        {
            Content = new HavenIcon { IconKey = "close", Width = 12, Height = 12 },
            Padding = new Thickness(4),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        dismissBtn.Classes.Add("chrome");

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
        };
        grid.Children.Add(icon);

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = source,
                    Classes = { "muted" },
                    FontSize = 10,
                    Margin = new Thickness(0, 1, 0, 0)
                }
            }
        };
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        Grid.SetColumn(dismissBtn, 2);
        grid.Children.Add(dismissBtn);

        var border = new HavenAdaptiveSurface
        {
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(10),
            Background = ResourceBrush(isPriority ? "HavenAccentSoftBrush" : "HavenOverlaySurfaceBrush", Colors.Transparent),
            BorderBrush = ResourceBrush("HavenBorderSubtleBrush", Colors.Transparent),
            BorderThickness = new Thickness(1),
            Child = grid
        };

        dismissBtn.Click += (_, _) =>
        {
            if (id != Guid.Empty)
                DismissRequested?.Invoke(this, id);
            var parent = border.Parent as Panel;
            parent?.Children.Remove(border);
            UpdateEmptyState();
        };
        if (target is not null)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerPressed += (_, e) =>
            {
                OpenRequested?.Invoke(this, target);
                e.Handled = true;
            };
        }

        return border;
    }

    private void UpdateEmptyState()
    {
        var priorityVisible = _priorityItems.Children.Any(child => child.IsVisible);
        var unreadVisible = _unreadItems.Children.Any(child => child.IsVisible);
        _prioritySection.IsVisible = priorityVisible;
        _unreadSection.IsVisible = unreadVisible;
        _emptyState.Text = string.IsNullOrWhiteSpace(_searchBox.Text) ? "No notifications" : "No notifications match this search";
        _emptyState.IsVisible = !priorityVisible && !unreadVisible;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var query = _searchBox.Text?.Trim() ?? string.Empty;
        FilterSection(_priorityItems, query);
        FilterSection(_unreadItems, query);
        UpdateEmptyState();
    }

    private static void FilterSection(StackPanel items, string query)
    {
        foreach (var child in items.Children.OfType<Border>())
        {
            if (child.Child is Grid grid)
            {
                var titleBlock = grid.Children.OfType<StackPanel>().FirstOrDefault()
                    ?.Children.OfType<TextBlock>().FirstOrDefault();
                var title = titleBlock?.Text ?? string.Empty;
                child.IsVisible = string.IsNullOrWhiteSpace(query) ||
                                  title.Contains(query, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _searchBox.TextChanged -= OnSearchChanged;
    }
}
