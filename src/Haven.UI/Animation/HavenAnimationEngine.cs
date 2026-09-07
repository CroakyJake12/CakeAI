using System.Globalization;

namespace Haven.UI;

public enum HavenAnimationKind { Transition, Keyframes }
public enum HavenAnimationLifecycleState { Started, Completed, Cancelled }

public sealed class HavenAnimationLifecycleEventArgs(
    HavenElement element,
    string name,
    HavenAnimationKind kind,
    HavenAnimationLifecycleState state) : EventArgs
{
    public HavenElement Element { get; } = element;
    public string Name { get; } = name;
    public HavenAnimationKind Kind { get; } = kind;
    public HavenAnimationLifecycleState State { get; } = state;
}

public sealed record HavenMotionPolicy(bool ReducedMotion = false, double DurationScale = 1d)
{
    public TimeSpan Apply(TimeSpan duration) => ReducedMotion
        ? TimeSpan.Zero
        : TimeSpan.FromTicks((long)Math.Max(0d, duration.Ticks * Math.Clamp(DurationScale, 0d, 10d)));
}

public sealed record HavenAnimationSnapshot(IReadOnlyDictionary<HavenProperty, object?> Values);

/// <summary>
/// Haven-owned transition and keyframe runtime. Platform hosts supply time and
/// schedule frames; property capture, interpolation and lifecycle stay here.
/// </summary>
public sealed class HavenAnimationEngine
{
    private readonly List<ActiveMotion> _active = [];
    private HavenMotionPolicy _motionPolicy = new();

    public bool HasActiveAnimations => _active.Count > 0;
    public HavenMotionPolicy MotionPolicy
    {
        get => _motionPolicy;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _motionPolicy = value;
            if (_active.Count > 0 && (value.ReducedMotion || value.DurationScale <= 0d)) CompleteActiveAnimations();
        }
    }
    public event EventHandler<HavenAnimationLifecycleEventArgs>? LifecycleChanged;

    public bool HasActiveAnimation(HavenElement element) => _active.Any(active => ReferenceEquals(active.Element, element));

    public HavenAnimationSnapshot Capture(HavenElement element, IEnumerable<string> propertyNames, bool includeAnimationValues = true)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(propertyNames);
        var values = new Dictionary<HavenProperty, object?>();
        foreach (var name in propertyNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var property = ResolveProperty(element, name, "Transition");
            values[property] = element.GetValue(property, includeAnimationValues ? HavenValueSource.Animation : HavenValueSource.State);
        }
        return new HavenAnimationSnapshot(values);
    }

    public bool StartTransition(
        HavenElement element,
        HavenTransitionDefinition definition,
        HavenAnimationSnapshot from,
        HavenAnimationSnapshot to,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var tracks = new List<MotionTrack>();
        foreach (var name in definition.Properties)
        {
            var property = ResolveProperty(element, name, $"Transition '{definition.Name}'");
            var fromValue = from.Values.GetValueOrDefault(property, element.GetValue(property));
            var toValue = to.Values.GetValueOrDefault(property, element.GetValue(property, HavenValueSource.State));
            if (Equals(fromValue, toValue)) continue;
            tracks.Add(new MotionTrack(property, [new MotionPoint(0, fromValue), new MotionPoint(100, toValue)]));
        }
        if (tracks.Count == 0) return false;
        Start(new ActiveMotion(element, definition.Name, HavenAnimationKind.Transition, definition.Duration, definition.Easing, tracks, startedAt));
        return HasActiveAnimation(element);
    }

    public void Start(HavenElement element, HavenAnimationDefinition definition, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(definition);
        var propertyNames = definition.Keyframes.SelectMany(frame => frame.Properties.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var tracks = new List<MotionTrack>();
        foreach (var name in propertyNames)
        {
            var property = ResolveProperty(element, name, $"Animation '{definition.Name}'");
            var baseValue = element.GetValue(property, HavenValueSource.State);
            var points = definition.Keyframes
                .Where(frame => frame.Properties.ContainsKey(name))
                .Select(frame => new MotionPoint(frame.Percent, ParseValue(property, frame.Properties[name])))
                .ToList();
            if (points.Count == 0) continue;
            if (points[0].Percent > 0) points.Insert(0, new MotionPoint(0, baseValue));
            if (points[^1].Percent < 100) points.Add(new MotionPoint(100, baseValue));
            tracks.Add(new MotionTrack(property, points));
        }
        Start(new ActiveMotion(element, definition.Name, HavenAnimationKind.Keyframes, definition.Duration, definition.Easing, tracks, startedAt));
    }

    public bool Tick(DateTimeOffset now)
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var active = _active[index];
            var duration = Math.Max(1d, active.EffectiveDuration.TotalMilliseconds);
            var raw = MotionPolicy.ReducedMotion
                ? 1d
                : Math.Clamp((now - active.StartedAt).TotalMilliseconds / duration, 0d, 1d);
            Apply(active, HavenEasing.Evaluate(raw, active.Easing));
            if (raw < 1d) continue;
            ClearAnimationValues(active);
            _active.RemoveAt(index);
            Raise(active, HavenAnimationLifecycleState.Completed);
        }
        return _active.Count > 0;
    }

    public void Stop(HavenElement element)
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(_active[index].Element, element)) continue;
            var active = _active[index];
            ClearAnimationValues(active);
            _active.RemoveAt(index);
            Raise(active, HavenAnimationLifecycleState.Cancelled);
        }
    }

    public void StopAll()
    {
        foreach (var element in _active.Select(active => active.Element).Distinct().ToArray()) Stop(element);
    }

    private void CompleteActiveAnimations()
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var active = _active[index];
            Apply(active, 1d);
            ClearAnimationValues(active);
            _active.RemoveAt(index);
            Raise(active, HavenAnimationLifecycleState.Completed);
        }
    }

    private void Start(ActiveMotion active)
    {
        Stop(active.Element);
        active.EffectiveDuration = MotionPolicy.Apply(active.Duration);
        Raise(active, HavenAnimationLifecycleState.Started);
        if (active.Tracks.Count == 0)
        {
            Raise(active, HavenAnimationLifecycleState.Completed);
            return;
        }
        Apply(active, 0d);
        if (active.EffectiveDuration > TimeSpan.Zero)
        {
            _active.Add(active);
            return;
        }
        Apply(active, 1d);
        ClearAnimationValues(active);
        Raise(active, HavenAnimationLifecycleState.Completed);
    }

    private static void Apply(ActiveMotion active, double progress)
    {
        active.Element.Update(() =>
        {
            foreach (var track in active.Tracks)
            {
                var percentage = Math.Clamp(progress, 0d, 1d) * 100d;
                var before = track.Points.LastOrDefault(point => point.Percent <= percentage) ?? track.Points[0];
                var after = track.Points.FirstOrDefault(point => point.Percent >= percentage) ?? track.Points[^1];
                var span = Math.Max(.0001d, after.Percent - before.Percent);
                var local = before.Percent == after.Percent ? 1d : Math.Clamp((percentage - before.Percent) / span, 0d, 1d);
                var value = Interpolate(before.Value, after.Value, local, out var continuous);
                active.Element.SetAnimationSample(track.Property, before.Value, after.Value, local);
                if (continuous) active.Element.SetValue(track.Property, value, HavenValueSource.Animation);
            }
        });
    }

    private static object? Interpolate(object? from, object? to, double amount, out bool continuous)
    {
        continuous = true;
        if (from is double a && to is double b) return a + (b - a) * amount;
        if (from is float fa && to is float fb) return fa + (fb - fa) * amount;
        if (from is int ia && to is int ib) return (int)Math.Round(ia + (ib - ia) * amount);
        if (from is HavenLength fromLength && to is HavenLength toLength && fromLength.Unit == toLength.Unit)
            return new HavenLength(fromLength.Value + (toLength.Value - fromLength.Value) * amount, fromLength.Unit);
        if (from is HavenThickness fromThickness && to is HavenThickness toThickness
            && TryInterpolate(fromThickness.Left, toThickness.Left, amount, out var left)
            && TryInterpolate(fromThickness.Top, toThickness.Top, amount, out var top)
            && TryInterpolate(fromThickness.Right, toThickness.Right, amount, out var right)
            && TryInterpolate(fromThickness.Bottom, toThickness.Bottom, amount, out var bottom))
            return new HavenThickness(left, top, right, bottom);
        if (from is HavenCornerRadius fromRadius && to is HavenCornerRadius toRadius
            && TryInterpolate(fromRadius.TopLeft, toRadius.TopLeft, amount, out var topLeft)
            && TryInterpolate(fromRadius.TopRight, toRadius.TopRight, amount, out var topRight)
            && TryInterpolate(fromRadius.BottomRight, toRadius.BottomRight, amount, out var bottomRight)
            && TryInterpolate(fromRadius.BottomLeft, toRadius.BottomLeft, amount, out var bottomLeft))
            return new HavenCornerRadius(topLeft, topRight, bottomRight, bottomLeft);
        continuous = false;
        return amount >= 1d ? to : from;
    }

    private static bool TryInterpolate(HavenLength from, HavenLength to, double amount, out HavenLength value)
    {
        if (from.Unit != to.Unit) { value = default; return false; }
        value = new HavenLength(from.Value + (to.Value - from.Value) * amount, from.Unit);
        return true;
    }

    private static object? ParseValue(HavenProperty property, string value)
    {
        var type = property.ValueType;
        if (type == typeof(string))
        {
            if (ReferenceEquals(property, HavenProperties.Shadow)) HavenEffects.TryResolveShadow(value, out _);
            return value;
        }
        if (type == typeof(double))
        {
            var parsed = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
            return double.IsFinite(parsed) ? parsed : throw new FormatException($"Animation value '{value}' for {property.Name} must be finite.");
        }
        if (type == typeof(double?)) return value.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? null : double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (type == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(value);
        if (type == typeof(bool?)) return value.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? null : bool.Parse(value);
        if (type == typeof(HavenLength)) return HavenLength.Parse(value);
        if (type == typeof(HavenThickness)) return HavenThickness.Parse(value);
        if (type == typeof(HavenCornerRadius)) return HavenCornerRadius.Uniform(HavenLength.Parse(value));
        if (type.IsEnum) return Enum.Parse(type, value, true);
        throw new FormatException($"Haven property '{property.Name}' of type {type.Name} cannot be animated.");
    }

    private static HavenProperty ResolveProperty(HavenElement element, string name, string owner)
    {
        if (HavenPropertyRegistry.TryResolve(name, out var property)) return property;
        property = element switch
        {
            Components.Toggle when name.Equals("Toggle.Checked", StringComparison.OrdinalIgnoreCase) => Components.Toggle.CheckedProperty,
            Components.Slider when name.Equals("Slider.Value", StringComparison.OrdinalIgnoreCase) => Components.Slider.ValueProperty,
            _ => throw new KeyNotFoundException($"{owner} references unknown Haven property '{name}' for component '{element.Metadata.ComponentName}'.")
        };
        return property;
    }

    private static void ClearAnimationValues(ActiveMotion active)
    {
        active.Element.Update(() =>
        {
            foreach (var track in active.Tracks)
            {
                active.Element.ClearValue(track.Property, HavenValueSource.Animation);
                active.Element.ClearAnimationSample(track.Property);
            }
        });
    }

    private void Raise(ActiveMotion active, HavenAnimationLifecycleState state) =>
        LifecycleChanged?.Invoke(this, new HavenAnimationLifecycleEventArgs(active.Element, active.Name, active.Kind, state));

    private sealed record MotionPoint(double Percent, object? Value);
    private sealed record MotionTrack(HavenProperty Property, IReadOnlyList<MotionPoint> Points);

    private sealed class ActiveMotion(
        HavenElement element,
        string name,
        HavenAnimationKind kind,
        TimeSpan duration,
        string easing,
        IReadOnlyList<MotionTrack> tracks,
        DateTimeOffset startedAt)
    {
        public HavenElement Element { get; } = element;
        public string Name { get; } = name;
        public HavenAnimationKind Kind { get; } = kind;
        public TimeSpan Duration { get; } = duration;
        public TimeSpan EffectiveDuration { get; set; } = duration;
        public string Easing { get; } = easing;
        public IReadOnlyList<MotionTrack> Tracks { get; } = tracks;
        public DateTimeOffset StartedAt { get; } = startedAt;
    }
}
