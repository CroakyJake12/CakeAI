using Haven.UI;

namespace Haven.Android.Hui;

/// <summary>The Android canvas state frame represented by a HUI push/pop command.</summary>
public enum AndroidHuiStateKind
{
    Transform,
    Clip
}

/// <summary>A bounded Android rendering operation adapted from a shared HUI draw command.</summary>
public abstract record AndroidHuiOperation(HavenRect Bounds);

public sealed record AndroidHuiPushTransformOperation(
    HavenRect Bounds,
    HavenTransform Transform,
    HavenPoint Origin) : AndroidHuiOperation(Bounds);

public sealed record AndroidHuiPopTransformOperation(HavenRect Bounds) : AndroidHuiOperation(Bounds);
public sealed record AndroidHuiPushClipOperation(HavenRect Bounds) : AndroidHuiOperation(Bounds);
public sealed record AndroidHuiPopClipOperation(HavenRect Bounds) : AndroidHuiOperation(Bounds);

public sealed record AndroidHuiFillRoundedRectOperation(
    HavenRect Bounds,
    HavenBrush Brush,
    double Radius,
    double Opacity) : AndroidHuiOperation(Bounds);

public sealed record AndroidHuiStrokeRoundedRectOperation(
    HavenRect Bounds,
    HavenPen Pen,
    double Radius,
    double Opacity) : AndroidHuiOperation(Bounds);

public sealed record AndroidHuiLineOperation(
    HavenRect Bounds,
    HavenPoint Start,
    HavenPoint End,
    HavenPen Pen,
    double Opacity) : AndroidHuiOperation(Bounds);

public sealed record AndroidHuiEllipseOperation(
    HavenRect Bounds,
    HavenBrush Brush,
    HavenPen? Pen,
    double Opacity) : AndroidHuiOperation(Bounds);

/// <summary>A shared HUI command intentionally not rendered by the bounded Android primitive path.</summary>
public sealed record AndroidHuiUnsupportedCommand(
    string CommandType,
    HavenRect Bounds,
    string Reason);

/// <summary>The deterministic Android render plan produced from the shared HUI command stream.</summary>
public sealed record AndroidHuiRenderPlan(
    HavenRenderSurfaceMetrics Metrics,
    IReadOnlyList<AndroidHuiOperation> Operations,
    IReadOnlyList<AndroidHuiUnsupportedCommand> UnsupportedCommands,
    IReadOnlyList<string> StructuralErrors)
{
    public bool CanExecute => StructuralErrors.Count == 0;
}

/// <summary>
/// Adapts the backend-neutral HUI drawing contract into a deliberately small Android canvas plan.
/// Unsupported commands are reported rather than approximated, and malformed canvas state is rejected.
/// </summary>
public sealed class AndroidHuiCommandAdapter
{
    private const string UnsupportedReason = "No Android canvas primitive is registered for this HUI command in the bounded adapter.";

    public AndroidHuiRenderPlan Adapt(
        IReadOnlyList<HavenDrawCommand> commands,
        HavenRenderSurfaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(metrics);
        ValidateMetrics(metrics);

        var operations = new List<AndroidHuiOperation>(commands.Count);
        var unsupported = new List<AndroidHuiUnsupportedCommand>();
        var structuralErrors = new List<string>();
        var stateStack = new Stack<AndroidHuiStateKind>();

        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);

            switch (command)
            {
                case HavenPushTransformCommand pushTransform:
                    stateStack.Push(AndroidHuiStateKind.Transform);
                    operations.Add(new AndroidHuiPushTransformOperation(
                        pushTransform.Bounds,
                        pushTransform.Transform,
                        pushTransform.Origin));
                    break;

                case HavenPopTransformCommand popTransform:
                    if (TryPopState(stateStack, AndroidHuiStateKind.Transform, structuralErrors))
                        operations.Add(new AndroidHuiPopTransformOperation(popTransform.Bounds));
                    break;

                case HavenPushClipCommand pushClip:
                    stateStack.Push(AndroidHuiStateKind.Clip);
                    operations.Add(new AndroidHuiPushClipOperation(pushClip.Bounds));
                    break;

                case HavenPopClipCommand popClip:
                    if (TryPopState(stateStack, AndroidHuiStateKind.Clip, structuralErrors))
                        operations.Add(new AndroidHuiPopClipOperation(popClip.Bounds));
                    break;

                case HavenFillRoundedRectCommand fill:
                    operations.Add(new AndroidHuiFillRoundedRectOperation(
                        fill.Bounds,
                        fill.Brush,
                        fill.Radius,
                        fill.Opacity));
                    break;

                case HavenStrokeRoundedRectCommand stroke:
                    operations.Add(new AndroidHuiStrokeRoundedRectOperation(
                        stroke.Bounds,
                        stroke.Pen,
                        stroke.Radius,
                        stroke.Opacity));
                    break;

                case HavenLineCommand line:
                    operations.Add(new AndroidHuiLineOperation(
                        line.Bounds,
                        line.Start,
                        line.End,
                        line.Pen,
                        line.Opacity));
                    break;

                case HavenEllipseCommand ellipse:
                    operations.Add(new AndroidHuiEllipseOperation(
                        ellipse.Bounds,
                        ellipse.Brush,
                        ellipse.Pen,
                        ellipse.Opacity));
                    break;

                default:
                    unsupported.Add(new AndroidHuiUnsupportedCommand(
                        command.GetType().Name,
                        command.Bounds,
                        UnsupportedReason));
                    break;
            }
        }

        if (stateStack.Count > 0)
        {
            structuralErrors.Add(
                $"HUI command stream ended with {stateStack.Count} unclosed Android canvas state frame(s): " +
                string.Join(", ", stateStack));
        }

        return new AndroidHuiRenderPlan(
            metrics,
            operations.ToArray(),
            unsupported.ToArray(),
            structuralErrors.ToArray());
    }

    private static bool TryPopState(
        Stack<AndroidHuiStateKind> stateStack,
        AndroidHuiStateKind expected,
        ICollection<string> structuralErrors)
    {
        if (stateStack.Count == 0)
        {
            structuralErrors.Add($"HUI command stream attempted to pop {expected} with no matching Android canvas state frame.");
            return false;
        }

        var actual = stateStack.Peek();
        if (actual != expected)
        {
            structuralErrors.Add($"HUI command stream attempted to pop {expected} while {actual} is the active Android canvas state frame.");
            return false;
        }

        stateStack.Pop();
        return true;
    }

    private static void ValidateMetrics(HavenRenderSurfaceMetrics metrics)
    {
        if (!double.IsFinite(metrics.RenderScale) || metrics.RenderScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(metrics), "RenderScale must be finite and greater than zero.");

        if (!double.IsFinite(metrics.Viewport.Width) || metrics.Viewport.Width < 0 ||
            !double.IsFinite(metrics.Viewport.Height) || metrics.Viewport.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(metrics), "Viewport dimensions must be finite and non-negative.");
    }
}
