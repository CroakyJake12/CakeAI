using Haven.Desktop.Overlay;
namespace Haven.Desktop.Tests;
public sealed class OverlayCaptureShellTests
{
    [Fact]
    public void Scene_exposes_visual_capture_control()
    {
        using var scene = new OverlayShellHavenScene();
        Assert.Equal("Overlay.Capture", scene.CaptureButton.Name);
        Assert.Equal("AI Select", scene.CaptureButton.Content);
    }
}
