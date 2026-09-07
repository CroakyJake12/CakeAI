using System.Globalization;

namespace Haven.UI;

public readonly record struct HavenPoint(double X, double Y);

public readonly record struct HavenSize(double Width, double Height)
{
    public static HavenSize Zero => new(0, 0);
}

public readonly record struct HavenRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(HavenPoint point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
}

public enum HavenLengthUnit
{
    Auto,
    Pixel,
    Percent,
    ViewportWidth,
    ViewportHeight,
    Fraction
}

/// <summary>
/// Logical Haven length. A Haven `px` is device-independent; platform backends
/// apply display scaling when drawing.
/// </summary>
public readonly record struct HavenLength(double Value, HavenLengthUnit Unit)
{
    public static HavenLength Auto => new(0, HavenLengthUnit.Auto);
    public static HavenLength Px(double value) => new(value, HavenLengthUnit.Pixel);
    public static HavenLength Percent(double value) => new(value, HavenLengthUnit.Percent);
    public static HavenLength Vw(double value) => new(value, HavenLengthUnit.ViewportWidth);
    public static HavenLength Vh(double value) => new(value, HavenLengthUnit.ViewportHeight);
    public static HavenLength Fr(double value) => new(value, HavenLengthUnit.Fraction);

    public bool IsAuto => Unit == HavenLengthUnit.Auto;

    public static HavenLength Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var text = value.Trim();
        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return Auto;

        static double ParseNumber(string number) =>
            double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);

        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) return Px(ParseNumber(text[..^2]));
        if (text.EndsWith('%')) return Percent(ParseNumber(text[..^1]));
        if (text.EndsWith("vw", StringComparison.OrdinalIgnoreCase)) return Vw(ParseNumber(text[..^2]));
        if (text.EndsWith("vh", StringComparison.OrdinalIgnoreCase)) return Vh(ParseNumber(text[..^2]));
        if (text.EndsWith("fr", StringComparison.OrdinalIgnoreCase)) return Fr(ParseNumber(text[..^2]));
        throw new FormatException($"'{value}' is not a Haven length. Use px, %, vw, vh, fr, or Auto.");
    }

    public double Resolve(double parentExtent, HavenSize viewport)
    {
        return Unit switch
        {
            HavenLengthUnit.Pixel => Value,
            HavenLengthUnit.Percent => parentExtent * Value / 100d,
            HavenLengthUnit.ViewportWidth => viewport.Width * Value / 100d,
            HavenLengthUnit.ViewportHeight => viewport.Height * Value / 100d,
            HavenLengthUnit.Auto or HavenLengthUnit.Fraction => double.NaN,
            _ => throw new InvalidOperationException($"Unsupported Haven length unit {Unit}.")
        };
    }

    public override string ToString() => Unit switch
    {
        HavenLengthUnit.Auto => "Auto",
        HavenLengthUnit.Pixel => $"{Value.ToString(CultureInfo.InvariantCulture)}px",
        HavenLengthUnit.Percent => $"{Value.ToString(CultureInfo.InvariantCulture)}%",
        HavenLengthUnit.ViewportWidth => $"{Value.ToString(CultureInfo.InvariantCulture)}vw",
        HavenLengthUnit.ViewportHeight => $"{Value.ToString(CultureInfo.InvariantCulture)}vh",
        HavenLengthUnit.Fraction => $"{Value.ToString(CultureInfo.InvariantCulture)}fr",
        _ => Value.ToString(CultureInfo.InvariantCulture)
    };
}

public readonly record struct HavenThickness(HavenLength Left, HavenLength Top, HavenLength Right, HavenLength Bottom)
{
    public static HavenThickness Zero => Uniform(HavenLength.Px(0));
    public static HavenThickness Uniform(HavenLength value) => new(value, value, value, value);

    public static HavenThickness Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(HavenLength.Parse)
            .ToArray();
        return parts.Length switch
        {
            1 => Uniform(parts[0]),
            2 => new(parts[1], parts[0], parts[1], parts[0]),
            4 => new(parts[3], parts[0], parts[1], parts[2]),
            _ => throw new FormatException("Haven thickness accepts one, two, or four length values.")
        };
    }
}

public readonly record struct HavenCornerRadius(HavenLength TopLeft, HavenLength TopRight, HavenLength BottomRight, HavenLength BottomLeft)
{
    public static HavenCornerRadius Uniform(HavenLength value) => new(value, value, value, value);
}
