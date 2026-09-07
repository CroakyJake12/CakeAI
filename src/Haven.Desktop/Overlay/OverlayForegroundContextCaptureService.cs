#if !ANDROID
using Haven.Application;
namespace Haven.Desktop.Overlay;
internal sealed class OverlayForegroundContextCaptureService(IComputerToolService computer)
{
    public async Task<OverlayContextEnvelope?> CaptureAsync(CancellationToken cancellationToken)
        => BuildContext(await computer.GetSelectionSnapshotAsync(cancellationToken).ConfigureAwait(false));

    internal static OverlayContextEnvelope? BuildContext(ComputerSelectionSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        var bounds = ValidBounds(snapshot);
        var text = string.IsNullOrWhiteSpace(snapshot.SelectedText) ? null : snapshot.SelectedText;
        var hasUi = bounds is not null || !string.IsNullOrWhiteSpace(snapshot.AccessibleName)
            || !string.IsNullOrWhiteSpace(snapshot.AutomationId) || !string.IsNullOrWhiteSpace(snapshot.ControlType);
        if (text is null && !hasUi) return null;

        var selections = new List<OverlaySelectionItem>();
        if (text is not null)
            selections.Add(new OverlaySelectionItem(Guid.NewGuid().ToString("N"), OverlaySelectionKind.Text,
                null, text, null, null, null, "Selected text").Bound());
        if (hasUi)
            selections.Add(new OverlaySelectionItem(Guid.NewGuid().ToString("N"), OverlaySelectionKind.UiComponent,
                bounds, null, null, null, new OverlaySelectionSemanticMetadata(
                    snapshot.ControlType, snapshot.AccessibleName, snapshot.AutomationId, snapshot.ControlType,
                    snapshot.IsEnabled, snapshot.IsSelected, null, null),
                snapshot.AccessibleName ?? snapshot.ControlType ?? "Focused UI component").Bound());

        var kind = text is not null && hasUi ? OverlayContextKind.Mixed
            : text is not null ? OverlayContextKind.Text : OverlayContextKind.UiComponent;
        var capturedAt = snapshot.CapturedAt == default ? DateTimeOffset.UtcNow : snapshot.CapturedAt;
        // WindowsComputerToolService already bounds the text to 8 KiB. Reapply the
        // same boundary here so alternate IComputerToolService implementations cannot
        // smuggle a larger prompt through the Overlay handoff.
        return new OverlayContextEnvelope(kind, text, [], null, new OverlayContextProvenance(
            snapshot.SourceApplication, snapshot.SourceWindow, bounds, capturedAt, capturedAt.AddMinutes(2),
            OverlayContextPermissionState.NotRequired,
            "Captured from the foreground Windows accessibility context."),
            snapshot.WasTruncated, selections).Bound(maxTextLength: 8_192);
    }

    private static OverlaySelectionBounds? ValidBounds(ComputerSelectionSnapshot snapshot)
    {
        if (snapshot.X is not double x || snapshot.Y is not double y || snapshot.Width is not double width
            || snapshot.Height is not double height || !double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return null;
        return new OverlaySelectionBounds(x, y, width, height);
    }
}
#endif
