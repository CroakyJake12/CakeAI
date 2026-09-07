using Haven.Application;
using Haven.Desktop.Overlay;
namespace Haven.Desktop.Tests;
public sealed class OverlayForegroundContextCaptureServiceTests
{
    [Fact]
    public void BuildContext_preserves_text_ui_metadata_and_provenance()
    {
        var at = DateTimeOffset.Parse("2026-08-19T20:00:00Z");
        var snapshot = new ComputerSelectionSnapshot("selected words", "notepad", "Notes",
            10, 20, 300, 40, "Document editor", "editor", "ControlType.Edit",
            true, false, at, false);
        var context = OverlayForegroundContextCaptureService.BuildContext(snapshot);
        Assert.NotNull(context);
        Assert.Equal(OverlayContextKind.Mixed, context!.Kind);
        Assert.Equal("selected words", context.SelectedText);
        Assert.Equal("notepad", context.Provenance.SourceApplication);
        Assert.Equal("Notes", context.Provenance.SourceWindow);
        Assert.Equal(at, context.Provenance.CapturedAt);
        Assert.Equal(2, context.SelectedItems.Count);
        Assert.Contains(context.SelectedItems, item => item.Kind == OverlaySelectionKind.Text && item.Text == "selected words");
        Assert.Contains(context.SelectedItems, item => item.Kind == OverlaySelectionKind.UiComponent
            && item.Semantic?.AutomationId == "editor" && item.Bounds?.Width == 300);
    }

    [Fact]
    public void BuildContext_keeps_truncation_and_does_not_invent_text_bounds()
    {
        var snapshot = new ComputerSelectionSnapshot(new string('x', 8192), "writer", "Document",
            5, 6, 100, 30, "Editor", null, "ControlType.Edit", true, null, DateTimeOffset.UtcNow, true);
        var context = OverlayForegroundContextCaptureService.BuildContext(snapshot);
        Assert.NotNull(context);
        Assert.True(context!.WasTruncated);
        var text = Assert.Single(context.SelectedItems, item => item.Kind == OverlaySelectionKind.Text);
        Assert.Null(text.Bounds);
        Assert.Equal(8192, text.Text!.Length);
    }

    [Fact]
    public void BuildContext_bounds_alternate_computer_provider_text_to_eight_kibibytes()
    {
        var snapshot = new ComputerSelectionSnapshot(new string('x', 20_000), "writer", "Document",
            null, null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow, false);

        var context = OverlayForegroundContextCaptureService.BuildContext(snapshot);

        Assert.NotNull(context);
        Assert.True(context!.WasTruncated);
        Assert.Equal(8_192, context.SelectedText!.Length);
        Assert.Equal(8_192, Assert.Single(context.SelectedItems).Text!.Length);
    }

    [Fact]
    public void BuildContext_returns_null_without_usable_windows_context()
    {
        var snapshot = new ComputerSelectionSnapshot(null, null, null, null, null, null, null,
            null, null, null, null, null, DateTimeOffset.UtcNow, false);
        Assert.Null(OverlayForegroundContextCaptureService.BuildContext(snapshot));
    }
}
