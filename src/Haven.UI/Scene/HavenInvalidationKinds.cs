namespace Haven.UI;

/// <summary>
/// Categories of scene change carried by a HavenElement invalidation so hosts can
/// scope reconciliation work to what actually changed.
/// </summary>
/// <remarks>
/// Paint-only changes must never trigger structural reconciliation; only Structure
/// changes reconcile subscriptions and native hosts. Hosts may treat combinations
/// conservatively but never skip rendering for any raised kind.
/// </remarks>
[Flags]
public enum HavenInvalidationKinds
{
    None = 0,

    /// <summary>Visual-only change: repaint is sufficient.</summary>
    Paint = 1 << 0,

    /// <summary>Measure-affecting change: layout must run before paint.</summary>
    Layout = 1 << 1,

    /// <summary>Class/style membership change: shared class styles must reapply.</summary>
    Style = 1 << 2,

    /// <summary>Motion-relevant change: transitions/keyframes must be rescanned.</summary>
    Motion = 1 << 3,

    /// <summary>Tree shape change: subscriptions and native hosts must reconcile.</summary>
    Structure = 1 << 4,

    /// <summary>Conservative default used when the caller cannot classify the change.</summary>
    All = Paint | Layout | Style | Motion | Structure
}

/// <summary>
/// Lightweight counters for DEBUG-style evidence about invalidation traffic.
/// Counters are monotonic for the process lifetime unless reset by tests/diagnostics.
/// </summary>
public static class HavenUiDiagnostics
{
    private static long _paint;
    private static long _layout;
    private static long _style;
    private static long _motion;
    private static long _structure;
    private static long _raises;
    private static long _deferredBatches;

    public static long PaintInvalidations => Interlocked.Read(ref _paint);
    public static long LayoutInvalidations => Interlocked.Read(ref _layout);
    public static long StyleInvalidations => Interlocked.Read(ref _style);
    public static long MotionInvalidations => Interlocked.Read(ref _motion);
    public static long StructureInvalidations => Interlocked.Read(ref _structure);
    public static long TotalRaises => Interlocked.Read(ref _raises);
    public static long DeferredBatches => Interlocked.Read(ref _deferredBatches);

    internal static void Record(HavenInvalidationKinds kinds)
    {
        Interlocked.Increment(ref _raises);
        if ((kinds & HavenInvalidationKinds.Paint) != 0) Interlocked.Increment(ref _paint);
        if ((kinds & HavenInvalidationKinds.Layout) != 0) Interlocked.Increment(ref _layout);
        if ((kinds & HavenInvalidationKinds.Style) != 0) Interlocked.Increment(ref _style);
        if ((kinds & HavenInvalidationKinds.Motion) != 0) Interlocked.Increment(ref _motion);
        if ((kinds & HavenInvalidationKinds.Structure) != 0) Interlocked.Increment(ref _structure);
    }

    internal static void RecordDeferredBatch() => Interlocked.Increment(ref _deferredBatches);

    public static void Reset()
    {
        Interlocked.Exchange(ref _paint, 0);
        Interlocked.Exchange(ref _layout, 0);
        Interlocked.Exchange(ref _style, 0);
        Interlocked.Exchange(ref _motion, 0);
        Interlocked.Exchange(ref _structure, 0);
        Interlocked.Exchange(ref _raises, 0);
        Interlocked.Exchange(ref _deferredBatches, 0);
    }
}
