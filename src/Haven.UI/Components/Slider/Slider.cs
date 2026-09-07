namespace Haven.UI.Components;

public static class SliderDefaults
{
    public const string SystemClass = "Slider";
    public const string Transition = "SliderChange";
    public static readonly HavenLength TrackHeight = HavenLength.Px(18);
    public static readonly HavenLength ThumbSize = HavenLength.Px(34);
}

/// <summary>Canonical Haven Slider with clamped semantic range state and Haven-owned interaction.</summary>
public sealed class Slider : HavenElement
{
    public static readonly HavenProperty<double> ValueProperty = HavenPropertyRegistry.Register(new HavenProperty<double>("Slider.Value", 0d));
    private double _minimum;
    private double _maximum = 100;

    public Slider()
    {
        Accessibility.Role = HavenAccessibleRole.Slider;
        Accessibility.Focusable = true;
        SetValue(HavenProperties.MinHeight, HavenLength.Px(44), HavenValueSource.Default);
        SetValue(HavenProperties.Foreground, "Accent", HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
        SetValue(HavenProperties.Transition, SliderDefaults.Transition, HavenValueSource.Default);
    }

    public event EventHandler? ValueChanged;

    public double Minimum
    {
        get => _minimum;
        set { _minimum = value; if (_maximum < _minimum) _maximum = _minimum; Value = Value; }
    }

    public double Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(value, _minimum); Value = Value; }
    }

    public double Step { get; set; }

    public double Value
    {
        get => GetValue(ValueProperty);
        set
        {
            var next = Math.Clamp(value, _minimum, _maximum);
            if (Step > 0)
                next = Math.Clamp(_minimum + Math.Round((next - _minimum) / Step, MidpointRounding.AwayFromZero) * Step, _minimum, _maximum);
            if (Math.Abs(next - GetValue(ValueProperty)) < .000001d) return;
            SetValue(ValueProperty, next);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double NormalizedValue => _maximum <= _minimum ? 0d : (Value - _minimum) / (_maximum - _minimum);

    /// <summary>SliderChange transitions animate the value, so changes must rescan motion.</summary>
    protected internal override HavenInvalidationKinds ClassifyValueChange(HavenProperty property) =>
        ReferenceEquals(property, ValueProperty)
            ? HavenInvalidationKinds.Motion | HavenInvalidationKinds.Paint
            : base.ClassifyValueChange(property);

    internal void SetFromPointer(double x)
    {
        var normalized = Bounds.Width <= 0 ? 0d : Math.Clamp((x - Bounds.X) / Bounds.Width, 0d, 1d);
        Value = _minimum + ((_maximum - _minimum) * normalized);
    }

    internal void Nudge(int direction)
    {
        var step = Step > 0 ? Step : Math.Max((_maximum - _minimum) / 100d, .01d);
        Value += step * Math.Sign(direction);
    }

    public override HavenComponentMetadata Metadata => new(
        "Slider",
        "Components/Slider/Slider.cs",
        [SliderDefaults.SystemClass],
        [SliderDefaults.Transition],
        "Track/thumb geometry, range semantics, snapping and interaction are defined beside the component.");
}
