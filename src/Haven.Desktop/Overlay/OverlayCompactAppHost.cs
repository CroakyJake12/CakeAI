#if !ANDROID
namespace Haven.Desktop.Overlay;

/// <summary>
/// Compact route state for an Overlay shell. This is navigation state only;
/// production app/session backends remain owned by their existing services.
/// </summary>
internal sealed record OverlayCompactAppRoute(
    string Key,
    string Title,
    string Identity,
    bool IsRouter = false)
{
    public bool IsHome => Key.Equals("home", StringComparison.OrdinalIgnoreCase);

    public static OverlayCompactAppRoute Home { get; } =
        new("home", "Overlay home", "Ask Haven about your Screen");
}

internal sealed class OverlayCompactAppHost
{
    private readonly List<OverlayCompactAppRoute> _history = [];

    public OverlayCompactAppHost() : this(OverlayCompactAppRoute.Home)
    {
    }

    public OverlayCompactAppHost(OverlayCompactAppRoute initialRoute)
    {
        CurrentRoute = initialRoute ?? throw new ArgumentNullException(nameof(initialRoute));
    }

    public OverlayCompactAppRoute CurrentRoute { get; private set; }
    public IReadOnlyList<OverlayCompactAppRoute> History => _history;
    public bool CanNavigateBack => _history.Count > 0;

    public void InitializeFromSession(OverlayCompactAppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        _history.Clear();
        CurrentRoute = route;
    }

    public bool NavigateTo(OverlayCompactAppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (CurrentRoute == route) return false;
        _history.Add(CurrentRoute);
        CurrentRoute = route;
        return true;
    }

    public bool NavigateHome() => NavigateTo(OverlayCompactAppRoute.Home);

    public bool TryNavigateBack()
    {
        if (_history.Count == 0) return false;
        var previous = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        CurrentRoute = previous;
        return true;
    }

    public static OverlayCompactAppRoute ForSession(OverlaySessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = string.IsNullOrWhiteSpace(session.AppKey) ? "unknown" : session.AppKey.Trim();
        var title = string.IsNullOrWhiteSpace(session.Title) ? key : session.Title.Trim();
        var context = session.Context?.Provenance.SourceApplication ?? session.SourceAssociation;
        var identity = string.IsNullOrWhiteSpace(context) || context.Equals(title, StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{title} · {context.Trim()}";
        return new OverlayCompactAppRoute(
            key,
            title,
            identity,
            key.Equals("go", StringComparison.OrdinalIgnoreCase));
    }
}
#endif
