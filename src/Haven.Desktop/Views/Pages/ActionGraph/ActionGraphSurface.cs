using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.ActionGraph;

/// <summary>
/// Retained execution-graph surface: cached layout, viewport culling with overspan,
/// cheap bezier connectors and spatial hit-testing. Panning never rebuilds the model,
/// zooming never re-queries data and selection never relays out the graph.
/// </summary>
internal sealed class ActionGraphSurface : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget, IHavenKeyboardInputTarget
{
    private const double NodeWidth = 216;
    private const double NodeHeight = 66;
    private const double LanePitch = 92;
    private const double RulerHeight = 52;
    private const double ContentTop = 26;
    private const double MinimumZoom = .25;
    private const double MaximumZoom = 2.5;
    internal const double OverspanSlots = 1.5;

    private ActionGraphModel _model = ActionGraphModel.Empty;
    private readonly Dictionary<Guid, LaidOutNode> _layout = [];
    private double _panX;
    private double _panY;
    private double _zoom = 1;
    private Guid? _selectedId;
    private bool _panning;
    private HavenPoint _panStartScreen;
    private double _panStartPanX;
    private double _panStartPanY;
    private bool _disposed;

    public ActionGraphSurface()
    {
        Name = "ActionGraph.Canvas";
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Execution graph canvas";
        Accessibility.Description = "Timeline of what Haven executed. Drag to pan, scroll to zoom, click a node for details, arrow keys to move between nodes.";
        Accessibility.Focusable = true;
        SetValue(HavenProperties.Background, "Transparent");
        SetValue(HavenProperties.Clip, true);
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.Cursor, HavenCursor.Grab);
    }

    public event Action<Guid?>? SelectionChanged;
    public event Action? ViewportChanged;

    public double Zoom => _zoom;
    public Guid? SelectedActionId => _selectedId;
    public int RealizedNodeCount { get; private set; }
    public int RealizedLinkCount { get; private set; }
    public int NodeCount => _model.Nodes.Count;
    public bool IsTimeMode => _model.TimeMode;

    private readonly record struct LaidOutNode(ActionGraphNode Node, double X, double Y);

    /// <summary>Replaces the graph. Layout is computed once here; later pan/zoom/selection reuse it.</summary>
    public void SetGraph(ActionGraphModel model, Guid? selectActionId = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        RelayOut();
        var previous = _selectedId;
        _selectedId = null;
        if (selectActionId is { } requested && _layout.ContainsKey(requested)) _selectedId = requested;
        FitToContent();
        if (!Equals(previous, _selectedId)) SelectionChanged?.Invoke(_selectedId);
        else Invalidate();
    }

    public void ClearSelection()
    {
        if (_selectedId is null) return;
        _selectedId = null;
        UpdateSelectionAccessibility();
        Invalidate();
    }

    public void SelectAction(Guid actionId, bool reveal = true)
    {
        if (!_layout.TryGetValue(actionId, out var node)) return;
        _selectedId = actionId;
        if (reveal) EnsureVisible(node);
        UpdateSelectionAccessibility();
        Invalidate();
    }

    public void PanBy(double screenDeltaX, double screenDeltaY)
    {
        _panX -= screenDeltaX / _zoom;
        _panY -= screenDeltaY / _zoom;
        ClampPan();
        ViewportChanged?.Invoke();
        Invalidate();
    }

    public void SetZoom(double value)
    {
        var next = Math.Clamp(value, MinimumZoom, MaximumZoom);
        if (Math.Abs(next - _zoom) < .0005) return;
        _zoom = next;
        ClampPan();
        ViewportChanged?.Invoke();
        Invalidate();
    }

    public void ZoomAt(double factor, HavenPoint localPoint)
    {
        var before = ScreenToWorld(localPoint);
        _zoom = Math.Clamp(_zoom * factor, MinimumZoom, MaximumZoom);
        _panX = before.X - localPoint.X / _zoom;
        _panY = before.Y - localPoint.Y / _zoom;
        ClampPan();
        ViewportChanged?.Invoke();
        Invalidate();
    }

    public bool FitToContent()
    {
        if (_layout.Count == 0)
        {
            _panX = 0;
            _panY = 0;
            return false;
        }
        var bounds = ContentBounds();
        var width = Math.Max(1, Bounds.Width == 0 ? 900 : Bounds.Width);
        var height = Math.Max(1, Bounds.Height == 0 ? 620 : Bounds.Height);
        var zoom = Math.Clamp(Math.Min(width / (bounds.Width + 48), height / (bounds.Height + 48)), MinimumZoom, 1.4);
        _zoom = zoom;
        _panX = bounds.X - (width - bounds.Width * zoom) / (2 * zoom);
        _panY = bounds.Y - (height - bounds.Height * zoom) / (2 * zoom);
        ClampPan();
        ViewportChanged?.Invoke();
        Invalidate();
        return true;
    }

    public HavenRect ContentBounds()
    {
        if (_layout.Count == 0) return new HavenRect(0, 0, 600, 300);
        var first = true;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        foreach (var entry in _layout.Values)
        {
            var rect = new HavenRect(entry.X, entry.Y, NodeWidth, NodeHeight);
            if (first)
            {
                minX = rect.X; minY = rect.Y; maxX = rect.Right; maxY = rect.Bottom;
                minY -= RulerHeight + ContentTop;
                first = false;
                continue;
            }
            minX = Math.Min(minX, rect.X); minY = Math.Min(minY, rect.Y);
            maxX = Math.Max(maxX, rect.Right); maxY = Math.Max(maxY, rect.Bottom);
        }
        return new HavenRect(minX, minY, maxX - minX, maxY - minY);
    }

    public bool TryHitTest(HavenPoint localPoint, out Guid actionId)
    {
        actionId = default;
        var world = ScreenToWorld(localPoint);
        foreach (var entry in HitOrder())
        {
            if (world.X >= entry.X && world.X <= entry.X + NodeWidth && world.Y >= entry.Y && world.Y <= entry.Y + NodeHeight)
            {
                actionId = entry.Node.ActionId;
                return true;
            }
        }
        return false;
    }

    /// <summary>Screen-space centre of a laid-out node; used by tests and reveal logic.</summary>
    internal HavenPoint NodeCenterScreen(Guid actionId) =>
        !_layout.TryGetValue(actionId, out var entry)
            ? default
            : WorldToScreen(entry.X + NodeWidth / 2, entry.Y + NodeHeight / 2);

    internal bool IsLaidOut(Guid actionId) => _layout.ContainsKey(actionId);

    public bool PointerPressed(HavenPointerInput input)
    {
        if (input.Button is not (HavenPointerButton.Primary or HavenPointerButton.Middle)) return false;
        if (input.Button == HavenPointerButton.Primary && TryHitTest(input.LocalPosition, out var actionId))
        {
            SelectAction(actionId);
            SelectionChanged?.Invoke(actionId);
            return true;
        }
        _panning = true;
        _panStartScreen = input.LocalPosition;
        _panStartPanX = _panX;
        _panStartPanY = _panY;
        SetValue(HavenProperties.Cursor, HavenCursor.Grabbing);
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (!_panning) return false;
        var deltaX = input.LocalPosition.X - _panStartScreen.X;
        var deltaY = input.LocalPosition.Y - _panStartScreen.Y;
        if (Math.Abs(deltaX) < .01 && Math.Abs(deltaY) < .01) return true;
        _panX = _panStartPanX - deltaX / _zoom;
        _panY = _panStartPanY - deltaY / _zoom;
        ClampPan();
        ViewportChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (!_panning) return false;
        _panning = false;
        SetValue(HavenProperties.Cursor, HavenCursor.Grab);
        return true;
    }

    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
    {
        if (Math.Abs(deltaX) < .001 && Math.Abs(deltaY) < .001) return false;
        if (Math.Abs(deltaX) > Math.Abs(deltaY))
        {
            PanBy(-deltaX * 36, 0);
            return true;
        }
        ZoomAt(deltaY < 0 ? 1.1 : 1 / 1.1, localPosition);
        return true;
    }

    public bool KeyDown(HavenKeyInput input)
    {
        if (_layout.Count == 0) return false;
        switch (input.Key)
        {
            case HavenKey.Escape when _selectedId is not null:
                _selectedId = null;
                UpdateSelectionAccessibility();
                Invalidate();
                SelectionChanged?.Invoke(null);
                return true;
            case HavenKey.Home or HavenKey.End:
            {
                var target = input.Key == HavenKey.Home ? FirstOrdered() : LastOrdered();
                if (target is null) return false;
                SelectAction(target.Value.Node.ActionId);
                SelectionChanged?.Invoke(target.Value.Node.ActionId);
                return true;
            }
            case HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down:
                return MoveSelection(input.Key);
            default:
                return false;
        }
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (_layout.Count == 0)
        {
            RealizedNodeCount = 0;
            RealizedLinkCount = 0;
            return;
        }
        var view = VisibleWorldRect();
        DrawRuler(context, view, opacity);
        DrawLinks(context, view, opacity);
        DrawNodes(context, view, opacity);
    }

    private void RelayOut()
    {
        _layout.Clear();
        var laneEnds = new List<double>();
        foreach (var node in _model.Nodes.OrderBy(item => item.SlotX).ThenBy(item => item.Ordinal))
        {
            var slotEnd = node.SlotX + NodeWidth;
            var lane = -1;
            for (var candidate = 0; candidate < laneEnds.Count; candidate++)
            {
                if (laneEnds[candidate] + 28 <= node.SlotX)
                {
                    lane = candidate;
                    break;
                }
            }
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(0);
            }
            laneEnds[lane] = slotEnd;
            _layout[node.ActionId] = new LaidOutNode(node, node.SlotX, ContentTop + lane * LanePitch);
        }
    }

    private IEnumerable<LaidOutNode> HitOrder() => VisibleNodes(VisibleWorldRect()).OrderBy(entry => entry.Node.Ordinal);

    private IEnumerable<LaidOutNode> VisibleNodes(HavenRect view)
    {
        var overspan = SlotSpan() * OverspanSlots + 80;
        foreach (var entry in _layout.Values)
        {
            if (entry.X > view.Right + overspan || entry.X + NodeWidth < view.Left - overspan) continue;
            if (entry.Y > view.Bottom + LanePitch || entry.Y + NodeHeight < view.Top - LanePitch) continue;
            yield return entry;
        }
    }

    private double SlotSpan() => _model.TimeMode
        ? (_model.ExtentWidth / Math.Max(1, _model.RulerTicks.Count - 1))
        : 250;

    private HavenRect VisibleWorldRect()
    {
        var width = Bounds.Width == 0 ? 900 : Bounds.Width;
        var height = Bounds.Height == 0 ? 620 : Bounds.Height;
        return new HavenRect(_panX, _panY, width / _zoom, height / _zoom);
    }

    private HavenPoint ScreenToWorld(HavenPoint local) => new(local.X / _zoom + _panX, local.Y / _zoom + _panY);

    private HavenPoint WorldToScreen(double worldX, double worldY) => new((worldX - _panX) * _zoom, (worldY - _panY) * _zoom);

    private void ClampPan()
    {
        var bounds = ContentBounds();
        var view = new HavenSize(Bounds.Width == 0 ? 900 : Bounds.Width, Bounds.Height == 0 ? 620 : Bounds.Height);
        var minPanX = bounds.Left - view.Width / _zoom * .75;
        var maxPanX = bounds.Right - view.Width / _zoom * .25;
        var minPanY = bounds.Top - view.Height / _zoom * .75;
        var maxPanY = bounds.Bottom - view.Height / _zoom * .25;
        _panX = maxPanX < minPanX ? (minPanX + maxPanX) / 2 : Math.Clamp(_panX, minPanX, maxPanX);
        _panY = maxPanY < minPanY ? (minPanY + maxPanY) / 2 : Math.Clamp(_panY, minPanY, maxPanY);
    }

    private void EnsureVisible(LaidOutNode node)
    {
        var view = VisibleWorldRect();
        var margin = SlotSpan() * .5 + 40;
        if (node.X < view.Left + margin || node.X + NodeWidth > view.Right - margin
            || node.Y < view.Top + 24 || node.Y + NodeHeight > view.Bottom - 24)
        {
            _panX = node.X + NodeWidth / 2 - view.Width / 2;
            _panY = node.Y + NodeHeight / 2 - view.Height / 2;
            ClampPan();
            ViewportChanged?.Invoke();
        }
    }

    private LaidOutNode? FirstOrdered()
    {
        LaidOutNode? best = null;
        foreach (var entry in _layout.Values)
            if (best is null || entry.Node.Ordinal < best.Value.Node.Ordinal) best = entry;
        return best;
    }

    private LaidOutNode? LastOrdered()
    {
        LaidOutNode? best = null;
        foreach (var entry in _layout.Values)
            if (best is null || entry.Node.Ordinal > best.Value.Node.Ordinal) best = entry;
        return best;
    }

    private bool MoveSelection(HavenKey key)
    {
        if (!_layout.TryGetValue(_selectedId ?? Guid.Empty, out var current))
        {
            var first = key == HavenKey.Left || key == HavenKey.Up ? LastOrdered() : FirstOrdered();
            if (first is null) return false;
            SelectAction(first.Value.Node.ActionId);
            SelectionChanged?.Invoke(first.Value.Node.ActionId);
            return true;
        }
        LaidOutNode? best = null;
        var bestScore = double.MaxValue;
        foreach (var entry in _layout.Values)
        {
            if (entry.Node.ActionId == current.Node.ActionId) continue;
            var dx = entry.X - current.X;
            var dy = entry.Y - current.Y;
            var forward = key switch
            {
                HavenKey.Right => dx > 8,
                HavenKey.Left => dx < -8,
                HavenKey.Down => dy > 8,
                _ => dy < -8
            };
            if (!forward) continue;
            var primary = key is HavenKey.Left or HavenKey.Right ? Math.Abs(dx) : Math.Abs(dy);
            var secondary = key is HavenKey.Left or HavenKey.Right ? Math.Abs(dy) : Math.Abs(dx);
            var score = primary + secondary * 2.5;
            if (score < bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }
        if (best is null) return true;
        SelectAction(best.Value.Node.ActionId);
        SelectionChanged?.Invoke(best.Value.Node.ActionId);
        return true;
    }

    private void DrawRuler(HavenDrawingContext context, HavenRect view, double opacity)
    {
        var baselineWorldY = ContentTop - 14;
        if (baselineWorldY < view.Top - 40 || baselineWorldY > view.Bottom + 40) return;
        var linePen = new HavenPen(new HavenTokenBrush("Border"), 1);
        var start = WorldToScreen(view.Left, baselineWorldY);
        var end = WorldToScreen(view.Right, baselineWorldY);
        context.Add(new HavenLineCommand(start, end, linePen, opacity * .8));
        foreach (var tick in _model.RulerTicks)
        {
            if (tick.SlotX > view.Right + 120 || tick.SlotX + 150 < view.Left - 120) continue;
            var top = WorldToScreen(tick.SlotX, baselineWorldY - 10);
            var bottom = WorldToScreen(tick.SlotX, baselineWorldY + 5);
            context.Add(new HavenLineCommand(top, bottom, new HavenPen(new HavenTokenBrush("TextMuted"), 1), opacity));
            context.Add(new HavenTextCommand(
                new HavenRect(top.X + 4, top.Y - 15, 148, 14),
                new HavenTextLayout(tick.PrimaryLabel, "Montserrat", 10, 700, 148),
                new HavenTokenBrush("TextSecondary"), opacity));
            if (tick.SecondaryLabel.Length > 0)
                context.Add(new HavenTextCommand(
                    new HavenRect(top.X + 4, bottom.Y + 2, 148, 13),
                    new HavenTextLayout(tick.SecondaryLabel, "Montserrat", 9, 500, 148),
                    new HavenTokenBrush("TextMuted"), opacity));
        }
    }

    private void DrawLinks(HavenDrawingContext context, HavenRect view, double opacity)
    {
        RealizedLinkCount = 0;
        foreach (var link in _model.Links)
        {
            if (!_layout.TryGetValue(link.FromActionId, out var from) || !_layout.TryGetValue(link.ToActionId, out var to)) continue;
            var fromVisible = IntersectsExpanded(from, view);
            var toVisible = IntersectsExpanded(to, view);
            if (!fromVisible && !toVisible) continue;
            RealizedLinkCount++;
            var startX = from.X + NodeWidth;
            var startY = from.Y + NodeHeight / 2;
            var endX = to.X;
            var endY = to.Y + NodeHeight / 2;
            if (endX < startX)
            {
                endX = to.X + NodeWidth / 2;
                endY = to.Y < from.Y ? to.Y + NodeHeight + 6 : to.Y - 6;
                startX = from.X + NodeWidth / 2;
                startY = from.Y < to.Y ? from.Y + NodeHeight + 6 : from.Y - 6;
            }
            var midX = (startX + endX) / 2;
            var figure = new HavenPathFigure(
                new HavenPoint(startX, startY),
                [new HavenCubicBezierSegment(new HavenPoint(midX, startY), new HavenPoint(midX, endY), new HavenPoint(endX, endY))]);
            var bounds = new HavenRect(
                Math.Min(startX, endX) - 4, Math.Min(startY, endY) - 4,
                Math.Abs(endX - startX) + 8, Math.Abs(endY - startY) + 8);
            var touchesSelection = Equals(link.FromActionId, _selectedId) || Equals(link.ToActionId, _selectedId);
            var token = link.Kind == ActionGraphLinkKind.Recovery ? "Warning"
                : touchesSelection ? "Accent" : "Border";
            var thickness = link.Kind == ActionGraphLinkKind.Recovery ? 1.7 : touchesSelection ? 2 : 1.3;
            context.Add(new HavenGeometryCommand(
                bounds,
                new HavenGeometry(new HavenPath([figure])),
                null,
                new HavenPen(new HavenTokenBrush(token), thickness),
                opacity * (touchesSelection ? 1d : .78d)));
        }
    }

    private bool IntersectsExpanded(LaidOutNode node, HavenRect view) =>
        node.X <= view.Right + NodeWidth && node.X + NodeWidth >= view.Left - NodeWidth
        && node.Y <= view.Bottom + LanePitch && node.Y + NodeHeight >= view.Top - LanePitch;

    private void DrawNodes(HavenDrawingContext context, HavenRect view, double opacity)
    {
        RealizedNodeCount = 0;
        foreach (var entry in HitOrder())
        {
            if (!IntersectsExpanded(entry, view)) continue;
            RealizedNodeCount++;
            DrawNode(context, entry, opacity);
        }
    }

    private void DrawNode(HavenDrawingContext context, LaidOutNode entry, double opacity)
    {
        var node = entry.Node;
        var isSelected = Equals(node.ActionId, _selectedId);
        var screen = WorldToScreen(entry.X, entry.Y);
        var width = NodeWidth * _zoom;
        var height = NodeHeight * _zoom;
        var rect = new HavenRect(screen.X, screen.Y, width, height);
        var radius = Math.Max(3, 12 * _zoom);

        context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush(isSelected ? "AccentSubtle" : "SurfaceRaised"), radius, opacity));
        context.Add(new HavenStrokeRoundedRectCommand(
            rect,
            new HavenPen(new HavenTokenBrush(isSelected ? "Accent" : "Border"), isSelected ? 2 : 1),
            radius,
            opacity));

        var categoryToken = ActionGraphCatalog.CategoryToken(node.Category);
        var statusToken = ActionGraphCatalog.StatusToken(node.Status);

        var stripeWidth = Math.Max(2, 3.5 * _zoom);
        context.Add(new HavenFillRoundedRectCommand(
            new HavenRect(rect.X + 1, rect.Y + 1, stripeWidth, height - 2),
            new HavenTokenBrush(categoryToken),
            radius - 1,
            opacity));

        var iconSize = 22 * _zoom;
        var iconRect = new HavenRect(
            rect.X + stripeWidth + 9 * _zoom,
            rect.Y + (height - iconSize) / 2,
            iconSize,
            iconSize);
        context.Add(new HavenIconCommand(iconRect, ActionGraphCatalog.CategoryIcon(node.Category), new HavenTokenBrush(categoryToken), opacity));

        var textLeft = iconRect.Right + 9 * _zoom;
        var textWidth = rect.Right - textLeft - 8 * _zoom;
        if (textWidth < 30) return;

        var titleSize = 12 * _zoom;
        var titleRect = new HavenRect(textLeft, rect.Y + 8 * _zoom, textWidth, titleSize * 1.35);
        context.Add(new HavenTextCommand(
            titleRect,
            new HavenTextLayout(TruncateForWidth(node.Name, textWidth, titleSize), "Montserrat", titleSize, 700, textWidth),
            new HavenTokenBrush("TextPrimary"),
            opacity));

        var captionSize = 9.5 * _zoom;
        var typeLabel = node.TypeLabel + (node.ComponentId.Length > 0 ? " · " + TruncateComponent(node.ComponentId) : string.Empty);
        var captionRect = new HavenRect(textLeft, titleRect.Bottom + 1 * _zoom, textWidth, captionSize * 1.4);
        context.Add(new HavenTextCommand(
            captionRect,
            new HavenTextLayout(TruncateForWidth(typeLabel, textWidth, captionSize), "Montserrat", captionSize, 500, textWidth),
            new HavenTokenBrush("TextMuted"),
            opacity));

        var summarySize = 10 * _zoom;
        var summaryRect = new HavenRect(textLeft, captionRect.Bottom + 1 * _zoom, textWidth, summarySize * 1.35);
        context.Add(new HavenTextCommand(
            summaryRect,
            new HavenTextLayout(TruncateForWidth(node.Summary, textWidth, summarySize), "Montserrat", summarySize, 500, textWidth),
            new HavenTokenBrush("TextSecondary"),
            opacity * .95));

        if (node.Duration is { } duration)
        {
            var badgeText = ActionGraphProjection.FormatDuration(duration);
            var badgeWidth = 12 + badgeText.Length * 6.4 * _zoom;
            badgeWidth = Math.Min(badgeWidth, width * .45);
            var badgeHeight = 15 * _zoom;
            var badgeRect = new HavenRect(rect.Right - badgeWidth - 7 * _zoom, rect.Y + 6 * _zoom, badgeWidth, badgeHeight);
            context.Add(new HavenFillRoundedRectCommand(badgeRect, new HavenTokenBrush("SurfaceSecondary"), badgeHeight / 2, opacity));
            context.Add(new HavenTextCommand(
                badgeRect,
                new HavenTextLayout(badgeText, "Montserrat", 9.5 * _zoom, 600, badgeWidth),
                new HavenTokenBrush(statusToken),
                opacity));
        }

        var dotSize = Math.Max(3, 7 * _zoom);
        var dotRect = new HavenRect(rect.Right - dotSize - 8 * _zoom, rect.Bottom - dotSize - 8 * _zoom, dotSize, dotSize);
        context.Add(new HavenEllipseCommand(dotRect, new HavenTokenBrush(statusToken), null, opacity));
        if (node.IsRetry)
            context.Add(new HavenEllipseCommand(dotRect, new HavenTokenBrush("Transparent"), new HavenPen(new HavenTokenBrush("Warning"), 1.4), opacity));
    }

    private static string TruncateForWidth(string value, double width, double fontSize)
    {
        var capacity = (int)(width / (fontSize * .62));
        if (capacity < 4) return string.Empty;
        var text = value.ReplaceLineEndings(" ");
        return text.Length <= capacity ? text : text[..(capacity - 1)] + "…";
    }

    private static string TruncateComponent(string component)
    {
        var slash = component.LastIndexOfAny(['.', '/', '\\']);
        var tail = slash >= 0 && slash + 1 < component.Length ? component[(slash + 1)..] : component;
        return tail.Length <= 18 ? tail : tail[..17] + "…";
    }

    private void UpdateSelectionAccessibility()
    {
        if (_selectedId is { } id && _layout.TryGetValue(id, out var selected))
        {
            Accessibility.Selected = true;
            Accessibility.Description = $"Selected step: {selected.Node.Name}, {ActionGraphCatalog.DescribeStatus(selected.Node.Status)}. Details are shown in the details pane.";
        }
        else
        {
            Accessibility.Selected = false;
            Accessibility.Description = "Timeline of what Haven executed. Drag to pan, scroll to zoom, click a node for details, arrow keys to move between nodes.";
        }
    }
}
