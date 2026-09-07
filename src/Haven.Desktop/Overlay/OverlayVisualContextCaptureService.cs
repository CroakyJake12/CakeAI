#if !ANDROID
using System.Text.Json;
using Haven.Application;
namespace Haven.Desktop.Overlay;
internal sealed class OverlayVisualContextCaptureService(IScreenShareService screenShare, IAppPaths paths)
{
    private const int MaxSnapshotDimension = 16_384;
    private const int MaxSnapshotBytes = 8 * 1024 * 1024;

    public async Task<OverlayContextEnvelope> CaptureAsync(CancellationToken cancellationToken)
    {
        if (!screenShare.IsSupported) throw new PlatformNotSupportedException(screenShare.UnavailableReason ?? "Visual capture is unavailable.");
        if (screenShare.IsSharing) throw new InvalidOperationException("Visual capture is unavailable while another Haven screen share is active. Stop sharing first.");
        var firstFrame = new TaskCompletionSource<ScreenShareSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSnapshot(object? sender, ScreenShareSnapshotEventArgs e) => firstFrame.TrySetResult(e.Snapshot);
        screenShare.SnapshotAvailable += OnSnapshot;
        try
        {
            var source = await screenShare.StartWithSystemPickerAsync(cancellationToken);
            var snapshot = await screenShare.GetLatestSnapshotAsync(cancellationToken)
                ?? await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            var path = await PersistAsync(snapshot, cancellationToken);
            return BuildContext(source, snapshot, path);
        }
        finally
        {
            screenShare.SnapshotAvailable -= OnSnapshot;
            try { await screenShare.StopAsync(CancellationToken.None); } catch { }
        }
    }

    /// <summary>
    /// Captures through the real picker while preserving a truthful, payload-free
    /// context for capability/permission failures. Cancellation is intentionally not
    /// converted into a context: a cancelled picker is not a completed selection.
    /// </summary>
    internal async Task<OverlayContextEnvelope> CaptureWithStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await CaptureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            return BuildStatusContext(OverlayContextPermissionState.Denied, exception.Message);
        }
        catch (PlatformNotSupportedException exception)
        {
            return BuildStatusContext(OverlayContextPermissionState.Unavailable, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return BuildStatusContext(OverlayContextPermissionState.Unavailable, exception.Message);
        }
        catch (TimeoutException exception)
        {
            return BuildStatusContext(OverlayContextPermissionState.Unavailable, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return BuildStatusContext(OverlayContextPermissionState.Unavailable, exception.Message);
        }
    }

    /// <summary>
    /// Removes only source frames persisted by this capture service. Draft cleanup
    /// is explicit so a session can release a replaced or abandoned picker frame
    /// without touching user-provided attachments.
    /// </summary>
    internal void CleanupCapture(OverlayContextEnvelope? context)
    {
        if (context is null) return;
        foreach (var attachment in context.Attachments)
        {
            if (!string.Equals(attachment.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsOwnedCapturePath(attachment.Id)) continue;
            try { File.Delete(attachment.Id); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private bool IsOwnedCapturePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string fullPath;
        string folder;
        try
        {
            fullPath = Path.GetFullPath(path);
            folder = Path.GetFullPath(Path.Combine(paths.AttachmentsDirectory, "Overlay"));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { return false; }
        return fullPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && Path.GetFileName(fullPath).StartsWith("overlay-", StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetExtension(fullPath), ".jpg", StringComparison.OrdinalIgnoreCase);
    }

    internal static OverlayContextEnvelope BuildStatusContext(
        OverlayContextPermissionState permissionState,
        string reason,
        DateTimeOffset? capturedAt = null)
    {
        if (permissionState is not (OverlayContextPermissionState.Denied or OverlayContextPermissionState.Unavailable))
            throw new ArgumentOutOfRangeException(nameof(permissionState), "A status context must represent a denied or unavailable capture.");

        var at = capturedAt ?? DateTimeOffset.UtcNow;
        return new OverlayContextEnvelope(
            OverlayContextKind.None,
            null,
            [],
            null,
            new OverlayContextProvenance(null, null, null, at, at.AddMinutes(2), permissionState,
                string.IsNullOrWhiteSpace(reason) ? "Visual capture did not produce a selection." : reason),
            false,
            []).Bound();
    }
    private async Task<string> PersistAsync(ScreenShareSnapshot snapshot, CancellationToken cancellationToken)
    {
        var bytes = DecodeSnapshot(snapshot);
        var folder = Path.Combine(paths.AttachmentsDirectory, "Overlay");
        Directory.CreateDirectory(folder);
        Cleanup(folder);
        var path = Path.Combine(folder, $"overlay-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }

    private static byte[] DecodeSnapshot(ScreenShareSnapshot snapshot)
    {
        if (snapshot.Width <= 0 || snapshot.Height <= 0
            || snapshot.Width > MaxSnapshotDimension || snapshot.Height > MaxSnapshotDimension)
            throw new InvalidDataException("The visual capture returned invalid or oversized dimensions.");
        if (string.IsNullOrWhiteSpace(snapshot.Base64Jpeg))
            throw new InvalidDataException("The visual capture returned an empty frame.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(snapshot.Base64Jpeg);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The visual capture returned an invalid JPEG payload.", exception);
        }

        if (bytes.Length is 0 or > MaxSnapshotBytes)
            throw new InvalidDataException("The visual capture returned an empty or oversized frame.");
        return bytes;
    }
    private static void Cleanup(string folder)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        try { foreach (var file in Directory.EnumerateFiles(folder, "overlay-*.jpg")) if (File.GetLastWriteTimeUtc(file) < cutoff) try { File.Delete(file); } catch { } } catch { }
    }
    internal static OverlayContextEnvelope BuildContext(
        ScreenShareSource source,
        ScreenShareSnapshot snapshot,
        string path,
        OverlayContextKind? contextKindOverride = null,
        OverlaySelectionBounds? bounds = null,
        string? mediaKind = null,
        double? mediaPositionSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A persisted capture path is required.", nameof(path));
        if (contextKindOverride == OverlayContextKind.None)
            throw new ArgumentOutOfRangeException(nameof(contextKindOverride));
        if (mediaPositionSeconds is double position && (!double.IsFinite(position) || position < 0))
            throw new ArgumentOutOfRangeException(nameof(mediaPositionSeconds));

        var inferred = source.Kind switch
        {
            ScreenShareSourceKind.Window => (OverlayContextKind.Window, OverlaySelectionKind.Window),
            ScreenShareSourceKind.Screen => (OverlayContextKind.Screen, OverlaySelectionKind.Screen),
            _ => (OverlayContextKind.Image, OverlaySelectionKind.Image)
        };
        var kind = contextKindOverride ?? inferred.Item1;
        var selectionKind = kind switch
        {
            OverlayContextKind.Region => OverlaySelectionKind.Region,
            OverlayContextKind.Video => OverlaySelectionKind.Video,
            OverlayContextKind.Window => OverlaySelectionKind.Window,
            OverlayContextKind.Screen => OverlaySelectionKind.Screen,
            _ => inferred.Item2
        };
        var effectiveMediaKind = mediaKind ?? (kind == OverlayContextKind.Video ? "video" : "image");
        var metadata = JsonSerializer.Serialize(new
        {
            snapshot.Width,
            snapshot.Height,
            SourceKind = source.Kind.ToString(),
            ContextKind = kind.ToString(),
            MediaPositionSeconds = mediaPositionSeconds
        });
        var attachment = new OverlayContextAttachmentReference(path, "image", "image/jpeg", source.Name, metadata);
        var normalizedBounds = bounds?.Normalize();
        var item = new OverlaySelectionItem(Guid.NewGuid().ToString("N"), selectionKind, normalizedBounds, null, null, attachment,
            new OverlaySelectionSemanticMetadata(null, source.Name, null, null, true, null, effectiveMediaKind, mediaPositionSeconds), source.Name).Bound();
        var capturedAt = snapshot.CapturedAt == default ? DateTimeOffset.UtcNow : snapshot.CapturedAt;
        return new OverlayContextEnvelope(kind, null, [attachment], null, new OverlayContextProvenance(
            null, source.Kind == ScreenShareSourceKind.Window ? source.Name : null, normalizedBounds, capturedAt, capturedAt.AddMinutes(2),
            OverlayContextPermissionState.Granted, "Chosen with the Windows screen-share picker."), false, [item]).Bound();
    }
}
#endif
