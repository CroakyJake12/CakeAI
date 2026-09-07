namespace Haven.UI;

[Flags]
public enum HavenElementState
{
    Default = 0,
    Hover = 1 << 0,
    Pressed = 1 << 1,
    Focused = 1 << 2,
    Selected = 1 << 3,
    Disabled = 1 << 4,
    Checked = 1 << 5,
    Expanded = 1 << 6
}

public sealed record HavenComponentMetadata(
    string ComponentName,
    string CanonicalSource,
    IReadOnlyList<string> SharedClasses,
    IReadOnlyList<string> SharedAnimations,
    string Notes);

/// <summary>Base node of the Haven-owned scene tree.</summary>
public abstract class HavenElement
{
    private readonly Dictionary<HavenProperty, Dictionary<HavenValueSource, object?>> _values = [];
    private readonly Dictionary<HavenProperty, HavenAnimationSample> _animationSamples = [];
    private readonly List<HavenElement> _children = [];
    private int _updateDepth;
    private bool _invalidationPending;
    private HavenInvalidationKinds _pendingKinds;

    public HavenElement? Parent { get; private set; }
    public IReadOnlyList<HavenElement> Children => _children;
    public virtual bool CreatesNameScope => false;
    public IList<IHavenRenderCondition> Conditions { get; } = new List<IHavenRenderCondition>();
    public IList<HavenAction> ClickActions { get; } = new List<HavenAction>();
    public HavenAccessibility Accessibility { get; } = new();
    public HavenElementState State { get; private set; }
    public HavenSize DesiredSize { get; internal set; }
    public HavenRect Bounds { get; internal set; }
    public bool IsIncluded { get; internal set; } = true;

    public event EventHandler? Invalidated;
    public event EventHandler? Invoked;
    public event EventHandler? SecondaryInvoked;

    /// <summary>
    /// Kinds accumulated by the most recent invalidation raise on this element.
    /// Valid inside the Invalidated handler; hosts must read it first because
    /// nested invalidations during reconciliation overwrite it.
    /// </summary>
    public HavenInvalidationKinds LastInvalidationKinds => _pendingKinds;

    internal event Action<HavenElement, HavenInvalidationKinds>? InvalidationRaised;

    internal void InvokeSecondary()
    {
        SecondaryInvoked?.Invoke(this, EventArgs.Empty);
        Invalidate(HavenInvalidationKinds.Paint | HavenInvalidationKinds.Layout);
    }

    /// <summary>Requests a new Haven measure/render pass after component-owned state changes.</summary>
    protected internal void Invalidate()
    {
        Invalidate(HavenInvalidationKinds.All);
    }

    /// <summary>Requests a scoped scene update so hosts can skip work that the change cannot affect.</summary>
    protected internal void Invalidate(HavenInvalidationKinds kinds)
    {
        if (kinds == HavenInvalidationKinds.None) return;
        if (_updateDepth > 0)
        {
            _invalidationPending = true;
            _pendingKinds |= kinds;
            return;
        }
        RaiseInvalidation(kinds);
    }

    private void RaiseInvalidation(HavenInvalidationKinds kinds)
    {
        _pendingKinds = kinds;
        HavenUiDiagnostics.Record(kinds);
        try
        {
            Invalidated?.Invoke(this, EventArgs.Empty);
            InvalidationRaised?.Invoke(this, kinds);
        }
        finally
        {
            _pendingKinds = HavenInvalidationKinds.None;
        }
    }

    public string? Name
    {
        get => GetValue(HavenProperties.Name);
        set => SetValue(HavenProperties.Name, value);
    }

    public string Group
    {
        get => GetValue(HavenProperties.Group);
        set => SetValue(HavenProperties.Group, value ?? string.Empty);
    }

    public string Class
    {
        get => GetValue(HavenProperties.Class);
        set => SetValue(HavenProperties.Class, value ?? string.Empty);
    }

    public virtual HavenComponentMetadata Metadata => new(
        GetType().Name,
        GetType().Name,
        [],
        [],
        "Generic Haven scene element.");

    public IReadOnlySet<string> Groups => SplitTokens(Group).ToHashSet(StringComparer.Ordinal);
    public IReadOnlySet<string> Classes => SplitTokens(Class).ToHashSet(StringComparer.Ordinal);
    public IReadOnlyList<string> GroupTokens => SplitTokens(Group);
    public IReadOnlyList<string> ClassTokens => SplitTokens(Class);

    public T GetValue<T>(HavenProperty<T> property)
    {
        var value = GetValue((HavenProperty)property);
        return value is T typed ? typed : property.DefaultValueTyped;
    }

    public object? GetValue(HavenProperty property) => GetValue(property, HavenValueSource.Animation);

    /// <summary>Returns the highest-precedence value no higher than the requested source.</summary>
    public object? GetValue(HavenProperty property, HavenValueSource maximumSource)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!_values.TryGetValue(property, out var slot) || slot.Count == 0) return property.DefaultValue;
        var sources = slot.Keys.Where(source => source <= maximumSource).ToArray();
        if (sources.Length == 0) return property.DefaultValue;
        return slot[sources.Max()];
    }

    public void SetValue<T>(HavenProperty<T> property, T value, HavenValueSource source = HavenValueSource.Explicit)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!_values.TryGetValue(property, out var slot))
        {
            slot = [];
            _values[property] = slot;
        }
        if (slot.TryGetValue(source, out var existing) && Equals(existing, value)) return;
        slot[source] = value;
        Invalidate(ClassifyChange(property));
    }

    internal void SetValue(HavenProperty property, object? value, HavenValueSource source)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (value is not null && !property.ValueType.IsInstanceOfType(value))
            throw new ArgumentException($"Value for Haven property '{property.Name}' must be {property.ValueType.Name}, not {value.GetType().Name}.", nameof(value));
        if (!_values.TryGetValue(property, out var slot))
        {
            slot = [];
            _values[property] = slot;
        }
        if (slot.TryGetValue(source, out var existing) && Equals(existing, value)) return;
        slot[source] = value;
        Invalidate(ClassifyChange(property));
    }

    public void ClearValue(HavenProperty property, HavenValueSource source)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!_values.TryGetValue(property, out var slot) || !slot.Remove(source)) return;
        if (slot.Count == 0) _values.Remove(property);
        Invalidate(ClassifyChange(property));
    }

    /// <summary>Classifies the scene impact of a component-specific property change. Shared properties are classified centrally.</summary>
    protected internal virtual HavenInvalidationKinds ClassifyValueChange(HavenProperty property) =>
        HavenInvalidationKinds.Layout | HavenInvalidationKinds.Paint;

    private HavenInvalidationKinds ClassifyChange(HavenProperty property)
    {
        if (ReferenceEquals(property, HavenProperties.Class) || ReferenceEquals(property, HavenProperties.Group))
            return HavenInvalidationKinds.Style | HavenInvalidationKinds.Layout | HavenInvalidationKinds.Paint;
        if (ReferenceEquals(property, HavenProperties.Animation) || ReferenceEquals(property, HavenProperties.Transition))
            return HavenInvalidationKinds.Motion | HavenInvalidationKinds.Style | HavenInvalidationKinds.Paint;
        if (ReferenceEquals(property, HavenProperties.Overflow))
            return HavenInvalidationKinds.Motion | HavenInvalidationKinds.Layout | HavenInvalidationKinds.Paint;
        if (IsVisualOnlyProperty(property))
            return HavenInvalidationKinds.Motion | HavenInvalidationKinds.Paint;
        return ClassifyValueChange(property);
    }

    private static bool IsVisualOnlyProperty(HavenProperty property) => ReferenceEquals(property, HavenProperties.Background)
        || ReferenceEquals(property, HavenProperties.Foreground)
        || ReferenceEquals(property, HavenProperties.Accent)
        || ReferenceEquals(property, HavenProperties.Opacity)
        || ReferenceEquals(property, HavenProperties.BorderColor)
        || ReferenceEquals(property, HavenProperties.BorderWidth)
        || ReferenceEquals(property, HavenProperties.Radius)
        || ReferenceEquals(property, HavenProperties.Shadow)
        || ReferenceEquals(property, HavenProperties.Glow)
        || ReferenceEquals(property, HavenProperties.BackdropBlur)
        || ReferenceEquals(property, HavenProperties.Scale)
        || ReferenceEquals(property, HavenProperties.Rotation)
        || ReferenceEquals(property, HavenProperties.TranslationX)
        || ReferenceEquals(property, HavenProperties.TranslationY)
        || ReferenceEquals(property, HavenProperties.TransformOrigin)
        || ReferenceEquals(property, HavenProperties.ZIndex)
        || ReferenceEquals(property, HavenProperties.Clip)
        || ReferenceEquals(property, HavenProperties.Hover)
        || ReferenceEquals(property, HavenProperties.PointerEvents)
        || ReferenceEquals(property, HavenProperties.Cursor)
        || ReferenceEquals(property, HavenProperties.Enabled);

    public void Add(HavenElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ReferenceEquals(child, this) || child.DescendantsAndSelf().Contains(this))
            throw new InvalidOperationException("A Haven element cannot contain itself or one of its ancestors.");
        if (child.Parent is not null)
            throw new InvalidOperationException("A Haven element already has a parent. Remove it before reparenting.");
        child.Parent = this;
        _children.Add(child);
        Invalidate(HavenInvalidationKinds.All);
    }

    public bool Remove(HavenElement child)
    {
        if (!_children.Remove(child)) return false;
        child.Parent = null;
        Invalidate(HavenInvalidationKinds.All);
        return true;
    }

    public void SetState(HavenElementState state, bool active)
    {
        var next = active ? State | state : State & ~state;
        if (next == State) return;
        Update(() =>
        {
            State = next;
            OnStateChanged();
            Invalidate(HavenInvalidationKinds.Motion | HavenInvalidationKinds.Paint);
        });
    }

    internal void Invoke()
    {
        Invoked?.Invoke(this, EventArgs.Empty);
        Invalidate(HavenInvalidationKinds.Paint | HavenInvalidationKinds.Layout);
    }

    protected virtual void OnStateChanged() { }

    internal bool TryGetAnimationSample(HavenProperty property, out HavenAnimationSample sample) =>
        _animationSamples.TryGetValue(property, out sample!);

    internal void SetAnimationSample(HavenProperty property, object? from, object? to, double progress)
    {
        _animationSamples[property] = new HavenAnimationSample(from, to, Math.Clamp(progress, 0d, 1d));
        Invalidate(HavenInvalidationKinds.Paint);
    }

    internal void ClearAnimationSample(HavenProperty property)
    {
        if (_animationSamples.Remove(property)) Invalidate(HavenInvalidationKinds.Paint);
    }

    internal void Update(Action update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _updateDepth++;
        try { update(); }
        finally
        {
            _updateDepth--;
            if (_updateDepth == 0 && _invalidationPending)
            {
                _invalidationPending = false;
                var kinds = _pendingKinds == HavenInvalidationKinds.None ? HavenInvalidationKinds.All : _pendingKinds;
                _pendingKinds = HavenInvalidationKinds.None;
                HavenUiDiagnostics.RecordDeferredBatch();
                RaiseInvalidation(kinds);
            }
        }
    }

    public bool MatchesConditions(HavenRenderContext context) => Conditions.All(condition => condition.Matches(context));

    public IEnumerable<HavenElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    public void ValidateUniqueNames()
    {
        ValidateNameScope(this);
    }

    private static void ValidateNameScope(HavenElement scope)
    {
        var scoped = EnumerateNameScope(scope).ToArray();
        var duplicates = scoped
            .Where(element => !string.IsNullOrWhiteSpace(element.Name))
            .GroupBy(element => element.Name!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate Haven element Name values in scope '{scope.Name ?? scope.GetType().Name}': {string.Join(", ", duplicates)}.");

        foreach (var childScope in scoped.Where(element => !ReferenceEquals(element, scope) && element.CreatesNameScope))
            ValidateNameScope(childScope);
    }

    private static IEnumerable<HavenElement> EnumerateNameScope(HavenElement element)
    {
        yield return element;
        foreach (var child in element.Children)
        {
            yield return child;
            if (child.CreatesNameScope) continue;
            foreach (var descendant in EnumerateNameScope(child).Skip(1)) yield return descendant;
        }
    }

    private static IReadOnlyList<string> SplitTokens(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

internal sealed record HavenAnimationSample(object? From, object? To, double Progress);
