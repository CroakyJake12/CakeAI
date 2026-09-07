#if !ANDROID
using System.Text.Json;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Crops a caller-owned, already captured Overlay image. It does not invoke the
/// picker or retain mutable session state; callers own the source draft and the
/// resulting context lifecycle.
/// </summary>
internal sealed class OverlayRegionCaptureService
{
    private static readonly string OwnedCropDirectory = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "Haven", "Vision", "regions"));

    /// <summary>
    /// Crops the exact persisted image represented by <paramref name="capturedContext"/>
    /// and creates a Region envelope. No registry or picker is touched here.
    /// </summary>
    public async Task<OverlayContextEnvelope> CreateRegionAsync(
        OverlayContextEnvelope capturedContext,
        HavenRect normalizedSelection,
        double previewViewportWidth,
        double previewViewportHeight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capturedContext);
        var selection = ValidateSelection(normalizedSelection);
        ValidateViewport(previewViewportWidth, previewViewportHeight);
        var sourceAttachment = GetSourceAttachment(capturedContext);
        var sourceDimensions = GetSourceDimensions(sourceAttachment);
        string? candidatePath = null;
        try
        {
            candidatePath = await VisionRegionCropper.CreateCropAsync(
                sourceAttachment.Id,
                new HavenRect(selection.X, selection.Y, selection.Width, selection.Height),
                previewViewportWidth,
                previewViewportHeight,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var region = BuildRegionContext(capturedContext, sourceAttachment, selection, candidatePath, sourceDimensions);
            candidatePath = null;
            return region;
        }
        catch
        {
            // A candidate that did not reach the return point is never exposed.
            DeleteOwnedCrop(candidatePath);
            throw;
        }
    }

    /// <summary>
    /// Removes only crop files created by this service. Arbitrary user attachments
    /// and source captures are intentionally left untouched.
    /// </summary>
    public void CleanupRegion(OverlayContextEnvelope? context)
    {
        if (context is null) return;
        foreach (var path in context.Attachments.Select(item => item.Id)
                     .Append(context.MediaReference)
                     .Concat(context.SelectedItems.SelectMany(item => new[] { item.MediaReference, item.Attachment?.Id })))
            DeleteOwnedCrop(path);
    }

    internal static OverlaySelectionBounds ValidateSelection(HavenRect selection)
    {
        if (!double.IsFinite(selection.X) || !double.IsFinite(selection.Y)
            || !double.IsFinite(selection.Width) || !double.IsFinite(selection.Height)
            || selection.X < 0 || selection.Y < 0
            || selection.Width <= 0 || selection.Height <= 0
            || selection.X + selection.Width > 1
            || selection.Y + selection.Height > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(selection),
                "The selected region must have finite, positive bounds wholly within normalized [0, 1] coordinates.");
        }

        return new OverlaySelectionBounds(selection.X, selection.Y, selection.Width, selection.Height);
    }

    internal static void ValidateViewport(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The visible preview viewport must have finite positive dimensions.");
    }

    internal static bool IsOwnedCropPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { return false; }
        return fullPath.StartsWith(OwnedCropDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && Path.GetFileName(fullPath).StartsWith("region-", StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteOwnedCrop(string? path)
    {
        if (!IsOwnedCropPath(path)) return;
        VisionRegionCropper.DeleteTemporary(path);
    }

    private static OverlayContextEnvelope BuildRegionContext(
        OverlayContextEnvelope sourceContext,
        OverlayContextAttachmentReference sourceAttachment,
        OverlaySelectionBounds selection,
        string cropPath,
        (int Width, int Height) sourceDimensions)
    {
        var sourceName = sourceAttachment.DisplayName ?? "screen capture";
        var metadata = JsonSerializer.Serialize(new
        {
            SourceCapture = sourceAttachment.Id,
            SourceWidth = sourceDimensions.Width,
            SourceHeight = sourceDimensions.Height,
            NormalizedBounds = selection
        });
        var attachment = new OverlayContextAttachmentReference(
            cropPath,
            "image",
            "image/png",
            $"Selected region from {sourceName}",
            metadata).Bound();
        var capturedAt = sourceContext.Provenance.CapturedAt == default
            ? DateTimeOffset.UtcNow
            : sourceContext.Provenance.CapturedAt;
        var expiresAt = sourceContext.Provenance.ExpiresAt > capturedAt
            ? sourceContext.Provenance.ExpiresAt
            : capturedAt.AddMinutes(2);
        var provenance = new OverlayContextProvenance(
            sourceContext.Provenance.SourceApplication,
            sourceContext.Provenance.SourceWindow,
            selection,
            capturedAt,
            expiresAt,
            sourceContext.Provenance.PermissionState,
            "Selected from a real Haven screen/window capture.");
        var item = new OverlaySelectionItem(
            Guid.NewGuid().ToString("N"),
            OverlaySelectionKind.Region,
            selection,
            null,
            cropPath,
            attachment,
            new OverlaySelectionSemanticMetadata(null, sourceName, null, null, true, true, "image", null),
            "Screen region").Bound();
        return new OverlayContextEnvelope(
            OverlayContextKind.Region,
            null,
            [attachment],
            cropPath,
            provenance,
            false,
            [item]).Bound();
    }

    private static OverlayContextAttachmentReference GetSourceAttachment(OverlayContextEnvelope context)
    {
        if (context.Kind is not (OverlayContextKind.Image or OverlayContextKind.Screen or OverlayContextKind.Window))
            throw new InvalidDataException("A real image, screen, or window capture is required before selecting a region.");
        var attachment = context.Attachments.FirstOrDefault(item =>
            string.Equals(item.Kind, "image", StringComparison.OrdinalIgnoreCase));
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.Id))
            throw new InvalidDataException("The real visual capture did not return a persisted image attachment.");
        if (!File.Exists(attachment.Id))
            throw new FileNotFoundException("The persisted visual capture is no longer available.", attachment.Id);
        return attachment;
    }

    private static (int Width, int Height) GetSourceDimensions(OverlayContextAttachmentReference attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.MetadataJson))
            throw new InvalidDataException("The visual capture did not provide source image dimensions.");
        try
        {
            using var document = JsonDocument.Parse(attachment.MetadataJson);
            var root = document.RootElement;
            var width = root.GetProperty("Width").GetInt32();
            var height = root.GetProperty("Height").GetInt32();
            if (width <= 0 || height <= 0)
                throw new InvalidDataException("The visual capture returned invalid source image dimensions.");
            return (width, height);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException("The visual capture did not provide source image dimensions.", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The visual capture returned invalid source image dimensions.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The visual capture returned invalid source image metadata.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The visual capture returned invalid source image dimensions.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The visual capture returned oversized source image dimensions.", exception);
        }
    }
}
#endif
