using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Tokens;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>Single Avalonia rendering/input surface for an entire Haven-owned scene.</summary>
public sealed class HavenSceneControl : Panel, IHavenMeasureContext
{
    private readonly HavenLayoutEngine _layout = new();
    private readonly HavenSceneRenderer _renderer = new();
    private readonly HavenAnimationEngine _animations = new();
    private readonly HavenResourceSet _resources = HavenResourceSet.LoadEmbedded();
    private readonly Dictionary<HavenElement, (string? Transition, string? Animation)> _motionTokens = [];
    private readonly Dictionary<HavenElement, HavenAnimationSnapshot> _motionSnapshots = [];
    private readonly HashSet<HavenElement> _subscriptions = [];
    private readonly IHavenAvaloniaImageResolver _images;
    private readonly IHavenAvaloniaNativeControlResolver _nativeControlResolver;
    private readonly Dictionary<HavenElement, Control> _nativeControls = [];
    private readonly HavenDrawingSurface _drawingSurface;
    private readonly Func<bool> _reduceMotion;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _animationTimer;
    private HavenElement? _root;
    private HavenInputRouter? _input;
    private bool _processingMotion;
    private TopLevel? _topLevel;

    public HavenSceneControl() : this(new HavenDesktopImageResolver(), new HavenAvaloniaNativeControlResolver()) { }

    public HavenSceneControl(IHavenAvaloniaImageResolver images) : this(images, new HavenAvaloniaNativeControlResolver()) { }

    public HavenSceneControl(
        IHavenAvaloniaImageResolver images,
        IHavenAvaloniaNativeControlResolver nativeControlResolver,
        Func<bool>? reduceMotion = null,
        TimeProvider? timeProvider = null)
    {
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _nativeControlResolver = nativeControlResolver ?? throw new ArgumentNullException(nameof(nativeControlResolver));
        _reduceMotion = reduceMotion ?? (() => Haven.Desktop.Services.MotionPreferencesService.Current.ReduceAnimations);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _drawingSurface = new HavenDrawingSurface(this);
        Children.Add(_drawingSurface);
        Focusable = true;
        ClipToBounds = true;
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => AdvanceAnimationFrame());
        _animationTimer.Stop();
        AttachedToVisualTree += (_, _) => AttachTopLevelPointerObserver();
        DetachedFromVisualTree += (_, _) =>
        {
            DetachTopLevelPointerObserver();
            _animationTimer.Stop();
        };
    }

    public HavenPlatform Platform { get; set; } = OperatingSystem.IsAndroid() ? HavenPlatform.Android : HavenPlatform.Windows;
    public HavenRenderSurfaceMetrics SurfaceMetrics { get; private set; } = new(HavenSize.Zero, 1d, HavenPlatform.Unknown);
    public bool HasActiveAnimations => _animations.HasActiveAnimations;
    public event Action<Input>? InputSubmitted;
    public event Action? PointerPressedOutside;

    // Per-instance reconciliation counters (DEBUG-style evidence; internal for tests).
    internal long DiagApplyClassesRuns { get; private set; }
    internal long DiagSubscriptionReconciles { get; private set; }
    internal long DiagNativeReconciles { get; private set; }
    internal long DiagMotionCaptures { get; private set; }
    internal long DiagMeasurePasses { get; private set; }
    internal long DiagRenderInvalidations { get; private set; }
    internal long DiagArrangeLayoutSkips { get; private set; }

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new HavenSceneAutomationPeer(this);

    public bool FocusElement(HavenElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_root is null || !_root.DescendantsAndSelf().Contains(element) || !element.Accessibility.Focusable) return false;
        _input?.Focus(element);
        return Focus();
    }

    internal bool ActivateElementForAutomation(HavenElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_root is null || _input is null || !_root.DescendantsAndSelf().Contains(element)) return false;
        _input.Focus(element);
        var handled = _input.KeyDown(HavenKey.Enter);
        handled |= _input.KeyUp(HavenKey.Enter);
        if (!handled) return false;
        InvalidateMeasure();
        InvalidateScene();
        return true;
    }

    public HavenElement? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value)) return;
            ClearSubscriptions();
            _root = value;
            _input = value is null ? null : new HavenInputRouter(value);
            if (_input is not null) _input.InputSubmitted += input => InputSubmitted?.Invoke(input);
            _motionTokens.Clear();
            _motionSnapshots.Clear();
            _animations.StopAll();
            RefreshNativeControls();
            if (_root is not null)
            {
                _resources.ApplyClasses(_root);
                RefreshSubscriptions();
                CaptureMotionState(_root, true);
            }
            InvalidateMeasure();
            InvalidateScene();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        DiagMeasurePasses++;
        if (_root is null) return default;

        // Avalonia uses +Infinity on an Auto/unconstrained axis. Preserve that
        // for Haven's intrinsic measure pass instead of substituting the
        // not-yet-arranged Bounds (which is 0 on first measure and collapses
        // retained scenes such as Overlay chrome). Responsive surface metrics
        // stay finite so breakpoints remain deterministic during measurement.
        var metricWidth = FiniteMeasureMetric(availableSize.Width, Bounds.Width, SurfaceMetrics.Viewport.Width, 1280d);
        var metricHeight = FiniteMeasureMetric(availableSize.Height, Bounds.Height, SurfaceMetrics.Viewport.Height, 720d);
        UpdateSurfaceMetrics(metricWidth, metricHeight);

        _layout.Layout(
            _root,
            new HavenSize(availableSize.Width, availableSize.Height),
            Platform,
            this);

        _lastMeasureAvailable = availableSize;

        return new Size(
            FiniteDesired(_root.DesiredSize.Width, metricWidth),
            FiniteDesired(_root.DesiredSize.Height, metricHeight));
    }

    private static double FiniteMeasureMetric(double available, double current, double previous, double fallback)
    {
        if (double.IsFinite(available)) return Math.Max(0, available);
        if (double.IsFinite(current) && current > 0) return current;
        if (double.IsFinite(previous) && previous > 0) return previous;
        return fallback;
    }

    private static double FiniteDesired(double desired, double fallback)
        => double.IsFinite(desired) ? Math.Max(0, desired) : Math.Max(0, fallback);

    private Size _lastMeasureAvailable;

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The measure pass already laid out and arranged the scene when it ran
        // against this exact finite size; re-running the full Haven layout here
        // would duplicate every measure/arrange computation for no change.
        var skipLayout = double.IsFinite(_lastMeasureAvailable.Width)
            && Math.Abs(_lastMeasureAvailable.Width - finalSize.Width) < .01d
            && Math.Abs(_lastMeasureAvailable.Height - finalSize.Height) < .01d;
        if (skipLayout)
        {
            DiagArrangeLayoutSkips++;
        }
        else
        {
            UpdateSurfaceMetrics(finalSize.Width, finalSize.Height);
            if (_root is not null) _layout.Layout(_root, SurfaceMetrics.Viewport, Platform, this);
        }
        _drawingSurface.Arrange(new Rect(finalSize));
        ArrangeNativeControls();
        return finalSize;
    }

    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        if (_nativeControls.TryGetValue(element, out var nativeControl))
        {
            nativeControl.Measure(new Size(available.Width, available.Height));
            return new HavenSize(
                Math.Min(available.Width, nativeControl.DesiredSize.Width),
                Math.Min(available.Height, nativeControl.DesiredSize.Height));
        }
        var text = element switch
        {
            Text value => value.Content,
            Haven.UI.Components.Button value => value.Content,
            Input value => string.IsNullOrEmpty(value.Text) ? value.Placeholder : value.DisplayText,
            Select value => value.SelectedItem ?? "Select",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(text)) return new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48));
        var formatted = CreateText(text, element, available.Width);
        var leadingIconAdvance = element is Haven.UI.Components.Button button && !string.IsNullOrWhiteSpace(button.IconKey)
            ? 30d
            : 0d;
        return new HavenSize(
            Math.Min(available.Width, formatted.Width + leadingIconAdvance + 2),
            Math.Min(available.Height, formatted.Height + 2));
    }

    private void RenderScene(DrawingContext context)
    {
        if (_root is null) return;
        var drawingScopes = new Stack<IDisposable>();
        try
        {
            foreach (var command in _renderer.Render(_root))
            {
                if (command is HavenPushTransformCommand push)
                {
                    var transform = Matrix.CreateTranslation(-push.Origin.X, -push.Origin.Y)
                                    * Matrix.CreateScale(push.Transform.ScaleX, push.Transform.ScaleY)
                                    * Matrix.CreateRotation(push.Transform.RotationDegrees * Math.PI / 180d)
                                    * Matrix.CreateTranslation(push.Origin.X + push.Transform.TranslateX, push.Origin.Y + push.Transform.TranslateY);
                    drawingScopes.Push(context.PushTransform(transform));
                    continue;
                }
                if (command is HavenPushClipCommand clip)
                {
                    drawingScopes.Push(context.PushClip(Rect(clip.Rect)));
                    continue;
                }
                if (command is HavenPopTransformCommand or HavenPopClipCommand)
                {
                    if (drawingScopes.Count > 0) drawingScopes.Pop().Dispose();
                    continue;
                }
                Draw(context, command);
            }
        }
        finally
        {
            while (drawingScopes.Count > 0) drawingScopes.Pop().Dispose();
        }
    }

    private void AttachTopLevelPointerObserver()
    {
        DetachTopLevelPointerObserver();
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnTopLevelPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void DetachTopLevelPointerObserver()
    {
        if (_topLevel is null) return;
        _topLevel.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
        _topLevel = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visual && (ReferenceEquals(visual, this) || visual.GetVisualAncestors().Contains(this))) return;
        NotifyPointerPressedOutside();
    }

    internal void NotifyPointerPressedOutside()
    {
        if (_input?.DismissPopups() == true) InvalidateScene();
        PointerPressedOutside?.Invoke();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsNativeControlSource(e.Source)) return;
        ConfigureInputRouter();
        var p = e.GetPosition(this);
        _input?.PointerMoved(
            new HavenPoint(p.X, p.Y),
            e.Pointer.Type == PointerType.Touch ? HavenPointerKind.Touch : e.Pointer.Type == PointerType.Pen ? HavenPointerKind.Pen : HavenPointerKind.Mouse,
            ToHavenModifiers(e.KeyModifiers));
        InvalidateScene();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _input?.PointerExited();
        InvalidateScene();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsNativeControlSource(e.Source)) return;
        ConfigureInputRouter();
        var p = e.GetPosition(this);
        var updateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        var pointerButton = updateKind switch
        {
            PointerUpdateKind.RightButtonPressed => HavenPointerButton.Secondary,
            PointerUpdateKind.MiddleButtonPressed => HavenPointerButton.Middle,
            _ => HavenPointerButton.Primary
        };
        _input?.PointerPressed(
            new HavenPoint(p.X, p.Y),
            e.Pointer.Type == PointerType.Touch ? HavenPointerKind.Touch : e.Pointer.Type == PointerType.Pen ? HavenPointerKind.Pen : HavenPointerKind.Mouse,
            pointerButton,
            ToHavenModifiers(e.KeyModifiers));
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateScene();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsNativeControlSource(e.Source)) return;
        ConfigureInputRouter();
        var p = e.GetPosition(this);
        _input?.PointerReleased(new HavenPoint(p.X, p.Y), ToHavenModifiers(e.KeyModifiers));
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateScene();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_input?.CancelPointer() != true) return;
        InvalidateScene();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsNativeControlSource(e.Source)) return;
        var p = e.GetPosition(this);
        if (_input?.Scroll(new HavenPoint(p.X, p.Y), -e.Delta.X * 48d, -e.Delta.Y * 48d) == true)
        {
            e.Handled = true;
            InvalidateMeasure();
            InvalidateScene();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        ConfigureInputRouter();
        if (_input?.KeyDown(MapInputKey(e.Key), ToHavenModifiers(e.KeyModifiers)) != true) return;
        e.Handled = true;
        InvalidateMeasure();
        InvalidateScene();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_input?.KeyUp(MapInputKey(e.Key)) != true) return;
        e.Handled = true;
        InvalidateScene();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_input?.TextInput(e.Text) != true) return;
        e.Handled = true;
        InvalidateMeasure();
        InvalidateScene();
    }

    private void ConfigureInputRouter()
    {
        if (_input is null) return;
        _input.InputCaretHitTest = HitTestInputCaret;
        _input.InputCaretNavigation = NavigateInputCaret;
        _input.ClipboardCopyRequested -= OnClipboardCopyRequested;
        _input.ClipboardCopyRequested += OnClipboardCopyRequested;
        _input.ClipboardPasteRequested -= OnClipboardPasteRequested;
        _input.ClipboardPasteRequested += OnClipboardPasteRequested;
    }

    internal int HitTestInputCaret(Input input, HavenPoint localPoint)
    {
        if (string.IsNullOrEmpty(input.Text)) return 0;

        var padding = input.GetValue(HavenProperties.Padding);
        var left = ResolveInputPixels(padding.Left);
        var top = ResolveInputPixels(padding.Top);
        var right = ResolveInputPixels(padding.Right);
        var bottom = ResolveInputPixels(padding.Bottom);
        var contentWidth = Math.Max(1d, input.Bounds.Width - left - right);
        var contentHeight = Math.Max(1d, input.Bounds.Height - top - bottom);
        var layoutInfo = InputTextLayout(input, contentWidth);
        using var layout = CreateEditableTextLayout(layoutInfo);

        var verticalOffset = input.Multiline ? 0d : Math.Max(0d, (contentHeight - layout.Height) / 2d);
        var point = new Point(
            Math.Max(0d, localPoint.X - left),
            Math.Max(0d, localPoint.Y - top - verticalOffset));
        var hit = layout.HitTestPoint(point).CharacterHit;
        return Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, input.Text.Length);
    }

    internal int NavigateInputCaret(Input input, HavenKey key)
    {
        if (!input.Multiline || string.IsNullOrEmpty(input.Text)) return input.CaretIndex;

        var padding = input.GetValue(HavenProperties.Padding);
        var left = ResolveInputPixels(padding.Left);
        var right = ResolveInputPixels(padding.Right);
        var contentWidth = Math.Max(1d, input.Bounds.Width - left - right);
        var layoutInfo = InputTextLayout(input, contentWidth);
        using var layout = CreateEditableTextLayout(layoutInfo);
        if (layout.TextLines.Count == 0) return input.CaretIndex;

        var caret = Math.Clamp(input.CaretIndex, 0, input.Text.Length);
        var lineIndex = Math.Clamp(layout.GetLineIndexFromCharacterIndex(caret, trailingEdge: false), 0, layout.TextLines.Count - 1);
        var line = layout.TextLines[lineIndex];

        if (key == HavenKey.Home)
            return Math.Clamp(line.FirstTextSourceIndex, 0, input.Text.Length);
        if (key == HavenKey.End)
            return Math.Clamp(line.FirstTextSourceIndex + Math.Max(0, line.Length - line.NewLineLength), 0, input.Text.Length);

        var targetLineIndex = key switch
        {
            HavenKey.Up => lineIndex - 1,
            HavenKey.Down => lineIndex + 1,
            _ => lineIndex
        };
        if (targetLineIndex < 0 || targetLineIndex >= layout.TextLines.Count) return caret;

        var targetLine = layout.TextLines[targetLineIndex];
        var targetStart = Math.Clamp(targetLine.FirstTextSourceIndex, 0, input.Text.Length);
        var targetEnd = Math.Clamp(
            targetLine.FirstTextSourceIndex + Math.Max(0, targetLine.Length - targetLine.NewLineLength),
            targetStart,
            input.Text.Length);
        var currentPosition = layout.HitTestTextPosition(caret);
        var targetPosition = layout.HitTestTextPosition(targetStart);
        var hit = layout.HitTestPoint(new Point(
            Math.Max(0d, currentPosition.X),
            targetPosition.Y + Math.Max(1d, targetLine.Height) / 2d)).CharacterHit;
        return Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, targetStart, targetEnd);
    }

    private static HavenTextLayout InputTextLayout(Input input, double contentWidth) => new(
        input.DisplayText,
        input.GetValue(HavenProperties.FontFamily),
        input.GetValue(HavenProperties.FontSize),
        input.GetValue(HavenProperties.FontWeight),
        contentWidth,
        CenterVertically: !input.Multiline);

    internal static HavenInputModifiers ToHavenModifiers(KeyModifiers modifiers) => new(
        Shift: modifiers.HasFlag(KeyModifiers.Shift),
        Control: modifiers.HasFlag(KeyModifiers.Control),
        Alt: modifiers.HasFlag(KeyModifiers.Alt),
        Meta: modifiers.HasFlag(KeyModifiers.Meta));

    internal static HavenKey MapInputKey(Key key) => key switch
    {
        Key.Enter => HavenKey.Enter,
        Key.Space => HavenKey.Space,
        Key.Escape => HavenKey.Escape,
        Key.Tab => HavenKey.Tab,
        Key.Left => HavenKey.Left,
        Key.Right => HavenKey.Right,
        Key.Up => HavenKey.Up,
        Key.Down => HavenKey.Down,
        Key.Home => HavenKey.Home,
        Key.End => HavenKey.End,
        Key.Back => HavenKey.Backspace,
        Key.Delete => HavenKey.Delete,
        Key.A => HavenKey.A,
        Key.C => HavenKey.C,
        Key.D => HavenKey.D,
        Key.F => HavenKey.F,
        Key.V => HavenKey.V,
        Key.X => HavenKey.X,
        Key.Y => HavenKey.Y,
        Key.Z => HavenKey.Z,
        _ => HavenKey.Unknown
    };

    private static double ResolveInputPixels(HavenLength length) =>
        length.Unit == HavenLengthUnit.Pixel ? Math.Max(0d, length.Value) : 0d;

    internal static IReadOnlyList<Avalonia.Rect> ResolveSelectionRects(HavenTextSelectionCommand selection)
    {
        if (selection.SelectionLength <= 0 || string.IsNullOrEmpty(selection.Layout.Text) || selection.Rect.Width <= 0 || selection.Rect.Height <= 0)
            return [];

        using var layout = CreateEditableTextLayout(selection.Layout);
        var start = Math.Clamp(selection.SelectionStart, 0, selection.Layout.Text.Length);
        var length = Math.Clamp(selection.SelectionLength, 0, selection.Layout.Text.Length - start);
        if (length == 0) return [];
        var origin = EditableTextOrigin(selection.Rect, selection.Layout, layout.Height);
        var result = new List<Avalonia.Rect>();
        foreach (var range in layout.HitTestTextRange(start, length))
        {
            var leftEdge = Math.Max(selection.Rect.X, origin.X + range.X);
            var topEdge = Math.Max(selection.Rect.Y, origin.Y + range.Y);
            var rightEdge = Math.Min(selection.Rect.Right, origin.X + range.Right);
            var bottomEdge = Math.Min(selection.Rect.Bottom, origin.Y + range.Bottom);
            if (rightEdge > leftEdge && bottomEdge > topEdge)
                result.Add(new Avalonia.Rect(leftEdge, topEdge, rightEdge - leftEdge, bottomEdge - topEdge));
        }
        return result;
    }

    internal static Avalonia.Rect ResolveCaretRect(HavenCaretCommand caret)
    {
        if (caret.Rect.Width <= 0 || caret.Rect.Height <= 0) return default;
        var layoutInfo = caret.FullLayout ?? caret.PrefixLayout;
        using var layout = CreateEditableTextLayout(layoutInfo);
        var caretIndex = caret.CaretIndex >= 0
            ? Math.Clamp(caret.CaretIndex, 0, layoutInfo.Text.Length)
            : layoutInfo.Text.Length;
        var origin = EditableTextOrigin(caret.Rect, layoutInfo, layout.Height);
        var position = string.IsNullOrEmpty(layoutInfo.Text)
            ? new Avalonia.Rect(0, 0, 0, Math.Max(layoutInfo.FontSize, layout.Height))
            : layout.HitTestTextPosition(caretIndex);
        var maxX = Math.Max(caret.Rect.X, caret.Rect.Right - 1.5d);
        var x = Math.Clamp(origin.X + position.X, caret.Rect.X, maxX);
        var y = Math.Clamp(origin.Y + position.Y, caret.Rect.Y, caret.Rect.Bottom);
        var lineHeight = position.Height > 0 ? position.Height : Math.Max(layoutInfo.FontSize, layout.Height);
        var height = Math.Max(0d, Math.Min(lineHeight, caret.Rect.Bottom - y));
        return new Avalonia.Rect(x, y, 1.5d, height);
    }

    private static Avalonia.Media.TextFormatting.TextLayout CreateEditableTextLayout(HavenTextLayout layout)
    {
        var family = layout.FontFamily;
        var fontFamily = HavenUiFont.Resolve(family);
        var typeface = new Typeface(fontFamily, FontStyle.Normal, Weight(layout.FontWeight), FontStretch.Normal);
        return new Avalonia.Media.TextFormatting.TextLayout(
            layout.Text,
            typeface,
            layout.FontSize,
            foreground: null,
            textAlignment: TextAlignment.Left,
            textWrapping: layout.CenterVertically ? TextWrapping.NoWrap : TextWrapping.Wrap,
            maxWidth: Math.Max(1d, layout.MaxWidth),
            maxHeight: double.PositiveInfinity);
    }

    private static Point EditableTextOrigin(HavenRect rect, HavenTextLayout layout, double layoutHeight) =>
        new(rect.X, rect.Y + (layout.CenterVertically ? Math.Max(0d, (rect.Height - layoutHeight) / 2d) : 0d));

    private async void OnClipboardCopyRequested(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Haven clipboard copy failed: " + ex.Message);
        }
    }

    private async void OnClipboardPasteRequested()
    {
        var router = _input;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (router is null || clipboard is null) return;
        try
        {
            var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
            if (!ReferenceEquals(router, _input) || string.IsNullOrEmpty(text) || !router.PasteText(text)) return;
            InvalidateMeasure();
            InvalidateScene();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Haven clipboard paste failed: " + ex.Message);
        }
    }

    private void Draw(DrawingContext context, HavenDrawCommand command)
    {
        if (command.Opacity < .9999d)
        {
            using var opacity = context.PushOpacity(Math.Clamp(command.Opacity, 0d, 1d));
            DrawCore(context, command);
            return;
        }
        DrawCore(context, command);
    }

    private void DrawCore(DrawingContext context, HavenDrawCommand command)
    {
        switch (command)
        {
            case HavenFillRoundedRectCommand fill: context.DrawRectangle(Resolve(fill.Brush), null, Rect(fill.Rect), fill.Radius, fill.Radius, default); break;
            case HavenStrokeRoundedRectCommand stroke: context.DrawRectangle(null, new Pen(Resolve(stroke.Pen.Brush), stroke.Pen.Thickness), Rect(stroke.Rect), stroke.Radius, stroke.Radius, default); break;
            case HavenTextCommand text:
            {
                var formatted = CreateText(text.Layout.Text, text.Layout.FontFamily, text.Layout.FontSize, text.Layout.FontWeight, text.Layout.MaxWidth, Resolve(text.Brush), text.Layout.Italic);
                var y = text.Layout.CenterVertically
                    ? text.Rect.Y + Math.Max(0, (text.Rect.Height - formatted.Height) / 2d)
                    : text.Rect.Y;
                context.DrawText(formatted, new Point(text.Rect.X, y));
                break;
            }
            case HavenTextSelectionCommand selection:
            {
                var brush = Resolve(selection.Brush);
                foreach (var range in ResolveSelectionRects(selection))
                    context.DrawRectangle(brush, null, range, 0, 0, default);
                break;
            }
            case HavenCaretCommand caret:
            {
                var rect = ResolveCaretRect(caret);
                if (rect.Width <= 0 || rect.Height <= 0) break;
                var brush = Resolve(caret.Brush);
                context.DrawLine(new Pen(brush, rect.Width), new Point(rect.X, rect.Y), new Point(rect.X, rect.Bottom));
                break;
            }
            case HavenLineCommand line: context.DrawLine(new Pen(Resolve(line.Pen.Brush), line.Pen.Thickness), new Point(line.Start.X, line.Start.Y), new Point(line.End.X, line.End.Y)); break;
            case HavenEllipseCommand ellipse: context.DrawEllipse(Resolve(ellipse.Brush), ellipse.Pen is null ? null : new Pen(Resolve(ellipse.Pen.Brush), ellipse.Pen.Thickness), new Point(ellipse.Rect.X + ellipse.Rect.Width / 2, ellipse.Rect.Y + ellipse.Rect.Height / 2), ellipse.Rect.Width / 2, ellipse.Rect.Height / 2); break;
            case HavenGeometryCommand geometry: context.DrawGeometry(geometry.Fill is null ? null : Resolve(geometry.Fill), geometry.Stroke is null ? null : new Pen(Resolve(geometry.Stroke.Brush), geometry.Stroke.Thickness), CreateGeometry(geometry.Geometry, geometry.Rect)); break;
            case HavenIconCommand icon: context.DrawGeometry(null, new Pen(Resolve(icon.Brush), Math.Max(1.5d, Math.Min(icon.Rect.Width, icon.Rect.Height) / 12d)), CreateGeometry(HavenIconCatalog.Resolve(icon.Key), icon.Rect, true)); break;
            case HavenImageCommand image: DrawImage(context, image); break;
            case HavenShadowCommand shadow: DrawEffect(context, shadow.Rect, shadow.Radius, shadow.Shadow.Brush, shadow.Shadow.OffsetX, shadow.Shadow.OffsetY, shadow.Shadow.Blur, shadow.Shadow.Spread); break;
            case HavenGlowCommand glow:
                DrawEffect(context, glow.Rect, glow.Radius, glow.Glow.Brush, 0, 0, glow.Glow.Blur, 0);
                break;
        }
    }

    private void DrawImage(DrawingContext context, HavenImageCommand command)
    {
        var target = Rect(command.Rect);
        if (!_images.TryResolve(command.Image.Source, out var image) || image is null)
        {
            var border = new Pen(HavenAvaloniaThemeResolver.Resolve("Border"), 1.5d);
            context.DrawRectangle(HavenAvaloniaThemeResolver.Resolve("SurfaceRaised"), border, target, 8, 8, default);
            context.DrawLine(border, target.TopLeft + new Vector(8, 8), target.BottomRight - new Vector(8, 8));
            context.DrawLine(border, target.TopRight + new Vector(-8, 8), target.BottomLeft + new Vector(8, -8));
            return;
        }

        var destination = ImageDestination(target, image.Size, command.Layout);
        if (command.Layout is HavenImageLayout.Cover or HavenImageLayout.None)
        {
            using var clip = context.PushClip(target);
            context.DrawImage(image, destination);
            return;
        }
        context.DrawImage(image, destination);
    }

    private static Avalonia.Rect ImageDestination(Avalonia.Rect target, Size source, HavenImageLayout layout)
    {
        if (layout == HavenImageLayout.Fill || source.Width <= 0 || source.Height <= 0) return target;
        if (layout == HavenImageLayout.None)
            return new Avalonia.Rect(target.X + (target.Width - source.Width) / 2d, target.Y + (target.Height - source.Height) / 2d, source.Width, source.Height);
        var scale = layout == HavenImageLayout.Cover ? Math.Max(target.Width / source.Width, target.Height / source.Height) : Math.Min(target.Width / source.Width, target.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Avalonia.Rect(target.X + (target.Width - width) / 2d, target.Y + (target.Height - height) / 2d, width, height);
    }

    private static void DrawEffect(DrawingContext context, HavenRect bounds, double radius, HavenBrush brush, double offsetX, double offsetY, double blur, double spread)
    {
        var resolved = Resolve(brush);
        var color = HavenAvaloniaThemeResolver.EffectColor(resolved, "Haven shadow/glow brush");
        var shadows = new BoxShadows(new BoxShadow { OffsetX = offsetX, OffsetY = offsetY, Blur = Math.Max(0, blur), Spread = spread, Color = color });
        context.DrawRectangle(Brushes.Transparent, null, Rect(bounds), radius, radius, shadows);
    }

    private static StreamGeometry CreateGeometry(HavenGeometry source, HavenRect target, bool preserveAspect = false)
    {
        var geometry = new StreamGeometry();
        using var writer = geometry.Open();
        writer.SetFillRule(source.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
        foreach (var figure in source.Path.Figures)
        {
            writer.BeginFigure(MapPoint(figure.Start, source.ViewBox, target, preserveAspect), true);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case HavenLineSegment line: writer.LineTo(MapPoint(line.End, source.ViewBox, target, preserveAspect), true); break;
                    case HavenQuadraticBezierSegment quadratic: writer.QuadraticBezierTo(MapPoint(quadratic.Control, source.ViewBox, target, preserveAspect), MapPoint(quadratic.End, source.ViewBox, target, preserveAspect), true); break;
                    case HavenCubicBezierSegment cubic: writer.CubicBezierTo(MapPoint(cubic.Control1, source.ViewBox, target, preserveAspect), MapPoint(cubic.Control2, source.ViewBox, target, preserveAspect), MapPoint(cubic.End, source.ViewBox, target, preserveAspect), true); break;
                    case HavenArcSegment arc: writer.ArcTo(MapPoint(arc.End, source.ViewBox, target, preserveAspect), MapSize(arc.Radius, source.ViewBox, target, preserveAspect), arc.RotationDegrees, arc.IsLargeArc, arc.SweepDirection == HavenSweepDirection.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, true); break;
                }
            }
            writer.EndFigure(figure.Closed);
        }
        return geometry;
    }

    private static Point MapPoint(HavenPoint point, HavenRect? viewBox, HavenRect target, bool preserveAspect)
    {
        if (viewBox is not { Width: > 0, Height: > 0 } source) return new Point(point.X, point.Y);
        var scaleX = target.Width / source.Width;
        var scaleY = target.Height / source.Height;
        if (preserveAspect) scaleX = scaleY = Math.Min(scaleX, scaleY);
        var contentWidth = source.Width * scaleX;
        var contentHeight = source.Height * scaleY;
        return new Point(target.X + (target.Width - contentWidth) / 2d + (point.X - source.X) * scaleX, target.Y + (target.Height - contentHeight) / 2d + (point.Y - source.Y) * scaleY);
    }

    private static Size MapSize(HavenSize size, HavenRect? viewBox, HavenRect target, bool preserveAspect)
    {
        if (viewBox is not { Width: > 0, Height: > 0 } source) return new Size(size.Width, size.Height);
        var scaleX = target.Width / source.Width;
        var scaleY = target.Height / source.Height;
        if (preserveAspect) scaleX = scaleY = Math.Min(scaleX, scaleY);
        return new Size(size.Width * scaleX, size.Height * scaleY);
    }

    private static IBrush Resolve(HavenBrush brush) => brush switch
    {
        HavenTokenBrush token => HavenAvaloniaThemeResolver.Resolve(token.Token),
        HavenSolidBrush solid => new SolidColorBrush(Color.FromArgb(solid.A, solid.R, solid.G, solid.B)),
        _ => throw new InvalidOperationException($"Unsupported Haven brush '{brush.GetType().Name}'.")
    };

    private static Avalonia.Rect Rect(HavenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private void UpdateSurfaceMetrics(double width, double height) => SurfaceMetrics = new(new HavenSize(Math.Max(0, width), Math.Max(0, height)), Math.Max(.01d, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d), Platform);
    private static FormattedText CreateText(string text, HavenElement element, double maxWidth) => CreateText(text, element.GetValue(HavenProperties.FontFamily), element.GetValue(HavenProperties.FontSize), element.GetValue(HavenProperties.FontWeight), maxWidth, HavenAvaloniaThemeResolver.Resolve(element.GetValue(HavenProperties.Foreground)));
    private static FormattedText CreateText(string text, string family, double size, int weight, double maxWidth, IBrush foreground, bool italic = false)
    {
        var fontFamily = HavenUiFont.Resolve(family);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(fontFamily, italic ? FontStyle.Italic : FontStyle.Normal, Weight(weight), FontStretch.Normal), size, foreground) { MaxTextWidth = Math.Max(1, maxWidth) };
    }
    private static FontWeight Weight(int weight) => weight switch { >= 800 => FontWeight.ExtraBold, >= 700 => FontWeight.Bold, >= 600 => FontWeight.SemiBold, >= 500 => FontWeight.Medium, _ => FontWeight.Normal };
    private static HavenKey MapKey(Key key) => key switch { Key.Enter => HavenKey.Enter, Key.Space => HavenKey.Space, Key.Escape => HavenKey.Escape, Key.Tab => HavenKey.Tab, Key.Left => HavenKey.Left, Key.Right => HavenKey.Right, Key.Up => HavenKey.Up, Key.Down => HavenKey.Down, Key.Home => HavenKey.Home, Key.End => HavenKey.End, Key.Back => HavenKey.Backspace, Key.Delete => HavenKey.Delete, _ => HavenKey.Unknown };
    private void RefreshSubscriptions()
    {
        if (_root is null) { ClearSubscriptions(); return; }
        var current = _root.DescendantsAndSelf().ToHashSet();
        foreach (var removed in _subscriptions.Where(element => !current.Contains(element)).ToArray())
        {
            removed.Invalidated -= OnSceneInvalidated;
            _subscriptions.Remove(removed);
        }
        foreach (var added in current.Where(element => !_subscriptions.Contains(element)))
        {
            added.Invalidated += OnSceneInvalidated;
            _subscriptions.Add(added);
        }
    }

    private void ClearSubscriptions()
    {
        foreach (var element in _subscriptions) element.Invalidated -= OnSceneInvalidated;
        _subscriptions.Clear();
    }

    private void OnSceneInvalidated(object? sender, EventArgs e)
    {
        // The kinds must be read before any reconciliation work because nested
        // invalidations raised during that work overwrite the element's kinds.
        var element = sender as HavenElement;
        var kinds = element?.LastInvalidationKinds ?? HavenInvalidationKinds.All;
        ApplyInvalidation(element, kinds);
    }

    private void ApplyInvalidation(HavenElement? element, HavenInvalidationKinds kinds)
    {
        if (_processingMotion)
        {
            if ((kinds & HavenInvalidationKinds.Layout) != 0 || _animations.HasActiveAnimations) InvalidateMeasure();
            InvalidateScene();
            return;
        }
        var reconciliationKinds = HavenInvalidationKinds.Style | HavenInvalidationKinds.Structure | HavenInvalidationKinds.Motion;
        if (_root is not null && (kinds & reconciliationKinds) != 0)
        {
            if ((kinds & HavenInvalidationKinds.Style) != 0)
            {
                _resources.ApplyClasses(_root);
                DiagApplyClassesRuns++;
            }
            if ((kinds & HavenInvalidationKinds.Structure) != 0)
            {
                RefreshSubscriptions();
                DiagSubscriptionReconciles++;
                RefreshNativeControls();
                DiagNativeReconciles++;
            }
            CaptureMotionState(_root, false);
            DiagMotionCaptures++;
        }
        if ((kinds & HavenInvalidationKinds.Layout) != 0) InvalidateMeasure();
        InvalidateScene();
    }

    private void RefreshNativeControls()
    {
        var current = _root?.DescendantsAndSelf().Where(IsNativeElement).ToHashSet() ?? [];
        foreach (var removed in _nativeControls.Keys.Where(element => !current.Contains(element)).ToArray())
        {
            Children.Remove(_nativeControls[removed]);
            _nativeControls.Remove(removed);
        }
        foreach (var element in current.Where(element => !_nativeControls.ContainsKey(element)))
        {
            if (!_nativeControlResolver.TryCreate(element, out var control) || control is null) continue;
            _nativeControls.Add(element, control);
            Children.Add(control);
        }
    }

    private void ArrangeNativeControls()
    {
        foreach (var (element, control) in _nativeControls)
        {
            control.IsVisible = element.IsIncluded
                && element.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible;
            if (control.IsVisible) control.Arrange(Rect(element.Bounds));
        }
    }

    private bool IsNativeControlSource(object? source)
    {
        if (source is not Visual visual) return false;
        return _nativeControls.Values.Any(control => ReferenceEquals(visual, control) || visual.GetVisualAncestors().Contains(control));
    }

    private static bool IsNativeElement(HavenElement element) => element is Video or Web or NativeHost;

    private void InvalidateScene()
    {
        DiagRenderInvalidations++;
        _drawingSurface.InvalidateVisual();
        InvalidateVisual();
    }

    private void CaptureMotionState(HavenElement root, bool initial)
    {
        var now = _timeProvider.GetUtcNow();
        _animations.MotionPolicy = new HavenMotionPolicy(_reduceMotion());
        var currentElements = root.DescendantsAndSelf().ToHashSet();
        foreach (var removed in _motionTokens.Keys.Where(element => !currentElements.Contains(element)).ToArray())
        {
            ProcessMotion(() => _animations.Stop(removed));
            _motionTokens.Remove(removed);
            _motionSnapshots.Remove(removed);
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            var next = (Transition: element.GetValue(HavenProperties.Transition), Animation: element.GetValue(HavenProperties.Animation));
            var known = _motionTokens.TryGetValue(element, out var previous);
            _motionTokens[element] = next;
            var startedKeyframes = false;

            if (!string.IsNullOrWhiteSpace(next.Animation) && (!known || !string.Equals(next.Animation, previous.Animation, StringComparison.Ordinal)))
            {
                if (!_resources.TryResolveAnimation(next.Animation, out var animation) || animation is null)
                    throw new KeyNotFoundException($"Animation '{next.Animation}' was not found in UserAnimations.hui or SystemAnimations.hui.");
                ProcessMotion(() => _animations.Start(element, animation, now));
                startedKeyframes = true;
            }

            if (string.IsNullOrWhiteSpace(next.Transition))
            {
                _motionSnapshots.Remove(element);
                continue;
            }
            if (!_resources.TryResolveTransition(next.Transition, out var transition) || transition is null)
                throw new KeyNotFoundException($"Transition '{next.Transition}' was not found in UserAnimations.hui or SystemAnimations.hui.");

            var target = _animations.Capture(element, transition.Properties, includeAnimationValues: false);
            if (!initial && !startedKeyframes && _motionSnapshots.TryGetValue(element, out var previousTarget))
            {
                var fromValues = new Dictionary<HavenProperty, object?>();
                foreach (var property in target.Values.Keys)
                {
                    fromValues[property] = _animations.HasActiveAnimation(element)
                        ? element.GetValue(property)
                        : previousTarget.Values.GetValueOrDefault(property, element.GetValue(property));
                }
                ProcessMotion(() => _animations.StartTransition(element, transition, new HavenAnimationSnapshot(fromValues), target, now));
            }
            _motionSnapshots[element] = target;
        }

        if (_animations.HasActiveAnimations && !_animationTimer.IsEnabled) _animationTimer.Start();
    }

    public bool AdvanceAnimationFrame()
    {
        _animations.MotionPolicy = new HavenMotionPolicy(_reduceMotion());
        var active = false;
        ProcessMotion(() => active = _animations.Tick(_timeProvider.GetUtcNow()));
        InvalidateScene();
        if (!active) _animationTimer.Stop();
        return active;
    }

    private void ProcessMotion(Action action)
    {
        _processingMotion = true;
        try { action(); }
        finally { _processingMotion = false; }
    }

    private sealed class HavenDrawingSurface(HavenSceneControl owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            owner.RenderScene(context);
        }
    }
}
