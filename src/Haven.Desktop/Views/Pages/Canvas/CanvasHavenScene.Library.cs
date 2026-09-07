using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Canvas;

internal sealed partial class CanvasHavenScene
{
    private Container _libraryHost = null!;
    private Container _libraryRecent = null!;
    private HavenText _libraryStatus = null!;
    private HavenButton _libraryButton = null!;

    public event Action<Guid>? DocumentOpenRequested;
    public event EventHandler? LibraryRequested;

    /// <summary>
    /// Restores the useful local-library landing from the older release branch on top of the
    /// current recovery scene. It deliberately uses the current toolbar/workspace instead of the
    /// removed release chrome.
    /// </summary>
    private void BuildLibrary()
    {
        _libraryButton = NewButton("Canvas.Library", "Library");
        _libraryButton.Accessibility.AccessibleName = "Open Canvas library";
        _libraryButton.Invoked += (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty);
        Toolbar.Add(_libraryButton);

        _libraryHost = new Container { Name = "Canvas.Library", Layout = HavenLayout.Vertical };
        _libraryHost.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        _libraryHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _libraryHost.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _libraryHost.SetValue(HavenProperties.Background, "Surface");
        _libraryHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("34px 42px"));
        _libraryHost.SetValue(HavenProperties.Gap, HavenLength.Px(18));
        _libraryHost.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _libraryHost.SetValue(HavenProperties.ZIndex, 500);
        _libraryHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        var title = new HavenText("Canvas") { Level = TextLevel.H2 };
        title.SetValue(HavenProperties.FontSize, 34d);
        title.SetValue(HavenProperties.FontWeight, 750);
        _libraryHost.Add(title);

        var subtitle = new HavenText("Sketch ideas, study visually, or build a freeform board. Canvases are saved locally and reopen exactly where you left them.") { Level = TextLevel.Paragraph };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        _libraryHost.Add(subtitle);

        var actions = new Container { Name = "Canvas.Library.Actions", Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var create = LibraryAction("Canvas.Library.New", "New canvas", ButtonVariant.Primary);
        create.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        var import = LibraryAction("Canvas.Library.Import", "Open / import canvas", ButtonVariant.Secondary);
        import.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        actions.Add(create);
        actions.Add(import);
        _libraryHost.Add(actions);

        _libraryHost.Add(LibraryHeading("Starting points"));
        var starters = new Container { Name = "Canvas.Library.Starters", Layout = HavenLayout.Wrap };
        starters.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var blank = LibraryAction("Canvas.Library.Blank", "Blank canvas", ButtonVariant.Navigation);
        blank.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        starters.Add(blank);
        _libraryHost.Add(starters);

        _libraryHost.Add(LibraryHeading("Recent"));
        _libraryRecent = new Container { Name = "Canvas.Library.Recent", Layout = HavenLayout.Wrap };
        _libraryRecent.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        _libraryHost.Add(_libraryRecent);

        _libraryStatus = Caption(string.Empty);
        _libraryStatus.Name = "Canvas.Library.Status";
        _libraryHost.Add(_libraryStatus);
        Root.Add(_libraryHost);
    }

    public void SetLibrary(IReadOnlyList<NotesDocumentSummary> documents)
    {
        documents ??= Array.Empty<NotesDocumentSummary>();
        foreach (var child in _libraryRecent.Children.ToArray()) _libraryRecent.Remove(child);

        if (documents.Count == 0)
        {
            _libraryRecent.Add(Caption("No canvases yet. Start with a blank canvas or import an existing Canvas file."));
        }
        else
        {
            foreach (var document in documents.OrderByDescending(value => value.UpdatedAt))
            {
                var card = new Container { Name = $"Canvas.Library.Card.{document.Id:N}", Layout = HavenLayout.Vertical };
                card.SetValue(HavenProperties.Width, HavenLength.Px(260));
                card.SetValue(HavenProperties.MinHeight, HavenLength.Px(120));
                card.SetValue(HavenProperties.Background, "SurfaceRaised");
                card.SetValue(HavenProperties.BorderColor, "Border");
                card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
                card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
                card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
                card.SetValue(HavenProperties.Gap, HavenLength.Px(6));

                var open = LibraryAction($"Canvas.Library.Open.{document.Id:N}", document.Title, ButtonVariant.Navigation);
                open.SetValue(HavenProperties.FontSize, 16d);
                open.Invoked += (_, _) => DocumentOpenRequested?.Invoke(document.Id);
                card.Add(open);
                card.Add(Caption($"Updated {document.UpdatedAt.LocalDateTime:g} · v{document.Version}{(document.HasRecovery ? " · recovery available" : string.Empty)}"));
                _libraryRecent.Add(card);
            }
        }

        _libraryStatus.Content = documents.Count == 1 ? "1 canvas saved locally" : $"{documents.Count} canvases saved locally";
        _libraryHost.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void ShowWorkspace() =>
        _libraryHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

    private static HavenText LibraryHeading(string text)
    {
        var value = new HavenText(text) { Level = TextLevel.H4 };
        value.SetValue(HavenProperties.FontSize, 18d);
        value.SetValue(HavenProperties.FontWeight, 700);
        return value;
    }

    private static HavenButton LibraryAction(string name, string label, ButtonVariant variant)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = variant };
        button.Accessibility.AccessibleName = label;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 14px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        return button;
    }
}
