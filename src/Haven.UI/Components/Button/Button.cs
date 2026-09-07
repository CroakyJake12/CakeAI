namespace Haven.UI.Components;

public enum ButtonVariant
{
    Primary,
    Secondary,
    Tertiary,
    Text,
    Ghost,
    Navigation,
    Danger,
    Icon
}

public sealed record ButtonVisualDefaults(
    string Background,
    string HoverBackground,
    string Foreground,
    HavenThickness Padding,
    HavenCornerRadius Radius,
    string HoverGlow,
    bool HoverEnabled);

/// <summary>Canonical, human-editable Button defaults and shared motion names.</summary>
public static class ButtonDefaults
{
    public const string SystemClass = "Button";
    public const string HoverTransition = "ButtonHover";
    public const string PressedTransition = "ButtonPressed";
    public const string ReleaseAnimation = "ButtonRelease";

    public static ButtonVisualDefaults For(ButtonVariant variant) => variant switch
    {
        ButtonVariant.Primary => Filled("Accent", "AccentHover", "ButtonTextPrimary", "AccentGlow"),
        ButtonVariant.Secondary => Filled("AccentSecondary", "AccentSecondaryHover", "TextOnAccent", "AccentSecondaryGlow"),
        ButtonVariant.Tertiary => Filled("AccentMuted", "AccentTertiaryHover", "ButtonTextSecondary", "AccentTertiaryGlow"),
        ButtonVariant.Danger => Filled("Danger", "DangerHover", "TextOnDanger", "DangerGlow"),
        ButtonVariant.Text => new("Transparent", "Transparent", "AccentSecondary", HavenThickness.Parse("4px 10px"), HavenCornerRadius.Uniform(HavenLength.Px(0)), "None", true),
        ButtonVariant.Ghost => new("Transparent", "AccentMuted", "TextPrimary", HavenThickness.Parse("8px 12px"), HavenCornerRadius.Uniform(HavenLength.Px(16)), "None", true),
        ButtonVariant.Navigation => new("Transparent", "AccentMuted", "TextPrimary", HavenThickness.Parse("10px 14px"), HavenCornerRadius.Uniform(HavenLength.Px(14)), "None", true),
        ButtonVariant.Icon => new("Surface", "AccentMuted", "TextPrimary", HavenThickness.Zero, HavenCornerRadius.Uniform(HavenLength.Px(22)), "AccentTertiaryGlow", true),
        _ => Filled("Accent", "AccentHover", "TextOnAccent", "AccentGlow")
    };

    private static ButtonVisualDefaults Filled(string background, string hover, string foreground, string glow) =>
        new(background, hover, foreground, HavenThickness.Parse("0px 28px"), HavenCornerRadius.Uniform(HavenLength.Px(24)), glow, true);
}

/// <summary>One canonical Haven Button. Variants are properties, not rendering primitives.</summary>
public sealed class Button : HavenElement, IHavenKeyboardInputTarget
{
    public static readonly HavenProperty<ButtonVariant> VariantProperty = HavenPropertyRegistry.Register(new HavenProperty<ButtonVariant>("Button.Variant", ButtonVariant.Primary));
    public static readonly HavenProperty<string> ContentProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Button.Content", string.Empty));
    public static readonly HavenProperty<string> IconKeyProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Button.IconKey", string.Empty));
    private bool _wasPressed;

    public string Content
    {
        get => GetValue(ContentProperty);
        set { SetValue(ContentProperty, value ?? string.Empty); Accessibility.AccessibleName = value; }
    }

    public string IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value ?? string.Empty);
    }

    public Button()
    {
        Accessibility.Role = HavenAccessibleRole.Button;
        Accessibility.Focusable = true;
        ApplyDefaults();
    }

    public ButtonVariant Variant
    {
        get => GetValue(VariantProperty);
        set { SetValue(VariantProperty, value); ApplyDefaults(); ApplyState(); }
    }

    public bool KeyDown(HavenKeyInput input)
    {
        if (input.Key is not (HavenKey.Enter or HavenKey.Space)) return false;
        if (State.HasFlag(HavenElementState.Disabled))
        {
            SetState(HavenElementState.Pressed, false);
            return true;
        }
        SetState(HavenElementState.Pressed, true);
        return true;
    }

    public bool KeyUp(HavenKeyInput input)
    {
        if (input.Key is not (HavenKey.Enter or HavenKey.Space)) return false;
        var wasPressed = State.HasFlag(HavenElementState.Pressed);
        SetState(HavenElementState.Pressed, false);
        if (wasPressed && !State.HasFlag(HavenElementState.Disabled)) Invoke();
        return true;
    }

    public override HavenComponentMetadata Metadata => new("Button", "Components/Button/Button.cs", [ButtonDefaults.SystemClass], [ButtonDefaults.HoverTransition, ButtonDefaults.PressedTransition, ButtonDefaults.ReleaseAnimation], "Variants, defaults and interactive-state mapping are defined beside the component.");

    protected override void OnStateChanged() => ApplyState();

    private void ApplyDefaults()
    {
        var defaults = ButtonDefaults.For(Variant);
        SetValue(HavenProperties.Background, defaults.Background, HavenValueSource.Default);
        SetValue(HavenProperties.Foreground, defaults.Foreground, HavenValueSource.Default);
        SetValue(HavenProperties.Padding, defaults.Padding, HavenValueSource.Default);
        SetValue(HavenProperties.Radius, defaults.Radius, HavenValueSource.Default);
        SetValue(HavenProperties.Hover, defaults.HoverEnabled, HavenValueSource.Default);
        SetValue(HavenProperties.FontFamily, "Montserrat", HavenValueSource.Default);
        SetValue(HavenProperties.FontWeight, 800, HavenValueSource.Default);
        SetValue(HavenProperties.MinHeight, HavenLength.Px(48), HavenValueSource.Default);
        SetValue(HavenProperties.Transition, ButtonDefaults.HoverTransition, HavenValueSource.Default);
    }

    private void ApplyState()
    {
        ClearValue(HavenProperties.Background, HavenValueSource.State);
        ClearValue(HavenProperties.Glow, HavenValueSource.State);
        ClearValue(HavenProperties.Scale, HavenValueSource.State);
        ClearValue(HavenProperties.Opacity, HavenValueSource.State);
        ClearValue(HavenProperties.Transition, HavenValueSource.State);
        ClearValue(HavenProperties.Animation, HavenValueSource.State);

        var defaults = ButtonDefaults.For(Variant);
        if (State.HasFlag(HavenElementState.Disabled))
        {
            SetValue(HavenProperties.Opacity, .52d, HavenValueSource.State);
            return;
        }

        if (State.HasFlag(HavenElementState.Pressed))
        {
            SetValue(HavenProperties.Scale, .94d, HavenValueSource.State);
            SetValue(HavenProperties.Transition, ButtonDefaults.PressedTransition, HavenValueSource.State);
            _wasPressed = true;
            return;
        }

        if (_wasPressed)
        {
            SetValue(HavenProperties.Animation, ButtonDefaults.ReleaseAnimation, HavenValueSource.State);
            _wasPressed = false;
        }

        if (State.HasFlag(HavenElementState.Hover) && defaults.HoverEnabled)
        {
            SetValue(HavenProperties.Background, defaults.HoverBackground, HavenValueSource.State);
            SetValue(HavenProperties.Glow, defaults.HoverGlow, HavenValueSource.State);
            SetValue(HavenProperties.Scale, 1.018d, HavenValueSource.State);
            SetValue(HavenProperties.Transition, ButtonDefaults.HoverTransition, HavenValueSource.State);
        }
    }
}
