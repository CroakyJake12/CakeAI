using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Studio;

internal sealed class StudioHubHavenScene : IDisposable
{
    private static readonly string[] Categories = ["Media", "Interactive", "Tools", "Documents"];
    private readonly Dictionary<string, HavenButton> _buttons = new(StringComparer.Ordinal);
    private bool _disposed;

    public StudioHubHavenScene()
    {
        Root = new Page { Name = "Studio.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("26px 30px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(22));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Accessibility.AccessibleName = "Studio creation hub";

        var header = new Container { Name = "Studio.Header", Layout = HavenLayout.Vertical };
        header.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        var title = new HavenText("Studio") { Level = TextLevel.H1 };
        title.SetValue(HavenProperties.FontSize, 34d);
        title.SetValue(HavenProperties.FontWeight, 800);
        var subtitle = new HavenText("What do you want to create?") { Level = TextLevel.Paragraph };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        header.Add(title);
        header.Add(subtitle);
        Root.Add(header);

        foreach (var category in Categories) Root.Add(BuildSection(category));

        StatusText = new HavenText("Choose a creation type to begin.") { Name = "Studio.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        StatusText.Accessibility.AccessibleName = "Studio status";
        Root.Add(StatusText);
    }

    public Page Root { get; }
    public HavenText StatusText { get; }
    public event Action<StudioCreationIntent>? CreationRequested;

    public void SetStatus(string text) => StatusText.Content = text ?? string.Empty;

    public void SetBusy(string itemId, bool busy)
    {
        if (_buttons.TryGetValue(itemId, out var button)) button.SetValue(HavenProperties.Enabled, !busy);
    }

    private Container BuildSection(string category)
    {
        var section = new Container { Name = $"Studio.Section.{category}", Layout = HavenLayout.Vertical };
        section.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var heading = new HavenText(category) { Level = TextLevel.H2 };
        heading.SetValue(HavenProperties.FontSize, 18d);
        heading.SetValue(HavenProperties.FontWeight, 750);
        section.Add(heading);

        var grid = new Container { Name = $"Studio.Grid.{category}", Layout = HavenLayout.Wrap };
        grid.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        grid.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        grid.SetValue(HavenProperties.Responsive, true);
        foreach (var item in StudioCreationCatalog.InCategory(category)) grid.Add(BuildTile(item));
        section.Add(grid);
        return section;
    }

    private HavenButton BuildTile(StudioCreationIntent item)
    {
        var button = new HavenButton
        {
            Name = $"Studio.Create.{item.Id}",
            Variant = ButtonVariant.Secondary,
            Content = $"{item.Name}\n{item.Description}"
        };
        button.SetValue(HavenProperties.MinWidth, HavenLength.Px(210));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(84));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px 16px"));
        button.SetValue(HavenProperties.Responsive, true);
        button.Accessibility.AccessibleName = $"Create {item.Name}";
        button.Accessibility.Description = item.Description;
        button.Invoked += (_, _) => CreationRequested?.Invoke(item);
        _buttons[item.Id] = button;
        return button;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buttons.Clear();
    }
}
