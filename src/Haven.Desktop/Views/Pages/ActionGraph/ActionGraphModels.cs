using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.ActionGraph;

/// <summary>Semantic grouping of one execution action for colour-coding, icons and the legend.</summary>
public enum ActionGraphCategory
{
    Input = 0,
    Planning = 1,
    Thinking = 2,
    Search = 3,
    Tool = 4,
    AppsAndAgents = 5,
    Recovery = 6,
    Blocked = 7,
    Output = 8
}

public enum ActionGraphLinkKind { Flow = 0, Recovery = 1 }

/// <summary>Retained-surface node built from one collapsed authoritative ExecutionEvent.</summary>
public sealed record ActionGraphNode(
    Guid ActionId,
    string Name,
    string TypeLabel,
    string Summary,
    string ComponentId,
    ExecutionActionStatus Status,
    ActionGraphCategory Category,
    DateTimeOffset? StartedAt,
    TimeSpan? Duration,
    bool IsRetry)
{
    /// <summary>Presentation-only slot index assigned while building the graph.</summary>
    public int Ordinal { get; init; }
    /// <summary>World-space x of the node slot; derived from real timestamps when they exist.</summary>
    public double SlotX { get; init; }
}

public sealed record ActionGraphLink(Guid FromActionId, Guid ToActionId, ActionGraphLinkKind Kind);

/// <summary>A labelled mark on the horizontal time ruler. Labels come from real event timestamps only.</summary>
public sealed record ActionGraphRulerTick(double SlotX, string PrimaryLabel, string SecondaryLabel);

/// <summary>Everything the retained surface needs to lay out and draw one execution.</summary>
public sealed record ActionGraphModel(
    IReadOnlyList<ActionGraphNode> Nodes,
    IReadOnlyList<ActionGraphLink> Links,
    IReadOnlyList<ActionGraphRulerTick> RulerTicks,
    bool TimeMode,
    double ExtentWidth)
{
    public static ActionGraphModel Empty { get; } = new([], [], [], false, 0);
}

/// <summary>The five header summary cards, always computed from real events.</summary>
public sealed record ActionGraphMetrics(
    int StepsExecuted,
    int StepsCompleted,
    int ToolsCalled,
    int AppsUsed,
    int Retries,
    TimeSpan? TotalTime)
{
    public int CompletedPercent => StepsExecuted == 0 ? 0 : (int)Math.Round(100d * StepsCompleted / StepsExecuted);
}

public static class ActionGraphCatalog
{
    public static ActionGraphCategory Categorize(ExecutionActionType type) => type switch
    {
        ExecutionActionType.UserPrompt or ExecutionActionType.Steer or ExecutionActionType.Queue
            or ExecutionActionType.ContextChanged or ExecutionActionType.InstructionsLoaded => ActionGraphCategory.Input,
        ExecutionActionType.Planning or ExecutionActionType.Replan or ExecutionActionType.Resume => ActionGraphCategory.Planning,
        ExecutionActionType.ReasoningSummary or ExecutionActionType.ModelExecution or ExecutionActionType.JudgeEvaluated => ActionGraphCategory.Thinking,
        ExecutionActionType.Search => ActionGraphCategory.Search,
        ExecutionActionType.ToolCall or ExecutionActionType.ToolResult or ExecutionActionType.FileAction
            or ExecutionActionType.Preview or ExecutionActionType.ProjectAction
            or ExecutionActionType.CheckpointCreated or ExecutionActionType.CheckpointRestored => ActionGraphCategory.Tool,
        ExecutionActionType.AppCall or ExecutionActionType.PluginCall or ExecutionActionType.McpCall
            or ExecutionActionType.ConnectorCall or ExecutionActionType.ExternalAgent => ActionGraphCategory.AppsAndAgents,
        ExecutionActionType.Retry or ExecutionActionType.AutomaticDiagnosis or ExecutionActionType.AutomaticRepair
            or ExecutionActionType.ModelFallback => ActionGraphCategory.Recovery,
        ExecutionActionType.PermissionDenied or ExecutionActionType.UserActionRequired
            or ExecutionActionType.Warning or ExecutionActionType.Error => ActionGraphCategory.Blocked,
        _ => ActionGraphCategory.Output
    };

    public static string CategoryName(ActionGraphCategory category) => category switch
    {
        ActionGraphCategory.Input => "Input",
        ActionGraphCategory.Planning => "Planning",
        ActionGraphCategory.Thinking => "Thinking",
        ActionGraphCategory.Search => "Search",
        ActionGraphCategory.Tool => "Tools",
        ActionGraphCategory.AppsAndAgents => "Apps & agents",
        ActionGraphCategory.Recovery => "Recovery",
        ActionGraphCategory.Blocked => "Blocked",
        _ => "Output"
    };

    /// <summary>Semantic token for the category. Only theme-managed tokens are used.</summary>
    public static string CategoryToken(ActionGraphCategory category) => category switch
    {
        ActionGraphCategory.Input => "Accent",
        ActionGraphCategory.Planning => "AccentMuted",
        ActionGraphCategory.Thinking => "AccentSecondary",
        ActionGraphCategory.Search => "HavenInformationBrush",
        ActionGraphCategory.Tool => "HavenSuccessBrush",
        ActionGraphCategory.AppsAndAgents => "HavenLinkBrush",
        ActionGraphCategory.Recovery => "Warning",
        ActionGraphCategory.Blocked => "Danger",
        _ => "Accent"
    };

    public static string CategoryIcon(ActionGraphCategory category) => category switch
    {
        ActionGraphCategory.Input => "prompt",
        ActionGraphCategory.Planning => "plan",
        ActionGraphCategory.Thinking => "cpu",
        ActionGraphCategory.Search => "search",
        ActionGraphCategory.Tool => "build",
        ActionGraphCategory.AppsAndAgents => "globe",
        ActionGraphCategory.Recovery => "refresh",
        ActionGraphCategory.Blocked => "bell",
        _ => "check"
    };

    /// <summary>Status dot / border token; distinct from the category fill.</summary>
    public static string StatusToken(ExecutionActionStatus status) => status switch
    {
        ExecutionActionStatus.Completed => "HavenSuccessBrush",
        ExecutionActionStatus.Failed => "Danger",
        ExecutionActionStatus.Warning or ExecutionActionStatus.UserActionRequired or ExecutionActionStatus.Blocked => "Warning",
        ExecutionActionStatus.Running => "Accent",
        ExecutionActionStatus.Cancelled or ExecutionActionStatus.Superseded => "TextMuted",
        _ => "Border"
    };

    public static string DescribeStatus(ExecutionActionStatus status) => status switch
    {
        ExecutionActionStatus.PendingSafeBoundary => "Pending safe boundary",
        ExecutionActionStatus.UserActionRequired => "User action required",
        _ => status.ToString()
    };
}

/// <summary>Builds the graph model, metrics and export payload from authoritative events. Pure and unit-testable.</summary>
public static class ActionGraphProjection
{
    private const double SlotWidth = 250;
    private const double RulerTickTarget = 7;

    public static IReadOnlyList<ExecutionEvent> Collapse(IReadOnlyList<ExecutionEvent> events) => events
        .GroupBy(item => item.ActionId)
        .Select(group => group.OrderByDescending(item => item.Timestamp).First())
        .OrderBy(item => item.StartedAt ?? item.Timestamp)
        .ThenBy(item => item.Timestamp)
        .ToArray();

    public static ActionGraphModel BuildGraph(IReadOnlyList<ExecutionEvent> rawEvents)
    {
        var events = Collapse(rawEvents);
        if (events.Count == 0) return ActionGraphModel.Empty;

        var times = events.Select(item => item.StartedAt ?? item.Timestamp).Distinct().Order().ToArray();
        var timeMode = times.Length >= 2;
        var slotByTime = new Dictionary<DateTimeOffset, double>(times.Length);
        for (var index = 0; index < times.Length; index++) slotByTime[times[index]] = index * SlotWidth;

        var nodes = new List<ActionGraphNode>(events.Count);
        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            var start = item.StartedAt ?? item.Timestamp;
            var slot = timeMode ? slotByTime[start] : index * SlotWidth;
            nodes.Add(new ActionGraphNode(
                item.ActionId,
                string.IsNullOrWhiteSpace(item.Name) ? item.ActionType.ToString() : item.Name,
                FriendlyType(item.ActionType),
                FirstLine(item.SafeReasoningSummary ?? item.SafeDetail ?? item.Failure?.Title ?? string.Empty),
                item.ComponentId ?? string.Empty,
                item.Status,
                ActionGraphCatalog.Categorize(item.ActionType),
                item.StartedAt ?? item.Timestamp,
                item.Duration,
                item.ActionType == ExecutionActionType.Retry || item.RetryOfActionId is not null)
            {
                Ordinal = index,
                SlotX = slot
            });
        }

        var byId = events.ToDictionary(item => item.ActionId);
        var links = new List<ActionGraphLink>();
        foreach (var item in events)
        {
            if (item.ParentActionId is { } parent && byId.ContainsKey(parent) && parent != item.ActionId)
                links.Add(new ActionGraphLink(parent, item.ActionId, ActionGraphLinkKind.Flow));
            var recoverySource = item.RetryOfActionId ?? item.RecoveryOfActionId;
            if (recoverySource is { } source && source != item.ParentActionId && byId.ContainsKey(source) && source != item.ActionId)
                links.Add(new ActionGraphLink(source, item.ActionId, ActionGraphLinkKind.Recovery));
        }

        var ticks = BuildRuler(events, times, timeMode, slotByTime);
        var extent = timeMode ? (times.Length - 1) * SlotWidth : (events.Count - 1) * SlotWidth;
        return new ActionGraphModel(nodes, links, ticks, timeMode, extent);
    }

    private static List<ActionGraphRulerTick> BuildRuler(
        IReadOnlyList<ExecutionEvent> events,
        DateTimeOffset[] times,
        bool timeMode,
        Dictionary<DateTimeOffset, double> slotByTime)
    {
        var ticks = new List<ActionGraphRulerTick>();
        if (!timeMode)
        {
            var stride = Math.Max(1, (int)Math.Ceiling(events.Count / (double)RulerTickTarget));
            for (var index = 0; index < events.Count; index += stride)
                ticks.Add(new ActionGraphRulerTick(index * SlotWidth, $"Step {index + 1}", string.Empty));
            return ticks;
        }
        var strideTime = Math.Max(1, (int)Math.Ceiling(times.Length / (double)RulerTickTarget));
        var origin = times[0];
        for (var index = 0; index < times.Length; index += strideTime)
        {
            var time = times[index];
            ticks.Add(new ActionGraphRulerTick(
                slotByTime[time],
                time.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                OffsetLabel(time - origin)));
        }
        return ticks;
    }

    public static ActionGraphMetrics ComputeMetrics(ExecutionSummary? summary, IReadOnlyList<ExecutionEvent> rawEvents)
    {
        var events = Collapse(rawEvents);
        var tools = events.Count(item => item.ActionType is ExecutionActionType.ToolCall
            or ExecutionActionType.PluginCall or ExecutionActionType.McpCall or ExecutionActionType.ConnectorCall);
        var apps = events.Where(item => item.ActionType is ExecutionActionType.AppCall or ExecutionActionType.ConnectorCall
                or ExecutionActionType.McpCall or ExecutionActionType.PluginCall or ExecutionActionType.ExternalAgent)
            .Select(item => item.ComponentId)
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var retries = events.Count(item => item.ActionType == ExecutionActionType.Retry || item.RetryOfActionId is not null);
        TimeSpan? total = summary is { } value && value.Duration > TimeSpan.Zero ? value.Duration : null;
        total ??= Span(events);
        return new ActionGraphMetrics(events.Count, events.Count(item => item.Status == ExecutionActionStatus.Completed), tools, apps, retries, total);
    }

    private static TimeSpan? Span(IReadOnlyList<ExecutionEvent> events)
    {
        if (events.Count < 2) return null;
        var start = events.Min(item => item.StartedAt ?? item.Timestamp);
        var end = events.Max(item => item.EndedAt ?? item.Timestamp);
        return end > start ? end - start : null;
    }

    public static string BuildExportJson(
        ExecutionSummary? summary,
        IReadOnlyList<ExecutionEvent> rawEvents,
        ActionGraphModel graph,
        ActionGraphMetrics metrics,
        DateTimeOffset generatedAt)
    {
        var events = Collapse(rawEvents).ToDictionary(item => item.ActionId);
        var payload = new ActionGraphExportPayload
        {
            Schema = "haven.action-graph.export/v1",
            GeneratedAt = generatedAt,
            Execution = summary is null ? null : new ExportExecution(
                summary.ExecutionId,
                summary.PromptSummary,
                summary.Origin.ToString(),
                summary.Status.ToString(),
                summary.StartedAt,
                summary.UpdatedAt,
                (long)summary.Duration.TotalMilliseconds),
            Metrics = new ExportMetrics(metrics.StepsExecuted, metrics.StepsCompleted, metrics.ToolsCalled, metrics.AppsUsed, metrics.Retries,
                metrics.TotalTime is { } time ? (long)time.TotalMilliseconds : null),
            Nodes = graph.Nodes.Select(node =>
            {
                var source = events.GetValueOrDefault(node.ActionId);
                return new ExportNode(
                    node.ActionId,
                    source?.ParentActionId,
                    source?.RetryOfActionId,
                    source?.RecoveryOfActionId,
                    source?.ActionType.ToString(),
                    ActionGraphCatalog.CategoryName(node.Category),
                    node.Name,
                    node.Status.ToString(),
                    node.StartedAt,
                    node.Duration is { } duration ? (long)duration.TotalMilliseconds : null,
                    node.ComponentId.Length == 0 ? null : node.ComponentId,
                    source?.SafeReasoningSummary,
                    source?.SafeDetail,
                    source?.Failure is { } failure ? new ExportFailure(failure.Code, failure.Title, failure.Message, failure.Attempt, failure.Recovered) : null,
                    source?.SafeMetadata is { } metadata && metadata.Count > 0 ? metadata : null);
            }).ToArray(),
            Links = graph.Links.Select(link => new ExportLink(link.FromActionId, link.ToActionId, link.Kind.ToString())).ToArray()
        };
        return JsonSerializer.Serialize(payload, ExportJsonOptions);
    }

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string FriendlyType(ExecutionActionType type) => type switch
    {
        ExecutionActionType.UserPrompt => "User prompt",
        ExecutionActionType.ReasoningSummary => "Reasoning",
        ExecutionActionType.ModelExecution => "Model",
        ExecutionActionType.ToolCall => "Tool call",
        ExecutionActionType.ToolResult => "Tool result",
        ExecutionActionType.AppCall => "App call",
        ExecutionActionType.PluginCall => "Plugin call",
        ExecutionActionType.McpCall => "MCP call",
        ExecutionActionType.ConnectorCall => "Connector call",
        ExecutionActionType.ExternalAgent => "External agent",
        ExecutionActionType.ProjectAction => "Project action",
        ExecutionActionType.FileAction => "File action",
        ExecutionActionType.AutomaticDiagnosis => "Diagnosis",
        ExecutionActionType.AutomaticRepair => "Repair",
        ExecutionActionType.UserActionRequired => "User action required",
        ExecutionActionType.FinalResponse => "Final response",
        ExecutionActionType.ModelFallback => "Model fallback",
        ExecutionActionType.PermissionDenied => "Permission denied",
        ExecutionActionType.InstructionsLoaded => "Instructions loaded",
        ExecutionActionType.CheckpointCreated => "Checkpoint created",
        ExecutionActionType.CheckpointRestored => "Checkpoint restored",
        ExecutionActionType.ContextChanged => "Context changed",
        ExecutionActionType.JudgeEvaluated => "Judge evaluated",
        ExecutionActionType.UpdateStaged => "Update staged",
        _ => type.ToString()
    };

    private static string FirstLine(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return string.Empty;
        var breakAt = trimmed.IndexOfAny(['\r', '\n']);
        var line = breakAt < 0 ? trimmed : trimmed[..breakAt].TrimEnd();
        return line.Length <= 96 ? line : line[..95] + "…";
    }

    public static string OffsetLabel(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero) return string.Empty;
        if (offset.TotalSeconds < 1) return $"{offset.TotalMilliseconds:0}ms";
        if (offset.TotalMinutes < 1) return $"+{offset.TotalSeconds:0.#}s";
        if (offset.TotalHours < 1) return $"+{(int)offset.TotalMinutes}m{offset.Seconds:00}s";
        return $"+{(int)offset.TotalHours}h{offset.Minutes:00}m";
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1) return "—";
        if (duration.TotalMinutes >= 1) return $"{duration.TotalMinutes:0.#}m";
        if (duration.TotalSeconds >= 1) return $"{duration.TotalSeconds:0.#}s";
        return $"{duration.TotalMilliseconds:0}ms";
    }

    public static string FormatRelative(DateTimeOffset time)
    {
        var delta = DateTimeOffset.UtcNow - time;
        return delta switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)delta.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)delta.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)delta.TotalDays}d ago",
            _ => time.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)
        };
    }

    private sealed record ActionGraphExportPayload(
        string Schema,
        DateTimeOffset GeneratedAt,
        ExportExecution? Execution,
        ExportMetrics Metrics,
        IReadOnlyList<ExportNode> Nodes,
        IReadOnlyList<ExportLink> Links);

    private sealed record ExportExecution(
        Guid ExecutionId,
        string PromptSummary,
        string Origin,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset UpdatedAt,
        long DurationMs);

    private sealed record ExportMetrics(
        int StepsExecuted,
        int StepsCompleted,
        int ToolsCalled,
        int AppsUsed,
        int Retries,
        long? TotalTimeMs);

    private sealed record ExportNode(
        Guid ActionId,
        Guid? ParentActionId,
        Guid? RetryOfActionId,
        Guid? RecoveryOfActionId,
        string? ActionType,
        string Category,
        string Name,
        string Status,
        DateTimeOffset? StartedAt,
        long? DurationMs,
        string? ComponentId,
        string? SafeReasoningSummary,
        string? SafeDetail,
        ExportFailure? Failure,
        IReadOnlyDictionary<string, string>? SafeMetadata);

    private sealed record ExportFailure(string Code, string Title, string Message, int Attempt, bool Recovered);

    private sealed record ExportLink(Guid FromActionId, Guid ToActionId, string Kind);
}
