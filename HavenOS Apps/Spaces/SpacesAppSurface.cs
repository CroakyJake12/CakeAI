using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces;

/// <summary>
/// The first HavenOS-owned Spaces destinations. The host keeps shell routing at the OS root;
/// this app surface only selects existing Haven modes and Space records.
/// </summary>
public enum SpacesDestination
{
    Home,
    Chat,
    Study,
    Tasks,
    Research
}

public sealed record SpacesNavigationItem(
    SpacesDestination Destination,
    string Label,
    string IconKey);

/// <summary>
/// Adapter implemented by the HavenOS shell. Existing shell/app launch services remain the source
/// of truth for how a mode or configured Space is actually presented.
/// </summary>
public interface ISpacesNavigationHost
{
    Task OpenHomeAsync(CancellationToken cancellationToken = default);
    Task OpenModeAsync(HavenMode mode, CancellationToken cancellationToken = default);
    Task OpenSpaceAsync(SpaceDefinition space, CancellationToken cancellationToken = default);
}

/// <summary>
/// Platform-neutral navigation surface for the HavenOS Spaces app.
/// </summary>
public sealed class SpacesAppSurface
{
    private static readonly IReadOnlyList<SpacesNavigationItem> NavigationItems = Array.AsReadOnly<SpacesNavigationItem>(
    [
        new(SpacesDestination.Home, "Home", "home"),
        new(SpacesDestination.Chat, "Chat", "chat"),
        new(SpacesDestination.Study, "Study", "book"),
        new(SpacesDestination.Tasks, "Tasks", "tasks"),
        new(SpacesDestination.Research, "Research", "search")
    ]);

    private readonly SpaceRegistry _spaces;
    private readonly ISpacesNavigationHost _host;

    public SpacesAppSurface(SpaceRegistry spaces, ISpacesNavigationHost host)
    {
        _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public static IReadOnlyList<SpacesNavigationItem> Navigation => NavigationItems;

    public SpacesDestination CurrentDestination { get; private set; } = SpacesDestination.Home;

    public event Action<SpacesDestination>? DestinationChanged;

    public async Task NavigateAsync(
        SpacesDestination destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (destination)
        {
            case SpacesDestination.Home:
                await _host.OpenHomeAsync(cancellationToken).ConfigureAwait(false);
                break;
            case SpacesDestination.Chat:
                await NavigateWithScopeAsync(
                    nextSpaceId: null,
                    token => _host.OpenModeAsync(HavenMode.Chat, token),
                    cancellationToken).ConfigureAwait(false);
                break;
            case SpacesDestination.Study:
                await NavigateToBuiltInSpaceAsync(SpaceRegistry.StudySpaceId, cancellationToken).ConfigureAwait(false);
                break;
            case SpacesDestination.Tasks:
                // The existing Agent built-in Space is intentionally used here. SpaceLaunchPolicy
                // already maps that Space kind onto HavenMode.Tasks.
                await NavigateToBuiltInSpaceAsync(SpaceRegistry.AgentSpaceId, cancellationToken).ConfigureAwait(false);
                break;
            case SpacesDestination.Research:
                await NavigateToBuiltInSpaceAsync(SpaceRegistry.ResearchSpaceId, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(destination), destination, "Unknown Spaces destination.");
        }

        CurrentDestination = destination;
        DestinationChanged?.Invoke(destination);
    }

    private async Task NavigateToBuiltInSpaceAsync(Guid spaceId, CancellationToken cancellationToken)
    {
        var space = await _spaces.GetAsync(spaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Required built-in Space '{spaceId}' is unavailable.");

        await NavigateWithScopeAsync(
            space.Id,
            token => _host.OpenSpaceAsync(space, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task NavigateWithScopeAsync(
        Guid? nextSpaceId,
        Func<CancellationToken, Task> launch,
        CancellationToken cancellationToken)
    {
        var previousSpaceId = await _spaces.GetCurrentSpaceIdAsync(cancellationToken).ConfigureAwait(false);
        var scopeChanged = previousSpaceId != nextSpaceId;

        if (scopeChanged)
            await _spaces.SetCurrentSpaceIdAsync(nextSpaceId, cancellationToken).ConfigureAwait(false);

        try
        {
            await launch(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception launchError)
        {
            if (!scopeChanged)
                throw;

            try
            {
                await _spaces.SetCurrentSpaceIdAsync(previousSpaceId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Spaces navigation failed and the previous Space scope could not be restored.",
                    launchError,
                    rollbackError);
            }

            throw;
        }
    }
}
