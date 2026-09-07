using Haven.Core;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>Polished Haven-owned body for the global Apps launcher.</summary>
internal sealed class AppLauncherFinalScene
{
    private readonly Container _sections;
    private readonly List<Button> _appButtons = [];
    private IReadOnlyList<ModeDefinition> _apps = [];
    private IReadOnlySet<Guid> _pinnedIds = new HashSet<Guid>();

    public AppLauncherFinalScene()
    {
        Root = new Page { Name = "HeaderDropdown.Apps.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Width, HavenLength.Px(560));
        Root.SetValue(HavenProperties.Height, HavenLength.Px(620));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px 24px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        Root.SetValue(HavenProperties.Background, "SurfaceRaised");
        Root.SetValue(HavenProperties.BorderColor, "Border");
        Root.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Root.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(30)));
        Root.SetValue(HavenProperties.Shadow, "Card");

        var title = new Text { Name = "HeaderDropdown.Apps.Title", Content = "Apps", Level = TextLevel.H1 };
        title.SetValue(HavenProperties.Height, HavenLength.Px(38));
        title.SetValue(HavenProperties.FontSize, 28d);
        title.SetValue(HavenProperties.FontWeight, 800);
        title.SetValue(HavenProperties.Foreground, "TextPrimary");
        Root.Add(title);

        var searchHost = new Container { Name = "HeaderDropdown.Apps.SearchHost", Layout = HavenLayout.Overlay };
        searchHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        searchHost.SetValue(HavenProperties.Height, HavenLength.Px(58));
        searchHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(58));

        Search = new Input { Name = "HeaderDropdown.Apps.Search", Placeholder = "Search Apps" };
        Search.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Search.SetValue(HavenProperties.Height, HavenLength.Px(58));
        Search.SetValue(HavenProperties.MinHeight, HavenLength.Px(58));
        Search.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 20px 0px 54px"));
        Search.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(29)));
        Search.SetValue(HavenProperties.Background, "Surface");
        Search.SetValue(HavenProperties.Foreground, "AccentSecondary");
        Search.SetValue(HavenProperties.FontSize, 15d);
        Search.Accessibility.AccessibleName = "Search Apps";
        searchHost.Add(Search);

        var searchIcon = new Icon { Name = "HeaderDropdown.Apps.SearchIcon", Key = "search" };
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

        _sections = new Container { Name = "HeaderDropdown.Apps.Sections", Layout = HavenLayout.Vertical };
        _sections.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _sections.SetValue(HavenProperties.Height, HavenLength.Px(382));
        _sections.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _sections.SetValue(HavenProperties.Clip, true);
        _sections.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        _sections.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 2px 8px 2px"));
        Root.Add(_sections);

        ManageButton = new Button
        {
            Name = "HeaderDropdown.Apps.Manage",
            Variant = ButtonVariant.Primary,
            IconKey = "settings",
            Content = "Manage Apps"
        };
        ManageButton.SetValue(HavenProperties.Width, HavenLength.Px(230));
        ManageButton.SetValue(HavenProperties.Height, HavenLength.Px(56));
        ManageButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(56));
        ManageButton.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        ManageButton.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 22px"));
        ManageButton.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(28)));
        ManageButton.SetValue(HavenProperties.FontSize, 14d);
        ManageButton.Accessibility.Description = "Open Apps management";
        Root.Add(ManageButton);

        Search.TextChanged += (_, _) => Rebuild();
        ManageButton.Invoked += (_, _) => ManageRequested?.Invoke();
    }

    public Page Root { get; }
    public Input Search { get; }
    public Button ManageButton { get; }
    public IReadOnlyList<Button> AppButtons => _appButtons;

    public event Action<ModeDefinition>? AppRequested;
    public event Action? ManageRequested;

    public void Configure(IReadOnlyList<ModeDefinition> apps, IReadOnlySet<Guid> pinnedIds)
    {
        _apps = apps;
        _pinnedIds = pinnedIds;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in _sections.Children.ToArray())
            _sections.Remove(child);
        _appButtons.Clear();

        var query = Search.Text.Trim();
        var filtered = _apps
            .Where(item => item.IsEnabled)
            .Where(item => string.IsNullOrWhiteSpace(query)
                           || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AddSection("General", filtered.Where(item => AppLauncherControl.CategoryFor(item) == "General").ToArray());
        AddSection("Productivity", filtered.Where(item => AppLauncherControl.CategoryFor(item) == "Productivity").ToArray());
        AddSection("Media & creativity", filtered.Where(item => AppLauncherControl.CategoryFor(item) == "Media & creativity").ToArray());
        AddSection("More", filtered.Where(item => AppLauncherControl.CategoryFor(item) == "More").ToArray());

        if (_appButtons.Count == 0)
        {
            var empty = new Text { Name = "HeaderDropdown.Apps.Empty", Content = "No Apps match this search." };
            empty.SetValue(HavenProperties.FontSize, 14d);
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            empty.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px 6px"));
            _sections.Add(empty);
        }
    }

    private void AddSection(string title, IReadOnlyList<ModeDefinition> items)
    {
        if (items.Count == 0) return;

        var section = new Container
        {
            Name = $"HeaderDropdown.Apps.Section.{string.Concat(title.Where(char.IsLetterOrDigit))}",
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

        const int columns = 2;
        var rows = Math.Max(1, (int)Math.Ceiling(items.Count / (double)columns));
        var grid = new Container
        {
            Name = $"{section.Name}.Grid",
            Layout = HavenLayout.Grid,
            Columns = "1fr 1fr",
            Rows = string.Join(' ', Enumerable.Repeat("64px", rows))
        };
        grid.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        grid.SetValue(HavenProperties.Gap, HavenLength.Px(10));

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var tile = BuildAppTile(item);
            tile.SetValue(HavenProperties.Column, index % columns);
            tile.SetValue(HavenProperties.Row, index / columns);
            grid.Add(tile);
        }

        section.Add(grid);
        _sections.Add(section);
    }

    private Container BuildAppTile(ModeDefinition item)
    {
        var tile = new Container { Name = $"HeaderDropdown.Apps.Tile.{item.Id:N}", Layout = HavenLayout.Overlay };
        tile.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        tile.SetValue(HavenProperties.Height, HavenLength.Px(64));

        var button = new Button
        {
            Name = $"HeaderDropdown.Apps.App.{item.Id:N}",
            Variant = ButtonVariant.Navigation,
            Content = item.Name
        };
        button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        button.SetValue(HavenProperties.Height, HavenLength.Px(64));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(64));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 18px 0px 66px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)));
        button.SetValue(HavenProperties.Background, "Surface");
        button.SetValue(HavenProperties.Foreground, "TextPrimary");
        button.SetValue(HavenProperties.FontSize, 15d);
        button.SetValue(HavenProperties.FontWeight, 800);
        button.Accessibility.Description = string.IsNullOrWhiteSpace(item.Description) ? $"Open {item.Name}" : item.Description;
        button.Invoked += (_, _) => AppRequested?.Invoke(item);
        tile.Add(button);
        _appButtons.Add(button);

        var badge = new Container { Layout = HavenLayout.Overlay };
        badge.SetValue(HavenProperties.Width, HavenLength.Px(40));
        badge.SetValue(HavenProperties.Height, HavenLength.Px(40));
        badge.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 12px"));
        badge.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        badge.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        badge.SetValue(HavenProperties.Background, IconBackgroundFor(item));
        badge.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(20)));
        badge.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        badge.SetValue(HavenProperties.ZIndex, 2);

        var icon = new Icon { Key = item.IconKey };
        icon.SetValue(HavenProperties.Width, HavenLength.Px(22));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(22));
        icon.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        icon.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        icon.SetValue(HavenProperties.Foreground, "TextOnAccent");
        icon.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        badge.Add(icon);
        tile.Add(badge);

        return tile;
    }

    private void AddInlineAction(string label, string iconKey, Action action)
    {
        var button = new Button { Variant = ButtonVariant.Ghost, IconKey = iconKey, Content = label };
        button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        button.SetValue(HavenProperties.Height, HavenLength.Px(44));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(44));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(20)));
        button.Invoked += (_, _) => action();
        _sections.Add(button);
    }

    private static string IconBackgroundFor(ModeDefinition item)
    {
        var key = item.Key.Trim().ToLowerInvariant();
        if (key is "tasks" or "data" or "plan" or "study" or "translate" or "studio") return "Warning";
        if (key is "imagine" or "present" or "vision" or "play") return "AccentSecondary";
        if (key is "training") return "AccentMuted";
        return "Accent";
    }
}
