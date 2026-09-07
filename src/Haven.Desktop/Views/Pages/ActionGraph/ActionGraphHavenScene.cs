using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.ActionGraph;

/// <summary>
/// HavenUI scene for the Action Graph surface: header with real view/export/live controls,
/// five live summary cards, recent-prompts sidebar, retained graph canvas with zoom overlay
/// and legend, list projection and a collapsible node-details pane.
/// </summary>
internal sealed class ActionGraphHavenScene
{
    private Container _workspace = null!;
    private bool _compact;
    private bool _detailsCollapsed;

    public ActionGraphHavenScene()
    {
        Root = new Page { Name = "ActionGraph.Root", Layout = HavenLayout.Grid };
        Root.Columns = "1fr";
        Root.Rows = "Auto Auto 1fr Auto";
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("20px 18px 20px 18px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(12));

        BuildHeader();
        BuildSummaryCards();
        BuildWorkspace();
        BuildStatusLine();
    }

    public Page Root { get; }

    // Header
    public HavenText TitleText { get; private set; } = null!;
    public HavenText SubtitleText { get; private set; } = null!;
    public HavenButton GraphViewButton { get; private set; } = null!;
    public HavenButton ListViewButton { get; private set; } = null!;
    public HavenButton ExportButton { get; private set; } = null!;
    public Select LiveSelect { get; private set; } = null!;

    // Summary cards
    public HavenText StepsValue { get; private set; } = null!;
    public HavenText StepsDetail { get; private set; } = null!;
    public HavenText ToolsValue { get; private set; } = null!;
    public HavenText ToolsDetail { get; private set; } = null!;
    public HavenText AppsValue { get; private set; } = null!;
    public HavenText AppsDetail { get; private set; } = null!;
    public HavenText RetriesValue { get; private set; } = null!;
    public HavenText RetriesDetail { get; private set; } = null!;
    public HavenText TimeValue { get; private set; } = null!;
    public HavenText TimeDetail { get; private set; } = null!;

    // Sidebar
    public Input HistorySearch { get; private set; } = null!;
    public Container HistoryList { get; private set; } = null!;
    public HavenButton ViewAllHistoryButton { get; private set; } = null!;

    // Canvas
    public Container GraphOverlay { get; private set; } = null!;
    public Container ZoomCluster { get; private set; } = null!;
    public HavenText EmptyStateText { get; private set; } = null!;
    public HavenButton ZoomOutButton { get; private set; } = null!;
    public HavenText ZoomLabel { get; private set; } = null!;
    public HavenButton ZoomInButton { get; private set; } = null!;
    public HavenButton FitButton { get; private set; } = null!;
    public Container LegendHost { get; private set; } = null!;
    public HavenText LegendCaption { get; private set; } = null!;

    // List mode
    public Container ListHost { get; private set; } = null!;

    // Details
    public Container DetailsCard { get; private set; } = null!;
    public HavenButton CollapseDetailsButton { get; private set; } = null!;
    public Container DetailsSections { get; private set; } = null!;
    public Container FeedbackSection { get; private set; } = null!;
    public HavenButton ThumbUpButton { get; private set; } = null!;
    public HavenButton ThumbDownButton { get; private set; } = null!;
    public Input CommentInput { get; private set; } = null!;
    public HavenButton SaveCommentButton { get; private set; } = null!;
    public HavenButton DeleteFeedbackButton { get; private set; } = null!;
    public Container RemediationSection { get; private set; } = null!;

    public HavenText StatusText { get; private set; } = null!;

    private void BuildHeader()
    {
        var header = new Container { Name = "ActionGraph.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Gap, HavenLength.Px(12));

        var titles = new Container { Name = "ActionGraph.Titles", Layout = HavenLayout.Vertical };
        titles.SetValue(HavenProperties.Gap, HavenLength.Px(3));
        titles.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        TitleText = new HavenText("Action Graph") { Name = "ActionGraph.Title", Level = TextLevel.H1 };
        SubtitleText = new HavenText("Visualize how Haven planned, executed, and delivered this response")
        {
            Name = "ActionGraph.Subtitle",
            Level = TextLevel.Caption
        };
        SubtitleText.SetValue(HavenProperties.Foreground, "TextSecondary");
        titles.Add(TitleText);
        titles.Add(SubtitleText);
        header.Add(titles);

        var actions = new Container { Name = "ActionGraph.HeaderActions", Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Column, 1);
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actions.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);

        var segmented = new Container { Name = "ActionGraph.ViewToggle", Layout = HavenLayout.Horizontal };
        segmented.SetValue(HavenProperties.Background, "SurfaceSecondary");
        segmented.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        segmented.SetValue(HavenProperties.Padding, HavenThickness.Parse("3px"));
        segmented.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        GraphViewButton = NewButton("ActionGraph.View.Graph", "Graph");
        GraphViewButton.Variant = ButtonVariant.Primary;
        ListViewButton = NewButton("ActionGraph.View.List", "List");
        ListViewButton.Variant = ButtonVariant.Tertiary;
        segmented.Add(GraphViewButton);
        segmented.Add(ListViewButton);
        actions.Add(segmented);

        ExportButton = NewButton("ActionGraph.Export", "Export");
        ExportButton.IconKey = "file";
        actions.Add(ExportButton);

        LiveSelect = new Select
        {
            Name = "ActionGraph.Live",
            Items = ["Live", "Paused", "Completed only", "Failures only"],
            SelectedIndex = 0
        };
        LiveSelect.Accessibility.AccessibleName = "Execution status and live updates";
        LiveSelect.SetValue(HavenProperties.MinWidth, HavenLength.Px(150));
        actions.Add(LiveSelect);
        header.Add(actions);
        Root.Add(header);
    }

    private void BuildSummaryCards()
    {
        var row = new Container { Name = "ActionGraph.Summary", Layout = HavenLayout.Grid, Columns = "1fr 1fr 1fr 1fr 1fr", Rows = "Auto" };
        row.SetValue(HavenProperties.Row, 1);
        row.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        (StepsValue, StepsDetail) = AddCard(row, 0, "ActionGraph.Card.Steps", "Steps executed");
        (ToolsValue, ToolsDetail) = AddCard(row, 1, "ActionGraph.Card.Tools", "Tools called");
        (AppsValue, AppsDetail) = AddCard(row, 2, "ActionGraph.Card.Apps", "Apps used");
        (RetriesValue, RetriesDetail) = AddCard(row, 3, "ActionGraph.Card.Retries", "Retries");
        (TimeValue, TimeDetail) = AddCard(row, 4, "ActionGraph.Card.Time", "Total time");
        Root.Add(row);
    }

    private (HavenText Value, HavenText Detail) AddCard(Container row, int column, string name, string label)
    {
        var card = new Container { Name = name + ".Card", Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Column, column);
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 10px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(1));
        var value = new HavenText("—") { Name = name + ".Value", Level = TextLevel.H2 };
        var caption = new HavenText(label) { Name = name + ".Label", Level = TextLevel.Caption };
        caption.SetValue(HavenProperties.Foreground, "TextSecondary");
        var detail = new HavenText(string.Empty) { Name = name + ".Detail", Level = TextLevel.Caption };
        detail.SetValue(HavenProperties.Foreground, "TextMuted");
        card.Add(value);
        card.Add(caption);
        card.Add(detail);
        row.Add(card);
        return (value, detail);
    }

    private void BuildWorkspace()
    {
        var workspace = new Container { Name = "ActionGraph.Workspace", Layout = HavenLayout.Grid, Columns = "264px 10px 1fr 10px 300px", Rows = "1fr" };
        _workspace = workspace;
        workspace.SetValue(HavenProperties.Row, 2);
        workspace.SetValue(HavenProperties.Gap, HavenLength.Px(0));
        workspace.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        BuildSidebar(workspace);
        BuildCanvasArea(workspace);
        BuildDetailsPane(workspace);
        Root.Add(workspace);
    }

    private static Container NewCard(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        return card;
    }

    private static HavenButton NewButton(string name, string label, ButtonVariant variant = ButtonVariant.Tertiary)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = variant };
        button.Accessibility.AccessibleName = label;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        return button;
    }

    private void BuildSidebar(Container workspace)
    {
        var sidebar = NewCard("ActionGraph.Sidebar");
        sidebar.Layout = HavenLayout.Grid;
        sidebar.Rows = "Auto Auto 1fr Auto";
        sidebar.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        sidebar.SetValue(HavenProperties.Gap, HavenLength.Px(8));

        var heading = new HavenText("Recent prompts") { Name = "ActionGraph.Sidebar.Heading", Level = TextLevel.H4 };
        sidebar.Add(heading);

        HistorySearch = new Input { Name = "ActionGraph.Sidebar.Search", Placeholder = "Filter executions…" };
        HistorySearch.Accessibility.AccessibleName = "Filter recent prompts";
        HistorySearch.SetValue(HavenProperties.Row, 1);
        sidebar.Add(HistorySearch);

        HistoryList = new Container { Name = "ActionGraph.Sidebar.List", Layout = HavenLayout.Vertical };
        HistoryList.SetValue(HavenProperties.Row, 2);
        HistoryList.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        HistoryList.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        sidebar.Add(HistoryList);

        ViewAllHistoryButton = NewButton("ActionGraph.Sidebar.ViewAll", "View all history");
        ViewAllHistoryButton.SetValue(HavenProperties.Row, 3);
        sidebar.Add(ViewAllHistoryButton);

        workspace.Add(sidebar);
    }

    private void BuildCanvasArea(Container workspace)
    {
        var area = new Container { Name = "ActionGraph.CanvasArea", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "1fr Auto" };
        area.SetValue(HavenProperties.Column, 2);
        area.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        GraphOverlay = new Container { Name = "ActionGraph.GraphOverlay", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "1fr" };
        GraphOverlay.SetValue(HavenProperties.Background, "SurfaceRaised");
        GraphOverlay.SetValue(HavenProperties.BorderColor, "Border");
        GraphOverlay.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        GraphOverlay.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        GraphOverlay.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        EmptyStateText = new HavenText("No execution trace is available yet.") { Name = "ActionGraph.EmptyState", Level = TextLevel.Paragraph };
        EmptyStateText.SetValue(HavenProperties.Foreground, "TextMuted");
        EmptyStateText.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        EmptyStateText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        EmptyStateText.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        GraphOverlay.Add(EmptyStateText);

        var zoomCluster = new Container { Name = "ActionGraph.Zoom.Cluster", Layout = HavenLayout.Horizontal };
        ZoomCluster = zoomCluster;
        zoomCluster.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        zoomCluster.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Right);
        zoomCluster.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Bottom);
        zoomCluster.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 12px 12px"));
        zoomCluster.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px"));
        zoomCluster.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        zoomCluster.SetValue(HavenProperties.Background, "SurfaceElevated");
        zoomCluster.SetValue(HavenProperties.BorderColor, "Border");
        zoomCluster.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        zoomCluster.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        FitButton = NewButton("ActionGraph.Zoom.Fit", "Fit");
        ZoomOutButton = NewButton("ActionGraph.Zoom.Out", "−");
        ZoomOutButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        ZoomOutButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(30));
        ZoomLabel = new HavenText("100%") { Name = "ActionGraph.Zoom.Label", Level = TextLevel.Caption };
        ZoomLabel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        ZoomLabel.SetValue(HavenProperties.MinWidth, HavenLength.Px(40));
        ZoomLabel.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        ZoomInButton = NewButton("ActionGraph.Zoom.In", "+");
        ZoomInButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        ZoomInButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(30));
        zoomCluster.Add(FitButton);
        zoomCluster.Add(ZoomOutButton);
        zoomCluster.Add(ZoomLabel);
        zoomCluster.Add(ZoomInButton);
        GraphOverlay.Add(zoomCluster);

        ListHost = new Container { Name = "ActionGraph.List", Layout = HavenLayout.Vertical };
        ListHost.SetValue(HavenProperties.Background, "SurfaceRaised");
        ListHost.SetValue(HavenProperties.BorderColor, "Border");
        ListHost.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        ListHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        ListHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        ListHost.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        ListHost.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        ListHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        area.Add(GraphOverlay);
        area.Add(ListHost);

        var footer = new Container { Name = "ActionGraph.CanvasFooter", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        footer.SetValue(HavenProperties.Row, 1);
        footer.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        LegendHost = new Container { Name = "ActionGraph.Legend", Layout = HavenLayout.Horizontal };
        LegendHost.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        LegendHost.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        LegendHost.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        LegendCaption = new HavenText(string.Empty) { Name = "ActionGraph.Legend.Caption", Level = TextLevel.Caption };
        LegendCaption.SetValue(HavenProperties.Foreground, "TextMuted");
        LegendCaption.SetValue(HavenProperties.Column, 1);
        LegendCaption.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        footer.Add(LegendHost);
        footer.Add(LegendCaption);
        area.Add(footer);

        workspace.Add(area);
    }

    private void BuildDetailsPane(Container workspace)
    {
        DetailsCard = NewCard("ActionGraph.Details");
        DetailsCard.SetValue(HavenProperties.Column, 4);
        DetailsCard.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        var detailsHeader = new Container { Name = "ActionGraph.Details.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        var heading = new HavenText("Node details") { Name = "ActionGraph.Details.Heading", Level = TextLevel.H4 };
        CollapseDetailsButton = new HavenButton { Name = "ActionGraph.Details.Collapse", IconKey = "chevron-right", Variant = ButtonVariant.Tertiary, Content = "Hide" };
        CollapseDetailsButton.Accessibility.AccessibleName = "Hide node details pane";
        CollapseDetailsButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        CollapseDetailsButton.SetValue(HavenProperties.Column, 1);
        detailsHeader.Add(heading);
        detailsHeader.Add(CollapseDetailsButton);
        DetailsCard.Add(detailsHeader);

        DetailsSections = new Container { Name = "ActionGraph.Details.Sections", Layout = HavenLayout.Vertical };
        DetailsSections.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        DetailsCard.Add(DetailsSections);

        FeedbackSection = new Container { Name = "ActionGraph.Details.Feedback", Layout = HavenLayout.Vertical };
        FeedbackSection.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        DetailsCard.Add(FeedbackSection);

        RemediationSection = new Container { Name = "ActionGraph.Details.Remediation", Layout = HavenLayout.Vertical };
        RemediationSection.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        DetailsCard.Add(RemediationSection);

        ThumbUpButton = NewButton("ActionGraph.Feedback.Up", "Helpful");
        ThumbDownButton = NewButton("ActionGraph.Feedback.Down", "Not helpful");
        SaveCommentButton = NewButton("ActionGraph.Feedback.Save", "Save comment", ButtonVariant.Primary);
        DeleteFeedbackButton = NewButton("ActionGraph.Feedback.Delete", "Delete feedback");
        CommentInput = new Input { Name = "ActionGraph.Feedback.Comment", Placeholder = "Comment on this action…" };
        CommentInput.Multiline = true;
        CommentInput.SubmitOnEnter = false;
        CommentInput.Accessibility.AccessibleName = "Comment on this action";

        workspace.Add(DetailsCard);
    }

    private void BuildStatusLine()
    {
        StatusText = new HavenText(string.Empty) { Name = "ActionGraph.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Row, 3);
        StatusText.SetValue(HavenProperties.Foreground, "TextMuted");
        Root.Add(StatusText);
    }

    /// <summary>Rebuilds the category legend from categories actually present in the trace.</summary>
    public void SetLegend(IReadOnlyList<ActionGraphCategory> categories, bool timeMode, int nodeCount)
    {
        LegendHost.Children.ToList().ForEach(child => child.Parent?.Remove(child));
        foreach (var category in categories)
        {
            var chip = new Container { Name = $"ActionGraph.Legend.{category}", Layout = HavenLayout.Horizontal };
            chip.SetValue(HavenProperties.Gap, HavenLength.Px(5));
            chip.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
            var swatch = new Container { Name = $"ActionGraph.Legend.{category}.Swatch" };
            swatch.SetValue(HavenProperties.Width, HavenLength.Px(11));
            swatch.SetValue(HavenProperties.Height, HavenLength.Px(11));
            swatch.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(3)));
            swatch.SetValue(HavenProperties.Background, ActionGraphCatalog.CategoryToken(category));
            swatch.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
            swatch.Accessibility.AccessibleName = $"{ActionGraphCatalog.CategoryName(category)} colour swatch";
            var label = new HavenText(ActionGraphCatalog.CategoryName(category)) { Level = TextLevel.Caption };
            label.SetValue(HavenProperties.Foreground, "TextSecondary");
            label.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
            chip.Add(swatch);
            chip.Add(label);
            LegendHost.Add(chip);
        }
        LegendCaption.Content = nodeCount == 0
            ? string.Empty
            : timeMode ? $"{nodeCount} steps · time ruler shows real timestamps" : $"{nodeCount} steps · sequence order (timestamps unavailable)";
    }

    public void SetMetrics(ActionGraphMetrics metrics)
    {
        StepsValue.Content = metrics.StepsExecuted.ToString(System.Globalization.CultureInfo.InvariantCulture);
        StepsDetail.Content = metrics.StepsExecuted == 0 ? "no activity yet" : $"{metrics.CompletedPercent}% completed";
        ToolsValue.Content = metrics.ToolsCalled.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ToolsDetail.Content = metrics.ToolsCalled == 1 ? "API or tool invocation" : "API or tool invocations";
        AppsValue.Content = metrics.AppsUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AppsDetail.Content = metrics.AppsUsed == 0 ? "no integrations used" : "distinct integrations";
        RetriesValue.Content = metrics.Retries.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RetriesDetail.Content = metrics.Retries == 0 ? "straight-through run" : "steps retried";
        TimeValue.Content = metrics.TotalTime is { } time ? ActionGraphProjection.FormatDuration(time) : "—";
        TimeDetail.Content = metrics.TotalTime is { } ? "end to end" : "timing not recorded";
    }

    public void SetZoomPercent(double zoom)
    {
        ZoomLabel.Content = Math.Round(zoom * 100, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    public void SetStatus(string message) => StatusText.Content = message ?? string.Empty;

    public void SetViewMode(bool graph)
    {
        GraphViewButton.Variant = graph ? ButtonVariant.Primary : ButtonVariant.Tertiary;
        ListViewButton.Variant = graph ? ButtonVariant.Tertiary : ButtonVariant.Primary;
        GraphOverlay.SetValue(HavenProperties.Visibility, graph ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ListHost.SetValue(HavenProperties.Visibility, graph ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetDetailsCollapsed(bool collapsed)
    {
        _detailsCollapsed = collapsed;
        DetailsCard.SetValue(HavenProperties.Visibility, collapsed ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        CollapseDetailsButton.Content = collapsed ? "Show" : "Hide";
        CollapseDetailsButton.IconKey = collapsed ? "chevron-left" : "chevron-right";
        CollapseDetailsButton.Accessibility.AccessibleName = collapsed ? "Show node details pane" : "Hide node details pane";
        ApplyWorkspaceColumns();
    }

    /// <summary>Narrow-window layout: the canvas keeps priority and the sidebar steps aside.</summary>
    public void SetCompact(bool compact)
    {
        _compact = compact;
        var sidebar = HistoryList.Parent as Container;
        if (sidebar is not null) sidebar.SetValue(HavenProperties.Visibility, compact ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        ApplyWorkspaceColumns();
    }

    public bool IsDetailsCollapsed() => _detailsCollapsed;

    private void ApplyWorkspaceColumns() =>
        _workspace.Columns = _compact
            ? "0px 0px 1fr 0px 0px"
            : _detailsCollapsed ? "264px 10px 1fr 10px 0px" : "264px 10px 1fr 10px 300px";
}
