using Haven.Android.Hui;
using Haven.UI;

namespace Haven.Android.Hui.Tests;

public sealed class AndroidHuiCommandAdapterTests
{
    private readonly AndroidHuiCommandAdapter _adapter = new();

    [Fact]
    public void Adapt_maps_supported_primitives_in_order()
    {
        var metrics = CreateMetrics();
        var rect = new HavenRect(4, 8, 120, 48);
        var pen = new HavenPen(new HavenTokenBrush("Border"), 2);
        HavenDrawCommand[] commands =
        [
            new HavenPushClipCommand(rect),
            new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("Surface"), 12, 0.9),
            new HavenStrokeRoundedRectCommand(rect, pen, 12, 0.8),
            new HavenLineCommand(new HavenPoint(4, 12), new HavenPoint(124, 12), pen, 0.7),
            new HavenEllipseCommand(new HavenRect(10, 16, 20, 20), new HavenSolidBrush(255, 20, 40, 60), pen, 0.6),
            new HavenPopClipCommand(rect)
        ];

        var plan = _adapter.Adapt(commands, metrics);

        Assert.True(plan.CanExecute);
        Assert.Same(metrics, plan.Metrics);
        Assert.Empty(plan.UnsupportedCommands);
        Assert.Empty(plan.StructuralErrors);
        Assert.Collection(
            plan.Operations,
            operation => Assert.IsType<AndroidHuiPushClipOperation>(operation),
            operation => Assert.IsType<AndroidHuiFillRoundedRectOperation>(operation),
            operation => Assert.IsType<AndroidHuiStrokeRoundedRectOperation>(operation),
            operation => Assert.IsType<AndroidHuiLineOperation>(operation),
            operation => Assert.IsType<AndroidHuiEllipseOperation>(operation),
            operation => Assert.IsType<AndroidHuiPopClipOperation>(operation));
    }

    [Fact]
    public void Adapt_reports_unsupported_commands_without_fallback_operations()
    {
        var rect = new HavenRect(0, 0, 100, 24);
        HavenDrawCommand[] commands =
        [
            new HavenTextCommand(
                rect,
                new HavenTextLayout("Haven", "Montserrat", 16, 600, rect.Width),
                new HavenTokenBrush("TextPrimary")),
            new HavenImageCommand(rect, new HavenImage("asset://hero"))
        ];

        var plan = _adapter.Adapt(commands, CreateMetrics());

        Assert.True(plan.CanExecute);
        Assert.Empty(plan.Operations);
        Assert.Collection(
            plan.UnsupportedCommands,
            unsupported => Assert.Equal(nameof(HavenTextCommand), unsupported.CommandType),
            unsupported => Assert.Equal(nameof(HavenImageCommand), unsupported.CommandType));
    }

    [Fact]
    public void Adapt_rejects_mismatched_canvas_state()
    {
        var rect = new HavenRect(0, 0, 10, 10);
        HavenDrawCommand[] commands =
        [
            new HavenPushClipCommand(rect),
            new HavenPopTransformCommand(rect)
        ];

        var plan = _adapter.Adapt(commands, CreateMetrics());

        Assert.False(plan.CanExecute);
        Assert.Single(plan.Operations);
        Assert.IsType<AndroidHuiPushClipOperation>(plan.Operations[0]);
        Assert.NotEmpty(plan.StructuralErrors);
    }

    [Fact]
    public void Adapt_rejects_unclosed_canvas_state()
    {
        var rect = new HavenRect(0, 0, 10, 10);
        HavenDrawCommand[] commands =
        [
            new HavenPushTransformCommand(rect, new HavenTransform(1.1, 1.1, 2, 3, 4), new HavenPoint(5, 5))
        ];

        var plan = _adapter.Adapt(commands, CreateMetrics());

        Assert.False(plan.CanExecute);
        Assert.Single(plan.Operations);
        Assert.Contains(plan.StructuralErrors, error => error.Contains("unclosed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Adapt_rejects_invalid_surface_metrics()
    {
        var invalidMetrics = new HavenRenderSurfaceMetrics(new HavenSize(100, 100), 0, default);

        Assert.Throws<ArgumentOutOfRangeException>(() => _adapter.Adapt([], invalidMetrics));
    }

    private static HavenRenderSurfaceMetrics CreateMetrics() =>
        new(new HavenSize(320, 640), 2, default);
}
