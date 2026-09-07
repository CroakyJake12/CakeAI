using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Experiences;

internal sealed class ExperiencesHavenScene : IDisposable
{
    private bool _disposed;

    public ExperiencesHavenScene()
    {
        Root = new Page { Name = "Experiences.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("26px 30px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(18));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Accessibility.AccessibleName = "Experiences library";

        var header = new Container { Name = "Experiences.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto", Rows = "Auto" };
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var copy = new Container { Layout = HavenLayout.Vertical };
        var title = new HavenText("Experiences") { Level = TextLevel.H1 };
        title.SetValue(HavenProperties.FontSize, 34d);
        title.SetValue(HavenProperties.FontWeight, 800);
        var subtitle = new HavenText("Saved interactive things made with Haven.") { Level = TextLevel.Paragraph };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        copy.Add(title); copy.Add(subtitle);
        header.Add(copy);

        NewButton = Button("Experiences.New", "New experience", ButtonVariant.Primary);
        NewButton.SetValue(HavenProperties.Column, 1);
        RefreshButton = Button("Experiences.Refresh", "Refresh", ButtonVariant.Secondary);
        RefreshButton.SetValue(HavenProperties.Column, 2);
        header.Add(NewButton); header.Add(RefreshButton);
        Root.Add(header);

        PinnedSection = Section("Pinned", "Experiences.Pinned", out var pinnedHost, out var pinnedEmpty);
        PinnedHost = pinnedHost;
        PinnedEmpty = pinnedEmpty;
        RecentSection = Section("Recent", "Experiences.Recent", out var recentHost, out var recentEmpty);
        RecentHost = recentHost;
        RecentEmpty = recentEmpty;
        Root.Add(PinnedSection); Root.Add(RecentSection);

        PreviewSection = new Container { Name = "Experiences.Preview.Section", Layout = HavenLayout.Vertical };
        PreviewSection.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        PreviewSection.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        PreviewTitle = new HavenText("Experience") { Level = TextLevel.H2 };
        PreviewTitle.SetValue(HavenProperties.FontWeight, 750);
        PreviewHost = new Container { Name = "Experiences.Preview", Layout = HavenLayout.Vertical };
        PreviewHost.SetValue(HavenProperties.Background, "SurfaceRaised");
        PreviewHost.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(14)));
        PreviewHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        PreviewSection.Add(PreviewTitle); PreviewSection.Add(PreviewHost);
        Root.Add(PreviewSection);

        StatusText = new HavenText("Loading saved experiences…") { Name = "Experiences.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        StatusText.Accessibility.AccessibleName = "Experiences status";
        Root.Add(StatusText);

        NewButton.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        RefreshButton.Invoked += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    public Page Root { get; }
    public Container PinnedSection { get; }
    public Container RecentSection { get; }
    public Container PinnedHost { get; }
    public Container RecentHost { get; }
    public HavenText PinnedEmpty { get; }
    public HavenText RecentEmpty { get; }
    public Container PreviewSection { get; }
    public Container PreviewHost { get; }
    public HavenText PreviewTitle { get; }
    public HavenText StatusText { get; }
    public HavenButton NewButton { get; }
    public HavenButton RefreshButton { get; }

    public event EventHandler? NewRequested;
    public event EventHandler? RefreshRequested;
    public event Action<Guid>? OpenRequested;
    public event Action<Guid, bool>? PinRequested;

    public void SetItems(IReadOnlyList<GenUiAppDefinition> pinned, IReadOnlyList<GenUiAppDefinition> recent)
    {
        Clear(PinnedHost); Clear(RecentHost);
        var pinnedIds = pinned.Select(item => item.Document.Origin.InstanceId).ToHashSet();
        foreach (var item in pinned) PinnedHost.Add(ItemCard(item, true));
        foreach (var item in recent.Where(item => !pinnedIds.Contains(item.Document.Origin.InstanceId))) RecentHost.Add(ItemCard(item, false));
        PinnedEmpty.SetValue(HavenProperties.Visibility, pinned.Count == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        var visibleRecentCount = recent.Count(item => !pinnedIds.Contains(item.Document.Origin.InstanceId));
        RecentEmpty.SetValue(HavenProperties.Visibility, visibleRecentCount == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void ShowExperience(string title, HavenElement root)
    {
        PreviewTitle.Content = title;
        Clear(PreviewHost);
        PreviewHost.Add(root);
        PreviewSection.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void SetStatus(string text) => StatusText.Content = text ?? string.Empty;

    private Container ItemCard(GenUiAppDefinition definition, bool pinned)
    {
        var id = definition.Document.Origin.InstanceId;
        var card = new Container { Name = $"Experiences.Item.{id:N}", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 14px"));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        var open = Button($"Experiences.Open.{id:N}", $"{definition.Document.Title}\nUpdated {definition.Document.UpdatedAt.LocalDateTime:g}", ButtonVariant.Ghost);
        open.Accessibility.AccessibleName = $"Open {definition.Document.Title}";
        open.Invoked += (_, _) => OpenRequested?.Invoke(id);
        var pin = Button($"Experiences.Pin.{id:N}", pinned ? "Unpin" : "Pin", ButtonVariant.Tertiary);
        pin.SetValue(HavenProperties.Column, 1);
        pin.Accessibility.AccessibleName = pinned ? $"Unpin {definition.Document.Title}" : $"Pin {definition.Document.Title}";
        pin.Invoked += (_, _) => PinRequested?.Invoke(id, !pinned);
        card.Add(open); card.Add(pin);
        return card;
    }

    private static Container Section(string title, string name, out Container host, out HavenText empty)
    {
        var section = new Container { Name = name, Layout = HavenLayout.Vertical };
        section.SetValue(HavenProperties.Gap, HavenLength.Px(9));
        var heading = new HavenText(title) { Level = TextLevel.H2 };
        heading.SetValue(HavenProperties.FontSize, 18d);
        heading.SetValue(HavenProperties.FontWeight, 750);
        host = new Container { Name = name + ".Items", Layout = HavenLayout.Vertical };
        host.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        empty = new HavenText(title == "Pinned" ? "No pinned experiences yet." : "No saved experiences yet. Create one to see it here.") { Level = TextLevel.Caption };
        empty.SetValue(HavenProperties.Foreground, "TextSecondary");
        section.Add(heading); section.Add(host); section.Add(empty);
        return section;
    }

    private static HavenButton Button(string name, string content, ButtonVariant variant)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
        return button;
    }

    private static void Clear(Container container)
    {
        foreach (var child in container.Children.ToArray()) container.Remove(child);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
