namespace Haven.UI;

public abstract record HavenBrush;
public sealed record HavenTokenBrush(string Token) : HavenBrush;
public sealed record HavenSolidBrush(byte A, byte R, byte G, byte B) : HavenBrush;
public sealed record HavenPen(HavenBrush Brush, double Thickness);

public enum HavenFillRule { EvenOdd, NonZero }
public enum HavenSweepDirection { CounterClockwise, Clockwise }

public abstract record HavenPathSegment;
public sealed record HavenLineSegment(HavenPoint End) : HavenPathSegment;
public sealed record HavenQuadraticBezierSegment(HavenPoint Control, HavenPoint End) : HavenPathSegment;
public sealed record HavenCubicBezierSegment(HavenPoint Control1, HavenPoint Control2, HavenPoint End) : HavenPathSegment;
public sealed record HavenArcSegment(
    HavenPoint End,
    HavenSize Radius,
    double RotationDegrees = 0,
    bool IsLargeArc = false,
    HavenSweepDirection SweepDirection = HavenSweepDirection.Clockwise) : HavenPathSegment;

public sealed record HavenPathFigure(HavenPoint Start, IReadOnlyList<HavenPathSegment> Segments, bool Closed = false);

/// <summary>A backend-neutral path containing one or more independently closed figures.</summary>
public sealed record HavenPath(IReadOnlyList<HavenPathFigure> Figures, HavenFillRule FillRule = HavenFillRule.EvenOdd)
{
    public HavenPath(IReadOnlyList<HavenPoint> points, bool closed = false)
        : this(ToFigure(points, closed)) { }

    private static IReadOnlyList<HavenPathFigure> ToFigure(IReadOnlyList<HavenPoint> points, bool closed)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) return [];
        return [new HavenPathFigure(points[0], points.Skip(1).Select(point => (HavenPathSegment)new HavenLineSegment(point)).ToArray(), closed)];
    }
}

/// <summary>Path geometry. A ViewBox maps reusable local coordinates into the command bounds.</summary>
public sealed record HavenGeometry(HavenPath Path, HavenRect? ViewBox = null);
public sealed record HavenTransform(double ScaleX = 1, double ScaleY = 1, double RotationDegrees = 0, double TranslateX = 0, double TranslateY = 0);
public sealed record HavenImage(string Source);
public enum HavenImageLayout { Contain, Cover, Fill, None }
public sealed record HavenTextLayout(string Text, string FontFamily, double FontSize, int FontWeight, double MaxWidth = double.PositiveInfinity, bool CenterVertically = false, bool Italic = false);
public sealed record HavenShadow(HavenBrush Brush, double Blur, double OffsetX, double OffsetY, double Spread = 0, double Opacity = 1d);
public sealed record HavenGlow(HavenBrush Brush, double Blur, double Opacity);

/// <summary>Logical viewport and physical render-scale information supplied by a platform backend.</summary>
public sealed record HavenRenderSurfaceMetrics(HavenSize Viewport, double RenderScale, HavenPlatform Platform)
{
    public HavenSize PixelSize => new(
        Math.Max(0, Viewport.Width * Math.Max(0, RenderScale)),
        Math.Max(0, Viewport.Height * Math.Max(0, RenderScale)));
}

public abstract record HavenDrawCommand(HavenRect Bounds, double Opacity = 1d);
public sealed record HavenPushTransformCommand(HavenRect Rect, HavenTransform Transform, HavenPoint Origin) : HavenDrawCommand(Rect);
public sealed record HavenPopTransformCommand(HavenRect Rect) : HavenDrawCommand(Rect);
public sealed record HavenPushClipCommand(HavenRect Rect) : HavenDrawCommand(Rect);
public sealed record HavenPopClipCommand(HavenRect Rect) : HavenDrawCommand(Rect);
public sealed record HavenFillRoundedRectCommand(HavenRect Rect, HavenBrush Brush, double Radius, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenStrokeRoundedRectCommand(HavenRect Rect, HavenPen Pen, double Radius, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenTextCommand(HavenRect Rect, HavenTextLayout Layout, HavenBrush Brush, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenTextSelectionCommand(HavenRect Rect, HavenTextLayout Layout, int SelectionStart, int SelectionLength, HavenBrush Brush, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenCaretCommand(HavenRect Rect, HavenTextLayout PrefixLayout, HavenBrush Brush, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha)
{
    public HavenTextLayout? FullLayout { get; init; }
    public int CaretIndex { get; init; } = -1;
}
public sealed record HavenLineCommand(HavenPoint Start, HavenPoint End, HavenPen Pen, double Alpha = 1d) : HavenDrawCommand(new HavenRect(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Abs(End.X - Start.X), Math.Abs(End.Y - Start.Y)), Alpha);
public sealed record HavenEllipseCommand(HavenRect Rect, HavenBrush Brush, HavenPen? Pen = null, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenGeometryCommand(HavenRect Rect, HavenGeometry Geometry, HavenBrush? Fill = null, HavenPen? Stroke = null, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenImageCommand(HavenRect Rect, HavenImage Image, HavenImageLayout Layout = HavenImageLayout.Contain, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenIconCommand(HavenRect Rect, string Key, HavenBrush Brush, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenShadowCommand(HavenRect Rect, HavenShadow Shadow, double Radius) : HavenDrawCommand(Rect, Shadow.Opacity);
public sealed record HavenGlowCommand(HavenRect Rect, HavenGlow Glow, double Radius) : HavenDrawCommand(Rect, Glow.Opacity);

public sealed class HavenDrawingContext
{
    private readonly List<HavenDrawCommand> _commands = [];
    private List<Haven.UI.Components.Select>? _overlaySelects;
    public IReadOnlyList<HavenDrawCommand> Commands => _commands;
    public void Add(HavenDrawCommand command) => _commands.Add(command ?? throw new ArgumentNullException(nameof(command)));

    internal IReadOnlyList<Haven.UI.Components.Select> OverlaySelects => _overlaySelects ?? (IReadOnlyList<Haven.UI.Components.Select>)[];

    internal void CollectOverlaySelect(Haven.UI.Components.Select select)
    {
        ArgumentNullException.ThrowIfNull(select);
        (_overlaySelects ??= []).Add(select);
    }

    internal void ClearOverlaySelects() => _overlaySelects?.Clear();
}
