using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>Polished Haven-owned body for the contextual Actions catalogue.</summary>
internal sealed class ActionsFlyoutFinalScene
{
    private static readonly string[] CategoryOrder =
    [
        "Pinned", "Recommended", "General", "Chat", "Study", "Tasks", "Studio", "Browser",
        "Plan", "Data", "Media", "File", "View", "Tools", "Help"
    ];

    private readonly Container _sections;
    private readonly List<Button> _actionButtons = [];
    private IReadOnlyList<DynamicActionToolbar.ToolbarAction> _actions = [];

    public ActionsFlyoutFinalScene()
    {
        Root = new Page { Name = "HeaderDropdown.Actions.Root", Layout = HavenLayout.Grid, Rows = "38px 58px 1fr 56px" };
        Root.SetValue(HavenProperties.Width, HavenLength.Px(690));
        Root.SetValue(HavenProperties.Height, HavenLength.Px(650));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px 24px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        Root.SetValue(HavenProperties.Background, "SurfaceRaised");
        Root.SetValue(HavenProperties.BorderColor, "Border");
        Root.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Root.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(30)));
        Root.SetValue(HavenProperties.Shadow, "Card");

        var title = new Text { Name = "HeaderDropdown.Actions.Title", Content = "Actions", Level = TextLevel.H1 };
        title.SetValue(HavenProperties.Row, 0);
        title.SetValue(HavenProperties.Height, HavenLength.Px(38));
        title.SetValue(HavenProperties.FontSize, 28d);
        title.SetValue(HavenProperties.FontWeight, 800);
        title.SetValue(HavenProperties.Foreground, "TextPrimary");
        Root.Add(title);

        var searchHost = new Container { Name = "HeaderDropdown.Actions.SearchHost", Layout = HavenLayout.Overlay };
        searchHost.SetValue(HavenProperties.Row, 1);
        searchHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        searchHost.SetValue(HavenProperties.Height, HavenLength.Px(58));
        searchHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(58));

        Search = new Input { Name = "HeaderDropdown.Actions.Search", Placeholder = "Search Actions" };
        Search.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Search.SetValue(HavenProperties.Height, HavenLength.Px(58));
        Search.SetValue(HavenProperties.MinHeight, HavenLength.Px(58));
        Search.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 20px 0px 54px"));
        Search.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(29)));
        Search.SetValue(HavenProperties.Background, "Surface");
        Search.SetValue(HavenProperties.Foreground, "AccentSecondary");
        Search.SetValue(HavenProperties.FontSize, 15d);
        Search.Accessibility.AccessibleName = "Search Actions";
        searchHost.Add(Search);

        var searchIcon = new Icon { Name = "HeaderDropdown.Actions.SearchIcon", Key = "search" };
        searchIcon.SetValue(HavenProperties.Width, HavenLength.Px(24));
        searchIcon.SetValue(HavenProperties.Height, HavenLength.Px(24));
        searchIcon.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 18px"));
        searchIcon.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        searchIcon.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        searchIcon.SetValue(HavenProperties.Foreground, "AccentSecondary");
        searchIcon.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        searchIcon.SetValue(HavenProperties.ZIndex, 2);
        searchHost.Add(searchIcon);
        Root.Add(searchHost);

        _sections = new Container { Name = "HeaderDropdown.Actions.Sections", Layout = HavenLayout.Vertical };
        _sections.SetValue(HavenProperties.Row, 2);
        _sections.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _sections.SetValue(HavenProperties.Height, HavenLength.Px(412));
        _sections.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _sections.SetValue(HavenProperties.Clip, true);
        _sections.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        _sections.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 2px 8px 2px"));
        Root.Add(_sections);

        EditButton = new Button
        {
            Name = "HeaderDropdown.Actions.Edit",
            Variant = ButtonVariant.Primary,
            IconKey = "settings",
            Content = "Edit Actions"
        };
        EditButton.SetValue(HavenProperties.Row, 3);
        EditButton.SetValue(HavenProperties.Width, HavenLength.Px(220));
        EditButton.SetValue(HavenProperties.Height, HavenLength.Px(56));
        EditButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(56));
        EditButton.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        EditButton.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 22px"));
        EditButton.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(28)));
        EditButton.SetValue(HavenProperties.FontSize, 14d);
        EditButton.Accessibility.Description = "Open Actions management";
        Root.Add(EditButton);

        Search.TextChanged += (_, _) => Rebuild();
        EditButton.Invoked += (_, _) => EditRequested?.Invoke();
    }

    public Page Root { get; }
    public Input Search { get; }
    public Button EditButton { get; }
    public IReadOnlyList<Button> ActionButtons => _actionButtons;

    public event Action<DynamicActionToolbar.ToolbarAction>? ActionRequested;
    public event Action? EditRequested;

    public void SetActions(IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions)
    {
        _actions = actions.ToArray();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in _sections.Children.ToArray())
            _sections.Remove(child);
        _actionButtons.Clear();

        var query = Search.Text.Trim();
        var matches = _actions
            .Where(action => string.IsNullOrWhiteSpace(query)
                             || action.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || action.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || action.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var category in CategoryOrder)
        {
            var actions = matches.Where(action => ResolveCategory(action) == category).ToArray();
            AddSection(category, actions);
        }

        if (_actionButtons.Count == 0)
        {
            var empty = new Text { Name = "HeaderDropdown.Actions.Empty", Content = "No Actions match this search in the current App." };
            empty.SetValue(HavenProperties.FontSize, 14d);
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            empty.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px 6px"));
            _sections.Add(empty);
        }
    }

    private void AddSection(string title, IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions)
    {
        if (actions.Count == 0) return;

        var section = new Container
        {
            Name = $"HeaderDropdown.Actions.Section.{string.Concat(title.Where(char.IsLetterOrDigit))}",
            Layout = HavenLayout.Vertical
        };
        section.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        section.SetValue(HavenProperties.Gap, HavenLength.Px(9));

        var heading = new Text { Content = title };
        heading.SetValue(HavenProperties.Height, HavenLength.Px(24));
        heading.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 6px"));
        heading.SetValue(HavenProperties.FontSize, 14d);
        heading.SetValue(HavenProperties.FontWeight, 800);
        heading.SetValue(HavenProperties.Foreground, "TextSecondary");
        section.Add(heading);

        const int columns = 3;
        var rows = Math.Max(1, (int)Math.Ceiling(actions.Count / (double)columns));
        var grid = new Container
        {
            Name = $"{section.Name}.Grid",
            Layout = HavenLayout.Grid,
            Columns = "1fr 1fr 1fr",
            Rows = string.Join(' ', Enumerable.Repeat("72px", rows))
        };
        grid.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        grid.SetValue(HavenProperties.Gap, HavenLength.Px(10));

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var tile = BuildActionTile(action, title);
            tile.SetValue(HavenProperties.Column, index % columns);
            tile.SetValue(HavenProperties.Row, index / columns);
            grid.Add(tile);
        }

        section.Add(grid);
        _sections.Add(section);
    }

    private Container BuildActionTile(DynamicActionToolbar.ToolbarAction action, string category)
    {
        var tile = new Container
        {
            Name = $"HeaderDropdown.Actions.Tile.{string.Concat(action.Label.Where(char.IsLetterOrDigit))}",
            Layout = HavenLayout.Overlay
        };
        tile.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        tile.SetValue(HavenProperties.Height, HavenLength.Px(72));

        var button = new Button
        {
            Name = $"HeaderDropdown.Actions.Action.{string.Concat(action.Label.Where(char.IsLetterOrDigit))}",
            Variant = ButtonVariant.Navigation,
            Content = action.Label
        };
        button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        button.SetValue(HavenProperties.Height, HavenLength.Px(72));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(72));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 14px 0px 56px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        button.SetValue(HavenProperties.Background, "Surface");
        button.SetValue(HavenProperties.Foreground, "TextPrimary");
        button.SetValue(HavenProperties.FontSize, 13d);
        button.SetValue(HavenProperties.FontWeight, 800);
        button.Accessibility.Description = AccessibleDescription(action);
        button.Invoked += (_, _) => ActionRequested?.Invoke(action);
        tile.Add(button);
        _actionButtons.Add(button);

        var badge = new Container { Layout = HavenLayout.Overlay };
        badge.SetValue(HavenProperties.Width, HavenLength.Px(34));
        badge.SetValue(HavenProperties.Height, HavenLength.Px(34));
        badge.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 11px"));
        badge.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        badge.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        badge.SetValue(HavenProperties.Background, BadgeBackground(category));
        badge.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(17)));
        badge.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        badge.SetValue(HavenProperties.ZIndex, 2);

        var icon = new Icon { Key = NormalizeIconKey(action.IconKey) };
        icon.SetValue(HavenProperties.Width, HavenLength.Px(19));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(19));
        icon.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        icon.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        icon.SetValue(HavenProperties.Foreground, "TextOnAccent");
        icon.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        badge.Add(icon);
        tile.Add(badge);

        return tile;
    }

    private static string AccessibleDescription(DynamicActionToolbar.ToolbarAction action)
    {
        var description = string.IsNullOrWhiteSpace(action.Description) ? action.Tooltip ?? action.Label : action.Description;
        return string.IsNullOrWhiteSpace(action.Shortcut) ? description : $"{description} Shortcut {action.Shortcut}.";
    }

    private static string BadgeBackground(string category) => category switch
    {
        "Pinned" => "Accent",
        "Recommended" or "Study" or "Studio" or "Data" or "Media" => "AccentSecondary",
        "Tasks" or "Plan" => "Warning",
        _ => "Accent"
    };

    private static string NormalizeIconKey(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "call" => "chat",
        "agent" => "agents",
        "branch" => "studio",
        "archive" => "file",
        "delete" => "close",
        "clock" => "calendar",
        "commands" => "studio",
        "automation" => "bolt",
        "edit" => "prompt",
        "plugin" => "bolt",
        "training" => "study",
        "sparkles" => "bolt",
        "plus" or "close" or "check" or "chevron-left" or "chevron-right" or "search" or "chat"
            or "refresh" or "study" or "file" or "agents" or "bolt" or "prompt" or "rocket" or "browse"
            or "tasks" or "plan" or "studio" or "test" or "bookmark" or "calendar" or "target" or "palette"
            or "present" or "data" or "vision" or "play" or "translate" or "dashboard" or "settings" or "pin" => (key ?? string.Empty).Trim().ToLowerInvariant(),
        _ => "bolt"
    };

    internal static string ResolveCategory(DynamicActionToolbar.ToolbarAction action)
    {
        if (action.IsFeatured) return "Pinned";
        if (CategoryOrder.Contains(action.Category, StringComparer.OrdinalIgnoreCase))
            return CategoryOrder.First(category => category.Equals(action.Category, StringComparison.OrdinalIgnoreCase));

        var name = action.Label.ToLowerInvariant();
        if (name.Contains("branch") || name.Contains("chat") || name.Contains("agent") || name.Contains("plugin") || name.Contains("response")) return "Chat";
        if (name.Contains("project") || name.Contains("build") || name.Contains("test") || name.Contains("git")) return "Studio";
        if (name.Contains("browser") || name.Contains("page") || name.Contains("bookmark")) return "Browser";
        if (name.Contains("task") || name.Contains("timer") || name.Contains("automation")) return "Tasks";
        if (name.Contains("plan") || name.Contains("calendar")) return "Plan";
        if (name.Contains("save") || name.Contains("new") || name.Contains("delete") || name.Contains("archive")) return "File";
        if (name.Contains("sidebar") || name.Contains("tab") || name.Contains("zoom")) return "View";
        return "Tools";
    }
}
