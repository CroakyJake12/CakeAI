namespace Haven.UI;

/// <summary>
/// A computed realised window over a virtual dataset: the slice of indices that
/// must have realised elements right now. Total datasets may be far larger than
/// the realised window; consumers realise only <see cref="Count"/> items.
/// </summary>
/// <remarks>
/// This is the generalised form of Data's retained spreadsheet viewport
/// (DataSpreadsheetSurface): keep one authoritative total count, compute the
/// visible index window from scroll state, and rebuild realised elements only
/// when the window identity changes. Action Graph and other large-list surfaces
/// should consume this primitive instead of realising every item.
///
/// Typical usage per scroll update:
/// <code>
/// var next = HavenVirtualWindow.Compute(total, viewportHeight, rowHeight, offsetY, overscan: 6);
/// if (!next.SameWindow(current)) { RebuildRealised(next); current = next; }
/// </code>
/// </remarks>
public readonly record struct HavenVirtualWindow(int FirstIndex, int Count, int TotalCount)
{
    /// <summary>
    /// Computes the realised window for a uniform-extent list or grid axis.
    /// Non-positive extents (unknown item size or zero viewport) fall back to
    /// realising everything up to <paramref name="maximumRealized"/>, which keeps
    /// small datasets correct without special cases.
    /// </summary>
    /// <param name="totalCount">Authoritative dataset size (may be huge).</param>
    /// <param name="viewportExtent">Visible extent in DIPs on the scrolling axis.</param>
    /// <param name="itemExtent">Uniform item extent in DIPs on the scrolling axis.</param>
    /// <param name="scrollOffset">Current scroll offset in DIPs (clamped internally).</param>
    /// <param name="overscan">Extra items realised on each side for smooth scrolling.</param>
    /// <param name="maximumRealized">Safety ceiling when extents are unusable.</param>
    public static HavenVirtualWindow Compute(
        int totalCount,
        double viewportExtent,
        double itemExtent,
        double scrollOffset,
        int overscan = 4,
        int maximumRealized = 4_096)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(overscan, 0);
        if (totalCount == 0) return new HavenVirtualWindow(0, 0, 0);
        var clampedOffset = double.IsFinite(scrollOffset) ? Math.Max(0d, scrollOffset) : 0d;
        if (!double.IsFinite(viewportExtent) || viewportExtent <= 0 || !double.IsFinite(itemExtent) || itemExtent <= 0)
            return new HavenVirtualWindow(0, Math.Min(Math.Max(1, totalCount), Math.Max(1, maximumRealized)), totalCount);

        var first = (int)Math.Floor(clampedOffset / itemExtent) - overscan;
        first = Math.Clamp(first, 0, totalCount - 1);
        var visible = (int)Math.Ceiling(viewportExtent / itemExtent) + (overscan * 2);
        visible = Math.Max(1, visible);
        var count = Math.Min(visible, totalCount - first);
        return new HavenVirtualWindow(first, Math.Max(1, count), totalCount);
    }

    /// <summary>True when the index has a realised element.</summary>
    public bool Contains(int index) => index >= FirstIndex && index < FirstIndex + Count && Count > 0;

    /// <summary>True when a rebuild of realised elements can be skipped because the window did not move or resize.</summary>
    public bool SameWindow(HavenVirtualWindow other) =>
        FirstIndex == other.FirstIndex && Count == other.Count && TotalCount == other.TotalCount;

    /// <summary>Scroll offset (DIPs) that places <see cref="FirstIndex"/> at the top of the viewport.</summary>
    public double ScrollOffsetFor(double itemExtent) =>
        itemExtent > 0 ? FirstIndex * itemExtent : 0d;
}
