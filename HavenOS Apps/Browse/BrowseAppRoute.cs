using Haven.Browser;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Owns the standalone HavenOS Browse route while delegating browser behavior to the existing browser capability.
/// </summary>
public sealed class BrowseAppRoute
{
    public const string PrimaryRouteKey = "browse";

    private readonly BrowserSessionService _browserSession;

    public BrowseAppRoute(BrowserSessionService browserSession)
    {
        _browserSession = browserSession ?? throw new ArgumentNullException(nameof(browserSession));
    }

    public BrowserSnapshot State => _browserSession.State;

    public bool IsInteractiveAvailable => _browserSession.IsInteractiveAvailable;

    /// <summary>
    /// Matches the route aliases already accepted by the shared Haven shell policy.
    /// </summary>
    public static bool Matches(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey)) return false;

        return routeKey.Trim().ToLowerInvariant() is "browse" or "browser" or "web";
    }

    /// <summary>
    /// Opens a domain, URL, or search phrase through the existing BrowserSessionService navigation policy.
    /// </summary>
    public async Task<BrowseNavigationResult> NavigateAsync(
        string routeKey,
        string domainOrAddress,
        CancellationToken cancellationToken = default)
    {
        if (!Matches(routeKey))
        {
            throw new ArgumentException("The route key does not target the Browse app.", nameof(routeKey));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(domainOrAddress);

        var status = await _browserSession
            .NavigateAsync(domainOrAddress.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return new BrowseNavigationResult(
            _browserSession.State,
            status,
            _browserSession.IsInteractiveAvailable);
    }
}

/// <summary>
/// Reports the existing browser session state after one standalone Browse navigation request.
/// </summary>
public sealed record BrowseNavigationResult(
    BrowserSnapshot Snapshot,
    string Status,
    bool IsInteractiveAvailable);
