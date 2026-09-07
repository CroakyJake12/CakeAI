using Haven.UI.Components;
using HavenImageComponent = Haven.UI.Components.Image;

namespace Haven.UI;

public sealed class HavenSceneRenderer
{
    public IReadOnlyList<HavenDrawCommand> Render(HavenElement root, Func<HavenElement, bool>? suppressContent = null)
    {
        var context = new HavenDrawingContext();
        RenderElement(root, context, suppressContent);
        RenderOverlays(root, context, suppressContent);
        return context.Commands;
    }

    private static void RenderElement(HavenElement element, HavenDrawingContext context, Func<HavenElement, bool>? suppressContent)
    {
        if (!element.IsIncluded || element.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) return;
        if (element is Select { IsExpanded: true } expandedSelect && suppressContent?.Invoke(element) != true)
            context.CollectOverlaySelect(expandedSelect);
        var opacity = Math.Clamp(element.GetValue(HavenProperties.Opacity), 0d, 1d);
        var scale = element.GetValue(HavenProperties.Scale);
        var rotation = element.GetValue(HavenProperties.Rotation);
        var translateX = ResolvePixels(element.GetValue(HavenProperties.TranslationX));
        var translateY = ResolvePixels(element.GetValue(HavenProperties.TranslationY));
        var transformed = Math.Abs(scale - 1d) > .0001d || Math.Abs(rotation) > .0001d || Math.Abs(translateX) > .0001d || Math.Abs(translateY) > .0001d;
        if (transformed)
            context.Add(new HavenPushTransformCommand(element.Bounds, new HavenTransform(scale, scale, rotation, translateX, translateY), new HavenPoint(element.Bounds.X + element.Bounds.Width / 2d, element.Bounds.Y + element.Bounds.Height / 2d)));

        var radius = ResolvePixels(element.GetValue(HavenProperties.Radius).TopLeft);
        DrawAnimatedShadow(element, context, opacity, radius);
        DrawAnimatedGlow(element, context, opacity, radius);
        DrawAnimatedFill(element, HavenProperties.Background, context, element.Bounds, radius, opacity);
        var borderWidth = ResolvePixels(element.GetValue(HavenProperties.BorderWidth));
        if (borderWidth > 0) DrawAnimatedStroke(element, context, element.Bounds, borderWidth, radius, opacity);

        var clipped = element.GetValue(HavenProperties.Clip)
            || element.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll;
        if (clipped) context.Add(new HavenPushClipCommand(element.Bounds));

        if (suppressContent?.Invoke(element) != true)
        {
            if (element is IHavenDrawCommandSource customDraw) customDraw.Draw(context, opacity);
            switch (element)
            {
            case Text text: DrawText(text.Content, element, context, opacity); break;
            case Button button when !string.IsNullOrWhiteSpace(button.Content): DrawText(button.Content, element, context, opacity, centerVertically: true, leadingIconKey: button.IconKey); break;
            case Button button when !string.IsNullOrWhiteSpace(button.IconKey): DrawButtonIcon(button, context, opacity); break;
            case Input input: DrawInput(input, context, opacity); break;
            case Select select: DrawText(select.SelectedItem ?? "Select", element, context, opacity, centerVertically: true); break;
            case Toggle toggle: DrawToggle(toggle, context, opacity); break;
            case Slider slider: DrawSlider(slider, context, opacity); break;
            case Progress progress: DrawProgress(progress, context, opacity); break;
            case Separator separator: DrawSeparator(separator, context, opacity); break;
            case HavenImageComponent image when !string.IsNullOrWhiteSpace(image.Source): context.Add(new HavenImageCommand(element.Bounds, new HavenImage(image.Source), MapImageLayout(image.Fit), opacity)); break;
            case Icon icon when !string.IsNullOrWhiteSpace(icon.Key): DrawAnimatedIcon(icon, context, opacity); break;
            }
        }
        foreach (var child in OrderedChildren(element)) RenderElement(child, context, suppressContent);
        if (clipped) context.Add(new HavenPopClipCommand(element.Bounds));
        if (transformed) context.Add(new HavenPopTransformCommand(element.Bounds));
    }

    /// <summary>
    /// Z-ordered children without allocating a sorted buffer for the common
    /// default-order case; OrderBy is stable so explicit ordering is identical.
    /// </summary>
    private static IEnumerable<HavenElement> OrderedChildren(HavenElement element)
    {
        var children = element.Children;
        if (children.Count < 2) return children;
        foreach (var child in children)
            if (child.GetValue(HavenProperties.ZIndex) != 0)
                return children.OrderBy(child => child.GetValue(HavenProperties.ZIndex));
        return children;
    }

    private static void RenderOverlays(HavenElement root, HavenDrawingContext context, Func<HavenElement, bool>? suppressContent)
    {
        // Expanded selects are collected during the main traversal; this pass
        // only orders and draws them instead of walking the tree again.
        foreach (var select in context.OverlaySelects.OrderBy(select => select.GetValue(HavenProperties.ZIndex)))
            DrawSelectPopup(select, root.Bounds, context);
        context.ClearOverlaySelects();
    }

    private static void DrawSelectPopup(Select select, HavenRect viewport, HavenDrawingContext context)
    {
        var popup = select.GetPopupLayout(viewport);
        if (popup is null) return;
        var opacity = Math.Clamp(select.GetValue(HavenProperties.Opacity), 0d, 1d);
        AddShadow(context, popup.Bounds, "Card", Select.PopupRadius, opacity);
        AddFill(context, popup.Bounds, "SurfaceRaised", Select.PopupRadius, opacity);
        AddStroke(context, popup.Bounds, "Border", 1d, Select.PopupRadius, opacity);

        foreach (var item in popup.Items)
        {
            if (item.Index == select.SelectedIndex)
                AddFill(context, item.Bounds, "AccentMuted", 10d, opacity * .72d);
            var textRect = new HavenRect(item.Bounds.X + 12d, item.Bounds.Y, Math.Max(0d, item.Bounds.Width - 24d), item.Bounds.Height);
            var layout = new HavenTextLayout(
                item.Text,
                select.GetValue(HavenProperties.FontFamily),
                select.GetValue(HavenProperties.FontSize),
                select.GetValue(HavenProperties.FontWeight),
                textRect.Width,
                true);
            AddText(context, textRect, layout, select.GetValue(HavenProperties.Foreground), opacity);
        }
    }

    private static void DrawButtonIcon(Button button, HavenDrawingContext context, double opacity)
    {
        var padding = button.GetValue(HavenProperties.Padding);
        var left = ResolvePixels(padding.Left);
        var top = ResolvePixels(padding.Top);
        var right = ResolvePixels(padding.Right);
        var bottom = ResolvePixels(padding.Bottom);
        var content = new HavenRect(
            button.Bounds.X + left,
            button.Bounds.Y + top,
            Math.Max(0, button.Bounds.Width - left - right),
            Math.Max(0, button.Bounds.Height - top - bottom));
        var size = Math.Min(20d, Math.Min(content.Width, content.Height));
        if (size <= 0) return;
        var rect = new HavenRect(
            content.X + (content.Width - size) / 2d,
            content.Y + (content.Height - size) / 2d,
            size,
            size);
        context.Add(new HavenIconCommand(rect, button.IconKey, new HavenTokenBrush(button.GetValue(HavenProperties.Foreground)), opacity));
    }

    private static void DrawInput(Input input, HavenDrawingContext context, double opacity)
    {
        var hasText = !string.IsNullOrEmpty(input.Text);
        var displayText = input.DisplayText;
        var focused = input.State.HasFlag(HavenElementState.Focused);
        var centerVertically = !input.Multiline;
        var padding = input.GetValue(HavenProperties.Padding);
        var left = ResolvePixels(padding.Left); var top = ResolvePixels(padding.Top); var right = ResolvePixels(padding.Right); var bottom = ResolvePixels(padding.Bottom);
        var rect = new HavenRect(input.Bounds.X + left, input.Bounds.Y + top, Math.Max(0, input.Bounds.Width - left - right), Math.Max(0, input.Bounds.Height - top - bottom));
        var fullLayout = new HavenTextLayout(displayText, input.GetValue(HavenProperties.FontFamily), input.GetValue(HavenProperties.FontSize), input.GetValue(HavenProperties.FontWeight), rect.Width, centerVertically);

        if (focused && hasText && input.HasSelection)
            context.Add(new HavenTextSelectionCommand(rect, fullLayout, input.SelectionStart, input.SelectionLength, new HavenTokenBrush("Accent"), opacity * .28d));

        DrawText(hasText ? displayText : input.Placeholder, input, context, hasText ? opacity : opacity * .64, centerVertically: centerVertically);
        if (!focused) return;

        var caretIndex = Math.Clamp(input.CaretIndex, 0, displayText.Length);
        var prefix = displayText[..caretIndex];
        var prefixLayout = new HavenTextLayout(prefix, input.GetValue(HavenProperties.FontFamily), input.GetValue(HavenProperties.FontSize), input.GetValue(HavenProperties.FontWeight), rect.Width, centerVertically);
        context.Add(new HavenCaretCommand(rect, prefixLayout, new HavenTokenBrush(input.GetValue(HavenProperties.Foreground)), opacity)
        {
            FullLayout = fullLayout,
            CaretIndex = caretIndex
        });
    }

    private static void DrawText(string value, HavenElement element, HavenDrawingContext context, double opacity, bool centerVertically = false, string? leadingIconKey = null)
    {
        if (string.IsNullOrEmpty(value)) return;
        var padding = element.GetValue(HavenProperties.Padding);
        var left = ResolvePixels(padding.Left); var top = ResolvePixels(padding.Top); var right = ResolvePixels(padding.Right); var bottom = ResolvePixels(padding.Bottom);
        var rect = new HavenRect(element.Bounds.X + left, element.Bounds.Y + top, Math.Max(0, element.Bounds.Width - left - right), Math.Max(0, element.Bounds.Height - top - bottom));
        if (!string.IsNullOrWhiteSpace(leadingIconKey) && rect.Width > 0 && rect.Height > 0)
        {
            var iconSize = Math.Min(20d, rect.Height);
            var iconRect = new HavenRect(rect.X, rect.Y + Math.Max(0, (rect.Height - iconSize) / 2d), iconSize, iconSize);
            context.Add(new HavenIconCommand(iconRect, leadingIconKey, new HavenTokenBrush(element.GetValue(HavenProperties.Foreground)), opacity));
            var advance = iconSize + 10d;
            rect = new HavenRect(rect.X + advance, rect.Y, Math.Max(0, rect.Width - advance), rect.Height);
        }
        var layout = new HavenTextLayout(value, element.GetValue(HavenProperties.FontFamily), element.GetValue(HavenProperties.FontSize), element.GetValue(HavenProperties.FontWeight), rect.Width, centerVertically);
        if (TryStringSample(element, HavenProperties.Foreground, out var from, out var to, out var progress))
        {
            AddText(context, rect, layout, from, opacity * (1d - progress));
            AddText(context, rect, layout, to, opacity * progress);
            return;
        }
        AddText(context, rect, layout, element.GetValue(HavenProperties.Foreground), opacity);
    }

    private static void DrawToggle(Toggle toggle, HavenDrawingContext context, double opacity)
    {
        var diameter = Math.Max(0, toggle.Bounds.Height - 6);
        var checkedProgress = toggle.IsChecked ? 1d : 0d;
        if (toggle.TryGetAnimationSample(Toggle.CheckedProperty, out var sample)
            && sample.From is bool from && sample.To is bool to)
            checkedProgress = (from ? 1d : 0d) + ((to ? 1d : 0d) - (from ? 1d : 0d)) * sample.Progress;
        var x = toggle.Bounds.X + 3 + Math.Max(0, toggle.Bounds.Width - diameter - 6) * checkedProgress;
        context.Add(new HavenEllipseCommand(new HavenRect(x, toggle.Bounds.Y + 3, diameter, diameter), new HavenTokenBrush("TextOnAccent"), null, opacity));
    }

    private static void DrawSlider(Slider slider, HavenDrawingContext context, double opacity)
    {
        var trackHeight = ResolvePixels(SliderDefaults.TrackHeight);
        var track = new HavenRect(slider.Bounds.X, slider.Bounds.Y + (slider.Bounds.Height - trackHeight) / 2, slider.Bounds.Width, trackHeight);
        context.Add(new HavenFillRoundedRectCommand(track, new HavenTokenBrush("SurfaceRaised"), trackHeight / 2, opacity));
        var activeWidth = track.Width * slider.NormalizedValue;
        if (activeWidth > 0) context.Add(new HavenFillRoundedRectCommand(track with { Width = activeWidth }, new HavenTokenBrush("Accent"), trackHeight / 2, opacity));
        var thumb = ResolvePixels(SliderDefaults.ThumbSize);
        var centerX = track.X + track.Width * slider.NormalizedValue;
        context.Add(new HavenEllipseCommand(new HavenRect(centerX - thumb / 2, slider.Bounds.Y + (slider.Bounds.Height - thumb) / 2, thumb, thumb), new HavenTokenBrush("TextPrimary"), null, opacity));
    }

    private static void DrawProgress(Progress progress, HavenDrawingContext context, double opacity)
    {
        var active = progress.Bounds with { Width = progress.Bounds.Width * progress.NormalizedValue };
        if (active.Width > 0) context.Add(new HavenFillRoundedRectCommand(active, new HavenTokenBrush(progress.GetValue(HavenProperties.Foreground)), ResolvePixels(progress.GetValue(HavenProperties.Radius).TopLeft), opacity));
    }

    private static void DrawSeparator(Separator separator, HavenDrawingContext context, double opacity)
    {
        var start = new HavenPoint(separator.Bounds.X, separator.Bounds.Y);
        var end = separator.Orientation == SeparatorOrientation.Horizontal ? new HavenPoint(separator.Bounds.Right, separator.Bounds.Y) : new HavenPoint(separator.Bounds.X, separator.Bounds.Bottom);
        context.Add(new HavenLineCommand(start, end, new HavenPen(new HavenTokenBrush(separator.GetValue(HavenProperties.Background)), 1), opacity));
    }

    private static double ResolvePixels(HavenLength length) => length.Unit == HavenLengthUnit.Pixel ? Math.Max(0, length.Value) : 0;

    private static void DrawAnimatedFill(HavenElement element, HavenProperty<string> property, HavenDrawingContext context, HavenRect rect, double radius, double opacity)
    {
        if (TryStringSample(element, property, out var from, out var to, out var progress))
        {
            AddFill(context, rect, from, radius, opacity * (1d - progress));
            AddFill(context, rect, to, radius, opacity * progress);
            return;
        }
        AddFill(context, rect, element.GetValue(property), radius, opacity);
    }

    private static void DrawAnimatedStroke(HavenElement element, HavenDrawingContext context, HavenRect rect, double width, double radius, double opacity)
    {
        if (TryStringSample(element, HavenProperties.BorderColor, out var from, out var to, out var progress))
        {
            AddStroke(context, rect, from, width, radius, opacity * (1d - progress));
            AddStroke(context, rect, to, width, radius, opacity * progress);
            return;
        }
        AddStroke(context, rect, element.GetValue(HavenProperties.BorderColor), width, radius, opacity);
    }

    private static void DrawAnimatedGlow(HavenElement element, HavenDrawingContext context, double opacity, double radius)
    {
        if (TryStringSample(element, HavenProperties.Glow, out var from, out var to, out var progress))
        {
            AddGlow(context, element.Bounds, from, radius, opacity * (1d - progress), 18d * Presence(from));
            AddGlow(context, element.Bounds, to, radius, opacity * progress, 18d * Presence(to));
            return;
        }
        AddGlow(context, element.Bounds, element.GetValue(HavenProperties.Glow), radius, opacity, 18);
    }

    private static void DrawAnimatedShadow(HavenElement element, HavenDrawingContext context, double opacity, double radius)
    {
        if (TryStringSample(element, HavenProperties.Shadow, out var from, out var to, out var progress))
        {
            AddShadow(context, element.Bounds, from, radius, opacity * (1d - progress));
            AddShadow(context, element.Bounds, to, radius, opacity * progress);
            return;
        }
        AddShadow(context, element.Bounds, element.GetValue(HavenProperties.Shadow), radius, opacity);
    }

    private static void DrawAnimatedIcon(Icon icon, HavenDrawingContext context, double opacity)
    {
        if (TryStringSample(icon, HavenProperties.Foreground, out var from, out var to, out var progress))
        {
            AddIcon(context, icon, from, opacity * (1d - progress));
            AddIcon(context, icon, to, opacity * progress);
            return;
        }
        AddIcon(context, icon, icon.GetValue(HavenProperties.Foreground), opacity);
    }

    private static bool TryStringSample(HavenElement element, HavenProperty<string> property, out string from, out string to, out double progress)
    {
        from = to = string.Empty;
        progress = 0;
        if (!element.TryGetAnimationSample(property, out var sample) || sample.From is not string fromValue || sample.To is not string toValue || fromValue.Equals(toValue, StringComparison.OrdinalIgnoreCase)) return false;
        from = fromValue;
        to = toValue;
        progress = sample.Progress;
        return true;
    }

    private static void AddFill(HavenDrawingContext context, HavenRect rect, string token, double radius, double opacity)
    {
        if (opacity <= .0001d || token.Equals("Transparent", StringComparison.OrdinalIgnoreCase) || token.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
        context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush(token), radius, opacity));
    }

    private static void AddStroke(HavenDrawingContext context, HavenRect rect, string token, double width, double radius, double opacity)
    {
        if (opacity <= .0001d || width <= 0 || token.Equals("Transparent", StringComparison.OrdinalIgnoreCase) || token.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush(token), width), radius, opacity));
    }

    private static void AddText(HavenDrawingContext context, HavenRect rect, HavenTextLayout layout, string token, double opacity)
    {
        if (opacity <= .0001d) return;
        context.Add(new HavenTextCommand(rect, layout, new HavenTokenBrush(token), opacity));
    }

    private static void AddIcon(HavenDrawingContext context, Icon icon, string token, double opacity)
    {
        if (opacity <= .0001d) return;
        context.Add(new HavenIconCommand(icon.Bounds, icon.Key, new HavenTokenBrush(token), opacity));
    }

    private static void AddGlow(HavenDrawingContext context, HavenRect rect, string token, double radius, double opacity, double blur)
    {
        if (opacity <= .0001d || blur <= .0001d || Presence(token) == 0) return;
        context.Add(new HavenGlowCommand(rect, new HavenGlow(new HavenTokenBrush(token), blur, opacity), radius));
    }

    private static void AddShadow(HavenDrawingContext context, HavenRect rect, string value, double radius, double opacity)
    {
        if (opacity <= .0001d || !HavenEffects.TryResolveShadow(value, out var shadow) || shadow is null) return;
        context.Add(new HavenShadowCommand(rect, shadow with { Opacity = shadow.Opacity * opacity }, radius));
    }

    private static double Presence(string token) => token.Equals("None", StringComparison.OrdinalIgnoreCase) || token.Equals("Transparent", StringComparison.OrdinalIgnoreCase) ? 0d : 1d;

    private static HavenImageLayout MapImageLayout(HavenImageFit fit) => fit switch
    {
        HavenImageFit.Cover => HavenImageLayout.Cover,
        HavenImageFit.Fill => HavenImageLayout.Fill,
        HavenImageFit.None => HavenImageLayout.None,
        _ => HavenImageLayout.Contain
    };
}
