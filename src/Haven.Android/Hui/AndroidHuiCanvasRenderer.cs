using AndroidCanvas = global::Android.Graphics.Canvas;
using AndroidColor = global::Android.Graphics.Color;
using AndroidPaint = global::Android.Graphics.Paint;
using AndroidPaintFlags = global::Android.Graphics.PaintFlags;
using AndroidRectF = global::Android.Graphics.RectF;
using Haven.UI;

namespace Haven.Android.Hui;

/// <summary>Resolves a HUI design-token brush to an Android ARGB color.</summary>
public interface IAndroidHuiTokenColorResolver
{
    bool TryResolveArgb(string token, out uint argb);
}

/// <summary>Outcome of executing the bounded HUI primitive plan against an Android canvas.</summary>
public sealed record AndroidHuiCanvasRenderReport(
    AndroidHuiRenderPlan Plan,
    int ExecutedOperations,
    IReadOnlyList<string> RuntimeSkips)
{
    public bool CompletedSupportedOperations => Plan.CanExecute && RuntimeSkips.Count == 0;
}

/// <summary>
/// Executes the primitive subset produced by <see cref="AndroidHuiCommandAdapter"/> on an Android canvas.
/// It intentionally does not emulate unsupported HUI commands or claim device/display behavior.
/// </summary>
public sealed class AndroidHuiCanvasRenderer
{
    private readonly AndroidHuiCommandAdapter _adapter;

    public AndroidHuiCanvasRenderer(AndroidHuiCommandAdapter? adapter = null)
    {
        _adapter = adapter ?? new AndroidHuiCommandAdapter();
    }

    public AndroidHuiCanvasRenderReport Render(
        AndroidCanvas canvas,
        IReadOnlyList<HavenDrawCommand> commands,
        HavenRenderSurfaceMetrics metrics,
        IAndroidHuiTokenColorResolver tokenColorResolver)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(tokenColorResolver);

        var plan = _adapter.Adapt(commands, metrics);
        if (!plan.CanExecute)
            return new AndroidHuiCanvasRenderReport(plan, 0, []);

        var runtimeSkips = new List<string>();
        var executedOperations = 0;
        var rootSaveCount = canvas.Save();

        try
        {
            var renderScale = (float)metrics.RenderScale;
            canvas.Scale(renderScale, renderScale);

            using var paint = new AndroidPaint(AndroidPaintFlags.AntiAlias);
            foreach (var operation in plan.Operations)
            {
                if (Execute(canvas, paint, operation, tokenColorResolver, runtimeSkips))
                    executedOperations++;
            }
        }
        finally
        {
            canvas.RestoreToCount(rootSaveCount);
        }

        return new AndroidHuiCanvasRenderReport(plan, executedOperations, runtimeSkips.ToArray());
    }

    private static bool Execute(
        AndroidCanvas canvas,
        AndroidPaint paint,
        AndroidHuiOperation operation,
        IAndroidHuiTokenColorResolver tokenColorResolver,
        ICollection<string> runtimeSkips)
    {
        switch (operation)
        {
            case AndroidHuiPushTransformOperation pushTransform:
                canvas.Save();
                ApplyTransform(canvas, pushTransform.Transform, pushTransform.Origin);
                return true;

            case AndroidHuiPopTransformOperation:
                canvas.Restore();
                return true;

            case AndroidHuiPushClipOperation pushClip:
                canvas.Save();
                canvas.ClipRect(ToRectF(pushClip.Bounds));
                return true;

            case AndroidHuiPopClipOperation:
                canvas.Restore();
                return true;

            case AndroidHuiFillRoundedRectOperation fill:
                if (!TryConfigurePaint(paint, fill.Brush, fill.Opacity, AndroidPaint.Style.Fill, 0, tokenColorResolver))
                    return Skip(runtimeSkips, operation, fill.Brush);
                canvas.DrawRoundRect(ToRectF(fill.Bounds), (float)Math.Max(0, fill.Radius), (float)Math.Max(0, fill.Radius), paint);
                return true;

            case AndroidHuiStrokeRoundedRectOperation stroke:
                if (!TryConfigurePaint(paint, stroke.Pen.Brush, stroke.Opacity, AndroidPaint.Style.Stroke, stroke.Pen.Thickness, tokenColorResolver))
                    return Skip(runtimeSkips, operation, stroke.Pen.Brush);
                canvas.DrawRoundRect(ToRectF(stroke.Bounds), (float)Math.Max(0, stroke.Radius), (float)Math.Max(0, stroke.Radius), paint);
                return true;

            case AndroidHuiLineOperation line:
                if (!TryConfigurePaint(paint, line.Pen.Brush, line.Opacity, AndroidPaint.Style.Stroke, line.Pen.Thickness, tokenColorResolver))
                    return Skip(runtimeSkips, operation, line.Pen.Brush);
                canvas.DrawLine((float)line.Start.X, (float)line.Start.Y, (float)line.End.X, (float)line.End.Y, paint);
                return true;

            case AndroidHuiEllipseOperation ellipse:
                if (!TryResolveColor(ellipse.Brush, ellipse.Opacity, tokenColorResolver, out var fillColor))
                    return Skip(runtimeSkips, operation, ellipse.Brush);

                AndroidColor? strokeColor = null;
                if (ellipse.Pen is not null)
                {
                    if (!TryResolveColor(ellipse.Pen.Brush, ellipse.Opacity, tokenColorResolver, out var resolvedStrokeColor))
                        return Skip(runtimeSkips, operation, ellipse.Pen.Brush);
                    strokeColor = resolvedStrokeColor;
                }

                ConfigurePaint(paint, fillColor, AndroidPaint.Style.Fill, 0);
                canvas.DrawOval(ToRectF(ellipse.Bounds), paint);
                if (ellipse.Pen is not null && strokeColor is not null)
                {
                    ConfigurePaint(paint, strokeColor.Value, AndroidPaint.Style.Stroke, ellipse.Pen.Thickness);
                    canvas.DrawOval(ToRectF(ellipse.Bounds), paint);
                }
                return true;

            default:
                runtimeSkips.Add($"{operation.GetType().Name} is not executable by the Android canvas renderer.");
                return false;
        }
    }

    private static void ApplyTransform(AndroidCanvas canvas, HavenTransform transform, HavenPoint origin)
    {
        canvas.Translate((float)(origin.X + transform.TranslateX), (float)(origin.Y + transform.TranslateY));
        if (Math.Abs(transform.RotationDegrees) > 0.0001)
            canvas.Rotate((float)transform.RotationDegrees);
        if (Math.Abs(transform.ScaleX - 1) > 0.0001 || Math.Abs(transform.ScaleY - 1) > 0.0001)
            canvas.Scale((float)transform.ScaleX, (float)transform.ScaleY);
        canvas.Translate((float)-origin.X, (float)-origin.Y);
    }

    private static bool TryConfigurePaint(
        AndroidPaint paint,
        HavenBrush brush,
        double opacity,
        AndroidPaint.Style style,
        double strokeWidth,
        IAndroidHuiTokenColorResolver tokenColorResolver)
    {
        if (!TryResolveColor(brush, opacity, tokenColorResolver, out var color))
            return false;

        ConfigurePaint(paint, color, style, strokeWidth);
        return true;
    }

    private static void ConfigurePaint(
        AndroidPaint paint,
        AndroidColor color,
        AndroidPaint.Style style,
        double strokeWidth)
    {
        paint.Color = color;
        paint.SetStyle(style);
        paint.StrokeWidth = (float)Math.Max(0, strokeWidth);
    }

    private static bool TryResolveColor(
        HavenBrush brush,
        double opacity,
        IAndroidHuiTokenColorResolver tokenColorResolver,
        out AndroidColor color)
    {
        uint argb;
        switch (brush)
        {
            case HavenSolidBrush solid:
                argb = ((uint)solid.A << 24) | ((uint)solid.R << 16) | ((uint)solid.G << 8) | solid.B;
                break;

            case HavenTokenBrush token when tokenColorResolver.TryResolveArgb(token.Token, out var resolvedArgb):
                argb = resolvedArgb;
                break;

            default:
                color = default;
                return false;
        }

        var clampedOpacity = Math.Clamp(opacity, 0, 1);
        var sourceAlpha = (int)((argb >> 24) & 0xff);
        var alpha = (int)Math.Round(sourceAlpha * clampedOpacity);
        color = AndroidColor.Argb(
            alpha,
            (int)((argb >> 16) & 0xff),
            (int)((argb >> 8) & 0xff),
            (int)(argb & 0xff));
        return true;
    }

    private static bool Skip(
        ICollection<string> runtimeSkips,
        AndroidHuiOperation operation,
        HavenBrush brush)
    {
        var brushName = brush is HavenTokenBrush token ? $"token '{token.Token}'" : brush.GetType().Name;
        runtimeSkips.Add($"{operation.GetType().Name} was skipped because {brushName} could not be resolved to an Android color.");
        return false;
    }

    private static AndroidRectF ToRectF(HavenRect rect) => new(
        (float)rect.X,
        (float)rect.Y,
        (float)rect.Right,
        (float)rect.Bottom);
}
