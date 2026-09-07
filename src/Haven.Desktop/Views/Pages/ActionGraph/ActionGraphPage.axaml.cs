using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.ActionGraph;

/// <summary>
/// Adapter between the persisted execution-trace services and the Action Graph HUI scene.
/// Graph and list are projections over the same authoritative events; feedback, remediation
/// and Fix-with-AI keep their original behaviour.
/// </summary>
public sealed partial class ActionGraphPage : UserControl, IDisposable
{
    private const int DefaultHistoryLimit = 120;
    private const int FullHistoryLimit = 500;
    private const int ListPageSize = 120;

    private readonly ExecutionTraceService _traces;
    private readonly IActionFeedbackRepository _feedback;
    private readonly IRemediationRepository _remediations;
    private readonly RemediationCoordinator _remediationCoordinator;
    private readonly ExecutionEventHub? _hub;
    private readonly Action<string> _fixWithAi;
    private readonly Action _openSettings;
    private readonly ActionGraphHavenScene _scene = new();
    private readonly ActionGraphSurface _surface = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _liveRefreshTimer;
    private IReadOnlyList<ExecutionSummary> _summaries = [];
    private IReadOnlyList<ExecutionEvent> _events = [];
    private ActionGraphModel _model = ActionGraphModel.Empty;
    private ActionGraphMetrics _metrics = new(0, 0, 0, 0, 0, null);
    private IReadOnlyList<ActionGraphNode> _visibleNodes = [];
    private ExecutionSummary? _selectedExecution;
    private Guid? _selectedActionId;
    private ActionFeedback? _selectedFeedback;
    private int _historyLimit = DefaultHistoryLimit;
    private int _listRowsShown;
    private bool _graphView = true;
    private bool _disposed;
    private int _liveRefreshQueued;

    public ActionGraphPage(
        ExecutionTraceService traces,
        IActionFeedbackRepository feedback,
        IRemediationRepository remediations,
        RemediationCoordinator remediationCoordinator,
        ExecutionEventHub? hub,
        Action<string> fixWithAi,
        Action openSettings)
    {
        _traces = traces;
        _feedback = feedback;
        _remediations = remediations;
        _remediationCoordinator = remediationCoordinator;
        _hub = hub;
        _fixWithAi = fixWithAi;
        _openSettings = openSettings;

        InitializeComponent();
        Scene.Root = _scene.Root;

        // The retained surface must paint and hit-test beneath the zoom overlay cluster.
        _scene.GraphOverlay.Children.ToList().ForEach(child => child.Parent?.Remove(child));
        _scene.GraphOverlay.Add(_surface);
        _scene.GraphOverlay.Add(_scene.EmptyStateText);
        _scene.GraphOverlay.Add(_scene.ZoomCluster);

        WireScene();
        _liveRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _liveRefreshTimer.Tick += RefreshLiveTrace;
        if (_hub is not null) _hub.Published += OnEventPublished;
        SizeChanged += (_, args) => _scene.SetCompact(args.NewSize.Width < 980);
        _ = LoadHistoryAsync();
    }

    internal ActionGraphHavenScene Scene => _scene;
    internal ActionGraphSurface Surface => _surface;
    internal HavenSceneControl SceneHost => Scene;

    private void WireScene()
    {
        _scene.GraphViewButton.Invoked += (_, _) => SetViewMode(true);
        _scene.ListViewButton.Invoked += (_, _) => SetViewMode(false);
        _scene.ExportButton.Invoked += (_, _) => _ = ExportAsync();
        _scene.LiveSelect.SelectionChanged += (_, _) => ApplyLiveMode();
        _scene.HistorySearch.TextChanged += (_, _) => RenderHistory();
        _scene.ViewAllHistoryButton.Invoked += (_, _) => _ = LoadFullHistoryAsync();
        _scene.ZoomInButton.Invoked += (_, _) => _surface.SetZoom(_surface.Zoom * 1.15);
        _scene.ZoomOutButton.Invoked += (_, _) => _surface.SetZoom(_surface.Zoom / 1.15);
        _scene.FitButton.Invoked += (_, _) => { if (_surface.FitToContent()) _scene.SetZoomPercent(_surface.Zoom); };
        _scene.CollapseDetailsButton.Invoked += (_, _) => _scene.SetDetailsCollapsed(!_scene.IsDetailsCollapsed());
        _scene.ThumbUpButton.Invoked += (_, _) => _ = SaveRatingAsync(ActionFeedbackRating.Positive);
        _scene.ThumbDownButton.Invoked += (_, _) => _ = SaveRatingAsync(ActionFeedbackRating.Negative);
        _scene.SaveCommentButton.Invoked += (_, _) => _ = SaveCommentAsync();
        _scene.DeleteFeedbackButton.Invoked += (_, _) => _ = DeleteFeedbackAsync();
        _surface.SelectionChanged += OnSurfaceSelectionChanged;
        _surface.ViewportChanged += () =>
        {
            if (_graphView) _scene.SetZoomPercent(_surface.Zoom);
        };
    }

    // ---------- History sidebar ----------

    private async Task LoadHistoryAsync()
    {
        try
        {
            var summaries = await _traces.SearchAsync(null, _historyLimit, _lifetime.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _summaries = summaries;
                RenderHistory();
                _scene.SetStatus(_summaries.Count == 0
                    ? "No executions recorded yet. Run a task or conversation and return here for its execution graph."
                    : $"{_summaries.Count} recent execution{(_summaries.Count == 1 ? string.Empty : "s")} loaded.");
                if (_selectedExecution is null && _summaries.Count > 0) _ = SelectExecutionAsync(_summaries[0]);
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task LoadFullHistoryAsync()
    {
        if (_historyLimit >= FullHistoryLimit)
        {
            _scene.SetStatus($"Full history window already loaded ({_summaries.Count} executions).");
            return;
        }
        _historyLimit = FullHistoryLimit;
        await LoadHistoryAsync();
        _scene.SetStatus($"Loaded the full history window: {_summaries.Count} executions.");
    }

    private void RenderHistory()
    {
        _scene.HistoryList.Children.ToList().ForEach(child => child.Parent?.Remove(child));
        var query = _scene.HistorySearch.Text?.Trim();
        var matches = _summaries
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.PromptSummary.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var summary in matches)
        {
            var selected = _selectedExecution?.ExecutionId == summary.ExecutionId;
            var row = new Container { Name = $"ActionGraph.History.{summary.ExecutionId:N}", Layout = HavenLayout.Vertical };
            row.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
            row.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
            row.SetValue(HavenProperties.Background, selected ? "AccentSubtle" : "Transparent");
            row.SetValue(HavenProperties.BorderColor, "Accent");
            row.SetValue(HavenProperties.BorderWidth, HavenLength.Px(selected ? 1 : 0));
            row.Accessibility.Role = HavenAccessibleRole.ListItem;
            row.Accessibility.AccessibleName =
                $"{summary.PromptSummary}, {ActionGraphProjection.FormatRelative(summary.UpdatedAt)}, {summary.ActionCount} steps";
            row.Accessibility.Focusable = true;
            var title = new HavenText(Truncate(summary.PromptSummary, 64)) { Level = TextLevel.Caption };
            title.SetValue(HavenProperties.FontWeight, 700);
            var meta = new HavenText($"{ActionGraphProjection.FormatRelative(summary.UpdatedAt)} · {summary.ActionCount} steps")
            {
                Level = TextLevel.Caption
            };
            meta.SetValue(HavenProperties.Foreground, "TextMuted");
            row.Add(title);
            row.Add(meta);
            var captured = summary;
            row.Invoked += async (_, _) => await SelectExecutionAsync(captured);
            row.SecondaryInvoked += async (_, _) => await SelectExecutionAsync(captured);
            _scene.HistoryList.Add(row);
        }
        if (_summaries.Count > 0 && matches.Length == 0)
        {
            var empty = new HavenText("No prompts match this filter.") { Level = TextLevel.Caption };
            empty.SetValue(HavenProperties.Foreground, "TextMuted");
            _scene.HistoryList.Add(empty);
        }
    }

    // ---------- Execution selection ----------

    public async Task OpenAsync(Guid executionId, Guid? actionId = null)
    {
        var summary = _summaries.FirstOrDefault(item => item.ExecutionId == executionId);
        if (summary is null)
        {
            _summaries = await _traces.SearchAsync(null, FullHistoryLimit, _lifetime.Token);
            summary = _summaries.FirstOrDefault(item => item.ExecutionId == executionId);
            await Dispatcher.UIThread.InvokeAsync(RenderHistory);
        }
        if (summary is null) return;
        await SelectExecutionAsync(summary);
        if (actionId is { } id && _events.Any(item => item.ActionId == id))
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _surface.SelectAction(id);
                OnSurfaceSelectionChanged(id);
            });
    }

    private async Task SelectExecutionAsync(ExecutionSummary summary)
    {
        IReadOnlyList<ExecutionEvent> trace;
        try { trace = await _traces.GetTraceAsync(summary.ExecutionId, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }

        var collapsed = ActionGraphProjection.Collapse(trace);
        var model = ActionGraphProjection.BuildGraph(trace);
        var metrics = ActionGraphProjection.ComputeMetrics(summary, trace);
        _selectedExecution = summary;
        _events = collapsed;
        _model = model;
        _metrics = metrics;
        _selectedActionId = null;
        _selectedFeedback = null;
        _listRowsShown = 0;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RenderHistory();
            _scene.SetMetrics(metrics);
            RenderLegend();
            ApplyFilterAndRender(preserveSelection: false);
            _scene.SetStatus($"{Truncate(summary.PromptSummary, 70)} · {collapsed.Count} steps recorded"
                + (trace.Count > collapsed.Count ? $" ({trace.Count} raw updates collapsed)" : string.Empty));
        });
        await RenderDetailsAsync();
    }

    private void RenderLegend()
    {
        var categories = _model.Nodes.Select(node => node.Category).Distinct().OrderBy(category => (int)category).ToArray();
        _scene.SetLegend(categories, _model.TimeMode, _model.Nodes.Count);
    }

    /// <summary>Status filters reshape graph/list projections only; metric cards stay execution-truth.</summary>
    private void ApplyFilterAndRender(bool preserveSelection)
    {
        var selection = preserveSelection ? _selectedActionId : null;
        var mode = LiveMode();
        IEnumerable<ActionGraphNode> nodes = _model.Nodes;
        if (mode == "Completed only") nodes = nodes.Where(node => node.Status == ExecutionActionStatus.Completed);
        else if (mode == "Failures only")
            nodes = nodes.Where(node => node.Status == ExecutionActionStatus.Failed || IsFailureNode(node));
        _visibleNodes = nodes.ToArray();
        var visibleIds = _visibleNodes.Select(node => node.ActionId).ToHashSet();
        var filtered = new ActionGraphModel(
            _visibleNodes,
            _model.Links.Where(link => visibleIds.Contains(link.FromActionId) && visibleIds.Contains(link.ToActionId)).ToArray(),
            _model.RulerTicks,
            _model.TimeMode,
            _model.ExtentWidth);
        _surface.SetGraph(filtered, selection);
        _scene.SetZoomPercent(_surface.Zoom);
        RebuildListRows(reset: true);
        UpdateEmptyState();
    }

    private static bool IsFailureNode(ActionGraphNode node) =>
        node.Status == ExecutionActionStatus.Failed || node.Category == ActionGraphCategory.Blocked;

    private void UpdateEmptyState()
    {
        var visible = _events.Count > 0 && (_visibleNodes.Count > 0 || !_graphView);
        _scene.EmptyStateText.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _scene.EmptyStateText.Content = _events.Count == 0
            ? "No execution trace is available yet."
            : LiveMode() switch
            {
                "Completed only" => "No completed steps matched this filter.",
                "Failures only" => "No failures were recorded in this execution.",
                _ => string.Empty
            };
    }

    private string LiveMode() => _scene.LiveSelect.SelectedItem ?? "Live";

    private void ApplyLiveMode()
    {
        var mode = LiveMode();
        _scene.SetStatus(mode switch
        {
            "Live" => "Live: the graph follows new activity for the selected execution.",
            "Paused" => "Paused: live updates are held; switch back to Live to follow activity.",
            "Completed only" => "Showing completed steps only. Metric cards always describe the full execution.",
            _ => "Showing failures only. Metric cards always describe the full execution."
        });
        ApplyFilterAndRender(preserveSelection: true);
    }

    // ---------- Graph surface selection ----------

    private void OnSurfaceSelectionChanged(Guid? actionId)
    {
        _selectedActionId = actionId;
        _ = RenderDetailsAsync();
    }

    private async Task RenderDetailsAsync()
    {
        var action = _selectedActionId is { } id ? _events.FirstOrDefault(item => item.ActionId == id) : null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ClearChildren(_scene.DetailsSections);
            ClearChildren(_scene.FeedbackSection);
            ClearChildren(_scene.RemediationSection);
            DetachPersistentFeedbackControls();
            if (action is null)
            {
                AddDetailCard("Node details", _events.Count == 0
                    ? "Select an execution on the left, then choose a step in the graph or list."
                    : "Select a node in the graph or a row in the list to inspect what Haven did.");
                return;
            }
            RenderActionDetails(action);
        });
        if (action is null) return;
        try
        {
            _selectedFeedback = await _feedback.GetAsync(action.ExecutionId, action.ActionId, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        await Dispatcher.UIThread.InvokeAsync(RenderCurrentFeedbackSection);
    }

    private void RenderActionDetails(ExecutionEvent action)
    {
        var category = ActionGraphCatalog.Categorize(action.ActionType);

        var overview = NewCard("ActionGraph.Details.Overview");
        var titleRow = new Container { Name = "ActionGraph.Details.TitleRow", Layout = HavenLayout.Grid, Columns = "Auto,1fr", Rows = "Auto Auto" };
        titleRow.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var icon = new Icon { Name = "ActionGraph.Details.Icon", Key = ActionGraphCatalog.CategoryIcon(category) };
        icon.SetValue(HavenProperties.Width, HavenLength.Px(22));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(22));
        icon.SetValue(HavenProperties.Foreground, ActionGraphCatalog.CategoryToken(category));
        icon.SetValue(HavenProperties.RowSpan, 2);
        var name = new HavenText(action.Name) { Name = "ActionGraph.Details.Name", Level = TextLevel.H4 };
        name.SetValue(HavenProperties.Column, 1);
        var chips = new Container { Name = "ActionGraph.Details.Chips", Layout = HavenLayout.Horizontal };
        chips.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        chips.SetValue(HavenProperties.Column, 1);
        chips.SetValue(HavenProperties.Row, 1);
        AddChip(chips, "status", ActionGraphCatalog.DescribeStatus(action.Status), ActionGraphCatalog.StatusToken(action.Status));
        AddChip(chips, "category", ActionGraphCatalog.CategoryName(category), ActionGraphCatalog.CategoryToken(category));
        if (action.Duration is { } duration) AddChip(chips, "duration", ActionGraphProjection.FormatDuration(duration), "TextSecondary");
        titleRow.Add(icon);
        titleRow.Add(name);
        titleRow.Add(chips);
        overview.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(action.SafeReasoningSummary) || !string.IsNullOrWhiteSpace(action.SafeDetail))
        {
            var body = action.SafeReasoningSummary is { } reasoning && action.SafeDetail is { } detail
                ? reasoning + Environment.NewLine + Environment.NewLine + detail
                : action.SafeReasoningSummary ?? action.SafeDetail ?? string.Empty;
            overview.Add(Caption("Description"));
            overview.Add(BodyText(body, "ActionGraph.Details.Description"));
        }
        _scene.DetailsSections.Add(overview);

        if (!string.IsNullOrWhiteSpace(action.ComponentId))
            AddDetailCard("ToolOrApp", "Tool or app", action.ComponentId);

        var metadataCard = NewCard("ActionGraph.Details.Metadata");
        metadataCard.Add(Caption("Metadata"));
        foreach (var line in MetadataLines(action))
        {
            var rowText = new HavenText(line) { Level = TextLevel.Caption };
            rowText.SetValue(HavenProperties.Foreground, "TextSecondary");
            metadataCard.Add(rowText);
        }
        _scene.DetailsSections.Add(metadataCard);

        var relationships = new StringBuilder();
        relationships.AppendLine($"Parent step: {ResolveName(action.ParentActionId)}");
        if (action.RetryOfActionId is { } retryOf) relationships.Append($"Retry of: {ResolveName(retryOf)}").AppendLine();
        if (action.RecoveryOfActionId is { } recoveryOf) relationships.Append($"Recovery of: {ResolveName(recoveryOf)}").AppendLine();
        AddDetailCard("Relationships", "Graph relationships", relationships.ToString().TrimEnd());

        if (action.Failure is { } failure)
        {
            var failureCard = NewCard("ActionGraph.Details.Failure");
            failureCard.SetValue(HavenProperties.BorderColor, "Danger");
            failureCard.Add(Caption($"Failure · {failure.Code}"));
            failureCard.Add(BodyText(
                failure.Title + Environment.NewLine + failure.Message
                + Environment.NewLine + $"Attempt {failure.Attempt}"
                + (failure.Recovered ? Environment.NewLine + "Recovered automatically." : string.Empty),
                "ActionGraph.Details.Failure.Body"));
            _scene.DetailsSections.Add(failureCard);
            if (!failure.Recovered)
            {
                var fix = NewButton("ActionGraph.Details.FixWithAi", "Fix with AI", ButtonVariant.Primary);
                var capturedAction = action;
                fix.Invoked += (_, _) => _fixWithAi(
                    $"Inspect and safely repair Action {capturedAction.ActionId} in Execution {capturedAction.ExecutionId}. "
                    + $"Failure: {failure.Code} — {failure.Title}. Stay within the original permissions and task scope.");
                _scene.DetailsSections.Add(fix);
            }
        }
    }

    private IEnumerable<string> MetadataLines(ExecutionEvent action)
    {
        yield return $"Start time:  {(action.StartedAt ?? action.Timestamp).ToLocalTime():g}";
        if (action.EndedAt is { } ended) yield return $"End time:  {ended.ToLocalTime():g}";
        if (action.Duration is { } duration) yield return $"Duration:  {ActionGraphProjection.FormatDuration(duration)}";
        yield return $"Type:  {action.ActionType}";
        yield return $"Origin:  {action.Origin}";
        var retryCount = _events.Count(item => item.RetryOfActionId == action.ActionId || item.RecoveryOfActionId == action.ActionId);
        yield return $"Retries / recoveries of this step:  {retryCount}";
        if (action.TaskId is { } taskId) yield return $"Task:  {taskId.ToString("N")[..12]}…";
        if (action.ProjectId is { } projectId) yield return $"Project:  {projectId.ToString("N")[..12]}…";
        if (action.SafeMetadata is { } pairs)
            foreach (var pair in pairs.Take(12))
                yield return $"{pair.Key}:  {pair.Value}";
    }

    private void RenderCurrentFeedbackSection()
    {
        var action = _selectedActionId is { } id ? _events.FirstOrDefault(item => item.ActionId == id) : null;
        if (action is null) return;
        var section = _scene.FeedbackSection;
        section.Add(Caption("Your feedback"));
        var buttons = new Container { Name = "ActionGraph.Feedback.Buttons", Layout = HavenLayout.Horizontal };
        buttons.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        _scene.ThumbUpButton.Variant = _selectedFeedback?.Rating == ActionFeedbackRating.Positive ? ButtonVariant.Primary : ButtonVariant.Tertiary;
        _scene.ThumbDownButton.Variant = _selectedFeedback?.Rating == ActionFeedbackRating.Negative ? ButtonVariant.Danger : ButtonVariant.Tertiary;
        buttons.Add(_scene.ThumbUpButton);
        buttons.Add(_scene.ThumbDownButton);
        section.Add(buttons);
        section.Add(_scene.CommentInput);
        _scene.CommentInput.Text = _selectedFeedback?.Comment ?? string.Empty;
        var actions = new Container { Name = "ActionGraph.Feedback.Actions", Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        actions.Add(_scene.SaveCommentButton);
        _scene.DeleteFeedbackButton.SetValue(HavenProperties.Visibility,
            _selectedFeedback is null ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        actions.Add(_scene.DeleteFeedbackButton);
        section.Add(actions);
        if (_selectedFeedback is { } feedback)
            section.Add(BodyText($"Saved {ActionGraphProjection.FormatRelative(feedback.UpdatedAt)}", "ActionGraph.Feedback.SavedAt"));
    }

    private void DetachPersistentFeedbackControls()
    {
        foreach (var control in new HavenElement[] { _scene.ThumbUpButton, _scene.ThumbDownButton, _scene.CommentInput, _scene.SaveCommentButton, _scene.DeleteFeedbackButton })
            control.Parent?.Remove(control);
    }

    private void RenderRemediation(RemediationRequest request)
    {
        var section = _scene.RemediationSection;
        var card = NewCard("ActionGraph.Remediation.Card");
        card.SetValue(HavenProperties.BorderColor,
            request.State is RemediationState.Completed or RemediationState.Cancelled ? "Border" : "Warning");
        var heading = request.State switch
        {
            RemediationState.Suspended => "Action paused",
            RemediationState.Completed => "Action resolved",
            RemediationState.Cancelled => "Action cancelled",
            RemediationState.Expired => "Action expired",
            RemediationState.Failed => "Resolution failed",
            _ => "User action required"
        };
        card.Add(Caption(heading));
        card.Add(BodyText(request.RequestingComponentName + (request.ProviderName is null ? string.Empty : " · " + request.ProviderName), "ActionGraph.Remediation.Component"));
        card.Add(BodyText(request.Explanation + Environment.NewLine + "Stored securely by Haven where applicable.", "ActionGraph.Remediation.Explanation"));

        if (request.State is RemediationState.Completed or RemediationState.Cancelled or RemediationState.Expired or RemediationState.Failed)
        {
            section.Add(card);
            return;
        }

        var canResumeNow = request.CanResume && _remediationCoordinator.CanResume(request.Id);
        if (request.CanResume && !canResumeNow)
            card.Add(BodyText(
                "Automatic resume is no longer available for this saved blocker, usually because Haven restarted. Resolve the setup issue, then retry the action from its task.",
                "ActionGraph.Remediation.ResumeNote"));

        if (request.Type == RemediationType.SecretInput)
        {
            var input = request.RequiredInputs.FirstOrDefault();
            var secret = new Input { Name = "ActionGraph.Remediation.Secret", Placeholder = input?.Label ?? "Required secret" };
            secret.IsSecret = true;
            secret.Accessibility.AccessibleName = input?.Label ?? "Required secret";
            secret.TextChanged += async (_, _) => await _remediationCoordinator.RecordInteractionAsync(request.Id, _lifetime.Token);
            var save = NewButton("ActionGraph.Remediation.SaveSecret", canResumeNow ? "Save securely & retry" : "Save securely", ButtonVariant.Primary);
            save.Invoked += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(secret.Text)) return;
                await _remediationCoordinator.SaveSecretAndResolveAsync(request.Id, input?.Key ?? "secret", secret.Text, _lifetime.Token);
                secret.Text = string.Empty;
                await RenderDetailsAsync();
            };
            card.Add(secret);
            card.Add(save);
            section.Add(card);
            return;
        }

        if (request.Type is RemediationType.PermissionRequest or RemediationType.Confirmation)
        {
            if (canResumeNow)
            {
                var approve = NewButton("ActionGraph.Remediation.Approve", "Approve & retry", ButtonVariant.Primary);
                approve.Invoked += async (_, _) =>
                {
                    await _remediationCoordinator.ApproveAndResolveAsync(request.Id, _lifetime.Token);
                    await RenderDetailsAsync();
                };
                card.Add(approve);
            }
            section.Add(card);
            return;
        }

        var open = NewButton("ActionGraph.Remediation.Open",
            request.Type switch
            {
                RemediationType.OAuthReconnect => "Reconnect account",
                RemediationType.ResourceSelection => "Select resource",
                _ => "Open settings"
            });
        open.Invoked += (_, _) => _openSettings();
        card.Add(open);

        if (canResumeNow && request.CanRetry)
        {
            var retry = NewButton("ActionGraph.Remediation.Retry", "Retry", ButtonVariant.Primary);
            retry.Invoked += async (_, _) =>
            {
                await _remediationCoordinator.RetryResolvedAsync(request.Id, _lifetime.Token);
                await RenderDetailsAsync();
            };
            card.Add(retry);
        }
        section.Add(card);
    }

    // ---------- Feedback persistence ----------

    private async Task SaveRatingAsync(ActionFeedbackRating rating)
    {
        if (_selectedActionId is not { } actionId) return;
        var action = _events.FirstOrDefault(item => item.ActionId == actionId);
        if (action is null) return;
        var now = DateTimeOffset.UtcNow;
        var feedback = new ActionFeedback(
            _selectedFeedback?.Id ?? Guid.NewGuid(), action.ExecutionId, action.ActionId, rating,
            _selectedFeedback?.Comment, action.ActionType.ToString(), action.ComponentId,
            action.SafeReasoningSummary, _selectedFeedback?.CreatedAt ?? now, now);
        await _traces.UpsertFeedbackAsync(feedback, _lifetime.Token);
        _selectedFeedback = feedback;
        await Dispatcher.UIThread.InvokeAsync(RenderCurrentFeedbackSection);
        _scene.SetStatus(rating == ActionFeedbackRating.Positive ? "Marked this step as helpful." : "Marked this step as not helpful.");
    }

    private async Task SaveCommentAsync()
    {
        if (_selectedActionId is not { } actionId || string.IsNullOrWhiteSpace(_scene.CommentInput.Text)) return;
        var action = _events.FirstOrDefault(item => item.ActionId == actionId);
        if (action is null) return;
        var now = DateTimeOffset.UtcNow;
        var feedback = new ActionFeedback(
            _selectedFeedback?.Id ?? Guid.NewGuid(), action.ExecutionId, action.ActionId, _selectedFeedback?.Rating,
            _scene.CommentInput.Text, action.ActionType.ToString(), action.ComponentId,
            action.SafeReasoningSummary, _selectedFeedback?.CreatedAt ?? now, now);
        await _traces.UpsertFeedbackAsync(feedback, _lifetime.Token);
        _selectedFeedback = feedback;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RenderCurrentFeedbackSection();
            _scene.SetStatus("Comment saved with this action.");
        });
    }

    private async Task DeleteFeedbackAsync()
    {
        if (_selectedFeedback is not { } feedback) return;
        await _traces.DeleteFeedbackAsync(feedback.Id, _lifetime.Token);
        _selectedFeedback = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RenderCurrentFeedbackSection();
            _scene.SetStatus("Feedback removed from this action.");
        });
    }

    // ---------- List projection ----------

    private void SetViewMode(bool graph)
    {
        _graphView = graph;
        _scene.SetViewMode(graph);
        if (!graph)
        {
            RebuildListRows(reset: true);
            UpdateEmptyState();
        }
        else
        {
            _scene.SetZoomPercent(_surface.Zoom);
            UpdateEmptyState();
        }
    }

    private void RebuildListRows(bool reset)
    {
        if (reset)
        {
            ClearChildren(_scene.ListHost);
            _listRowsShown = 0;
        }
        var nodes = _visibleNodes;
        if (nodes.Count == 0)
        {
            _scene.ListHost.Add(BodyText(
                _events.Count == 0 ? "This execution has no recorded steps yet." : "No steps match the current status filter.",
                "ActionGraph.List.Empty"));
            return;
        }
        var end = Math.Min(nodes.Count, _listRowsShown + ListPageSize);
        for (var index = _listRowsShown; index < end; index++)
        {
            var node = nodes[index];
            var row = new HavenButton
            {
                Name = $"ActionGraph.List.Row.{node.Ordinal}",
                Content = $"{index + 1}.  {Truncate(node.Name, 52)}   ·   {node.TypeLabel}   ·   {ActionGraphCatalog.DescribeStatus(node.Status)}"
                    + (node.Duration is { } duration ? $"   ·   {ActionGraphProjection.FormatDuration(duration)}" : string.Empty),
                Variant = Equals(node.ActionId, _selectedActionId) ? ButtonVariant.Primary : ButtonVariant.Tertiary
            };
            row.Accessibility.AccessibleName = $"Step {index + 1}: {node.Name}, {node.TypeLabel}, {ActionGraphCatalog.DescribeStatus(node.Status)}";
            row.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
            var captured = node;
            row.Invoked += (_, _) =>
            {
                _surface.SelectAction(captured.ActionId);
                OnSurfaceSelectionChanged(captured.ActionId);
            };
            _scene.ListHost.Add(row);
        }
        _listRowsShown = end;
        if (end < nodes.Count)
        {
            var showMore = NewButton("ActionGraph.List.ShowMore", $"Show more ({nodes.Count - end} remaining)");
            showMore.Invoked += (_, _) => RebuildListRows(reset: false);
            _scene.ListHost.Add(showMore);
        }
        else if (end < _model.Nodes.Count)
        {
            _scene.ListHost.Add(BodyText($"{_model.Nodes.Count - end} more steps hidden by the current status filter.", "ActionGraph.List.FilterNote"));
        }
    }

    // ---------- Export ----------

    internal static string BuildExportDocument(ExecutionSummary? summary, IReadOnlyList<ExecutionEvent> events, DateTimeOffset generatedAt)
    {
        var model = ActionGraphProjection.BuildGraph(events);
        var metrics = ActionGraphProjection.ComputeMetrics(summary, events);
        return ActionGraphProjection.BuildExportJson(summary, events, model, metrics, generatedAt);
    }

    private async Task ExportAsync()
    {
        if (_selectedExecution is null)
        {
            _scene.SetStatus("Nothing to export yet — select an execution first.");
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            _scene.SetStatus("Export isn't available from this platform surface.");
            return;
        }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export execution graph",
            SuggestedFileName = $"haven-action-graph-{_selectedExecution.ExecutionId.ToString("N")[..8]}",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON execution graph") { Patterns = ["*.json"] }],
            ShowOverwritePrompt = true
        });
        if (file is null) return;
        try
        {
            var json = BuildExportDocument(_selectedExecution, _events, DateTimeOffset.UtcNow);
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                await File.WriteAllTextAsync(localPath, json, _lifetime.Token);
            }
            else
            {
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(json.AsMemory(), _lifetime.Token);
            }
            _scene.SetStatus($"Exported this execution graph ({_events.Count} steps) to {file.Name}.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _scene.SetStatus("Couldn't export this graph: " + ex.Message);
        }
    }

    // ---------- Live updates ----------

    private void OnEventPublished(object? sender, ExecutionEvent item)
    {
        if (LiveMode() != "Live") return;
        if (_selectedExecution?.ExecutionId != item.ExecutionId) return;
        if (Interlocked.Exchange(ref _liveRefreshQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                Interlocked.Exchange(ref _liveRefreshQueued, 0);
                return;
            }
            _liveRefreshTimer.Start();
        });
    }

    private async void RefreshLiveTrace(object? sender, EventArgs args)
    {
        _liveRefreshTimer.Stop();
        Interlocked.Exchange(ref _liveRefreshQueued, 0);
        try
        {
            if (!_disposed && _selectedExecution is { } current)
                await ReselectPreservingSelection(current);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task ReselectPreservingSelection(ExecutionSummary summary)
    {
        var previousSelection = _selectedActionId;
        await SelectExecutionAsync(summary);
        if (previousSelection is { } id && _events.Any(item => item.ActionId == id))
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _surface.SelectAction(id, reveal: false);
                OnSurfaceSelectionChanged(id);
            });
    }

    // ---------- Small builders ----------

    private static void ClearChildren(Container host) => host.Children.ToList().ForEach(child => child.Parent?.Remove(child));

    private static Container NewCard(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        return card;
    }

    private static HavenButton NewButton(string name, string label, ButtonVariant variant = ButtonVariant.Tertiary)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = variant };
        button.Accessibility.AccessibleName = label;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        return button;
    }

    private void AddDetailCard(string nameSuffix, string heading, string body)
    {
        var card = NewCard("ActionGraph.Detail." + nameSuffix);
        card.Add(Caption(heading));
        card.Add(BodyText(body, card.Name + ".Body"));
        _scene.DetailsSections.Add(card);
    }

    private static HavenText Caption(string text)
    {
        var caption = new HavenText(text) { Level = TextLevel.Caption };
        caption.SetValue(HavenProperties.FontWeight, 700);
        return caption;
    }

    private static HavenText BodyText(string text, string name)
    {
        var body = new HavenText(text) { Name = name, Level = TextLevel.Paragraph };
        body.SetValue(HavenProperties.FontSize, 12.5);
        return body;
    }

    private static void AddChip(Container host, string name, string label, string token)
    {
        var chip = new Container { Name = $"ActionGraph.Details.Chip.{name}", Layout = HavenLayout.Horizontal };
        chip.SetValue(HavenProperties.Background, "SurfaceSecondary");
        chip.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(9)));
        chip.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 3px"));
        var text = new HavenText(label) { Level = TextLevel.Caption };
        text.SetValue(HavenProperties.Foreground, token);
        chip.Add(text);
        host.Add(chip);
    }

    private string ResolveName(Guid? actionId)
    {
        if (actionId is not { } id) return "none";
        var node = _model.Nodes.FirstOrDefault(item => item.ActionId == id);
        return node is null ? id.ToString("N")[..8] : Truncate(node.Name, 40);
    }

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..Math.Max(0, maximum - 1)] + "…";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hub is not null) _hub.Published -= OnEventPublished;
        _liveRefreshTimer.Stop();
        _liveRefreshTimer.Tick -= RefreshLiveTrace;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
