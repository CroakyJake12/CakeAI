using System.Collections.Immutable;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Overlay;

/// <summary>Describes what the compact Overlay can do with a routed request.</summary>
public enum OverlayCompactRouteKind
{
    Compact,
    Clarification,
    FullHaven
}

/// <summary>
/// Immutable route result consumed by the Overlay shell. A full-Haven result is
/// deliberately explicit: the compact shell never presents an unsupported app
/// as if it had a working compact surface.
/// </summary>
public sealed record OverlayCompactRouteResult(
    OverlayCompactRouteKind Kind,
    ModeDefinition? Mode,
    HavenAppRoute? Route,
    string OriginalInstruction,
    ImmutableArray<string> Attachments,
    string Reason,
    string? Clarification,
    GoRoutingContext Context)
{
    public bool IsCompact => Kind == OverlayCompactRouteKind.Compact;
    public bool RequiresClarification => Kind == OverlayCompactRouteKind.Clarification;
    public bool RequiresFullHaven => Kind == OverlayCompactRouteKind.FullHaven;
}

/// <summary>
/// Resolves Overlay requests through Haven's installed mode registry and the
/// same Go/App policies used by the full shell. It contains no UI or second app
/// registry and only advertises destinations for which a compact host exists.
/// </summary>
public sealed class OverlayCompactRouteResolver
{
    private readonly IModeRegistry _modeRegistry;

    public OverlayCompactRouteResolver(IModeRegistry modeRegistry)
    {
        _modeRegistry = modeRegistry ?? throw new ArgumentNullException(nameof(modeRegistry));
    }

    public async Task<OverlayCompactRouteResult> ResolveGoAsync(
        string instruction,
        GoRoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context = SnapshotContext(context ?? GoRoutingContext.Empty);
        var decision = GoRouteIntentPolicy.Resolve(instruction, context);

        if (decision.Destination == GoRouteDestination.Clarify)
        {
            return Clarification(decision, decision.Clarification ?? "Tell Haven what you want to do.");
        }

        var modes = await GetAvailableModesAsync(cancellationToken).ConfigureAwait(false);
        if (decision.Destination == GoRouteDestination.Project)
        {
            return FullHaven(
                decision,
                "Project work opens in full Haven so the project workspace and its tools remain available.");
        }

        var targetKey = decision.Destination == GoRouteDestination.Chat ? "chat" : decision.TargetKey;
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            return FullHaven(decision, "Haven could not identify a registered destination for this request.");
        }

        return ResolveRegisteredMode(decision.Instruction, decision.Context, targetKey, modes, decision);
    }

    /// <summary>
    /// Resolves a shortcut using the installed registry. This is intentionally
    /// separate from Go text routing so a shortcut cannot bypass availability or
    /// the common full-Haven escape policy.
    /// </summary>
    public async Task<OverlayCompactRouteResult> ResolveShortcutAsync(
        string appKey,
        string? instruction = null,
        GoRoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context = SnapshotContext(context ?? GoRoutingContext.Empty);
        var normalizedKey = appKey?.Trim() ?? string.Empty;
        var originalInstruction = string.IsNullOrWhiteSpace(instruction)
            ? $"Open {normalizedKey}"
            : instruction;
        var decision = new GoRouteDecision(GoRouteDestination.App, originalInstruction, context, normalizedKey);

        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return Clarification(decision, "Choose an installed Haven app to open.");
        }

        var registered = await _modeRegistry.GetModeByKeyAsync(normalizedKey, cancellationToken).ConfigureAwait(false);
        if (registered is null || !registered.IsEnabled)
        {
            return FullHaven(decision, $"The Haven app '{normalizedKey}' is not installed or enabled.");
        }

        var modes = await GetAvailableModesAsync(cancellationToken).ConfigureAwait(false);
        // A registry implementation may expose a shortcut through its key
        // lookup before its inventory projection has refreshed. Keep the real
        // definition in the same policy input without inventing a placeholder.
        if (modes.All(mode => mode.Id != registered.Id))
        {
            modes = modes.Append(registered).ToArray();
        }

        return ResolveRegisteredMode(originalInstruction, context, normalizedKey, modes, decision);
    }

    private async Task<IReadOnlyList<ModeDefinition>> GetAvailableModesAsync(CancellationToken cancellationToken)
    {
        var modes = await _modeRegistry.GetModesAsync(cancellationToken).ConfigureAwait(false);
        return modes
            .Where(mode => mode.IsEnabled)
            .GroupBy(mode => mode.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static OverlayCompactRouteResult ResolveRegisteredMode(
        string instruction,
        GoRoutingContext context,
        string targetKey,
        IReadOnlyList<ModeDefinition> modes,
        GoRouteDecision decision)
    {
        var mode = modes.FirstOrDefault(item => item.Key.Equals(targetKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (mode is null)
        {
            return FullHaven(decision, $"The Haven app '{targetKey.Trim()}' is not installed or enabled.");
        }

        var route = HavenAppRoutePolicy.Resolve(mode);
        if (IsCompactCapable(mode, route))
        {
            return Compact(instruction, context, mode, route, $"Route {mode.Name} in the compact Haven Overlay.");
        }

        return FullHaven(
            decision,
            $"{mode.Name} is registered, but it does not have a compact Overlay surface; open it in full Haven.",
            mode,
            route);
    }

    private static bool IsCompactCapable(
        ModeDefinition mode,
        HavenAppRoute route)
    {
        var key = mode.Key.Trim();
        if (key.Equals("chat", StringComparison.OrdinalIgnoreCase)
            && route.Surface == HavenSurface.Chat)
        {
            return true;
        }

        if (key.Equals("go", StringComparison.OrdinalIgnoreCase)
            && route.Kind == HavenAppRouteKind.Go)
        {
            return true;
        }

        if (key.Equals("translate", StringComparison.OrdinalIgnoreCase)
            && route.Kind == HavenAppRouteKind.Translate)
        {
            return true;
        }

        if (key.Equals("vision", StringComparison.OrdinalIgnoreCase)
            && route.Kind == HavenAppRouteKind.Vision)
        {
            return true;
        }

        if (key.Equals("tasks", StringComparison.OrdinalIgnoreCase)
            && route.Surface == HavenSurface.Tasks)
        {
            return true;
        }

        // Calculator is the only utility with a real compact Overlay surface.
        // A generic utility tag is metadata, not evidence that a compact host
        // exists, so other tagged modes must escape to full Haven.
        return mode.Key.Equals("calculator", StringComparison.OrdinalIgnoreCase)
               && IsLowRiskUtility(mode);
    }

    private static bool IsLowRiskUtility(ModeDefinition mode)
    {
        if (!mode.Key.Equals("calculator", StringComparison.OrdinalIgnoreCase)
            || mode.BaseMode != HavenMode.Chat
            || !HasUtilityTag(mode.TagsJson))
        {
            return false;
        }

        // A compact utility is only eligible when its manifest advertises no
        // command, connector, or other capability that would expand Overlay's
        // execution scope. The actual utility remains registry-owned.
        return IsEmptyJsonArray(mode.ToolAllowlistJson)
               && IsEmptyJsonArray(mode.ToolDenylistJson)
               && IsEmptyJsonArray(mode.CapabilitiesJson);
    }

    private static bool HasUtilityTag(string tagsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(tagsJson ?? "[]");
            return document.RootElement.ValueKind == JsonValueKind.Array
                   && document.RootElement.EnumerateArray().Any(tag =>
                       tag.ValueKind == JsonValueKind.String
                       && tag.GetString()!.Equals("utility", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsEmptyJsonArray(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "[]");
            return document.RootElement.ValueKind == JsonValueKind.Array
                   && document.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static OverlayCompactRouteResult Compact(
        string instruction,
        GoRoutingContext context,
        ModeDefinition mode,
        HavenAppRoute route,
        string reason)
        => new(
            OverlayCompactRouteKind.Compact,
            mode,
            route,
            instruction,
            ImmutableArray.CreateRange(context.AttachmentPaths),
            reason,
            null,
            context);

    private static OverlayCompactRouteResult Clarification(GoRouteDecision decision, string clarification)
        => new(
            OverlayCompactRouteKind.Clarification,
            null,
            null,
            decision.Instruction,
            ImmutableArray.CreateRange(decision.Context.AttachmentPaths),
            "The request needs clarification before Haven can choose a destination.",
            clarification,
            decision.Context);

    private static OverlayCompactRouteResult FullHaven(GoRouteDecision decision, string reason)
        => FullHaven(decision, reason, null, null);

    private static OverlayCompactRouteResult FullHaven(
        GoRouteDecision decision,
        string reason,
        ModeDefinition? mode,
        HavenAppRoute? route)
        => new(
            OverlayCompactRouteKind.FullHaven,
            mode,
            route,
            decision.Instruction,
            ImmutableArray.CreateRange(decision.Context.AttachmentPaths),
            reason,
            null,
            decision.Context);

    private static GoRoutingContext SnapshotContext(GoRoutingContext context)
        => new(context.AttachmentPaths.ToArray(), context.ProjectNames.ToArray());
}
