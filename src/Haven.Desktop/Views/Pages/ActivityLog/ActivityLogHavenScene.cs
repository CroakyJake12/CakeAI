using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.ActivityLog;

internal sealed record ActivityLogRow(string Title, string Mode, DateTimeOffset UpdatedAt);

/// <summary>
/// Canonical HUI scene for the desktop Activity Log surface. Repository access and
/// HavenEventBus compatibility remain in <see cref="ActivityLogPage"/>.
/// </summary>
internal sealed class ActivityLogHavenScene : IDisposable
{
    private readonly Dictionary<HavenElement, TrackedInteraction> _trackedInteractions = [];
    private readonly List<HavenButton> _itemButtons = [];
    private bool _disposed;

    public ActivityLogHavenScene()
    {
        Root = BuildRoot();
        Refresh = Get<HavenButton>("Refresh");
        Search = Get<Input>("Search");
        Items = Get<Container>("Items");
        Status = Get<HavenText>("Status");

        Refresh.Invoked += OnRefreshInvoked;
        Search.TextChanged += OnSearchTextChanged;
        TrackInteraction(Refresh, "ActivityLog.Actions.Refresh");
    }

    public Page Root { get; }
    public HavenButton Refresh { get; }
    public Input Search { get; }
    public Container Items { get; }
    public HavenText Status { get; }
    public IReadOnlyList<HavenButton> ItemButtons => _itemButtons;

    public event EventHandler? RefreshRequested;
    public event EventHandler<string>? SearchChanged;
    public event EventHandler<string>? ItemInvoked;
    public event EventHandler<string>? PointerEventRequested;

    public void SetItems(IReadOnlyList<ActivityLogRow> items)
    {
        foreach (var button in _itemButtons.ToArray()) UntrackInteraction(button);
        _itemButtons.Clear();
        foreach (var child in Items.Children.ToArray()) Items.Remove(child);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var qualifiedName = $"ActivityLog.List.Item{index}";
            var card = new Container { Layout = HavenLayout.Vertical };
            card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            card.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
            card.SetValue(HavenProperties.Gap, HavenLength.Px(3));
            card.SetValue(HavenProperties.Background, "Surface");
            card.SetValue(HavenProperties.BorderColor, "Border");
            card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));

            var open = new HavenButton
            {
                Name = qualifiedName,
                Content = item.Title,
                Variant = ButtonVariant.Navigation
            };
            open.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            open.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
            open.Accessibility.AccessibleName = $"Activity log conversation {item.Title}";
            open.Invoked += (_, _) => ItemInvoked?.Invoke(this, qualifiedName);
            TrackInteraction(open, qualifiedName);
            _itemButtons.Add(open);
            card.Add(open);

            var metadata = new HavenText { Content = $"{item.Mode} · {item.UpdatedAt:MMM dd, HH:mm}" };
            metadata.SetValue(HavenProperties.Foreground, "TextSecondary");
            metadata.SetValue(HavenProperties.FontSize, 10d);
            card.Add(metadata);
            Items.Add(card);
        }
    }

    public void SetStatus(string? value) => Status.Content = value ?? string.Empty;

    public string? GetQualifiedActionAt(double x, double y)
    {
        var point = new HavenPoint(x, y);
        foreach (var (element, tracked) in _trackedInteractions)
        {
            if (!element.IsIncluded || element.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) continue;
            if (element.Bounds.Contains(point)) return tracked.QualifiedName;
        }
        return null;
    }

    private void TrackInteraction(HavenElement element, string qualifiedName)
    {
        _trackedInteractions[element] = new TrackedInteraction(qualifiedName, element.State);
        element.Invalidated += OnTrackedElementInvalidated;
    }

    private void UntrackInteraction(HavenElement element)
    {
        element.Invalidated -= OnTrackedElementInvalidated;
        _trackedInteractions.Remove(element);
    }

    private void OnTrackedElementInvalidated(object? sender, EventArgs e)
    {
        if (sender is not HavenElement element || !_trackedInteractions.TryGetValue(element, out var tracked)) return;
        var current = element.State;
        var changed = tracked.State ^ current;

        if (changed.HasFlag(HavenElementState.Hover))
            PointerEventRequested?.Invoke(this, $"{tracked.QualifiedName}.{(current.HasFlag(HavenElementState.Hover) ? "Hover" : "Leave")}");
        if (changed.HasFlag(HavenElementState.Pressed))
            PointerEventRequested?.Invoke(this, $"{tracked.QualifiedName}.{(current.HasFlag(HavenElementState.Pressed) ? "Press" : "Release")}");

        _trackedInteractions[element] = tracked with { State = current };
    }

    private void OnRefreshInvoked(object? sender, EventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnSearchTextChanged(object? sender, EventArgs e) => SearchChanged?.Invoke(this, Search.Text.Trim());

    private T Get<T>(string name) where T : HavenElement =>
        (T)Root.DescendantsAndSelf().Single(element => element.Name == name);

    private static Page BuildRoot()
    {
        const string markup = """
            <Page Name="ActivityLogRoot" Layout="Grid" Width="100%" Height="100%" Rows="Auto Auto 1fr Auto" Gap="12px" Padding="20px" Background="Surface">
              <Container Row="0" Layout="Grid" Columns="1fr Auto" Width="100%" Gap="12px">
                <Text Column="0" Content="Activity Log" Level="H1" />
                <Button Name="Refresh" Column="1" Content="Refresh" Variant="Primary" MinHeight="36px" />
              </Container>
              <Input Name="Search" Row="1" Width="100%" Placeholder="Search conversations…" />
              <Container Name="Items" Row="2" Layout="Vertical" Width="100%" Height="100%" Overflow="Scroll" Clip="true" Gap="2px" Padding="8px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="12px" />
              <Container Row="3" Layout="Vertical" Width="100%" Padding="8px 12px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="8px">
                <Text Name="Status" Content="" Foreground="TextSecondary" FontSize="11" />
              </Container>
            </Page>
            """;
        return (Page)new HavenMarkupParser().Parse(markup, "ActivityLog.hui");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Refresh.Invoked -= OnRefreshInvoked;
        Search.TextChanged -= OnSearchTextChanged;
        foreach (var element in _trackedInteractions.Keys.ToArray()) UntrackInteraction(element);
        _itemButtons.Clear();
    }

    private sealed record TrackedInteraction(string QualifiedName, HavenElementState State);
}
