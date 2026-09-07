using Avalonia;
using Avalonia.Controls;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task OpenMobileContextDrawerAsync()
    {
        if (_mobileDrawerContent is null)
            return;

        await RefreshRecentsAsync(CancellationToken.None);
        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, RecentHeading);

        var conversations = PinnedConversations.Concat(RecentConversations).ToArray();
        foreach (var item in conversations)
        {
            var conversation = item;
            var button = MobileListButton(
                conversation.Definition.Title,
                conversation.Definition.UpdatedAt.LocalDateTime.ToString("g"),
                "chat");
            button.Click += async (_, _) =>
            {
                CloseMobileDrawer();
                await OpenConversationAsync(conversation);
            };
            _mobileDrawerContent.Children.Add(button);
        }

        if (conversations.Length == 0)
        {
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = "No conversations here yet.",
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4)
            });
        }

        if (IsProjectOpen)
        {
            AddDrawerHeading(_mobileDrawerContent, ActiveProjectName);
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = ActiveProjectRoot,
                FontSize = 11,
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4, 0, 4, 4)
            });

            foreach (var file in ActiveProjectFiles.Take(100))
            {
                _mobileDrawerContent.Children.Add(MobileListButton(
                    DisplayObject(file),
                    "Project content",
                    "file"));
            }
        }

        OpenMobileDrawer();
    }

    private async Task ShowMobileLauncherAsync()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Haven apps");

        var modes = await _modeRegistry.GetModesAsync(CancellationToken.None);
        foreach (var mode in modes.OrderBy(item => DisplayObject(item), StringComparer.CurrentCultureIgnoreCase))
        {
            var selected = mode;
            var button = MobileListButton(
                DisplayObject(mode),
                "Haven app",
                string.IsNullOrWhiteSpace(mode.Key) ? "apps" : mode.Key);
            button.Click += async (_, _) =>
            {
                CloseMobileDrawer();
                await LaunchAppAsync(selected, openInNewTab: false);
            };
            _mobileDrawerContent.Children.Add(button);
        }

        OpenMobileDrawer();
    }

    private void ShowMobileActions()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Actions");

        AddMobileAction(
            "Models",
            "Browse device-aware GGUF recommendations or import local models.",
            "cpu",
            () =>
            {
                CloseMobileDrawer();
                LaunchAndroidModelRecommendations();
            });

        foreach (var item in AllCommandItems)
        {
            var action = item;
            var button = MobileListButton(
                action.Name,
                action.Description,
                ActionIcon(action.Name));
            button.IsEnabled = action.RunCommand.CanExecute(null);
            button.Click += (_, _) =>
            {
                CloseMobileDrawer();
                if (action.RunCommand.CanExecute(null))
                    action.RunCommand.Execute(null);
            };
            _mobileDrawerContent.Children.Add(button);
        }

        OpenMobileDrawer();
    }

    private void AddMobileAction(string title, string detail, string icon, Action action)
    {
        if (_mobileDrawerContent is null)
            return;

        var button = MobileListButton(title, detail, icon);
        button.Click += (_, _) => action();
        _mobileDrawerContent.Children.Add(button);
    }

    private async Task ShowMobileInstalledAppsAsync()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Connect an app");
        _mobileDrawerContent.Children.Add(new TextBlock
        {
            Text = "Selecting an app adds it to chat context. Haven will use supported Android APIs or documented web APIs and will not simply launch the app.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = ResourceBrush("HavenTextSoftBrush"),
            Margin = new Thickness(4, 0, 4, 8)
        });

        var apps = await GetInstalledAndroidAppsAsync();
        foreach (var app in apps)
        {
            var selected = app;
            var button = MobileListButton(selected.Label, selected.PackageName, "apps");
            button.Click += async (_, _) =>
            {
                CloseMobileDrawer();
                await ConnectAndroidAppToChatAsync(selected);
            };
            _mobileDrawerContent.Children.Add(button);
        }

        OpenMobileDrawer();
    }

    private void ShowMobileNotifications()
    {
        if (_mobileDrawerContent is null)
            return;

        _ = global::Haven.Android.AndroidRuntimePermissions
            .EnsureNotificationsPermissionAsync(CancellationToken.None);

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Notifications");

        if (Notifications.Count == 0)
        {
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = "You’re all caught up.",
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4)
            });
        }

        foreach (var notification in Notifications)
        {
            var row = MobileListButton(notification.Title, notification.Message, "notification");
            row.Click += (_, _) => _notifications.Dismiss(notification.Id);
            _mobileDrawerContent.Children.Add(row);
        }

        OpenMobileDrawer();
    }

    private void ShowMobileYou()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "You");

        var search = MobileListButton("Search Haven", "Apps, chats, tasks, tabs and actions", "search");
        search.Click += (_, _) =>
        {
            CloseMobileDrawer();
            OpenCommandPalette();
        };
        _mobileDrawerContent.Children.Add(search);

        AddDrawerHeading(_mobileDrawerContent, "Tabs");
        foreach (var tab in OpenTabs)
        {
            var selected = tab;
            var row = MobileListButton(tab.Title, ReferenceEquals(tab, SelectedTab) ? "Current tab" : "Open tab", "window");
            row.Click += (_, _) =>
            {
                SelectedTab = selected;
                CloseMobileDrawer();
            };
            _mobileDrawerContent.Children.Add(row);
        }

        AddDrawerHeading(_mobileDrawerContent, "Notifications");
        if (Notifications.Count == 0)
        {
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = "You’re all caught up.",
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4)
            });
        }

        foreach (var notification in Notifications)
        {
            var row = MobileListButton(notification.Title, notification.Message, "notification");
            row.Click += (_, _) => _notifications.Dismiss(notification.Id);
            _mobileDrawerContent.Children.Add(row);
        }

        OpenMobileDrawer();
    }
}
