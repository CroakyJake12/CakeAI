using Haven.Application;
using Haven.Desktop.Overlay;
namespace Haven.Desktop.Tests;
public sealed class OverlayVisualContextCaptureServiceTests
{
    [Fact]
    public void Explicit_region_override_preserves_real_capture_provenance_and_bounds()
    {
        var at = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        var source = new ScreenShareSource("capture", "Selected display", ScreenShareSourceKind.Screen);
        var snapshot = new ScreenShareSnapshot("AA==", 1920, 1080, at);
        var context = OverlayVisualContextCaptureService.BuildContext(
            source,
            snapshot,
            @"C:\tmp\capture.jpg",
            OverlayContextKind.Region,
            new OverlaySelectionBounds(10, 20, 500, 300));

        Assert.Equal(OverlayContextKind.Region, context.Kind);
        var item = Assert.Single(context.SelectedItems);
        Assert.Equal(OverlaySelectionKind.Region, item.Kind);
        Assert.Equal(500, item.Bounds?.Width);
        Assert.Equal(500, context.Provenance.Bounds?.Width);
        Assert.True(context.HasVisualSelection);
        Assert.Equal(OverlayContextPermissionState.Granted, context.Provenance.PermissionState);
    }

    [Fact]
    public void Explicit_video_override_keeps_media_position_provenance()
    {
        var source = new ScreenShareSource("capture", "Lesson video", ScreenShareSourceKind.Unknown);
        var snapshot = new ScreenShareSnapshot("AA==", 640, 360, DateTimeOffset.UtcNow);
        var context = OverlayVisualContextCaptureService.BuildContext(
            source,
            snapshot,
            @"C:\tmp\frame.jpg",
            OverlayContextKind.Video,
            new OverlaySelectionBounds(0, 0, 640, 360),
            "video",
            42.5);

        var item = Assert.Single(context.SelectedItems);
        Assert.Equal(OverlaySelectionKind.Video, item.Kind);
        Assert.Equal("video", item.Semantic?.MediaKind);
        Assert.Equal(42.5, item.Semantic?.MediaPositionSeconds);
        Assert.Equal(640, context.Provenance.Bounds?.Width);
        Assert.True(context.HasMediaSelection);
    }

    [Fact]
    public void Status_context_is_payload_free_and_truthful()
    {
        var at = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        var context = OverlayVisualContextCaptureService.BuildStatusContext(
            OverlayContextPermissionState.Denied,
            "The user denied screen capture.",
            at);

        Assert.Equal(OverlayContextKind.None, context.Kind);
        Assert.False(context.HasPayload);
        Assert.Equal(OverlayContextPermissionState.Denied, context.Provenance.PermissionState);
        Assert.Equal("The user denied screen capture.", context.Provenance.PermissionDescription);
    }

    [Fact]
    public void Invalid_status_state_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayVisualContextCaptureService.BuildStatusContext(OverlayContextPermissionState.Granted, "no"));
    }

    [Fact]
    public void Unknown_picker_source_remains_image_context()
    {
        var at = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        var source = new ScreenShareSource("capture", "Selected display", ScreenShareSourceKind.Unknown);
        var snapshot = new ScreenShareSnapshot("AA==", 1920, 1080, at);
        var context = OverlayVisualContextCaptureService.BuildContext(source, snapshot, @"C:\tmp\capture.jpg");
        Assert.Equal(OverlayContextKind.Image, context.Kind);
        Assert.Equal(OverlayContextPermissionState.Granted, context.Provenance.PermissionState);
        Assert.Null(context.Provenance.SourceWindow);
        Assert.Equal(at.AddMinutes(2), context.Provenance.ExpiresAt);
        var item = Assert.Single(context.SelectedItems);
        Assert.Equal(OverlaySelectionKind.Image, item.Kind);
        Assert.Equal("image/jpeg", item.Attachment?.MimeType);
        Assert.Contains("Unknown", item.Attachment?.MetadataJson);
    }
    [Fact]
    public void Known_window_source_maps_to_window_context()
    {
        var source = new ScreenShareSource("window", "Calculator", ScreenShareSourceKind.Window);
        var snapshot = new ScreenShareSnapshot("AA==", 800, 600, DateTimeOffset.UtcNow);
        var context = OverlayVisualContextCaptureService.BuildContext(source, snapshot, @"C:\tmp\window.jpg");
        Assert.Equal(OverlayContextKind.Window, context.Kind);
        Assert.Equal("Calculator", context.Provenance.SourceWindow);
        Assert.Equal(OverlaySelectionKind.Window, Assert.Single(context.SelectedItems).Kind);
    }
}
