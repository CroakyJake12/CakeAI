using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

public sealed class TaskbarShelfLayoutTests
{
    [Theory]
    [InlineData(900d, TaskbarShelfState.Compact)]
    [InlineData(1200d, TaskbarShelfState.Standard)]
    [InlineData(1800d, TaskbarShelfState.Expanded)]
    public void Shelf_state_tracks_available_width(double width, TaskbarShelfState expected)
    {
        Assert.Equal(expected, TaskbarShelfLayout.Resolve(width));
    }

    [Fact]
    public void Taskbar_layout_reuses_real_shell_actions_and_model_picker()
    {
        var scene = new TopRailFinalScene();

        TaskbarShelfLayout.Apply(scene, TaskbarShelfState.Standard);
        scene.SetModelSummary("qwen-local", 75);

        Assert.Equal("Taskbar.Root", scene.Root.Name);
        Assert.Equal("Taskbar.Go", scene.LogoButton.Name);
        Assert.Equal("Go", scene.LogoButton.Content);
        Assert.Equal("Go", scene.LogoButton.Accessibility.AccessibleName);
        Assert.Equal("Taskbar.ActionsHost", scene.ActionsHost.Name);
        Assert.Equal("Taskbar.ModelHost", scene.ModelHost.Name);
        Assert.Contains("qwen-local", scene.ModelButton.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("75%", scene.ModelButton.Content?.ToString(), StringComparison.Ordinal);
    }
}
