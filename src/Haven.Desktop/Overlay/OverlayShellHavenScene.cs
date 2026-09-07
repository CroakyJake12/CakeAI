#if !ANDROID
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Overlay;

internal sealed class OverlayShellHavenScene : IDisposable
{
    private readonly DynamicUI _dynamicUi;
    private readonly HavenButton[] _suggestionButtons;
    private readonly string[] _suggestionPrompts =
    [
        "Continue Code Refactor",
        "Review today's tasks",
        "Summarise this file",
        "Research a question"
    ];
    private bool _disposed;

    public OverlayShellHavenScene()
    {
        Root = new Page { Name = "Overlay.Root", Layout = HavenLayout.Vertical };
        Set(Root, HavenProperties.Width, HavenLength.Percent(100));
        Set(Root, HavenProperties.Background, "Transparent");

        CollapsedPromptButton = Action("Overlay.CollapsedPrompt", "Ask Haven about your Screen", ButtonVariant.Primary);
        Set(CollapsedPromptButton, HavenProperties.Width, HavenLength.Percent(100));
        Set(CollapsedPromptButton, HavenProperties.MinHeight, HavenLength.Px(56));
        CollapsedPromptButton.Accessibility.AccessibleName = "Ask Haven about your Screen";
        Root.Add(CollapsedPromptButton);

        ExpandedPanel = new Container { Name = "Overlay.Expanded", Layout = HavenLayout.Vertical };
        Set(ExpandedPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ExpandedPanel, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(ExpandedPanel, HavenProperties.Gap, HavenLength.Px(12));
        Set(ExpandedPanel, HavenProperties.Background, "Surface");
        Set(ExpandedPanel, HavenProperties.BorderColor, "Border");
        Set(ExpandedPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(ExpandedPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        Root.Add(ExpandedPanel);

        var header = new OverlayDragHandle
        {
            Name = "Overlay.Header",
            Layout = HavenLayout.Grid,
            Columns = "Auto 1fr Auto Auto Auto Auto Auto"
        };
        Set(header, HavenProperties.Width, HavenLength.Percent(100));
        Set(header, HavenProperties.Gap, HavenLength.Px(6));
        Set(header, HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        Set(header, HavenProperties.Background, "Overlay");
        Set(header, HavenProperties.BorderColor, "AccentSecondary");
        Set(header, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(header, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(header, HavenProperties.Shadow, "Floating");
        Set(header, HavenProperties.Glow, "AccentSecondaryGlow");
        header.DragDelta += delta => DragDelta?.Invoke(delta);

        ModeButton = Action("Overlay.Mode", "Go", ButtonVariant.Tertiary);
        header.Add(ModeButton);

        var identity = new Container { Name = "Overlay.Identity", Layout = HavenLayout.Vertical };
        Set(identity, HavenProperties.Column, 1);
        TitleText = new HavenText("Overlay") { Name = "Overlay.Title", Level = TextLevel.Caption };
        SourceText = Secondary("Independent Haven workspace", "Overlay.Source");
        Set(SourceText, HavenProperties.Visibility, HavenVisibility.Collapsed);
        identity.Add(TitleText);
        identity.Add(SourceText);
        header.Add(identity);

        BackButton = Action("Overlay.Back", "Back", ButtonVariant.Ghost);
        Set(BackButton, HavenProperties.Column, 2);
        Set(BackButton, HavenProperties.Visibility, HavenVisibility.Collapsed);
        header.Add(BackButton);

        CaptureButton = Action("Overlay.Capture", "AI Select", ButtonVariant.Secondary);
        NewChatButton = Action("Overlay.NewChat", "New chat", ButtonVariant.Ghost);
        PinButton = Action("Overlay.Pin", "Pin", ButtonVariant.Ghost);
        CollapseButton = Action("Overlay.Collapse", "Collapse", ButtonVariant.Ghost);
        CloseButton = Action("Overlay.Close", "Close", ButtonVariant.Ghost);
        Set(CaptureButton, HavenProperties.Column, 3);
        Set(PinButton, HavenProperties.Column, 4);
        Set(CollapseButton, HavenProperties.Column, 5);
        Set(CloseButton, HavenProperties.Column, 6);
        Set(NewChatButton, HavenProperties.Visibility, HavenVisibility.Collapsed);
        header.Add(CaptureButton);
        header.Add(PinButton);
        header.Add(CollapseButton);
        header.Add(CloseButton);
        header.Add(NewChatButton);
        ExpandedPanel.Add(header);

        Heading = new HavenText("Ask Haven about your Screen") { Name = "Overlay.Heading", Level = TextLevel.H2 };
        ExpandedPanel.Add(Heading);

        AppHostPanel = new Container { Name = "Overlay.AppHost", Layout = HavenLayout.Vertical };
        Set(AppHostPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(AppHostPanel, HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
        Set(AppHostPanel, HavenProperties.Background, "SurfaceRaised");
        Set(AppHostPanel, HavenProperties.BorderColor, "Border");
        Set(AppHostPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(AppHostPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        AppHostTitle = new HavenText(string.Empty) { Name = "Overlay.AppHost.Title", Level = TextLevel.Caption };
        AppHostIdentity = Secondary(string.Empty, "Overlay.AppHost.Identity");
        AppHostPanel.Add(AppHostTitle);
        AppHostPanel.Add(AppHostIdentity);
        Set(AppHostPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(AppHostPanel);

        SuggestionsPanel = new Container
        {
            Name = "Overlay.Suggestions",
            Layout = HavenLayout.Grid,
            Columns = "1fr 1fr",
            Rows = "Auto Auto"
        };
        Set(SuggestionsPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(SuggestionsPanel, HavenProperties.Gap, HavenLength.Px(8));
        _suggestionButtons = new HavenButton[4];
        for (var index = 0; index < _suggestionButtons.Length; index++)
        {
            var button = Action($"Overlay.Suggestion.{index}", _suggestionPrompts[index], ButtonVariant.Secondary);
            Set(button, HavenProperties.Row, index / 2);
            Set(button, HavenProperties.Column, index % 2);
            var captured = index;
            button.Invoked += (_, _) => SubmitRequested?.Invoke(this, _suggestionPrompts[captured]);
            SuggestionsPanel.Add(button);
            _suggestionButtons[index] = button;
        }
        ExpandedPanel.Add(SuggestionsPanel);

        SessionTabs = new DynamicUIRuntime { Name = "Overlay.SessionTabs", Layout = HavenLayout.Horizontal };
        Set(SessionTabs, HavenProperties.Width, HavenLength.Percent(100));
        Set(SessionTabs, HavenProperties.Gap, HavenLength.Px(6));
        Set(SessionTabs, HavenProperties.Overflow, HavenOverflow.Scroll);
        Set(SessionTabs, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(SessionTabs);

        ContextPanel = new Container { Name = "Overlay.Context", Layout = HavenLayout.Vertical };
        Set(ContextPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ContextPanel, HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        Set(ContextPanel, HavenProperties.Background, "SurfaceRaised");
        Set(ContextPanel, HavenProperties.BorderColor, "Border");
        Set(ContextPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(ContextPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        ContextSummary = new HavenText("No selected context.") { Name = "Overlay.Context.Summary", Level = TextLevel.Caption };
        PermissionText = Secondary("Capture inactive", "Overlay.Context.Permission");
        ContextPanel.Add(ContextSummary);
        ContextPanel.Add(PermissionText);
        Set(ContextPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(ContextPanel);

        RegionPreviewPanel = new Container { Name = "Overlay.RegionPreview", Layout = HavenLayout.Vertical };
        Set(RegionPreviewPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(RegionPreviewPanel, HavenProperties.Gap, HavenLength.Px(6));
        Set(RegionPreviewPanel, HavenProperties.Padding, HavenThickness.Parse("8px"));
        Set(RegionPreviewPanel, HavenProperties.Background, "SurfaceRaised");
        Set(RegionPreviewPanel, HavenProperties.BorderColor, "Border");
        Set(RegionPreviewPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(RegionPreviewPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        RegionPreview = new VisionPreviewElement { Name = "Overlay.RegionPreview.Image" };
        Set(RegionPreview, HavenProperties.Width, HavenLength.Percent(100));
        Set(RegionPreview, HavenProperties.Height, HavenLength.Px(180));
        RegionPreview.SetMode(VisionInteractionMode.SelectRegion);
        RegionStatus = Secondary("Choose a region from the captured frame.", "Overlay.RegionPreview.Status");
        RegionPreviewPanel.Add(RegionPreview);
        RegionPreviewPanel.Add(RegionStatus);
        var regionActions = new Container { Name = "Overlay.RegionPreview.Actions", Layout = HavenLayout.Wrap };
        Set(regionActions, HavenProperties.Width, HavenLength.Percent(100));
        Set(regionActions, HavenProperties.Gap, HavenLength.Px(6));
        ApplySelectionButton = Action("Overlay.RegionPreview.Apply", "Apply selection", ButtonVariant.Primary);
        ReplaceCaptureButton = Action("Overlay.RegionPreview.Replace", "Replace capture", ButtonVariant.Secondary);
        ClearSelectionButton = Action("Overlay.RegionPreview.Clear", "Clear selection", ButtonVariant.Ghost);
        Set(ApplySelectionButton, HavenProperties.Enabled, false);
        regionActions.Add(ApplySelectionButton);
        regionActions.Add(ReplaceCaptureButton);
        regionActions.Add(ClearSelectionButton);
        RegionPreviewPanel.Add(regionActions);
        Set(RegionPreviewPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(RegionPreviewPanel);
        RegionPreview.RegionChanged += (_, _) => UpdateRegionSelectionState();

        Actions = new DynamicUIRuntime { Name = "Overlay.Actions", Layout = HavenLayout.Horizontal };
        Set(Actions, HavenProperties.Width, HavenLength.Percent(100));
        Set(Actions, HavenProperties.Gap, HavenLength.Px(6));
        Set(Actions, HavenProperties.Overflow, HavenOverflow.Scroll);
        ExpandedPanel.Add(Actions);

        var composerRow = new Container
        {
            Name = "Overlay.Composer",
            Layout = HavenLayout.Grid,
            Columns = "Auto 1fr Auto"
        };
        Set(composerRow, HavenProperties.Width, HavenLength.Percent(100));
        Set(composerRow, HavenProperties.Gap, HavenLength.Px(8));
        Set(composerRow, HavenProperties.Padding, HavenThickness.Parse("8px"));
        Set(composerRow, HavenProperties.Background, "SurfaceRaised");
        Set(composerRow, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));

        AddButton = Action("Overlay.Composer.Add", "+", ButtonVariant.Ghost);
        ComposerInput = new Input
        {
            Name = "Overlay.Composer.Input",
            Placeholder = "Ask Haven anything",
            SubmitOnEnter = true
        };
        Set(ComposerInput, HavenProperties.Column, 1);
        Set(ComposerInput, HavenProperties.Width, HavenLength.Percent(100));
        SendButton = Action("Overlay.Composer.Send", "→", ButtonVariant.Primary);
        Set(SendButton, HavenProperties.Column, 2);
        composerRow.Add(AddButton);
        composerRow.Add(ComposerInput);
        composerRow.Add(SendButton);
        ExpandedPanel.Add(composerRow);

        _dynamicUi = new DynamicUI(Root, HavenDynamicUITemplateCatalog.FromAssembly(typeof(OverlayShellHavenScene).Assembly));

        CollapsedPromptButton.Invoked += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        BackButton.Invoked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        CaptureButton.Invoked += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);
        ApplySelectionButton.Invoked += (_, _) => ApplySelectionRequested?.Invoke(this, EventArgs.Empty);
        ReplaceCaptureButton.Invoked += (_, _) => ReplaceCaptureRequested?.Invoke(this, EventArgs.Empty);
        ClearSelectionButton.Invoked += (_, _) => ClearSelectionRequested?.Invoke(this, EventArgs.Empty);
        NewChatButton.Invoked += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        PinButton.Invoked += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
        CollapseButton.Invoked += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.Invoked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        AddButton.Invoked += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        SendButton.Invoked += (_, _) => SubmitComposer();

        AppHost = new OverlayCompactAppHost();
        SetCollapsed(false);
    }

    public event EventHandler? CaptureRequested;
    public event EventHandler? ApplySelectionRequested;
    public event EventHandler? ReplaceCaptureRequested;
    public event EventHandler? ClearSelectionRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? NewChatRequested;
    public event EventHandler? PinToggleRequested;
    public event EventHandler? CollapseToggleRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? AddRequested;
    public event EventHandler<string>? SubmitRequested;
    public event EventHandler<Guid>? SessionActivated;
    public event EventHandler<OverlayContextActionDescriptor>? ActionRequested;
    public event Action<HavenPoint>? DragDelta;

    public Page Root { get; }
    public Container ExpandedPanel { get; }
    public HavenButton CollapsedPromptButton { get; }
    public HavenButton ModeButton { get; }
    public HavenButton BackButton { get; }
    public HavenText Heading { get; }
    public HavenText TitleText { get; }
    public HavenText SourceText { get; }
    public Container AppHostPanel { get; }
    public HavenText AppHostTitle { get; }
    public HavenText AppHostIdentity { get; }
    public HavenButton CaptureButton { get; }
    public HavenButton NewChatButton { get; }
    public HavenButton PinButton { get; }
    public HavenButton CollapseButton { get; }
    public HavenButton CloseButton { get; }
    public Container SuggestionsPanel { get; }
    public DynamicUIRuntime SessionTabs { get; }
    public Container ContextPanel { get; }
    public HavenText ContextSummary { get; }
    public HavenText PermissionText { get; }
    public Container RegionPreviewPanel { get; }
    public VisionPreviewElement RegionPreview { get; }
    public HavenText RegionStatus { get; }
    public HavenButton ApplySelectionButton { get; }
    public HavenButton ReplaceCaptureButton { get; }
    public HavenButton ClearSelectionButton { get; }
    public HavenRect? SelectedRegion => RegionPreview.SelectedRegion;
    public DynamicUIRuntime Actions { get; }
    public HavenButton AddButton { get; }
    public Input ComposerInput { get; }
    public HavenButton SendButton { get; }
    public OverlayCompactAppHost AppHost { get; }
    public OverlayCompactAppRoute CurrentRoute => AppHost.CurrentRoute;

    public void ApplySnapshot(OverlayWorkspaceSnapshot snapshot, Guid windowSessionId)
    {
        var current = snapshot.Sessions.FirstOrDefault(session => session.Id == windowSessionId);
        if (current is null) return;

        TitleText.Content = current.Title;
        SourceText.Content = SourceLabel(current);
        var route = OverlayCompactAppHost.ForSession(current);
        if (AppHost.CurrentRoute.IsHome && !AppHost.CanNavigateBack)
            AppHost.InitializeFromSession(route);
        ProjectRoute(AppHost.CurrentRoute);
        PinButton.Content = current.IsPinned ? "Unpin" : "Pin";
        PinButton.Variant = current.IsPinned ? ButtonVariant.Primary : ButtonVariant.Ghost;
        CollapseButton.Content = current.IsCollapsed ? "Expand" : "Collapse";
        CollapseButton.Variant = current.IsCollapsed ? ButtonVariant.Secondary : ButtonVariant.Ghost;
        ContextSummary.Content = current.Context is null ? "No selected context." : ContextLabel(current.Context);
        PermissionText.Content = current.Context is null ? "Capture inactive" : PermissionLabel(current.Context.Provenance);
        Set(ContextPanel, HavenProperties.Visibility, current.Context is null && RegionPreviewPanel.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible
            ? HavenVisibility.Collapsed
            : HavenVisibility.Visible);
        RebuildSessions(snapshot, windowSessionId);
        SetCollapsed(current.IsCollapsed);
    }

    public void SetBackNavigation(bool canGoBack, string? destination = null)
    {
        Set(BackButton, HavenProperties.Visibility, canGoBack ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        BackButton.Accessibility.AccessibleName = string.IsNullOrWhiteSpace(destination)
            ? "Back"
            : "Back to " + destination;
    }

    public bool NavigateTo(OverlayCompactAppRoute route)
    {
        if (!AppHost.NavigateTo(route)) return false;
        ProjectRoute(AppHost.CurrentRoute);
        SetBackNavigation(AppHost.CanNavigateBack, AppHost.History[^1].Title);
        return true;
    }

    public bool NavigateHome()
    {
        if (!AppHost.NavigateHome()) return false;
        ProjectRoute(AppHost.CurrentRoute);
        SetBackNavigation(AppHost.CanNavigateBack, AppHost.History[^1].Title);
        return true;
    }

    public bool NavigateBack()
    {
        if (!AppHost.TryNavigateBack()) return false;
        ProjectRoute(AppHost.CurrentRoute);
        SetBackNavigation(AppHost.CanNavigateBack,
            AppHost.CanNavigateBack ? AppHost.History[^1].Title : null);
        return true;
    }

    public void SetSuggestions(IReadOnlyList<string> labels)
    {
        for (var index = 0; index < _suggestionButtons.Length; index++)
        {
            var label = index < labels.Count && !string.IsNullOrWhiteSpace(labels[index])
                ? labels[index].Trim()
                : _suggestionPrompts[index];
            _suggestionPrompts[index] = label;
            _suggestionButtons[index].Content = label;
            _suggestionButtons[index].Accessibility.AccessibleName = label;
        }
    }

    public void SubmitComposer()
    {
        var text = ComposerInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        ComposerInput.Text = string.Empty;
        SubmitRequested?.Invoke(this, text);
    }

    public void ShowRegionDraft(string sourcePath, string status)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A captured source path is required.", nameof(sourcePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The captured source image is unavailable.", sourcePath);
        RegionPreview.Source = sourcePath;
        RegionPreview.SetMode(VisionInteractionMode.SelectRegion);
        Set(RegionPreviewPanel, HavenProperties.Visibility, HavenVisibility.Visible);
        Set(ContextPanel, HavenProperties.Visibility, HavenVisibility.Visible);
        UpdateRegionSelectionState();
        RegionStatus.Content = string.IsNullOrWhiteSpace(status)
            ? "Drag over the captured frame to choose a region."
            : status;
    }

    public void SetRegionStatus(string status)
    {
        Set(RegionPreviewPanel, HavenProperties.Visibility, HavenVisibility.Visible);
        Set(ContextPanel, HavenProperties.Visibility, HavenVisibility.Visible);
        UpdateRegionSelectionState();
        RegionStatus.Content = string.IsNullOrWhiteSpace(status) ? "Capture status unavailable." : status;
    }

    public void ClearRegionDraft()
    {
        RegionPreview.Source = null;
        RegionPreview.ClearRegion();
        Set(RegionPreviewPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        Set(ContextPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        RegionStatus.Content = "Choose a region from the captured frame.";
        UpdateRegionSelectionState();
    }

    private void UpdateRegionSelectionState()
    {
        var hasSelection = RegionPreview.SelectedRegion is HavenRect region
                           && double.IsFinite(region.X) && double.IsFinite(region.Y)
                           && double.IsFinite(region.Width) && double.IsFinite(region.Height)
                           && region.Width > 0 && region.Height > 0;
        Set(ApplySelectionButton, HavenProperties.Enabled, hasSelection);
        if (RegionPreview.Source is not null && !hasSelection)
            RegionStatus.Content = "Drag over the captured frame to choose a region.";
        else if (hasSelection)
            RegionStatus.Content = "Region selected. Apply it when ready, or replace the capture.";
    }

    public void SetCollapsed(bool collapsed)
    {
        Set(CollapsedPromptButton, HavenProperties.Visibility, collapsed ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Set(ExpandedPanel, HavenProperties.Visibility, collapsed ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetActions(IReadOnlyList<OverlayContextActionDescriptor> actions)
    {
        _dynamicUi.Clear("Overlay.Actions");
        Set(Actions, HavenProperties.Visibility, actions.Count == 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        for (var index = 0; index < actions.Count; index++)
        {
            var descriptor = actions[index];
            var item = _dynamicUi.CreateItem(
                "OverlayActionChip", "Overlay.Actions", ActionId(descriptor.Id, index),
                new Dictionary<string, object?> { ["LABEL"] = ActionLabel(descriptor) }, index);
            var button = item.GetComponent<HavenButton>("Invoke");
            button.Variant = descriptor.Id.Equals("ask-haven", StringComparison.OrdinalIgnoreCase)
                ? ButtonVariant.Primary
                : descriptor.IsGenerated ? ButtonVariant.Secondary : ButtonVariant.Ghost;
            button.Accessibility.AccessibleName = descriptor.Label;
            button.Invoked += (_, _) => ActionRequested?.Invoke(this, descriptor);
        }
    }

    private void RebuildSessions(OverlayWorkspaceSnapshot snapshot, Guid windowSessionId)
    {
        _dynamicUi.Clear("Overlay.SessionTabs");
        for (var index = 0; index < snapshot.Sessions.Count; index++)
        {
            var session = snapshot.Sessions[index];
            var item = _dynamicUi.CreateItem(
                "OverlaySessionTab", "Overlay.SessionTabs", session.Id.ToString("N"),
                new Dictionary<string, object?> { ["LABEL"] = session.Title + (session.IsPinned ? " · pinned" : "") }, index);
            var button = item.GetComponent<HavenButton>("Activate");
            button.Variant = session.Id == windowSessionId ? ButtonVariant.Primary : ButtonVariant.Secondary;
            var id = session.Id;
            button.Invoked += (_, _) => SessionActivated?.Invoke(this, id);
        }
    }

    private static string SourceLabel(OverlaySessionState session)
    {
        var provenance = session.Context?.Provenance;
        var parts = new[] { provenance?.SourceApplication, provenance?.SourceWindow }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);
        var label = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(label)
            ? session.SourceAssociation
              ?? (session.AppKey.Equals("chat", StringComparison.OrdinalIgnoreCase) ? "Haven Chat" : "Haven Go")
            : label;
    }

    private void ProjectRoute(OverlayCompactAppRoute route)
    {
        ModeButton.Content = route.IsRouter ? "Go · route" : route.Title;
        ModeButton.Accessibility.AccessibleName = route.IsHome
            ? "Overlay home"
            : route.IsRouter ? "Go routing" : "Current Overlay app " + route.Title;
        AppHostTitle.Content = route.IsRouter ? "Go routing" : route.Title;
        AppHostIdentity.Content = route.Identity;
        Set(AppHostPanel, HavenProperties.Visibility,
            route.IsHome ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private static string ContextLabel(OverlayContextEnvelope context) =>
        OverlaySelectionPresentation.ContextLabel(context);
    private static string PermissionLabel(OverlayContextProvenance provenance)
    {
        var state = provenance.PermissionState switch
        {
            OverlayContextPermissionState.Granted => "Capture allowed",
            OverlayContextPermissionState.Denied => "Capture denied",
            OverlayContextPermissionState.Unavailable => "Capture unavailable",
            _ => "No capture permission required"
        };
        return string.IsNullOrWhiteSpace(provenance.PermissionDescription)
            ? state
            : state + " · " + provenance.PermissionDescription;
    }
    private static string ActionLabel(OverlayContextActionDescriptor descriptor) =>
        descriptor.IsGenerated && descriptor.Availability == CapabilityAvailability.PermissionRequired
            ? descriptor.Label + " · asks"
            : descriptor.Label;
    private static string ActionId(string value, int index) =>
        new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()) + "-" + index;

    private static HavenText Secondary(string content, string name)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.Caption };
        Set(text, HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton Action(string name, string content, ButtonVariant variant) =>
        new() { Name = name, Content = content, Variant = variant };

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) =>
        element.SetValue(property, value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dynamicUi.Clear("Overlay.SessionTabs");
        _dynamicUi.Clear("Overlay.Actions");
    }
}
#endif
