#if !ANDROID
namespace Haven.Desktop.Overlay;

internal enum OverlayContextKind
{
    None = 0,
    Text = 1,
    Image = 2,
    Region = 3,
    Mixed = 4,
    UiComponent = 5,
    Video = 6,
    Window = 7,
    Screen = 8
}

internal enum OverlaySelectionKind
{
    Text = 1,
    Image = 2,
    UiComponent = 3,
    Video = 4,
    Region = 5,
    Window = 6,
    Screen = 7
}

internal enum OverlayContextPermissionState
{
    NotRequired = 0,
    Granted = 1,
    Denied = 2,
    Unavailable = 3
}

internal sealed record OverlaySelectionBounds(double X, double Y, double Width, double Height)
{
    // Bounds come from UI Automation or the OS capture surface. Keep them finite and
    // bounded before they reach persistence, prompts, or an attachment review.
    internal OverlaySelectionBounds? Normalize()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y)
            || !double.IsFinite(Width) || !double.IsFinite(Height)
            || Width <= 0 || Height <= 0)
            return null;

        const double maxCoordinate = 10_000_000;
        const double maxDimension = 10_000_000;
        return this with
        {
            X = Math.Clamp(X, -maxCoordinate, maxCoordinate),
            Y = Math.Clamp(Y, -maxCoordinate, maxCoordinate),
            // Normalized region coordinates commonly use fractions below one;
            // only cap oversized dimensions and preserve every finite positive value.
            Width = Math.Min(Width, maxDimension),
            Height = Math.Min(Height, maxDimension)
        };
    }
}

internal sealed record OverlayContextProvenance(
    string? SourceApplication,
    string? SourceWindow,
    OverlaySelectionBounds? Bounds,
    DateTimeOffset CapturedAt,
    DateTimeOffset ExpiresAt,
    OverlayContextPermissionState PermissionState,
    string? PermissionDescription)
{
    internal OverlayContextProvenance Bound(int maxStringLength = 512)
    {
        if (maxStringLength < 1) throw new ArgumentOutOfRangeException(nameof(maxStringLength));
        return this with
        {
            SourceApplication = Limit(SourceApplication, maxStringLength),
            SourceWindow = Limit(SourceWindow, maxStringLength),
            Bounds = Bounds?.Normalize(),
            PermissionDescription = Limit(PermissionDescription, maxStringLength * 2)
        };
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record OverlayContextAttachmentReference(
    string Id,
    string Kind,
    string? MimeType,
    string? DisplayName,
    string? MetadataJson)
{
    internal OverlayContextAttachmentReference Bound(
        int maxIdLength = 4_096,
        int maxDisplayNameLength = 512,
        int maxMetadataLength = 8_192)
    {
        if (maxIdLength < 1) throw new ArgumentOutOfRangeException(nameof(maxIdLength));
        if (maxDisplayNameLength < 1) throw new ArgumentOutOfRangeException(nameof(maxDisplayNameLength));
        if (maxMetadataLength < 1) throw new ArgumentOutOfRangeException(nameof(maxMetadataLength));
        return this with
        {
            Id = Limit(Id, maxIdLength) ?? string.Empty,
            Kind = Limit(Kind, 128) ?? string.Empty,
            MimeType = Limit(MimeType, 128),
            // Display names are shown to users. Preserve the truncation marker so a
            // bounded label cannot be mistaken for the complete source name.
            DisplayName = LimitWithEllipsis(DisplayName, maxDisplayNameLength),
            MetadataJson = Limit(MetadataJson, maxMetadataLength)
        };
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private static string? LimitWithEllipsis(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : maxLength == 1 ? "…" : value[..(maxLength - 1)] + "…";
}

internal sealed record OverlaySelectionSemanticMetadata(
    string? Role,
    string? AccessibleName,
    string? AutomationId,
    string? ControlType,
    bool? IsEnabled,
    bool? IsSelected,
    string? MediaKind,
    double? MediaPositionSeconds)
{
    public OverlaySelectionSemanticMetadata Bound(int maxStringLength = 512)
    {
        if (maxStringLength < 1) throw new ArgumentOutOfRangeException(nameof(maxStringLength));
        return this with
        {
            Role = Limit(Role, maxStringLength),
            AccessibleName = Limit(AccessibleName, maxStringLength),
            AutomationId = Limit(AutomationId, maxStringLength),
            ControlType = Limit(ControlType, maxStringLength),
            MediaKind = Limit(MediaKind, maxStringLength)
        };
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record OverlaySelectionItem(
    string Id,
    OverlaySelectionKind Kind,
    OverlaySelectionBounds? Bounds,
    string? Text,
    string? MediaReference,
    OverlayContextAttachmentReference? Attachment,
    OverlaySelectionSemanticMetadata? Semantic,
    string? DisplayName)
{
    public bool HasPayload => Bounds is not null
                              || !string.IsNullOrWhiteSpace(Text)
                              || !string.IsNullOrWhiteSpace(MediaReference)
                              || Attachment is not null
                              || Semantic is not null
                              || !string.IsNullOrWhiteSpace(DisplayName);

    public OverlaySelectionItem Bound(int maxTextLength = 8_192, int maxDisplayNameLength = 512)
    {
        if (maxTextLength < 1) throw new ArgumentOutOfRangeException(nameof(maxTextLength));
        if (maxDisplayNameLength < 1) throw new ArgumentOutOfRangeException(nameof(maxDisplayNameLength));
        return this with
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Limit(Id, 256)!,
            Bounds = Bounds?.Normalize(),
            Text = Limit(Text, maxTextLength),
            DisplayName = LimitWithEllipsis(DisplayName, maxDisplayNameLength),
            Attachment = Attachment?.Bound(),
            MediaReference = Limit(MediaReference, 4_096),
            Semantic = Semantic?.Bound()
        };
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private static string? LimitWithEllipsis(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : maxLength == 1 ? "…" : value[..(maxLength - 1)] + "…";
}

internal sealed record OverlayContextEnvelope(
    OverlayContextKind Kind,
    string? SelectedText,
    List<OverlayContextAttachmentReference> Attachments,
    string? MediaReference,
    OverlayContextProvenance Provenance,
    bool WasTruncated = false,
    List<OverlaySelectionItem>? Selections = null)
{
    public IReadOnlyList<OverlaySelectionItem> SelectedItems => Selections ?? [];

    public bool HasTextualSelection =>
        !string.IsNullOrWhiteSpace(SelectedText)
        || SelectedItems.Any(item => item.Kind == OverlaySelectionKind.Text
            && !string.IsNullOrWhiteSpace(item.Text));

    public bool HasVisualSelection =>
        SelectedItems.Any(item => item.Kind is OverlaySelectionKind.Image or OverlaySelectionKind.Region
            or OverlaySelectionKind.Video or OverlaySelectionKind.Window or OverlaySelectionKind.Screen
            && item.HasPayload)
        || (Kind is OverlayContextKind.Image or OverlayContextKind.Region or OverlayContextKind.Video
            or OverlayContextKind.Window or OverlayContextKind.Screen)
            && (Attachments.Count > 0 || !string.IsNullOrWhiteSpace(MediaReference));

    public bool HasInteractiveSelection =>
        SelectedItems.Any(item => item.Kind == OverlaySelectionKind.UiComponent && item.HasPayload);

    public bool HasMediaSelection =>
        (Kind == OverlayContextKind.Video
            && (Attachments.Count > 0 || !string.IsNullOrWhiteSpace(MediaReference)))
        || SelectedItems.Any(item => item.Kind == OverlaySelectionKind.Video && item.HasPayload);

    public bool HasWindowOrScreenSelection =>
        (Kind is OverlayContextKind.Window or OverlayContextKind.Screen)
            && (Attachments.Count > 0 || !string.IsNullOrWhiteSpace(MediaReference))
        || SelectedItems.Any(item => item.Kind is OverlaySelectionKind.Window or OverlaySelectionKind.Screen
            && item.HasPayload);

    public bool HasPayload => !string.IsNullOrWhiteSpace(SelectedText)
                              || Attachments.Count > 0
                              || !string.IsNullOrWhiteSpace(MediaReference)
                              || SelectedItems.Any(item => item.HasPayload);

    public bool IsExpired(DateTimeOffset now) => now >= Provenance.ExpiresAt;

    public OverlayContextEnvelope Bound(
        int maxTextLength = 32_768,
        int maxAttachments = 8,
        int maxSelections = 16)
    {
        if (maxTextLength < 1) throw new ArgumentOutOfRangeException(nameof(maxTextLength));
        if (maxAttachments < 0) throw new ArgumentOutOfRangeException(nameof(maxAttachments));
        if (maxSelections < 0) throw new ArgumentOutOfRangeException(nameof(maxSelections));

        var text = SelectedText;
        var truncated = WasTruncated;
        if (text is { Length: > 0 } && text.Length > maxTextLength)
        {
            text = text[..maxTextLength];
            truncated = true;
        }

        var rawAttachments = Attachments ?? [];
        var attachments = rawAttachments.Take(maxAttachments).Select(item => item.Bound()).ToList();
        if (attachments.Count != rawAttachments.Count) truncated = true;
        if (rawAttachments.Zip(attachments).Any(pair => pair.First != pair.Second)) truncated = true;

        var rawSelections = SelectedItems;
        var selections = rawSelections.Take(maxSelections).Select(item => item.Bound()).ToList();
        if (selections.Count != rawSelections.Count) truncated = true;
        if (rawSelections.Zip(selections).Any(pair => pair.First.Text?.Length != pair.Second.Text?.Length
                                                     || pair.First.DisplayName?.Length != pair.Second.DisplayName?.Length
                                                     || pair.First.MediaReference?.Length != pair.Second.MediaReference?.Length
                                                     || pair.First.Bounds != pair.Second.Bounds
                                                     || pair.First.Attachment != pair.Second.Attachment))
            truncated = true;

        return this with
        {
            SelectedText = text,
            Attachments = attachments,
            Selections = selections,
            Provenance = Provenance.Bound(),
            WasTruncated = truncated
        };
    }
}

internal sealed record OverlaySurfaceGeometry(double Width, double Height, double X, double Y)
{
    public static OverlaySurfaceGeometry Default => new(520, 620, 120, 120);

    public OverlaySurfaceGeometry Bound() => this with
    {
        Width = Math.Clamp(Width, 240, 1600),
        Height = Math.Clamp(Height, 160, 1200)
    };
}

internal sealed record OverlaySessionState(
    Guid Id,
    string AppKey,
    string Title,
    Guid? ThreadId,
    bool IsPinned,
    bool IsVisible,
    OverlaySurfaceGeometry Geometry,
    OverlayContextEnvelope? Context,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SourceAssociation,
    bool IsCollapsed = false);

internal sealed class OverlayWorkspacePersistedState
{
    public int Version { get; init; } = 1;
    public List<OverlaySessionState> PinnedSessions { get; init; } = [];
}

internal sealed record OverlayWorkspaceSnapshot(Guid? ActiveSessionId, IReadOnlyList<OverlaySessionState> Sessions);

internal sealed record OverlayContextActionDescriptor(
    string Id,
    string Label,
    string IconKey,
    bool RequiresContext,
    bool IsGenerated = false,
    string? ToolName = null,
    Haven.Core.CapabilityRiskClass? RiskClass = null,
    Haven.Core.CapabilityAvailability? Availability = null,
    string? ProviderId = null,
    string? ImplementationKey = null);

internal static class OverlayContextActionCatalog
{
    public static IReadOnlyList<OverlayContextActionDescriptor> BuildFixed(OverlayContextEnvelope? context)
    {
        var actions = new List<OverlayContextActionDescriptor>
        {
            Action("paste", "Paste", "paste", false)
        };

        if (context is null || !context.HasPayload) return actions;

        actions.Add(Action("ask-haven", "Ask Haven", "sparkles", true));
        actions.Add(Action("share", "Share", "share", true));

        if (context.HasTextualSelection)
        {
            actions.Add(Action("copy", "Copy text", "copy", true));
            actions.Add(Action("cut", "Cut text", "cut", true));
            actions.Add(Action("explain", "Explain", "info", true));
            actions.Add(Action("summarise", "Summarise", "summary", true));
            actions.Add(Action("rewrite", "Rewrite", "edit", true));
            actions.Add(Action("add-task", "Add as task", "tasks", true));
            actions.Add(Action("add-plan", "Add to Plan", "calendar", true));
            actions.Add(Action("send-study", "Send to Study", "study", true));
            actions.Add(Action("search", "Search", "search", true));
            actions.Add(Action("save-write", "Save to Write", "write", true));
        }

        if (context.HasVisualSelection)
        {
            actions.Add(Action("analyse", "Analyse", "vision", true));
            actions.Add(Action("visual-search", "Search visually", "search", true));
            actions.Add(Action("send-vision", "Send to Vision", "vision", true));
            if (!context.HasMediaSelection)
                actions.Add(Action("ocr-copy", "Extract text", "scan", true));
        }

        if (context.HasInteractiveSelection)
        {
            actions.Add(Action("inspect-control", "Inspect control", "info", true));
            actions.Add(Action("run-automation", "Run automation", "automation", true));
            actions.Add(Action("open-in-app", "Open in app", "apps", true));
        }

        if (context.HasMediaSelection)
        {
            actions.Add(Action("analyse-frame", "Analyse this frame", "vision", true));
            actions.Add(Action("summarise-media", "Summarise visible media", "summary", true));
        }

        return actions
            .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static OverlayContextActionDescriptor Action(string id, string label, string iconKey, bool requiresContext) =>
        new(id, label, iconKey, requiresContext);
}
#endif
