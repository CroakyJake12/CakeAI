using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Production Haven-native conversation surface. Avalonia owns only the platform host/window/file-picker boundary;
/// visible Chat composition, editing, transcript rendering, menus and generated UI live in Haven scene elements.
/// </summary>
public sealed partial class NewChatPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IConversationRepository _conversations;
    private readonly IOllamaClient _ollama;
    private readonly ChatSessionService _sessions;
    private readonly IConversationSafetyService _safety;
    private readonly IConversationVersioningService _versioning;
    private readonly IMessageAttachmentService? _messageAttachments;
    private readonly IConversationProductionRepository? _conversationProduction;
    private readonly UserPreferencesService _preferences;
    private readonly GenerativeUiEventRouter _genUiRouter;
    private readonly GenUiInstanceStore _genUiInstances;
    private readonly CalculatorTemplateRuntime _calculatorTemplate;
    private readonly StructuredFormTemplateRuntime _structuredFormTemplate;
    private readonly ChoicePromptTemplateRuntime _choicePromptTemplate;
    private readonly ChecklistTemplateRuntime _checklistTemplate;
    private readonly DataGridTemplateRuntime _dataGridTemplate;
    private readonly CardDeckTemplateRuntime _cardDeckTemplate;
    private readonly GraphTemplateRuntime _graphTemplate;
    private readonly TaskListTemplateRuntime _taskListTemplate;
    private readonly DashboardTemplateRuntime _dashboardTemplate;
    private readonly AssessmentTemplateRuntime _assessmentTemplate;
    private readonly WorkflowTemplateRuntime _workflowTemplate;
    private readonly CustomTemplateRuntime _customTemplate;
    private readonly ChatHavenScene _scene;
    private readonly List<ChatMessage> _messages = [];
    private readonly Stack<ChatMessage> _redoMessages = [];
    private readonly HashSet<Guid> _streamingMessages = [];
    private readonly List<PromptDefinition> _activeInstructions = [];
    private readonly List<string> _attachedImages = [];
    private readonly Dictionary<string, string> _attachedContext = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MessageAttachment> _persistedAttachments = [];
    private readonly Dictionary<Guid, string> _attachmentSourcePaths = [];
    private readonly TaskAttachmentContext _taskAttachments = new();
    private readonly Dictionary<Guid, string> _thinkingContent = [];
    private readonly Dictionary<Guid, long> _thinkingStartTick = [];
    private readonly Dictionary<Guid, long> _thinkingEndTick = [];
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), ChatGenUiSurfaceMount> _generatedSurfaces = [];
    private readonly ChatGenUiNativeControlResolver _genUiNativeResolver = new();
    private DualModelChatController? _dual;
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), Guid> _generatedInstanceIds = [];
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), string> _generatedSignatures = [];
    private IReadOnlyList<CapabilityDefinition> _availableCapabilities = [];
    private IReadOnlyList<ModeDefinition> _availableApps = [];
    private ModeDefinition? _modeDefinition;
    private Conversation _conversation;
    private AgentDefinition? _activeAgent;
    private ChatActionMode? _chatActionModeOverride;
    private GenerativeUiResponseMode? _chatGenerativeUiResponseModeOverride;
    private string? _registeredContextOverride;
    private EffortLevel? _effortOverride;
    private ModelDescriptor? _selectedModel;
    private string? _pendingInstruction;
    private bool _pendingInstructionPreservesDraft;
    private ChatMentionQuery? _activeMention;
    private CancellationTokenSource? _sendCancellation;
    private readonly DispatcherTimer _sendProgressTimer;
    private bool _isSending;
    private bool _safetyLocked;
    private bool _isTaskMode;
    private bool _lastReportedHasStarted;
    private bool _disposed;
    private long _sendStartTick;
    private string? _projectionStatus;
    private ChatProjectionState _projectionState = ChatProjectionState.Empty;
    private bool _projectionDirty = true;

    public NewChatPage(
        HavenEventBus bus,
        IConversationRepository conversations,
        IOllamaClient ollama,
        ChatSessionService sessions,
        IConversationSafetyService safety,
        IConversationVersioningService versioning,
        UserPreferencesService preferences,
        GenerativeUiEventRouter genUiRouter,
        GenUiInstanceStore genUiInstances,
        CalculatorTemplateRuntime calculatorTemplate,
        StructuredFormTemplateRuntime structuredFormTemplate,
        ChoicePromptTemplateRuntime choicePromptTemplate,
        ChecklistTemplateRuntime checklistTemplate,
        DataGridTemplateRuntime dataGridTemplate,
        CardDeckTemplateRuntime cardDeckTemplate,
        GraphTemplateRuntime graphTemplate,
        TaskListTemplateRuntime taskListTemplate,
        DashboardTemplateRuntime dashboardTemplate,
        AssessmentTemplateRuntime assessmentTemplate,
        WorkflowTemplateRuntime workflowTemplate,
        CustomTemplateRuntime customTemplate,
        IMessageAttachmentService? messageAttachments = null,
        IConversationProductionRepository? conversationProduction = null)
    {
        _bus = bus;
        _conversations = conversations;
        _ollama = ollama;
        _sessions = sessions;
        _safety = safety;
        _versioning = versioning;
        _messageAttachments = messageAttachments;
        _conversationProduction = conversationProduction;
        _preferences = preferences;
        _genUiRouter = genUiRouter;
        _genUiInstances = genUiInstances;
        _calculatorTemplate = calculatorTemplate;
        _structuredFormTemplate = structuredFormTemplate;
        _choicePromptTemplate = choicePromptTemplate;
        _checklistTemplate = checklistTemplate;
        _dataGridTemplate = dataGridTemplate;
        _cardDeckTemplate = cardDeckTemplate;
        _graphTemplate = graphTemplate;
        _taskListTemplate = taskListTemplate;
        _dashboardTemplate = dashboardTemplate;
        _assessmentTemplate = assessmentTemplate;
        _workflowTemplate = workflowTemplate;
        _customTemplate = customTemplate;
        _conversation = CreateConversation(HavenMode.Chat);

        _scene = new ChatHavenScene();
        AutomationProperties.SetAutomationId(this, "HavenNativeChatPage");
        AutomationProperties.SetName(this, "Haven-native Chat page");
        Scene = new HavenSceneControl(new HavenAvaloniaImageResolver(), _genUiNativeResolver) { Root = _scene.Root };
        AutomationProperties.SetAutomationId(Scene, "HavenNativeChatScene");
        AutomationProperties.SetName(Scene, "Haven-native Chat");
        Content = Scene;
        WireScene();

        _sendProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sendProgressTimer.Tick += (_, _) =>
        {
            if (!_isSending) return;
            var followLatest = IsFollowingLatest();
            foreach (var messageId in _streamingMessages.ToArray())
            {
                var message = _messages.FirstOrDefault(item => item.Id == messageId);
                if (message is not null) RefreshMessage(message);
            }
            ScrollToEndIfFollowing(followLatest);
        };

        RefreshResponseControls();
        RefreshVisualState();
        _ = InitialiseAsync();
    }

    public HavenSceneControl Scene { get; }

    public event EventHandler? ModelChanged;
    public event EventHandler? ConversationStateChanged;
    public event EventHandler<ChatProjectionStateChangedEventArgs>? ProjectionStateChanged;
    public event EventHandler<AddMenu.AddMenuAction>? AddActionSelected;
    public event EventHandler<AddMenuSelection>? AddCatalogItemSelected;

    public string? SelectedModelName => _selectedModel?.Name;
    public ModelDescriptor? SelectedModel => _selectedModel;
    public Guid ConversationId => _conversation.Id;
    public Conversation CurrentConversation => _conversation;
    public bool IsTemporary => _conversation.IsTemporary;
    public bool HasStarted => _messages.Count > 0;
    public string ActiveAgentName => _activeAgent?.Name ?? "No Agent (Default)";
    public ChatActionMode EffectiveChatActionMode => _chatActionModeOverride ?? ChatActionMode.AllowBasicActions;
    public bool IsSending => _isSending;

    public ChatProjectionState ProjectionState
    {
        get
        {
            if (_projectionDirty) RebuildProjectionState();
            return _projectionState;
        }
    }

    /// <summary>
    /// Requests cancellation through the same cancellation source used by the full Chat surface.
    /// Returns false when no response is currently cancellable.
    /// </summary>
    public bool TryStopResponse()
    {
        var cancellation = _sendCancellation;
        if (cancellation is null || cancellation.IsCancellationRequested) return false;

        SetProjectionStatus("Stopping response...");
        cancellation.Cancel();
        return true;
    }

    public void ConfigureMode(ModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        if (HasStarted) throw new InvalidOperationException("A started conversation cannot be reassigned to another app.");
        _modeDefinition = mode;
        _isTaskMode = false;
        _conversation = CreateConversation(mode.BaseMode);
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _scene.SetComposerPlaceholder(mode.Key switch
        {
            "imagine" => "Describe an image, style or visual concept",
            "present" => "Describe the presentation you want to create",
            "data" => "Attach data or ask Haven to analyse it",
            "vision" => "Attach an image and ask what you want to inspect",
            "play" => "Describe what you want to play, build or explore",
            "translate" => "Paste text and name the target language",
            "launcher" => "Find an app, project, command or recent item",
            _ => $"Ask Haven {mode.Name}"
        });
        SetProjectionStatus(mode.Description);
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ConfigureTaskMode()
    {
        if (HasStarted) throw new InvalidOperationException("A started conversation cannot be reassigned to Tasks.");
        _isTaskMode = true;
        _modeDefinition = null;
        _conversation = CreateConversation(HavenMode.Tasks);
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _scene.SetComposerPlaceholder("Describe your task");
        SetProjectionStatus("Describe what you want Haven to complete. Haven will ask only for details that materially affect the result.");
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectModel(ModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _selectedModel = model;
        _preferences.SetModelDefaults(model.Name, _preferences.DefaultEffort);
        SetProjectionStatus(null);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        RefreshVisualState();
        TrySubmitPendingInstruction();
    }

    public async Task RefreshModelsAsync()
    {
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var selectedName = _selectedModel?.Name ?? _preferences.DefaultModel;
                _selectedModel = models.FirstOrDefault(model => model.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase)) ?? models.FirstOrDefault();
                SetProjectionStatus(_selectedModel is null ? "No local model is available." : null);
                ModelChanged?.Invoke(this, EventArgs.Empty);
                RefreshVisualState();
                TrySubmitPendingInstruction();
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            await SetStatusAsync("Local models are unavailable. Start Ollama, then try again.");
        }
    }

    public void Submit(string instruction)
    {
        var pending = instruction?.Trim();
        if (string.IsNullOrWhiteSpace(pending)) return;
        _scene.Instruction.Text = pending;
        _pendingInstruction = pending;
        _pendingInstructionPreservesDraft = false;
        TrySubmitPendingInstruction();
    }

    public void FocusComposer() => Scene.FocusElement(_scene.Instruction);

    public void SetDraft(string instruction)
    {
        _scene.Instruction.Text = instruction ?? string.Empty;
        _scene.Instruction.PlaceCaretAtEnd();
        FocusComposer();
    }

    public void ConfigureRegisteredContext(string? context, EffortLevel? effortOverride = null)
    {
        if (HasStarted) throw new InvalidOperationException("A started conversation cannot change its registered workspace context.");
        _registeredContextOverride = string.IsNullOrWhiteSpace(context) ? null : context.Trim();
        _effortOverride = effortOverride;
    }

    public async Task AssignSpaceAsync(Guid? spaceId)
    {
        _conversation = _conversation with { SpaceId = spaceId, UpdatedAt = DateTimeOffset.UtcNow };
        if (!_conversation.IsTemporary)
            await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowAddMenu() => _scene.ShowAddMenu();

    public async Task RegenerateLatestAsync()
    {
        if (!await EnsureConversationMayActAsync("chat.regenerate")) return;
        var response = _messages.LastOrDefault(message => message.Role == MessageRole.Assistant);
        if (response is not null) await RegenerateResponseAsync(response, ResponseRegenerationMode.Here);
    }

    public async Task BranchLatestAsync()
    {
        if (!await EnsureConversationMayActAsync("chat.branch")) return;
        var through = _messages.LastOrDefault();
        if (through is not null) await BranchIntoNewChatAsync(through);
    }

    public async Task UndoLatestAsync()
    {
        if (_isSending)
        {
            SetProjectionStatus("Stop the current response before undoing a message.");
            return;
        }
        if (!await EnsureConversationMayActAsync("chat.undo")) return;
        if (_messages.Count == 0)
        {
            SetProjectionStatus("There is no message to undo.");
            return;
        }

        var message = _messages[^1];
        try
        {
            if (!_conversation.IsTemporary)
                await _conversations.DeleteMessageAsync(_conversation.Id, message.Id, CancellationToken.None);
            _messages.RemoveAt(_messages.Count - 1);
            _redoMessages.Push(message);
            RefreshMessages();
            SetProjectionStatus("Undid the latest message. Use Redo to restore it.");
            ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or IOException)
        {
            SetProjectionStatus("The latest message could not be undone: " + exception.Message);
        }
    }

    public async Task RedoLatestAsync()
    {
        if (_isSending)
        {
            SetProjectionStatus("Stop the current response before redoing a message.");
            return;
        }
        if (!await EnsureConversationMayActAsync("chat.redo")) return;
        if (_redoMessages.Count == 0)
        {
            SetProjectionStatus("There is no undone message to restore.");
            return;
        }

        var message = _redoMessages.Peek();
        try
        {
            if (!_conversation.IsTemporary)
                await _conversations.AddMessageAsync(message, CancellationToken.None);
            _messages.Add(message);
            _redoMessages.Pop();
            RefreshMessages();
            SetProjectionStatus("Restored the latest undone message.");
            ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or IOException)
        {
            SetProjectionStatus("The message could not be restored: " + exception.Message);
        }
    }

    public async Task CompactContextAsync()
    {
        if (!await EnsureConversationMayActAsync("chat.compact-context")) return;
        if (_selectedModel is null)
        {
            SetProjectionStatus("Choose an available model before compacting context.");
            return;
        }

        var compactable = _messages
            .Where(message => !message.IsCompacted && message.Role is MessageRole.User or MessageRole.Assistant)
            .SkipLast(6)
            .ToArray();
        if (compactable.Length < 4)
        {
            SetProjectionStatus("There is not enough older context to compact yet.");
            return;
        }

        SetProjectionStatus("Compacting older context…");
        var transcript = string.Join("\n\n", compactable.Select(message => $"{message.Role}: {message.Content}"));
        if (transcript.Length > 180_000) transcript = transcript[^180_000..];
        string summary;
        try
        {
            summary = await _ollama.CompleteAsync(
                new OllamaChatRequest(
                    _selectedModel.Name,
                    [new OllamaMessage("user", "Summarise this conversation context for a future assistant. Preserve requirements, decisions, named files, unresolved questions, errors, and verified evidence. Do not invent facts.\n\n" + transcript)],
                    EffortLevel.Medium,
                    Options: _preferences.GenerationOptions with { Temperature = 0.2 }),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException)
        {
            SetProjectionStatus("Context compaction failed: " + exception.Message);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!_conversation.IsTemporary)
        {
            await _conversations.AddContextEntryAsync(
                new ConversationContextEntry(
                    Guid.NewGuid(), _conversation.Id, ContextEntryKind.CompactSummary,
                    "Manual compact summary", summary,
                    $"Compacted {compactable.Length} messages at {now:O}", now),
                CancellationToken.None);
            await _conversations.MarkMessagesCompactedAsync(
                _conversation.Id, compactable.Select(message => message.Id).ToArray(), CancellationToken.None);
            _conversation = _conversation with { CompactedAt = now, UpdatedAt = now };
            await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        }

        var compactedIds = compactable.Select(message => message.Id).ToHashSet();
        for (var index = 0; index < _messages.Count; index++)
            if (compactedIds.Contains(_messages[index].Id)) _messages[index] = _messages[index] with { IsCompacted = true };
        _redoMessages.Clear();
        RefreshMessages();
        SetProjectionStatus($"Compacted {compactable.Length} older messages into a durable summary.");
        await RefreshContextEntriesAsync();
    }

    public void StartFreshConversation(Guid? chatGroupId = null)
    {
        ResetToFreshConversation(_modeDefinition?.BaseMode ?? HavenMode.Chat, chatGroupId, null);
        _ = PersistFreshConversationAsync(_conversation);
        NotifyFreshConversationReady();
    }

    public async Task StartFreshConversationAsync(Guid? chatGroupId = null)
    {
        ResetToFreshConversation(_modeDefinition?.BaseMode ?? HavenMode.Chat, chatGroupId, null);
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        NotifyFreshConversationReady();
    }

    public async Task StartFreshConversationAsync(HavenMode mode, Guid? containerId, Guid? lessonId = null, Guid? spaceId = null)
    {
        ResetToFreshConversation(mode, containerId, lessonId, spaceId);
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        NotifyFreshConversationReady();
    }

    public void SetAddCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _availableCapabilities = capabilities ?? [];
        _availableApps = apps ?? [];
        _scene.SetCatalogue(agents ?? [], capabilities ?? [], instructions ?? [], apps ?? []);
    }

    public async Task LoadConversationAsync(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _conversation = conversation;
        _activeAgent = null;
        _activeInstructions.Clear();
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _registeredContextOverride = null;
        _effortOverride = null;
        _attachedImages.Clear();
        _attachedContext.Clear();
        _pendingAttachmentIds.Clear();
        _persistedAttachments.Clear();
        _attachmentSourcePaths.Clear();
        _taskAttachments.Clear();
        _pendingInstruction = null;
        _pendingInstructionPreservesDraft = false;
        RefreshResponseControls();
        _redoMessages.Clear();
        _messages.Clear();
        _messages.AddRange(await _conversations.GetMessagesAsync(conversation.Id, CancellationToken.None));
        await LoadPendingPersistedAttachmentsAsync(conversation);
        RefreshAttachmentStatus();
        await RefreshSafetyStateAsync();
        RefreshMessages();
        await RefreshContextEntriesAsync();
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        FocusComposer();
    }

    public void ApplyAddSelection(AddMenuSelection selection)
    {
        switch (selection.Item)
        {
            case AgentDefinition agent:
                _activeAgent = agent;
                SetProjectionStatus($"{agent.Name} selected.");
                break;
            case CapabilityDefinition capability:
                AttachCapability(capability);
                break;
            case PromptDefinition instruction:
                if (_activeInstructions.All(item => item.Id != instruction.Id)) _activeInstructions.Add(instruction);
                SetProjectionStatus($"{instruction.Name} instruction added.");
                break;
            case ChatActionMode actionMode:
                _chatActionModeOverride = actionMode;
                SetProjectionStatus($"{ActionModeLabel(actionMode)} for this chat.");
                break;
            case GenerativeUiResponseMode responseMode:
                _chatGenerativeUiResponseModeOverride = responseMode;
                SetProjectionStatus($"{VisualResponseModeLabel(responseMode)} for this chat.");
                break;
            case ModeDefinition app:
                _taskAttachments.AttachApp(app);
                SetProjectionStatus($"{app.Name} attached to this chat.");
                RefreshAttachmentStatus();
                break;
        }
        RefreshResponseControls();
    }

    public bool IsCapabilityAttached(Guid capabilityId) => _taskAttachments.IsCapabilityAttached(capabilityId);

    public void ToggleCapability(CapabilityDefinition capability)
    {
        if (_taskAttachments.IsCapabilityAttached(capability.Id))
        {
            _taskAttachments.RemoveCapability(capability.Id);
            SetProjectionStatus($"{capability.Name} removed from this chat context.");
            RefreshAttachmentStatus();
            return;
        }
        AttachCapability(capability);
    }

    public void AttachSnapshot(TaskAttachmentSnapshot snapshot)
    {
        _taskAttachments.AttachSnapshot(snapshot);
        RefreshAttachmentStatus();
        _ = AddFilesAsync(snapshot.Files);
    }

    public async Task AddFileAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files to this chat",
            AllowMultiple = true
        });
        await AddFilesAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0) return;

        if (_messageAttachments is null)
        {
            await AddFilesLegacyFallbackAsync(selected);
            return;
        }

        if (_conversation.IsTemporary)
        {
            _scene.SetStatus("Files are not persisted in a temporary chat. Turn off Temporary Chat before attaching files.");
            return;
        }

        try
        {
            await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _scene.SetStatus("This chat could not be saved before attaching files: " + exception.Message);
            return;
        }

        Guid? branchId;
        try
        {
            branchId = await EnsureAttachmentBranchAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _scene.SetStatus("This chat could not prepare attachment persistence: " + exception.Message);
            return;
        }

        var added = 0;
        var failed = 0;
        foreach (var path in selected)
        {
            try
            {
                var attachment = await _messageAttachments.ImportAsync(
                    _conversation.Id,
                    messageId: null,
                    branchId,
                    path,
                    options: null,
                    CancellationToken.None);
                _pendingAttachmentIds.Add(attachment.Id);
                _persistedAttachments.RemoveAll(item => item.Id == attachment.Id);
                _persistedAttachments.Add(attachment);
                _attachmentSourcePaths[attachment.Id] = path;
                _taskAttachments.AttachFiles([path]);
                added++;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                failed++;
                _scene.SetStatus($"Could not attach {Path.GetFileName(path)}: {exception.Message}");
            }
        }

        await RefreshPersistedAttachmentPromptContextAsync();
        if (added > 0) await SavePendingAttachmentDraftAsync();
        RefreshAttachmentStatus();
        if (added > 0)
        {
            var status = $"{added} file{(added == 1 ? "" : "s")} attached to this chat and added to File Library.";
            if (failed > 0) status += $" {failed} file{(failed == 1 ? "" : "s")} could not be attached.";
            _scene.SetStatus(status);
        }
    }

    private async Task AddFilesLegacyFallbackAsync(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif")
            {
                if (!_attachedImages.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _attachedImages.Add(path);
                    _taskAttachments.AttachFiles([path]);
                    added++;
                }
                continue;
            }
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 2_000_000)
                    _attachedContext[path] = $"Attached file: {info.Name} ({info.Length:N0} bytes; content omitted because it is too large).";
                else
                    _attachedContext[path] = $"Attached file {info.Name}:\n{await File.ReadAllTextAsync(path, CancellationToken.None)}";
                _taskAttachments.AttachFiles([path]);
                added++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SetProjectionStatus($"Could not attach {Path.GetFileName(path)}: {exception.Message}");
            }
        }
        if (added > 0)
        {
            SetProjectionStatus($"{added} file{(added == 1 ? "" : "s")} attached to this chat.");
            RefreshAttachmentStatus();
        }
    }

    private async Task RefreshPersistedAttachmentPromptContextAsync()
    {
        if (_messageAttachments is null) return;
        _attachedImages.Clear();
        _attachedContext.Remove("persisted-attachments");
        if (_persistedAttachments.Count == 0) return;
        try
        {
            var context = await _messageAttachments.BuildPromptContextAsync(
                _conversation.Id,
                _persistedAttachments.Select(item => item.Id).ToArray(),
                options: null,
                CancellationToken.None);
            _attachedImages.AddRange(context.ImageBase64);
            var sections = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.ExtractedText)) sections.Add(context.ExtractedText);
            if (context.Notices.Count > 0) sections.Add("Attachment processing:\n" + string.Join("\n", context.Notices));
            if (sections.Count > 0) _attachedContext["persisted-attachments"] = string.Join("\n\n", sections);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _scene.SetStatus("Attached file context could not be loaded: " + exception.Message);
        }
    }

    public string? GetLastAssistantResponse() => _messages.LastOrDefault(message => message.Role == MessageRole.Assistant)?.Content;

    public async Task TogglePinAsync()
    {
        _conversation = _conversation with { IsPinned = !_conversation.IsPinned, UpdatedAt = DateTimeOffset.UtcNow };
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        SetProjectionStatus(_conversation.IsPinned ? "Chat pinned." : "Chat unpinned.");
    }

    public async Task ArchiveAsync()
    {
        if (_messages.Count > 0)
        {
            _conversation = _conversation with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow };
            await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        }
        StartFreshConversation();
    }

    public async Task DeleteConversationAsync()
    {
        if (_messages.Count > 0) await _conversations.DeleteConversationAsync(_conversation.Id, CancellationToken.None);
        StartFreshConversation();
    }

    public void ShowRenameFlyout()
    {
        _scene.ShowTextPrompt(
            "Rename chat",
            "Give this conversation a clear title.",
            _conversation.Title,
            "Rename chat",
            async title =>
            {
                _conversation = _conversation with { Title = title.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
                await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
                ConversationStateChanged?.Invoke(this, EventArgs.Empty);
            });
    }

    public void ShowDeleteConfirmation()
    {
        _scene.ShowChoicePrompt(
            "Delete this chat?",
            "This permanently deletes the saved conversation and its messages.",
            [("Delete chat", () => _ = DeleteConversationAsync())]);
    }

    public void SetTemporary(bool isTemporary) =>
        _conversation = _conversation with { IsTemporary = isTemporary, UpdatedAt = DateTimeOffset.UtcNow };

    public async Task ToggleTemporaryAsync()
    {
        if (!await EnsureConversationMayActAsync("chat.temporary-toggle")) return;
        _conversation = _conversation with
        {
            IsTemporary = !_conversation.IsTemporary,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        if (!_conversation.IsTemporary)
            foreach (var message in _messages)
                await _conversations.AddMessageAsync(message, CancellationToken.None);
        SetProjectionStatus(_conversation.IsTemporary
            ? "Temporary chat is on. New messages remain only in this session."
            : "Temporary chat is off. This conversation is saved locally.");
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> EnsureConversationMayActAsync(string operation)
    {
        try
        {
            await _safety.EnsureMayActAsync(_conversation.Id, operation, CancellationToken.None);
            return true;
        }
        catch (ConversationSafetyLockException)
        {
            await RefreshSafetyStateAsync();
            return false;
        }
    }

    private async Task RefreshSafetyStateAsync()
    {
        var snapshot = await _safety.GetSnapshotAsync(_conversation.Id, CancellationToken.None);
        _safetyLocked = snapshot.State == ConversationSafetyState.Locked;
        _scene.SetSafetyLocked(_safetyLocked);
        if (_safetyLocked)
            SetProjectionStatus("This conversation is safety-locked after three confirmed safety flags. Sending, editing, branching and regeneration are disabled; deletion remains available.");
    }

    private void WireScene()
    {
        _bus.RegisterElement("Chat.Composer.Add", Scene);
        _bus.RegisterElement("Chat.Composer.Instruction", Scene);
        _bus.RegisterElement("Chat.Composer.Send", Scene);
        _bus.RegisterElement("Chat.Problems.Resolve", Scene);

        // Dual-model comparison is optional: it only appears when App services exposed DualModelService.
        if (App.Services?.GetService<DualModelService>() is { } dualModels)
        {
            _dual = new DualModelChatController(dualModels);
            _scene.EnsureDualModelBar();
            _scene.DualToggleRequested += OnDualToggleRequested;
            _scene.DualModelPickerRequested += OnDualModelPickerRequested;
            _scene.DualSecondModelChosen += OnDualSecondModelChosen;
        }

        _scene.SendRequested += async (_, _) => await SubmitCurrentInstructionAsync();
        _scene.StopRequested += OnStopRequested;
        _scene.Instruction.Invalidated += OnComposerEditCompleted;
        _scene.ResolveProblemsRequested += (_, _) =>
        {
            _bus.Fire("Chat.Problems.Resolve.Click");
            ShowResolveProblems();
        };
        _scene.AddActionSelected += (_, action) =>
        {
            if (_activeMention is not null) ConsumeActiveMention();
            AddActionSelected?.Invoke(this, action);
        };
        _scene.CatalogItemSelected += (_, selection) =>
        {
            if (_activeMention is not null) ConsumeActiveMention();
            ApplyAddSelection(selection);
            AddCatalogItemSelected?.Invoke(this, selection);
            FocusComposer();
        };
        _scene.MessageActionRequested += OnMessageActionRequested;
        _scene.MarkdownCodeActionRequested += OnMarkdownCodeActionRequested;
        _scene.ContextRemoveRequested += async (_, entryId) => await RemoveContextEntryAsync(entryId);
        _scene.AttachmentRemoveRequested += OnAttachmentRemoveRequested;
        Scene.InputSubmitted += OnInputSubmitted;
        Scene.PointerPressedOutside += _scene.HideAddMenu;
    }

    private async Task InitialiseAsync()
    {
        await SetStatusAsync("Connecting to local modelsâ€¦");
        await RefreshModelsAsync();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        TryStopResponse();
    }

    private void OnInputSubmitted(Input input)
    {
        if (ReferenceEquals(input, _scene.Instruction))
        {
            _ = SubmitCurrentInstructionAsync();
            return;
        }
        foreach (var surface in _generatedSurfaces.Values)
        {
            if (!surface.OwnsInput(input)) continue;
            _ = surface.SubmitInputAsync(input);
            return;
        }
    }

    private void OnComposerEditCompleted(object? sender, EventArgs e)
    {
        if (ChatMentionQueryParser.TryParse(_scene.Instruction.Text, _scene.Instruction.CaretIndex, out var mention))
        {
            _activeMention = mention;
            _scene.ShowMentionSearch(mention.Query);
            return;
        }

        if (_activeMention is null) return;
        _activeMention = null;
        _scene.HideAddMenu();
    }

    private void ConsumeActiveMention()
    {
        if (_activeMention is not { } mention) return;
        _activeMention = null;
        var text = _scene.Instruction.Text;
        if (mention.Start < 0 || mention.Length <= 0 || mention.Start + mention.Length > text.Length) return;
        if (text[mention.Start] != '@') return;
        _scene.Instruction.Text = text.Remove(mention.Start, mention.Length);
        _scene.Instruction.SetSelection(mention.Start, mention.Start);
    }

    private void ShowResolveProblems()
    {
        _scene.ShowResolveProblemsMenu(
            "Resolve Problems",
            "Choose what is going wrong. Haven will apply the selected recovery directly.",
            [
                ("Hallucinating", () => SetRecoveryDraft("Stop and audit your previous responses in this chat for hallucinations. Re-check every factual claim against the information actually available, clearly separate verified facts from assumptions, correct anything unsupported, and ask for missing information instead of guessing.")),
                ("Looping", () => SetRecoveryDraft("You are repeating the same approach. Stop the loop, briefly identify what has already been attempted and why it did not work, then choose a materially different approach and continue from there without repeating earlier steps.")),
                ("Recurring Bug in Produced Code", () => SetRecoveryDraft("The produced code has a recurring bug. Reproduce or trace the failure, compare it with the previous attempted fixes, identify the underlying root cause, then propose the smallest complete correction and explain how to verify that the same bug no longer recurs. Do not repeat an earlier patch unchanged.")),
                ("Something Else", () => SetRecoveryDraft("Review this conversation for the unresolved problem. Summarise the observed symptoms and attempted fixes, identify the most likely root cause, state what evidence is still missing, and propose the safest next step without claiming success until it is verified."))
            ]);
    }

    internal Task SubmitOverlayInstructionAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        SetRecoveryDraft(text.Trim());
        return Task.CompletedTask;
    }
    private void SetRecoveryDraft(string text)
    {
        if (_isSending)
        {
            _pendingInstruction = text;
            _pendingInstructionPreservesDraft = true;
            SetProjectionStatus("Stopping the current response and applying the recovery…");
            _sendCancellation?.Cancel();
            return;
        }

        var preservedDraft = _scene.Instruction.Text;
        if (_selectedModel is null)
        {
            _pendingInstruction = text;
            _pendingInstructionPreservesDraft = true;
            SetProjectionStatus("Connecting to the selected local model…");
            return;
        }
        SetDraft(text);
        _ = SubmitCurrentInstructionAsync();
        SetDraft(preservedDraft);
    }

    private async Task SubmitCurrentInstructionAsync()
    {
        var instruction = _scene.Instruction.Text.Trim();
        if (_isSending || string.IsNullOrWhiteSpace(instruction)) return;
        if (!await EnsureConversationMayActAsync("chat.send")) return;
        if (_selectedModel is null)
        {
            _pendingInstruction = instruction;
            _pendingInstructionPreservesDraft = false;
            SetProjectionStatus("Connecting to the selected local model…");
            return;
        }

        if (_dual is { IsActive: true })
        {
            if (!_dual.CanRun)
            {
                SetProjectionStatus(_dual.SecondModelKey is null
                    ? "Choose Model B beside the composer before sending in dual mode."
                    : "A dual comparison is already running.");
                return;
            }
            await RunDualComparisonAsync(instruction);
            return;
        }

        _pendingInstruction = null;
        _redoMessages.Clear();
        _scene.Instruction.Text = string.Empty;
        _bus.Fire("Chat.Composer.Send.Click");
        _isSending = true;
        _sendStartTick = Environment.TickCount64;
        _sendProgressTimer.Start();
        _sendCancellation = new CancellationTokenSource();
        RefreshVisualState();

        var now = DateTimeOffset.UtcNow;
        if (_messages.Count == 0)
        {
            var title = instruction.Length > 56 ? instruction[..53] + "â€¦" : instruction;
            _conversation = _conversation with { Title = title, UpdatedAt = now };
        }

        try
        {
            Guid? emittedUserMessageId = null;
            var sendReportedFailure = false;
            var deltaBuffer = new StringBuilder();
            Guid? bufferedMessageId = null;
            var nextDeltaFlushAt = Environment.TickCount64 + 50;

            async Task FlushDeltasAsync()
            {
                if (bufferedMessageId is not { } messageId || deltaBuffer.Length == 0) return;
                var delta = deltaBuffer.ToString();
                deltaBuffer.Clear();
                await Dispatcher.UIThread.InvokeAsync(() => ApplyStreamEvent(ChatStreamEvent.AssistantDelta(messageId, delta)));
            }

            await foreach (var streamEvent in _sessions.SendAsync(
                               _conversation,
                               instruction,
                               _selectedModel,
                               _effortOverride ?? _preferences.DefaultEffort,
                               [],
                               _activeAgent?.Name ?? "Haven",
                               _activeAgent?.Instructions ?? string.Empty,
                               DuoMode.Solo,
                               null,
                               null,
                               null,
                               _attachedImages.Count == 0 ? null : _attachedImages.ToArray(),
                               _sendCancellation.Token,
                               prompts: _activeInstructions.Select(item => new ActivePrompt(item.Name, item.IconKey, item.Persists, item.Instructions)).ToArray(),
                               registeredContext: BuildRegisteredContext(),
                               generationOptions: _preferences.GenerationOptions,
                               filePermission: _preferences.FilePermission,
                               commandPermission: _preferences.CommandPermission,
                               browserPermission: _preferences.BrowserPermission,
                               availableCapabilities: ActiveCapabilitiesForCurrentChat()).ConfigureAwait(false))
            {
                if (streamEvent.Kind == ChatStreamEventKind.AssistantDelta && streamEvent.MessageId is { } deltaMessageId)
                {
                    if (bufferedMessageId is not null && bufferedMessageId != deltaMessageId) await FlushDeltasAsync();
                    bufferedMessageId = deltaMessageId;
                    deltaBuffer.Append(streamEvent.Delta);
                    if (Environment.TickCount64 < nextDeltaFlushAt) continue;
                    await FlushDeltasAsync();
                    nextDeltaFlushAt = Environment.TickCount64 + 50;
                    continue;
                }
                if (streamEvent.Kind == ChatStreamEventKind.UserMessage && streamEvent.Message is not null)
                    emittedUserMessageId = streamEvent.Message.Id;
                else if (streamEvent.Kind == ChatStreamEventKind.PreflightFailed)
                    sendReportedFailure = true;
                await FlushDeltasAsync();
                await Dispatcher.UIThread.InvokeAsync(() => ApplyStreamEvent(streamEvent));
            }
            await FlushDeltasAsync();
            if (!sendReportedFailure && emittedUserMessageId is { } userMessageId)
                await AssociatePendingAttachmentsWithUserMessageAsync(userMessageId, CancellationToken.None);
            await SetStatusAsync(string.Empty);
        }
        catch (OperationCanceledException)
        {
            await SetStatusAsync("Response stopped.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            await SetStatusAsync("Haven could not complete that response: " + exception.Message);
        }
        finally
        {
            _sendProgressTimer.Stop();
            _sendCancellation?.Dispose();
            _sendCancellation = null;
            _isSending = false;
            await RefreshSafetyStateAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshVisualState();
                TrySubmitPendingInstruction();
            });
        }
    }

    private async void OnDualToggleRequested(object? sender, EventArgs e)
    {
        if (_dual is null) return;
        var activating = !_dual.IsActive;
        _dual.SetActive(activating);
        _scene.SetDualActive(activating);
        if (!activating)
        {
            SetProjectionStatus("Dual-model comparison off.");
            return;
        }
        if (_dual.SecondModelKey is not null)
        {
            SetProjectionStatus($"Dual-model comparison on — Model A {_selectedModel?.Name ?? "?"} vs {_dual.SecondModelKey}. Session only; nothing is saved.");
            return;
        }
        await TryAutoSelectDualSecondModelAsync();
    }

    /// <summary>Picks a sensible default Model B from the same installed-model list the model picker uses.</summary>
    private async Task TryAutoSelectDualSecondModelAsync()
    {
        var controller = _dual!;
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            if (_dual != controller || !controller.IsActive) return;
            var primary = _selectedModel?.Name;
            var candidate = models.Select(model => model.Name)
                .FirstOrDefault(name => !string.Equals(name, primary, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault()?.Name;
            if (candidate is null)
            {
                SetProjectionStatus("Dual mode is on, but no local model is available for Model B yet.");
                return;
            }
            controller.SetSecondModel(candidate);
            _scene.SetDualSecondModel(candidate);
            SetProjectionStatus($"Dual-model comparison on — Model A {primary ?? "primary"} vs {candidate}. Session only; nothing is saved.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            SetProjectionStatus("Dual mode is on, but installed models could not be listed: " + exception.Message);
        }
    }

    private async void OnDualModelPickerRequested(object? sender, EventArgs e)
    {
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var primary = _selectedModel?.Name;
            var keys = models.Select(model => model.Name)
                .Where(name => !string.Equals(name, primary, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (keys.Length == 0) keys = models.Select(model => model.Name).ToArray();
            if (keys.Length == 0)
            {
                SetProjectionStatus("No local models are installed for a dual comparison.");
                return;
            }
            _scene.ShowDualModelChoices(keys);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            SetProjectionStatus("Installed models could not be listed: " + exception.Message);
        }
    }

    private void OnDualSecondModelChosen(object? sender, string modelKey)
    {
        if (_dual is null || string.IsNullOrWhiteSpace(modelKey)) return;
        _dual.SetSecondModel(modelKey);
        _scene.SetDualSecondModel(modelKey);
        SetProjectionStatus($"Model B set to {modelKey}.");
    }

    /// <summary>
    /// Runs the side-by-side dual comparison instead of the persisted chat pipeline. Deliberately
    /// session-only and non-streaming: both sides render once from CompleteAsync results and are never
    /// written to conversation storage; per-side failures stay visible on their own labelled block.
    /// </summary>
    private async Task RunDualComparisonAsync(string instruction)
    {
        var controller = _dual!;
        var primaryKey = _selectedModel!.Name;
        _pendingInstruction = null;
        _redoMessages.Clear();
        _scene.Instruction.Text = string.Empty;
        _bus.Fire("Chat.Composer.Send.Click");
        _isSending = true;
        _sendStartTick = Environment.TickCount64;
        _sendProgressTimer.Start();
        _sendCancellation = new CancellationTokenSource();
        RefreshVisualState();

        var now = DateTimeOffset.UtcNow;
        if (_messages.Count == 0)
        {
            var title = instruction.Length > 56 ? instruction[..53] + "â€¦" : instruction;
            _conversation = _conversation with { Title = title, UpdatedAt = now };
        }
        UpsertMessage(new ChatMessage(Guid.NewGuid(), _conversation.Id, MessageRole.User, instruction, null, null, null, now));
        RefreshMessages();

        try
        {
            var run = await controller.RunAsync(
                instruction,
                primaryKey,
                _effortOverride ?? _preferences.DefaultEffort,
                _sendCancellation.Token);
            if (run is null)
            {
                await SetStatusAsync(controller.SecondModelKey is null
                    ? "Choose Model B beside the composer before running a dual comparison."
                    : "A dual comparison is already running.");
                return;
            }
            AppendDualSide(run.First);
            AppendDualSide(run.Second);
            await SetStatusAsync($"Dual comparison complete — Model A {FormatDualSideOutcome(run.First)}, Model B {FormatDualSideOutcome(run.Second)}.");
        }
        catch (OperationCanceledException)
        {
            await SetStatusAsync("Dual comparison stopped.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            await SetStatusAsync("Haven could not complete that dual comparison: " + exception.Message);
        }
        finally
        {
            _sendProgressTimer.Stop();
            _sendCancellation?.Dispose();
            _sendCancellation = null;
            _isSending = false;
            await RefreshSafetyStateAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshVisualState();
                TrySubmitPendingInstruction();
            });
        }
    }

    private void AppendDualSide(DualModelSide side)
    {
        var label = (side.Label == "First" ? "Model A" : "Model B") + $" — {side.ModelKey}";
        var content = side.Succeeded ? side.Content : $"This side failed: {side.Error}";
        var message = new ChatMessage(Guid.NewGuid(), _conversation.Id, MessageRole.Assistant, content, label, side.ModelKey, null, DateTimeOffset.UtcNow);
        UpsertMessage(message);
        RefreshMessage(message);
        ScrollToEndIfFollowing(true);
    }

    private static string FormatDualSideOutcome(DualModelSide side) =>
        $"{side.Duration.TotalSeconds:0.#}s{(side.Succeeded ? string.Empty : " (failed)")}";

    private void ApplyStreamEvent(ChatStreamEvent streamEvent)
    {
        switch (streamEvent.Kind)
        {
            case ChatStreamEventKind.UserMessage when streamEvent.Message is not null:
                UpsertMessage(streamEvent.Message);
                RefreshMessages();
                break;
            case ChatStreamEventKind.AssistantStarted when streamEvent.MessageId is { } assistantId:
                _streamingMessages.Add(assistantId);
                _thinkingStartTick[assistantId] = Environment.TickCount64;
                _thinkingEndTick.Remove(assistantId);
                _thinkingContent.Remove(assistantId);
                UpsertMessage(new ChatMessage(
                    assistantId,
                    _conversation.Id,
                    MessageRole.Assistant,
                    string.Empty,
                    streamEvent.Agent,
                    streamEvent.Model,
                    null,
                    DateTimeOffset.UtcNow));
                RefreshMessages();
                break;
            case ChatStreamEventKind.AssistantDelta when streamEvent.MessageId is { } deltaId:
            {
                var followLatest = IsFollowingLatest();
                var index = _messages.FindIndex(message => message.Id == deltaId);
                if (index < 0) break;
                _messages[index] = _messages[index] with { Content = _messages[index].Content + (streamEvent.Delta ?? string.Empty) };
                RefreshMessage(_messages[index]);
                ScrollToEndIfFollowing(followLatest);
                break;
            }
            case ChatStreamEventKind.ThinkingDelta when streamEvent.MessageId is { } thinkingId:
            {
                var followLatest = IsFollowingLatest();
                if (!_thinkingContent.ContainsKey(thinkingId))
                    _thinkingContent[thinkingId] = string.Empty;
                if (!_thinkingStartTick.ContainsKey(thinkingId))
                    _thinkingStartTick[thinkingId] = Environment.TickCount64;
                _thinkingContent[thinkingId] += streamEvent.Thinking ?? string.Empty;
                var message = _messages.FirstOrDefault(item => item.Id == thinkingId);
                if (message is not null)
                {
                    RefreshMessage(message);
                    ScrollToEndIfFollowing(followLatest);
                }
                break;
            }
            case ChatStreamEventKind.AssistantCompleted when streamEvent.Message is not null:
            {
                var followLatest = IsFollowingLatest();
                _streamingMessages.Remove(streamEvent.Message.Id);
                if (_thinkingStartTick.ContainsKey(streamEvent.Message.Id) && !_thinkingEndTick.ContainsKey(streamEvent.Message.Id))
                    _thinkingEndTick[streamEvent.Message.Id] = Environment.TickCount64;
                UpsertMessage(streamEvent.Message);
                RefreshMessage(streamEvent.Message);
                ScrollToEndIfFollowing(followLatest);
                break;
            }
            case ChatStreamEventKind.ToolActivity when streamEvent.ToolActivity is { } activity && streamEvent.MessageId is { } toolMessageId:
            {
                var followLatest = IsFollowingLatest();
                
                var toolMessageIndex = _messages.FindIndex(message => message.Id == toolMessageId);
                if (toolMessageIndex >= 0)
                {
                    _messages[toolMessageIndex] = AppendToolActivity(_messages[toolMessageIndex], activity);
                    RefreshMessage(_messages[toolMessageIndex]);
                    ScrollToEndIfFollowing(followLatest);
                }
                break;
            }
            case ChatStreamEventKind.PreflightFailed:
                SetProjectionStatus(streamEvent.PreflightResult is { Missing.Count: > 0 } preflight
                    ? string.Join(" ", preflight.Missing.Select(item => item.Reason))
                    : "The selected model cannot complete this request.");
                break;
        }
    }

    private void RefreshMessages()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshMessages);
            return;
        }
        var followLatest = IsFollowingLatest();
        PruneGeneratedSurfaces();
        var sceneMessages = _messages.Select(BuildSceneMessage).ToArray();
        _scene.SyncMessages(sceneMessages);
        foreach (var message in _messages.Where(message => message.Role == MessageRole.Assistant)) RefreshGeneratedContent(message);
        if (_lastReportedHasStarted != HasStarted)
        {
            _lastReportedHasStarted = HasStarted;
            ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        }
        PublishProjectionState();
        ScrollToEndIfFollowing(followLatest);
    }

    private void RefreshMessage(ChatMessage message)
    {
        _scene.UpdateMessage(BuildSceneMessage(message));
        if (message.Role == MessageRole.Assistant) RefreshGeneratedContent(message);
        PublishProjectionState();
    }

    private bool IsFollowingLatest() =>
        ChatTranscriptScrollPolicy.ShouldFollow(_scene.Messages.MaxScrollY, _scene.Messages.ScrollY);

    private void ScrollToEndIfFollowing(bool wasFollowing)
    {
        if (wasFollowing) Dispatcher.UIThread.Post(_scene.ScrollToEnd, DispatcherPriority.Background);
    }

    private static IReadOnlyList<ToolActivity> ReadToolActivities(ChatMessage message)
    {
        if (!message.Metadata.TryGetValue("toolActivities", out var value) || value.ValueKind != JsonValueKind.Array) return [];
        try
        {
            return value.Deserialize<ToolActivity[]>() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ChatMessage AppendToolActivity(ChatMessage message, ToolActivity activity)
    {
        var activities = ReadToolActivities(message).Where(item => item.Id != activity.Id).Append(activity).ToArray();
        var metadata = message.Metadata.Where(pair => !string.Equals(pair.Key, "toolActivities", StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        metadata["toolActivities"] = activities;
        return message with { MetadataJson = JsonSerializer.Serialize(metadata) };
    }

    private ChatSceneMessage BuildSceneMessage(ChatMessage message)
    {
        var directive = message.Role == MessageRole.Assistant
            ? GenUiChatDirectiveParser.Parse(message.Content)
            : new GenUiChatDirectiveParseResult(message.Content, [], null, false);
        var display = directive.DisplayContent;
        if (message.Role == MessageRole.Assistant && string.IsNullOrWhiteSpace(display) && directive.HasDirective && directive.Requests.Count == 0)
            display = "Generating interactive contentâ€¦";
        var thinking = string.Empty;
        if (_thinkingContent.TryGetValue(message.Id, out var content) && !string.IsNullOrWhiteSpace(content))
        {
            var elapsed = 0L;
            if (_thinkingStartTick.TryGetValue(message.Id, out var start))
            {
                var end = _thinkingEndTick.TryGetValue(message.Id, out var completed) ? completed : Environment.TickCount64;
                elapsed = Math.Max(0, (end - start) / 1000);
            }
            thinking = (elapsed > 0 ? $"Thought for {elapsed} seconds\n" : "Thinkingâ€¦\n") + content;
        }
        var isStreaming = _streamingMessages.Contains(message.Id);
        if (message.Role == MessageRole.Assistant && _thinkingStartTick.TryGetValue(message.Id, out var responseStart))
        {
            var responseEnd = _thinkingEndTick.TryGetValue(message.Id, out var completed) ? completed : Environment.TickCount64;
            var elapsed = Math.Max(0, (responseEnd - responseStart) / 1000);
            _thinkingContent.TryGetValue(message.Id, out var detail);
            thinking = ChatProgressText.Format(isStreaming, elapsed, detail);
        }
        return new ChatSceneMessage(
            message.Id,
            message.Role,
            display ?? string.Empty,
            message.AgentName ?? "Haven",
            _streamingMessages.Contains(message.Id),
            thinking,
            ReadToolActivities(message));
    }

    private void RefreshGeneratedContent(ChatMessage message)
    {
        var directive = GenUiChatDirectiveParser.Parse(message.Content);
        var generated = new List<HavenElement>();
        if (_streamingMessages.Contains(message.Id) && directive.Requests.Count == 0 && (directive.HasDirective || ShouldExpectGeneratedSurface(message)))
        {
            var preview = new Text { Content = "Preparing interactive contentâ€¦" };
            preview.SetValue(HavenProperties.Foreground, "TextSoft");
            generated.Add(preview);
        }
        var needsRecovery = false;
        for (var surfaceIndex = 0; surfaceIndex < directive.Requests.Count; surfaceIndex++)
        {
            try
            {
                var surface = GetOrCreateGeneratedSurface(message, surfaceIndex, directive.Requests[surfaceIndex]);
                generated.Add(surface.Root);
                if (surface.Document is { } document)
                {
                    foreach (var issue in GenUiDocumentQualityValidator.Validate(document))
                    {
                        var warning = new Text { Content = "GenUI self-check: " + issue.Message };
                        warning.SetValue(HavenProperties.Foreground, "Danger");
                        generated.Add(warning);
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                needsRecovery = true;
                var warning = new Text { Content = $"Could not render {directive.Requests[surfaceIndex].TemplateKey}: {exception.Message}" };
                warning.SetValue(HavenProperties.Foreground, "Danger");
                generated.Add(warning);
            }
        }
        RemoveGeneratedSurfacesAfter(message.Id, directive.Requests.Count);
        if (!string.IsNullOrWhiteSpace(directive.Error))
        {
            needsRecovery = true;
            var warning = new Text { Content = "Interactive content could not be rendered: " + directive.Error };
            warning.SetValue(HavenProperties.Foreground, "Danger");
            generated.Add(warning);
        }
        if (needsRecovery && !_streamingMessages.Contains(message.Id))
        {
            generated.Add(ChatGeneratedContentRecovery.CreateRetryButton(
                () => _ = RegenerateResponseAsync(message, ResponseRegenerationMode.Here)));
        }
        if (generated.Count == 0) _scene.ClearGeneratedContent(message.Id);
        else _scene.SetGeneratedContent(message.Id, generated);
    }

    private ChatGenUiSurfaceMount GetOrCreateGeneratedSurface(ChatMessage message, int surfaceIndex, GenUiTemplateRequest request)
    {
        var slot = (message.Id, surfaceIndex);
        var signature = request.Signature;
        if (_generatedSurfaces.TryGetValue(slot, out var mounted)
            && _generatedSignatures.TryGetValue(slot, out var mountedSignature)
            && mountedSignature.Equals(signature, StringComparison.Ordinal))
            return mounted;

        if (_generatedSurfaces.Remove(slot, out var previousSurface)) previousSurface.Dispose();
        GenUiDocument? document = null;
        var reuse = _generatedSignatures.TryGetValue(slot, out var previousSignature)
                    && previousSignature.Equals(signature, StringComparison.Ordinal)
                    && _generatedInstanceIds.TryGetValue(slot, out var existingInstanceId)
                    && (document = _genUiInstances.TryGet(existingInstanceId)) is not null;

        var appKey = _modeDefinition?.Key ?? (_isTaskMode ? "tasks" : "chat");
        GenUiGenerationPlan? plan = null;
        GenUiGenerationSpecification? specification = null;
        if (!reuse)
        {
            if (_generatedInstanceIds.Remove(slot, out var obsoleteInstance)) _genUiInstances.Remove(obsoleteInstance);
            document = CreateGeneratedDocument(request, appKey);
            if (!string.IsNullOrWhiteSpace(request.AccentKey)) document = document with { AccentKey = request.AccentKey };
            plan = new GenUiGenerationPlan(
                Intent: $"Render {request.TemplateKey} interactive content for chat message {message.Id:N}",
                AppKey: appKey,
                TemplateKey: request.TemplateKey);
            specification = GenUiGenerationPipeline.CreateSpecification(plan, document);
        }

        var rendering = specification?.Definition.Rendering ?? GenUiRenderingLayerSelector.Select(document!);
        var surface = ChatGenUiSurfaceMount.Create(rendering, _genUiRouter, _genUiInstances, _genUiNativeResolver);
        if (reuse)
        {
            surface.PresentExisting(document!);
        }
        else
        {
            var pipeline = GenUiGenerationPipeline.Execute(
                plan!, specification!, surface.Present,
                definition => GenUiGenerationPipeline.InspectRegisteredRuntime(definition, _genUiInstances));
            document = pipeline.Definition.Document;
        }
        _generatedSurfaces[slot] = surface;
        _generatedInstanceIds[slot] = document!.Origin.InstanceId;
        _generatedSignatures[slot] = signature;
        return surface;
    }

    private GenUiDocument CreateGeneratedDocument(GenUiTemplateRequest request, string appKey) =>
        request.TemplateKey switch
        {
            "calculator" => _calculatorTemplate.Create(_conversation.Id, appKey, request.Expression),
            "structured-form" => _structuredFormTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "choice-prompt" => _choicePromptTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "checklist" => _checklistTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "data-grid" => _dataGridTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "card-deck" => _cardDeckTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "graph" => _graphTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "task-list" => _taskListTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "dashboard" => _dashboardTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "assessment" => _assessmentTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "workflow" => _workflowTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "custom" => _customTemplate.Create(_conversation.Id, appKey, request.Inputs),
            _ => throw new InvalidOperationException($"Live GenUI template '{request.TemplateKey}' has no trusted runtime.")
        };

    private void PruneGeneratedSurfaces()
    {
        var messageIds = _messages.Select(message => message.Id).ToHashSet();
        foreach (var slot in _generatedSurfaces.Keys.Where(slot => !messageIds.Contains(slot.MessageId)).ToArray()) RemoveGeneratedSurface(slot);
    }

    private void RemoveGeneratedSurfacesAfter(Guid messageId, int keepCount)
    {
        foreach (var slot in _generatedSurfaces.Keys.Where(slot => slot.MessageId == messageId && slot.SurfaceIndex >= keepCount).ToArray())
            RemoveGeneratedSurface(slot);
    }

    private void RemoveGeneratedSurface((Guid MessageId, int SurfaceIndex) slot)
    {
        if (_generatedSurfaces.Remove(slot, out var surface)) surface.Dispose();
        if (_generatedInstanceIds.Remove(slot, out var instanceId)) _genUiInstances.Remove(instanceId);
        _generatedSignatures.Remove(slot);
    }

    private void ClearGeneratedSurfaces()
    {
        foreach (var slot in _generatedSurfaces.Keys.ToArray()) RemoveGeneratedSurface(slot);
        _generatedInstanceIds.Clear();
        _generatedSignatures.Clear();
    }

    private bool ShouldExpectGeneratedSurface(ChatMessage assistantMessage)
    {
        var responseMode = _chatGenerativeUiResponseModeOverride ?? GenerativeUiResponseMode.Auto;
        if (responseMode == GenerativeUiResponseMode.AlwaysVisual) return true;
        var index = _messages.FindIndex(message => message.Id == assistantMessage.Id);
        var prompt = index > 0 ? _messages.Take(index).LastOrDefault(message => message.Role == MessageRole.User)?.Content : null;
        if (string.IsNullOrWhiteSpace(prompt)) return responseMode == GenerativeUiResponseMode.PreferVisual;
        string[] terms = ["generative ui", "generate ui", "generated ui", "interactive ui", "flashcard", "whiteboard", "dashboard", "calculator", "data grid", "interactive form", "quiz", "assessment", "workflow", "task list", "graph", "chart", "visual response"];
        return terms.Any(term => prompt.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private async void OnMessageActionRequested(object? sender, ChatMessageActionRequest request)
    {
        var message = _messages.FirstOrDefault(item => item.Id == request.MessageId);
        if (message is null) return;
        if (request.Action is ChatMessageAction.Regenerate or ChatMessageAction.Branch or ChatMessageAction.Edit
            && !await EnsureConversationMayActAsync($"chat.message.{request.Action.ToString().ToLowerInvariant()}"))
            return;
        switch (request.Action)
        {
            case ChatMessageAction.Copy:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(message.Content);
                break;
            case ChatMessageAction.Delete:
                await DeleteMessageAsync(message);
                break;
            case ChatMessageAction.Forget:
                await _conversations.MarkMessagesCompactedAsync(_conversation.Id, [message.Id], CancellationToken.None);
                _messages.RemoveAll(item => item.Id == message.Id);
                RefreshMessages();
                break;
            case ChatMessageAction.Regenerate:
                ShowRegenerateChoices(message);
                break;
            case ChatMessageAction.Branch:
                ShowBranchChoices(message);
                break;
            case ChatMessageAction.Edit:
                ShowEditChoices(message);
                break;
        }
    }

    private async void OnMarkdownCodeActionRequested(object? sender, ChatMarkdownCodeActionRequest request)
    {
        if (request.Request.Action == Haven.UI.Components.MarkdownCodeAction.Copy)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(request.Request.Code);
            return;
        }
        var verb = request.Request.Action == Haven.UI.Components.MarkdownCodeAction.AskToRun ? "Run" : "Apply";
        var language = string.IsNullOrWhiteSpace(request.Request.Language) ? string.Empty : request.Request.Language;
        SetDraft($"{verb} this code safely and explain the result:\n```{language}\n{request.Request.Code}\n```");
    }

    private void ShowRegenerateChoices(ChatMessage message)
    {
        _scene.ShowMessageChoiceMenu(
            message.Id,
            "Re-generate response",
            [
                ("Re-generate in current chat", () => _ = RegenerateResponseAsync(message, ResponseRegenerationMode.Here)),
                ("Re-generate in branch", () => _ = RegenerateResponseAsync(message, ResponseRegenerationMode.NewBranch))
            ]);
    }

    private void ShowBranchChoices(ChatMessage message)
    {
        _scene.ShowMessageChoiceMenu(
            message.Id,
            "Branch message",
            [
                ("Branch in new chat", () => _ = BranchIntoNewChatAsync(message)),
                ("Branch in existing chat", () => _ = ShowExistingChatPickerAsync(message))
            ]);
    }

    private void ShowEditChoices(ChatMessage message)
    {
        _scene.ShowMessageChoiceMenu(
            message.Id,
            "Edit message",
            [
                ("Restart from here", () => ShowMessageEditor(message, MessageEditChoice.RestartHere)),
                ("Edit in new branch", () => ShowMessageEditor(message, MessageEditChoice.NewBranch)),
                ("Edit in memory only", () => ShowMessageEditor(message, MessageEditChoice.MemoryOnly))
            ]);
    }

    private void ShowMessageEditor(ChatMessage message, MessageEditChoice choice)
    {
        _scene.ShowTextPrompt(
            "Edit message",
            choice == MessageEditChoice.MemoryOnly ? "This edit changes only the current in-memory session." : "The conversation versioning service will preserve history semantics.",
            message.Content,
            choice == MessageEditChoice.MemoryOnly ? "Apply for this session" : "Apply edit",
            content => ApplyMessageEditAsync(message, content, choice));
    }

    private async Task RegenerateResponseAsync(ChatMessage message, ResponseRegenerationMode requestedMode)
    {
        if (!await EnsureConversationMayActAsync("chat.regenerate")) return;
        try
        {
            var index = _messages.FindIndex(item => item.Id == message.Id);
            if (index < 0) return;
            var precedingUser = _messages.Take(index).LastOrDefault(item => item.Role == MessageRole.User)
                                ?? throw new InvalidOperationException("This response has no preceding user message.");
            var latestAssistant = _messages.LastOrDefault(item => item.Role == MessageRole.Assistant);
            var isLatest = latestAssistant?.Id == message.Id;
            var mode = isLatest ? requestedMode : ResponseRegenerationMode.NewBranch;
            await _versioning.PrepareRegenerationAsync(_conversation.Id, message.Id, isLatest, mode, CancellationToken.None);
            var persisted = await _conversations.GetMessagesAsync(_conversation.Id, CancellationToken.None);
            _messages.Clear();
            _messages.AddRange(persisted);
            RefreshMessages();
            Submit(precedingUser.Content);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            SetProjectionStatus(exception.Message);
        }
    }

    private async Task ApplyMessageEditAsync(ChatMessage message, string content, MessageEditChoice choice)
    {
        if (!await EnsureConversationMayActAsync("chat.edit")) return;
        var index = _messages.FindIndex(item => item.Id == message.Id);
        if (index < 0) return;
        if (choice == MessageEditChoice.MemoryOnly)
        {
            _messages[index] = message with { Content = content };
            RefreshMessages();
            return;
        }
        try
        {
            if (message.Role == MessageRole.User)
            {
                await _versioning.EditUserMessageAsync(
                    _conversation.Id,
                    message.Id,
                    content,
                    choice == MessageEditChoice.NewBranch ? MessageEditMode.NewBranch : MessageEditMode.OverwriteCurrentBranch,
                    CancellationToken.None);
                var persisted = await _conversations.GetMessagesAsync(_conversation.Id, CancellationToken.None);
                _messages.Clear();
                _messages.AddRange(persisted);
            }
            else
            {
                var updated = message with { Content = content };
                await _conversations.AddMessageAsync(updated, CancellationToken.None);
                _messages[index] = updated;
                if (choice == MessageEditChoice.RestartHere && index + 1 < _messages.Count)
                    _messages.RemoveRange(index + 1, _messages.Count - index - 1);
            }
            RefreshMessages();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            SetProjectionStatus(exception.Message);
        }
    }

    private async Task BranchIntoNewChatAsync(ChatMessage throughMessage)
    {
        if (!await EnsureConversationMayActAsync("chat.branch")) return;
        await SavePendingAttachmentDraftAsync();
        var index = _messages.FindIndex(message => message.Id == throughMessage.Id);
        if (index < 0) return;
        var source = _conversation;
        var now = DateTimeOffset.UtcNow;
        var branch = new Conversation(
            Guid.NewGuid(), source.Mode, source.Kind, $"Branch of {source.Title}", source.ContainerId, source.LessonId,
            false, source.IsTemporary, now, now, ParentConversationId: source.Id);
        var copies = _messages.Take(index + 1)
            .Select((message, order) => message with { Id = Guid.NewGuid(), ConversationId = branch.Id, CreatedAt = now.AddTicks(order) })
            .ToArray();
        await _conversations.UpsertConversationAsync(branch, CancellationToken.None);
        foreach (var copy in copies) await _conversations.AddMessageAsync(copy, CancellationToken.None);
        _conversation = branch;
        ClearPendingPersistedAttachmentsFromComposer();
        _messages.Clear();
        _messages.AddRange(copies);
        ClearGeneratedSurfaces();
        RefreshMessages();
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        FocusComposer();
    }

    private async Task ShowExistingChatPickerAsync(ChatMessage sourceMessage)
    {
        if (!await EnsureConversationMayActAsync("chat.branch-existing")) return;
        var recent = (await _conversations.GetRecentAsync(_conversation.Mode, 12, CancellationToken.None))
            .Where(item => item.Id != _conversation.Id && !item.IsArchived)
            .ToArray();
        var choices = recent
            .Select<Conversation, (string Label, Action Action)>(conversation =>
                (string.IsNullOrWhiteSpace(conversation.Title) ? "Untitled chat" : conversation.Title,
                    () => _ = ContinueInExistingChatAsync(conversation, sourceMessage)))
            .ToArray();
        if (choices.Length == 0)
        {
            SetProjectionStatus("No other saved chats yet.");
            return;
        }
        _scene.ShowMessageChoiceMenu(sourceMessage.Id, "Choose an existing chat", choices);
    }

    private async Task ContinueInExistingChatAsync(Conversation target, ChatMessage sourceMessage)
    {
        if (!await EnsureConversationMayActAsync("chat.branch-existing")) return;
        await SavePendingAttachmentDraftAsync();
        await LoadConversationAsync(target);
        SetDraft(sourceMessage.Content);
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeleteMessageAsync(ChatMessage message)
    {
        try
        {
            await _conversations.DeleteMessageAsync(_conversation.Id, message.Id, CancellationToken.None);
            _messages.RemoveAll(item => item.Id == message.Id);
            RefreshMessages();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or IOException)
        {
            SetProjectionStatus(exception.Message);
        }
    }

    private void ResetToFreshConversation(HavenMode mode, Guid? containerId, Guid? lessonId, Guid? spaceId = null)
    {
        _sendCancellation?.Cancel();
        _conversation = CreateConversation(mode, containerId, lessonId, spaceId);
        _activeAgent = null;
        _activeInstructions.Clear();
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _registeredContextOverride = null;
        _effortOverride = null;
        _attachedImages.Clear();
        _attachedContext.Clear();
        _pendingAttachmentIds.Clear();
        _persistedAttachments.Clear();
        _attachmentSourcePaths.Clear();
        _taskAttachments.Clear();
        _messages.Clear();
        _redoMessages.Clear();
        _safetyLocked = false;
        _scene.SetSafetyLocked(false);
        _streamingMessages.Clear();
        _thinkingContent.Clear();
        _thinkingStartTick.Clear();
        _thinkingEndTick.Clear();
        _pendingInstruction = null;
        _pendingInstructionPreservesDraft = false;
        ClearGeneratedSurfaces();
        SetProjectionStatus(null);
        RefreshAttachmentStatus();
        RefreshResponseControls();
        _ = RefreshContextEntriesAsync();
        RefreshMessages();
    }

    private async Task PersistFreshConversationAsync(Conversation conversation)
    {
        try
        {
            await _conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            await SetStatusAsync("The new chat could not be saved yet. It will be retried when you send a message.");
        }
    }

    private void NotifyFreshConversationReady()
    {
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        FocusComposer();
    }

    private void AttachCapability(CapabilityDefinition capability)
    {
        var owner = _availableApps.FirstOrDefault(app => app.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase));
        _taskAttachments.AttachCapability(capability, owner);
        SetProjectionStatus($"{capability.Name} attached as chat relevance; permissions are unchanged.");
        RefreshAttachmentStatus();
    }

    private void RefreshAttachmentStatus() =>
        _scene.SetAttachmentChips(BuildAttachmentChips());

    /// <summary>
    /// Reloads the persisted context rows for the active conversation into the scene Context card,
    /// using the same conversation_context source that registered-context injection reads.
    /// </summary>
    private async Task RefreshContextEntriesAsync()
    {
        var conversationId = _conversation.Id;
        IReadOnlyList<ConversationContextEntry> entries = [];
        try
        {
            if (!_conversation.IsTemporary)
                entries = await _conversations.GetContextEntriesAsync(conversationId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            if (_conversation.Id == conversationId)
                SetProjectionStatus("Context entries could not be loaded: " + exception.Message);
            return;
        }
        if (_conversation.Id != conversationId || _disposed) return;
        var mapped = entries.Select(entry => new ChatSceneContextEntry(
            entry.Id,
            ContextEntryViewModel.CategoryLabelFor(entry.Kind),
            entry.Title,
            TruncatePreview(entry.Content),
            entry.Kind != ContextEntryKind.CompactSummary)).ToArray();
        _scene.SetContextEntries(BuildContextSummaryLine(entries), mapped);
    }

    /// <summary>
    /// Deletes one removable persisted context row; protected compact summaries are refused with an explanation.
    /// </summary>
    private async Task RemoveContextEntryAsync(Guid entryId)
    {
        if (_conversation.IsTemporary) return;
        var conversationId = _conversation.Id;
        bool removed;
        try
        {
            removed = await _conversations.DeleteContextEntryAsync(conversationId, entryId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            if (_conversation.Id == conversationId)
                SetProjectionStatus("That context entry could not be removed: " + exception.Message);
            return;
        }
        if (_conversation.Id != conversationId || _disposed) return;
        if (!removed)
        {
            var remaining = await _conversations.GetContextEntriesAsync(conversationId, CancellationToken.None);
            if (remaining.Any(entry => entry.Id == entryId && entry.Kind == ContextEntryKind.CompactSummary))
                SetProjectionStatus("Compact summaries cannot be removed individually.");
            else
                SetProjectionStatus("That context entry is no longer available.");
            return;
        }
        await RefreshContextEntriesAsync();
        SetProjectionStatus("Context entry removed from this conversation.");
    }

    /// <summary>
    /// Builds the estimated usage line for the Context card header from the same character estimate used elsewhere.
    /// </summary>
    private string BuildContextSummaryLine(IReadOnlyList<ConversationContextEntry> entries)
    {
        var characters = _messages.Sum(message => message.Content.Length) +
                         entries.Sum(entry => entry.Content.Length + entry.Evidence.Length) +
                         _activeInstructions.Sum(item => item.Instructions.Length) +
                         _attachedContext.Values.Sum(value => value.Length);
        var tokens = Math.Max(0, (int)Math.Ceiling(characters / 3.7d));
        var limit = Math.Max(1, _preferences.GenerationOptions.ContextLimit);
        var percent = Math.Clamp((int)Math.Round(tokens * 100d / limit), 0, 100);
        var count = entries.Count;
        return $"{count} context entr{(count == 1 ? "y" : "ies")} · estimated {tokens:N0} / {limit:N0} tokens ({percent}%)";
    }

    private static string TruncatePreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var firstLine = content.Split('\n', 2)[0].Trim();
        return firstLine.Length <= 160 ? firstLine : firstLine[..157] + "…";
    }

    private IReadOnlyList<ActiveCapability> ActiveCapabilitiesForCurrentChat()
    {
        IEnumerable<CapabilityDefinition> allowed = EffectiveChatActionMode switch
        {
            ChatActionMode.JustChat => [],
            ChatActionMode.AllowBasicActions => _availableCapabilities.Where(item => item.RiskClass is CapabilityRiskClass.ReadOnly or CapabilityRiskClass.Low),
            _ => _availableCapabilities
        };
        return allowed.Select(ActiveCapability.FromDefinition).ToArray();
    }

    private string? BuildRegisteredContext()
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(_registeredContextOverride)) sections.Add(_registeredContextOverride);
        if (_modeDefinition is { } mode)
        {
            sections.Add($"Active Haven app: {mode.Name}.\nPurpose: {mode.Description}");
            if (!string.IsNullOrWhiteSpace(mode.SystemPromptSuffix)) sections.Add(mode.SystemPromptSuffix.Trim());
        }
        if (_taskAttachments.BuildAppContext() is { } appContext) sections.Add(appContext);
        if (_taskAttachments.BuildCapabilityContext() is { } capabilityContext) sections.Add(capabilityContext);
        sections.AddRange(_attachedContext.Values.Where(item => !string.IsNullOrWhiteSpace(item)));
        sections.Add(GenUiChatDirectiveParser.ModelInstructionFor(_chatGenerativeUiResponseModeOverride ?? GenerativeUiResponseMode.Auto));
        return sections.Count == 0 ? null : string.Join("\n\n", sections);
    }

    private void UpsertMessage(ChatMessage message)
    {
        var index = _messages.FindIndex(existing => existing.Id == message.Id);
        if (index < 0) _messages.Add(message);
        else _messages[index] = message;
    }

    private void TrySubmitPendingInstruction()
    {
        if (_selectedModel is null || _isSending || string.IsNullOrWhiteSpace(_pendingInstruction)) return;
        var pending = _pendingInstruction;
        var restoreDraft = _pendingInstructionPreservesDraft ? _scene.Instruction.Text : null;
        _scene.Instruction.Text = pending;
        _pendingInstruction = null;
        _pendingInstructionPreservesDraft = false;
        _ = SubmitCurrentInstructionAsync();
        if (restoreDraft is not null) SetDraft(restoreDraft);
    }

    private void RefreshResponseControls() =>
        _scene.SetResponseState(
            ActiveAgentName,
            EffectiveChatActionMode,
            _chatGenerativeUiResponseModeOverride ?? GenerativeUiResponseMode.Auto);

    private void RefreshVisualState()
    {
        _scene.SetSending(_isSending, _selectedModel is not null);
        RefreshMessages();
    }

    private async Task SetStatusAsync(string status) =>
        await Dispatcher.UIThread.InvokeAsync(() => SetProjectionStatus(string.IsNullOrWhiteSpace(status) ? null : status));

    private void SetProjectionStatus(string? status)
    {
        _projectionStatus = string.IsNullOrWhiteSpace(status) ? null : status;
        _scene.SetStatus(_projectionStatus);
        PublishProjectionState();
    }

    private void PublishProjectionState()
    {
        // Rebuilding the projection snapshots every message on each stream delta.
        // Mark dirty and defer that work until a consumer observes the state.
        _projectionDirty = true;
        if (ProjectionStateChanged is null) return;
        RebuildProjectionState();
        ProjectionStateChanged?.Invoke(this, new ChatProjectionStateChangedEventArgs(_projectionState));
    }

    private void RebuildProjectionState()
    {
        _projectionDirty = false;
        var messages = _messages.Select(message => new ChatProjectionMessage(
            message.Id,
            message.Role,
            message.Content,
            message.AgentName,
            message.ModelName,
            _streamingMessages.Contains(message.Id),
            ReadToolActivities(message)
                .Select(activity => new ChatProjectionToolActivity(
                    activity.Id,
                    activity.Title,
                    activity.Detail,
                    activity.Succeeded,
                    activity.Duration,
                    activity.Timestamp,
                    activity.LinesAdded,
                    activity.LinesRemoved))
                .ToImmutableArray(),
            message.CreatedAt)).ToArray();
        _projectionState = ChatProjectionState.Create(
            _conversation.Id,
            _conversation.Title,
            _modeDefinition?.Name ?? (_isTaskMode ? "Tasks" : "Chat"),
            _selectedModel?.Name,
            ActiveAgentName,
            _isSending,
            _projectionStatus,
            messages);
    }

    private static string ActionModeLabel(ChatActionMode mode) => mode switch
    {
        ChatActionMode.AllowAllActions => "Allow All Actions",
        ChatActionMode.JustChat => "Just Chat",
        _ => "Allow Basic Actions"
    };

    private static string VisualResponseModeLabel(GenerativeUiResponseMode mode) => mode switch
    {
        GenerativeUiResponseMode.AlwaysVisual => "Always Visual",
        GenerativeUiResponseMode.PreferVisual => "Prefer Visual",
        GenerativeUiResponseMode.PreferText => "Prefer Text",
        GenerativeUiResponseMode.AlwaysText => "Always Text",
        _ => "Auto"
    };

    private static Conversation CreateConversation(HavenMode mode, Guid? containerId = null, Guid? lessonId = null, Guid? spaceId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var kind = mode switch
        {
            HavenMode.Study when lessonId is not null => ConversationKind.LessonChat,
            HavenMode.Study => ConversationKind.QuickChat,
            HavenMode.Tasks => ConversationKind.Task,
            HavenMode.Studio => ConversationKind.StudioChat,
            _ => ConversationKind.Chat
        };
        if (mode == HavenMode.Study && lessonId is null) containerId = null;
        return new Conversation(
            Guid.NewGuid(),
            mode,
            kind,
            mode == HavenMode.Study ? "New study chat" : mode == HavenMode.Tasks ? "New task" : "New chat",
            containerId,
            lessonId,
            false,
            false,
            now,
            now,
            SpaceId: spaceId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        _sendProgressTimer.Stop();
        _scene.StopRequested -= OnStopRequested;
        _scene.Instruction.Invalidated -= OnComposerEditCompleted;
        _scene.AttachmentRemoveRequested -= OnAttachmentRemoveRequested;
        Scene.InputSubmitted -= OnInputSubmitted;
        Scene.PointerPressedOutside -= _scene.HideAddMenu;
        ClearGeneratedSurfaces();
        _scene.Dispose();
    }

    private enum MessageEditChoice
    {
        RestartHere,
        NewBranch,
        MemoryOnly
    }
}
