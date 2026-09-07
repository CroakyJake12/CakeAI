using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Automations;

using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Browser;
using Haven.Desktop.Views.Pages.Catalog;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.ContainerSettings;
using Haven.Desktop.Views.Pages.ActionGraph;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Pages.Home;
using Haven.Desktop.Views.Pages.Mail;
using Haven.Desktop.Views.Pages.Plan;
using Haven.Desktop.Views.Pages.Play;
using Haven.Desktop.Views.Pages.ProjectPreview;
using Haven.Desktop.Views.Pages.Settings;
using Haven.Desktop.Views.Pages.StudioProject;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Desktop.Views.Pages.Terminal;
using Haven.Desktop.Views.Pages.WorkspaceEditor;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView : UserControl, INotifyPropertyChanged, IDisposable
{
#pragma warning disable CS8618
    private readonly IConversationRepository _conversations;
    private readonly HavenEventBus _bus;
    private readonly IContainerRepository _containers;
    private readonly ICatalogRepository _catalog;
    private readonly AgentTaskRuntimeService? _agentRuntime;
    private readonly IAutomationRepository _automations;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly IWorkspaceToolService _workspaceTools;
    private readonly IProjectIntelligenceService _projectIntelligence;
    private readonly IOllamaClient _ollama;
    private readonly ChatSessionService _sessions;
    private readonly IConversationSafetyService _conversationSafety;
    private readonly IConversationVersioningService _conversationVersioning;
    private readonly CapabilityPreflightService _preflight;
    private readonly CapabilityRegistryService _capabilityRegistry;
    private readonly ICapabilityRepository _capabilityRepository;
    private readonly IGenUiTemplateRepository _genUiTemplates;
    private readonly GenerativeUiEventRouter _genUiRouter;
    private readonly GenUiInstanceStore _genUiInstances;
    private readonly IGenUiAppRepository _genUiApps;
    private readonly GenUiAppSessionService _genUiSessions;
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
    private readonly BrowserSessionService _browser;
    private readonly BrowserDataService _browserData;

    private readonly ScheduledTaskRunner _automationRunner;
    private readonly ScheduledTaskScheduleCalculator _scheduleCalculator;
    private readonly UserPreferencesService _preferences;
    private readonly IPrivacyPreferenceStore _privacy;
    private readonly IModelProviderRegistry _modelProviders;
    private readonly IProviderConfigurationStore _providerConfigurations;
    private readonly IProviderSecretStore _providerSecrets;
    private readonly GoSuggestionService _goSuggestions;
    private readonly Dictionary<GoPage, CancellationTokenSource> _goSuggestionRefreshes = [];
    private Flyout? _modelSelectorFlyout;
    private readonly ProjectCreationService _projectCreator;
    private readonly NotificationService _notifications;
    private readonly ITrainingRepository _trainingRepo;
    private readonly IContainerResourceRepository _containerResources;
    private readonly IDashboardRepository _dashboard;
    private readonly IDashboardLayoutRepository _dashboardLayout;
    private readonly IVersionedSettingsStore _versionedSettings;
    private readonly Haven.Application.Play.PlaySessionService _playSessions;
    private readonly IReadOnlyList<IDashboardTileProvider> _dashboardProviders;
    private readonly ICallCoordinator _callCoordinator;
    private readonly IPlannerRepository _planner;
    private readonly IPlannerProposalService _plannerProposals;
    private readonly ICalendarSyncProviderRegistry _calendarProviders;
    private readonly SurfaceOrchestrationService _surfaceOrchestration;
    private readonly IModeRegistry _modeRegistry;
    private readonly IModeUsageRepository _modeUsage;
    private readonly IPinRepository _pins;
    private readonly CompanionDockViewModel _companionDockVm;
    private readonly Dictionary<HavenMode, ChatPage> _chats = [];
    private readonly Dictionary<Guid, ChatPage> _projectChats = [];
    private readonly Dictionary<Guid, StudioProjectPage> _projectPages = [];
    private readonly Dictionary<Guid, ChatPage> _groupChats = [];
    private readonly Dictionary<Guid, ChatGroupPageViewModel> _groupPages = [];
    private readonly Dictionary<string, NewChatPage> _modeWorkspaces = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CapabilityDefinition> _availableCapabilities = [];
    private HomePage? _homePage;
    private NewDashboardPage? _newDashboardPage;
    private GoPage? _goPage;
    private NewChatPage? _newChatPage;
    private PlanPageViewModel? _planPage;
    private TerminalPage? _terminalPage;
    private Haven.Desktop.Views.Pages.Mail.MailPage? _mailPage;
    private PlayPage? _playPage;
    private readonly DispatcherTimer _reminderTimer;
    private int _isPollingReminders;
    private object? _currentPage;
    private ChatPage _currentChat;
    private WorkspaceTabViewModel? _selectedTab;
    private string _startupStatus = "Starting Haven\u2026";
    private string _searchQuery = string.Empty;
    private readonly Haven.Desktop.HavenUI.Runtime.TrailingDebouncer _sidebarSearchDebouncer = new(TimeSpan.FromMilliseconds(200));
    private readonly Haven.Desktop.HavenUI.Runtime.LatestOperationGate _sidebarSearchGate = new();
    private string _commandSearch = string.Empty;
    private bool _isSidebarOpen = true;
    private bool _isCommandPaletteOpen;
    private bool _isRenameOpen;
    private bool _isDeleteConfirmationOpen;
    private string _renameDraft = string.Empty;
    private ContainerDefinition? _activeProject;
    private StudioProjectPage? _activeProjectPage;
    private readonly HavenEventBus _eventBus;
    private object? _previousContent;
    private HavenShellEdition _edition = HavenShellEdition.Classic;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(name);
        return true;
    }

    public MainView(
        HavenEventBus bus,
        IConversationRepository conversations,
        IContainerRepository containers,
        ICatalogRepository catalog,
        IAutomationRepository automations,
        IWorkspaceStateRepository workspaceState,
        IWorkspaceToolService workspaceTools,
        IProjectIntelligenceService projectIntelligence,
        IProviderModelClient ollama,
        ChatSessionService sessions,
        IConversationSafetyService conversationSafety,
        IConversationVersioningService conversationVersioning,
        CapabilityPreflightService preflight,
        CapabilityRegistryService capabilityRegistry,
        ICapabilityRepository capabilityRepository,
        IGenUiTemplateRepository genUiTemplates,
        GenerativeUiEventRouter genUiRouter,
        GenUiInstanceStore genUiInstances,
        IGenUiAppRepository genUiApps,
        GenUiAppSessionService genUiSessions,
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
        BrowserSessionService browser,
        BrowserDataService browserData,

        ScheduledTaskRunner automationRunner,
        ScheduledTaskScheduleCalculator scheduleCalculator,
        UserPreferencesService preferences,
        IPrivacyPreferenceStore privacy,
        IModelProviderRegistry modelProviders,
        IProviderConfigurationStore providerConfigurations,
        IProviderSecretStore providerSecrets,
        ProjectCreationService projectCreator,
        NotificationService notifications,
        ITrainingRepository trainingRepo,
        IContainerResourceRepository containerResources,
        IDashboardRepository dashboard,
        IDashboardLayoutRepository dashboardLayout,
        IVersionedSettingsStore versionedSettings,
        Haven.Application.Play.PlaySessionService playSessions,
        IDashboardTileProviderRegistry dashboardProviders,
        ICallCoordinator callCoordinator,
        ISpeechModelManager speechModels,
        IPlannerRepository planner,
        IPlannerProposalService plannerProposals,
        ICalendarSyncProviderRegistry calendarProviders,
        SurfaceOrchestrationService surfaceOrchestration,
        IModeRegistry modeRegistry,
        IModeUsageRepository modeUsage,
        IPinRepository pins,
        AgentTaskRuntimeService? agentRuntime = null)
    {
        _eventBus = bus;
        _bus = bus;
        _conversations = conversations;
        _containers = containers;
        _catalog = catalog;
        _agentRuntime = agentRuntime;
        _automations = automations;
        _workspaceState = workspaceState;
        _workspaceTools = workspaceTools;
        _projectIntelligence = projectIntelligence;
        _ollama = ollama;
        _sessions = sessions;
        _conversationSafety = conversationSafety;
        _conversationVersioning = conversationVersioning;
        _preflight = preflight;
        _capabilityRegistry = capabilityRegistry;
        _capabilityRepository = capabilityRepository;
        _genUiTemplates = genUiTemplates;
        _genUiRouter = genUiRouter;
        _genUiInstances = genUiInstances;
        _genUiApps = genUiApps;
        _genUiSessions = genUiSessions;
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
        _browser = browser;
        _browserData = browserData;

        _automationRunner = automationRunner;
        _scheduleCalculator = scheduleCalculator;
        _preferences = preferences;
        _privacy = privacy;
        _modelProviders = modelProviders;
        _providerConfigurations = providerConfigurations;
        _providerSecrets = providerSecrets;
        _goSuggestions = new GoSuggestionService(_conversations, _ollama, _preferences);
        _projectCreator = projectCreator;
        _notifications = notifications;
        _trainingRepo = trainingRepo;
        _containerResources = containerResources;
        _dashboard = dashboard;
        _dashboardLayout = dashboardLayout;
        _versionedSettings = versionedSettings;
        _playSessions = playSessions;
        _dashboardProviders = dashboardProviders.Providers;
        _callCoordinator = callCoordinator;
        _ = speechModels; // Retained in the composition signature while the retired Call surface is removed.
        _planner = planner;
        _plannerProposals = plannerProposals;
        _calendarProviders = calendarProviders;
        _surfaceOrchestration = surfaceOrchestration;
        _modeRegistry = modeRegistry;
        _modeUsage = modeUsage;
        _pins = pins;
        _companionDockVm = new CompanionDockViewModel(new Haven.Infrastructure.CompanionDockService(), _conversations);
        _reminderTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
            async (_, _) => await PollPlannerRemindersAsync());

        NavigateChatCommand = new AsyncRelayCommand(() => NavigateModeAsync(HavenMode.Chat, false));
        NavigateStudyCommand = new AsyncRelayCommand(() => SwitchNativeChatModeAsync(HavenMode.Study));
        NavigateTasksCommand = new AsyncRelayCommand(() =>
        {
            OpenTasksDashboard();
            return Task.CompletedTask;
        });
        NavigateStudioCommand = new AsyncRelayCommand(() => NavigateModeAsync(HavenMode.Studio, true));
        NavigateBrowserCommand = new RelayCommand(OpenBrowser);
        NavigateTrainingCommand = new RelayCommand(OpenTraining);
        NavigateHomeCommand = new AsyncRelayCommand(OpenHomeAsync);
        NavigateCallCommand = new AsyncRelayCommand(OpenVoiceSessionFromActionAsync);
        OpenLiveCallCommand = new AsyncRelayCommand(OpenVoiceSessionFromActionAsync);
        EndLiveCallCommand = new AsyncRelayCommand(() => _callCoordinator.EndAsync(CancellationToken.None));
        NavigatePlanCommand = new RelayCommand(OpenPlan);
        NavigateAgentsCommand = new RelayCommand(() => OpenCatalog(CatalogPageKind.Agents));
        NavigateCapabilitiesCommand = new RelayCommand(OpenCapabilities);
        NavigatePromptsCommand = new RelayCommand(() => OpenCatalog(CatalogPageKind.Prompts));
        NavigateAutomationsCommand = new RelayCommand(OpenAutomationsDashboard);
        NavigateArchiveCommand = new RelayCommand(OpenArchive);
        NavigateActivityLogCommand = new RelayCommand(OpenActivityLog);
        DismissNotificationCommand = new RelayCommand<Guid>(id => _notifications.Dismiss(id));
        NavigateSettingsCommand = new RelayCommand(OpenApplicationSettings);
        NavigateContainerSettingsCommand = new RelayCommand(OpenContainerSettings);
        NavigateCurrentChatCommand = new AsyncRelayCommand(NavigateCurrentChatAsync);
        NewChatCommand = new RelayCommand(StartNewConversation);
        NewContainerCommand = new RelayCommand(OpenNewContainer);
        DeleteContainerCommand = new AsyncRelayCommand<ContainerItemViewModel>(item =>
        {
            if (item is not null && CurrentChat.DeleteContainerCommand.CanExecute(item))
                CurrentChat.DeleteContainerCommand.Execute(item);
            return Task.CompletedTask;
        });
        NewProjectChatCommand = new AsyncRelayCommand(() => StartProjectChatAsync(string.Empty));
        NavigateProjectHomeCommand = new RelayCommand(OpenActiveProjectHome);
        ToggleTemporaryCommand = new AsyncRelayCommand(ToggleTemporaryActiveConversationAsync);
        RefreshModelsCommand = new AsyncRelayCommand(RefreshActiveModelsAsync);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        SelectContainerCommand = new AsyncRelayCommand<ContainerItemViewModel>(SelectContainerAsync);
        OpenCommandPaletteCommand = new RelayCommand(OpenCommandPalette);
        CloseCommandPaletteCommand = new RelayCommand(() => IsCommandPaletteOpen = false);
        SelectTabCommand = new RelayCommand<WorkspaceTabViewModel>(item => SelectedTab = item);
        CloseTabCommand = new RelayCommand<WorkspaceTabViewModel>(CloseTab);
        AddNewTabCommand = new RelayCommand(AddNewTab);
        NavigateBackCommand = new RelayCommand(NavigateBack, () => SelectedTab?.CanGoBack == true);
        NavigateForwardCommand = new RelayCommand(NavigateForward, () => SelectedTab?.CanGoForward == true);
        BranchCurrentCommand = new AsyncRelayCommand(BranchActiveConversationAsync);
        CompactCurrentCommand = new AsyncRelayCommand(CompactActiveConversationAsync);
        ArchiveCurrentCommand = new AsyncRelayCommand(ArchiveActiveConversationAsync);
        TogglePinCurrentCommand = new AsyncRelayCommand(TogglePinCurrentAsync);
        BeginRenameCurrentCommand = new RelayCommand(BeginRenameCurrent);
        SaveRenameCurrentCommand = new AsyncRelayCommand(SaveRenameCurrentAsync, () => !string.IsNullOrWhiteSpace(RenameDraft));
        CancelRenameCurrentCommand = new RelayCommand(() => IsRenameOpen = false);
        RequestDeleteCurrentCommand = new RelayCommand(RequestDeleteCurrent);
        ConfirmDeleteCurrentCommand = new AsyncRelayCommand(DeleteCurrentAsync);
        CancelDeleteCurrentCommand = new RelayCommand(() => IsDeleteConfirmationOpen = false);
        ConfigureModelCommand = new RelayCommand(ShowModelSelector);
        CopyLastResponseCommand = new RelayCommand(CopyLastResponse);
        DictateCommand = new RelayCommand(() => DictateRequested?.Invoke(this, EventArgs.Empty));
        UndoCurrentCommand = new AsyncRelayCommand(UndoActiveAsync);
        RedoCurrentCommand = new AsyncRelayCommand(RedoActiveAsync);
        SaveCurrentCommand = new RelayCommand(() => (CurrentPage as WorkspaceEditorPage)?.SaveCommand.Execute(null));
        BuildBrowserExtensionCommand = new RelayCommand(() =>
        {
            OpenCurrentChatTab();
            CurrentChat.UsePrompt(">Rigid Build a limited-scope Haven Browse extension. Ask for the allowed origins and behavior, then create a declarative manifest and content script without privileged APIs.");
        });
        SwitchSurfaceCommand = new AsyncRelayCommand<string>(SwitchSurfaceAsync);
        NavigateModeLibraryCommand = new RelayCommand(OpenModeLibrary);

        // Initialize chat BEFORE setting DataContext so bindings don't fail
        _currentChat = CreateChat(HavenMode.Chat);
        _chats[HavenMode.Chat] = _currentChat;
        AttachChat(_currentChat);
        _currentPage = _currentChat;
        AddOrSelectTab("chat-general", "General chat", _currentChat, false, HavenSurface.Chat);

        InitializeComponent();
        AttachedToVisualTree += (_, _) => TrackWorkspaceWindowGeometry();
        InitialiseNativeChatSidebar();
        DataContext = this;

        WireShellControls();
        SplitDivider.DragCompleted += (_, _) =>
        {
            if (IsSplitView) QueueWorkspaceSessionSave();
        };
        SplitDivider.KeyUp += (_, args) =>
        {
            if (IsSplitView && args.Key is Key.Left or Key.Right) QueueWorkspaceSessionSave();
        };
        TopRail.AttachNotifications(_notifications);
        PageContent.Content = _currentPage;
        ApplyShellVisualState();
        RefreshTopRailTabs();

        _callCoordinator.StateChanged += OnCallStateChanged;
        BuildCommandPalette();
        AttachBetaOverlays();
    }

    public event EventHandler<string>? CopyRequested;
    public event EventHandler? DictateRequested;
    public ObservableCollection<RecentConversationViewModel> PinnedConversations { get; } = [];
    public ObservableCollection<RecentConversationViewModel> RecentConversations { get; } = [];
    public ObservableCollection<WorkspaceTabViewModel> OpenTabs { get; } = [];
    public ObservableCollection<CommandPaletteItemViewModel> CommandItems { get; } = [];
    public ObservableCollection<ToastNotification> Notifications => _notifications.Notifications;
    private IReadOnlyList<CommandPaletteItemViewModel> AllCommandItems { get; set; } = [];
    private int _launcherSearchGeneration;
    public CompanionDockViewModel CompanionDock => _companionDockVm;

    public HavenEventBus EventBus => _eventBus;

    public HavenShellEdition Edition => _edition;

    /// <summary>
    /// Selects the independently designed product surface before the window is
    /// shown. New Haven never inserts Classic's sidebar or ChatPage visuals.
    /// </summary>
    public void ApplyEdition(HavenShellEdition edition)
    {
        if (_edition == edition && (edition == HavenShellEdition.Classic || _goPage is not null)) return;
        _edition = edition;

        if (edition == HavenShellEdition.New)
        {
            SidebarControl.IsVisible = false;
            ShellContextBar.IsVisible = false;
            StoredChatDropdown.IsVisible = false;
            ContentArea.Background = Brushes.Transparent;
            ContentArea.BorderThickness = new Avalonia.Thickness(0);
            ContentArea.CornerRadius = new Avalonia.CornerRadius(0);
            PageContent.Margin = new Avalonia.Thickness(0);

            foreach (var tab in OpenTabs.ToArray()) tab.Dispose();
            OpenTabs.Clear();
            _selectedTab = null;
            _currentPage = null;
            _goPage = CreateGoPage();
            AddOrSelectTab("go", "Go", _goPage, false, HavenSurface.Go, forceNewTab: true);
        }
        else
        {
            ShellContextBar.IsVisible = false;
            ContentArea.Background = Avalonia.Application.Current?.Resources["HavenPanelBrush"] as IBrush;
            ContentArea.BorderThickness = new Avalonia.Thickness(1);
            ContentArea.CornerRadius = new Avalonia.CornerRadius(24);
            PageContent.Margin = new Avalonia.Thickness(6);
        }

        ApplyShellVisualState();
    }

    public ChatPage CurrentChat
    {
        get => _currentChat;
        private set
        {
            if (ReferenceEquals(_currentChat, value)) return;
            DetachChat(_currentChat);
            _currentChat = value;
            AttachChat(value);
            RaisePropertyChanged();
            RaiseShellProperties();
        }
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            if (PageContent is not null)
                PageContent.Content = value;
            RaisePropertyChanged(nameof(IsChatVisible));
            RaisePropertyChanged(nameof(IsPageVisible));
            RaisePropertyChanged(nameof(IsBrowseMode));
            RaisePropertyChanged(nameof(IsTrainingMode));
            RaisePropertyChanged(nameof(IsSidebarVisible));
            RaisePropertyChanged(nameof(HasFullSidebar));
            RaisePropertyChanged(nameof(HasCompactSidebar));
            RaisePropertyChanged(nameof(IsWorkspaceHeaderVisible));
            RaisePropertyChanged(nameof(ProductName));
        }
    }

    public WorkspaceTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value) || value is null) return;
            if (ReferenceEquals(_secondaryTab, value))
            {
                SwapSplitPanes();
                return;
            }
            if (_selectedTab is not null)
            {
                _selectedTab.IsSelected = false;
                if (_selectedTab.Page is IActivatablePage previous) previous.Deactivate();
            }
            if (!SetProperty(ref _selectedTab, value)) return;
            value.IsSelected = true;
            ApplySelectedTab(value);
            RefreshTopRailTabs();
            QueueWorkspaceSessionSave();
        }
    }

    private void ApplySelectedTab(WorkspaceTabViewModel value)
    {
        if (value.Page is ChatPage chat)
        {
            CurrentChat = chat;
            if (chat.Mode == HavenMode.Studio && chat.SelectedContainer is not null)
                ActivateProject(chat.SelectedContainer.Definition);
            else if (chat.Mode == HavenMode.Studio)
                ClearActiveProject();
        }
        else if (value.Page is StudioProjectPage project)
        {
            ActivateProject(project.Definition, project);
            if (_projectChats.TryGetValue(project.ProjectId, out var projectChat)) CurrentChat = projectChat;
        }
        else if (value.Page is WorkspaceEditorPage editor)
        {
            ActivateProject(editor.Container);
            if (_projectChats.TryGetValue(editor.Container.Id, out var projectChat)) CurrentChat = projectChat;
        }

        CurrentPage = value.Page;
        NavigateBackCommand.RaiseCanExecuteChanged();
        NavigateForwardCommand.RaiseCanExecuteChanged();
        RaiseShellProperties();
        if (value.Page is IActivatablePage activatable)
            _ = activatable.ActivateAsync(CancellationToken.None);
    }

    public bool IsChatVisible => ReferenceEquals(CurrentPage, CurrentChat);
    public bool IsPageVisible => !IsChatVisible;
    public HavenSurface CurrentSurface => SelectedTab?.Surface ?? HavenSurface.Chat;
    public bool IsBrowseMode => CurrentSurface == HavenSurface.Browse;
    public bool IsTrainingMode => CurrentSurface == HavenSurface.Training;
    public bool IsProjectOpen => CurrentSurface == HavenSurface.Studio && ActiveProject is not null;
    public bool IsWorkspaceHeaderVisible => IsChatVisible;
    public bool IsHorizontalTabsVisible => OpenTabs.Count > 0;

    public ContainerDefinition? ActiveProject
    {
        get => _activeProject;
        private set
        {
            if (!SetProperty(ref _activeProject, value)) return;
            RaisePropertyChanged(nameof(IsProjectOpen));
            RaisePropertyChanged(nameof(ActiveProjectName));
            RaisePropertyChanged(nameof(ActiveProjectRoot));
            RaisePropertyChanged(nameof(RecentHeading));
            RaisePropertyChanged(nameof(ShowNoProjectChats));
        }
    }

    public StudioProjectPage? ActiveProjectPage
    {
        get => _activeProjectPage;
        private set
        {
            if (!SetProperty(ref _activeProjectPage, value)) return;
            RaisePropertyChanged(nameof(ActiveProjectFiles));
            RaisePropertyChanged(nameof(ActiveProjectRefreshCommand));
            RaisePropertyChanged(nameof(ActiveProjectBuildCommand));
            RaisePropertyChanged(nameof(ActiveProjectTestCommand));
            ApplyShellVisualState();
        }
    }

    public IEnumerable<WorkspaceFileItemViewModel> ActiveProjectFiles =>
        ActiveProjectPage?.Files ?? [];
    public AsyncRelayCommand? ActiveProjectRefreshCommand => ActiveProjectPage?.RefreshCommand;
    public AsyncRelayCommand? ActiveProjectBuildCommand => ActiveProjectPage?.BuildCommand;
    public AsyncRelayCommand? ActiveProjectTestCommand => ActiveProjectPage?.TestCommand;

    public string ActiveProjectName => ActiveProject?.Name ?? "Project";
    public string ActiveProjectRoot => ActiveProject?.RootPath ?? "Folder not connected";

    public string StartupStatus { get => _startupStatus; private set => SetProperty(ref _startupStatus, value); }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value)) return;
            // Typing stays immediate; the expensive repository refresh is
            // debounced so "hello" triggers roughly one refresh, and a stale
            // completion can never overwrite a newer one.
            var generation = _sidebarSearchGate.Begin();
            _sidebarSearchDebouncer.Schedule(() =>
            {
                if (!_sidebarSearchGate.IsActive(generation)) return;
                _ = RunSidebarSearchRefreshAsync(generation);
            });
        }
    }

    private async Task RunSidebarSearchRefreshAsync(int generation)
    {
        try
        {
            await RefreshRecentsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sidebar search] {ex}");
        }
    }

    public string CommandSearch
    {
        get => _commandSearch;
        set
        {
            if (!SetProperty(ref _commandSearch, value)) return;
            FilterCommands();
        }
    }

    public bool IsCommandPaletteOpen { get => _isCommandPaletteOpen; private set => SetProperty(ref _isCommandPaletteOpen, value); }
    public bool IsRenameOpen { get => _isRenameOpen; private set => SetProperty(ref _isRenameOpen, value); }
    public bool IsDeleteConfirmationOpen { get => _isDeleteConfirmationOpen; private set => SetProperty(ref _isDeleteConfirmationOpen, value); }
    public string RenameDraft { get => _renameDraft; set { if (SetProperty(ref _renameDraft, value)) SaveRenameCurrentCommand.RaiseCanExecuteChanged(); } }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        set
        {
            if (!SetProperty(ref _isSidebarOpen, value)) return;
            RaisePropertyChanged(nameof(IsSidebarClosed));
            RaisePropertyChanged(nameof(IsSidebarVisible));
            RaisePropertyChanged(nameof(HasFullSidebar));
            RaisePropertyChanged(nameof(HasCompactSidebar));
            ApplyShellVisualState();
        }
    }

    public bool IsSidebarClosed => !IsSidebarOpen;
    public bool SupportsConversationSidebar => CurrentSurface is HavenSurface.Chat or HavenSurface.Study or HavenSurface.Tasks or HavenSurface.Studio;
    public bool SupportsConversationCommands => SupportsConversationSidebar;
    public bool SupportsEditingCommands => SupportsConversationCommands || CurrentPage is WorkspaceEditorPage;
    public bool IsSidebarVisible => SupportsConversationSidebar;
    public bool HasFullSidebar => IsSidebarOpen && SupportsConversationSidebar;
    public bool HasCompactSidebar => !IsSidebarOpen && SupportsConversationSidebar;
    public bool HasPinnedConversations => PinnedConversations.Count > 0;
    public bool HasRecentConversations => RecentConversations.Count > 0;
    public bool ShowNoProjectChats => IsProjectOpen && !HasPinnedConversations && !HasRecentConversations;
    public HavenMode CurrentMode => CurrentChat?.Mode ?? HavenMode.Chat;
    public bool IsStudy => CurrentSurface == HavenSurface.Study;
    public bool IsChatProduct => CurrentSurface is HavenSurface.Chat or HavenSurface.Study;
    public bool HasContainers => CurrentChat?.HasContainers ?? false;
    public bool HasAnyContainers => CurrentChat?.HasAnyContainers ?? false;
    public bool SupportsDuo => CurrentChat?.SupportsDuo ?? false;
    public string ChatTypeLabel => CurrentSurface == HavenSurface.Study ? "Study" : "General";

    public string ProductName => CurrentSurface switch
    {
        HavenSurface.Home => "Haven Home",
        HavenSurface.Chat or HavenSurface.Study => "Haven Chat",
        HavenSurface.Tasks => "Haven Tasks",
        HavenSurface.Studio => "Haven Studio",
        HavenSurface.Browse => "Haven Browse",
        HavenSurface.Plan => "Haven Plan",
        HavenSurface.Training => "Haven Training",
        HavenSurface.Mesh => "Haven Mesh",
        HavenSurface.Mail => "Haven Mail",
        _ => "Haven"
    };

    public string NewItemLabel => CurrentMode switch
    {
        HavenMode.Tasks => "+ New task",
        HavenMode.Study => "+ Quick chat",
        HavenMode.Studio => "+ New studio chat",
        _ => "New chat"
    };

    public string FileNewLabel => CurrentMode switch { HavenMode.Tasks => "New task", HavenMode.Study => "New study chat", HavenMode.Studio => "New studio chat", _ => "New chat" };
    public string FileNewContainerLabel => CurrentMode switch { HavenMode.Chat => "New Chat Group", HavenMode.Study => "New Subject", HavenMode.Tasks => "New Task Group", _ => "New Project" };
    public string ContainerHeading => CurrentMode switch { HavenMode.Chat => "Chat Groups", HavenMode.Study => "Subjects", HavenMode.Tasks => "Task Groups", _ => "Projects" };
    public string ProjectMenuHeader => CurrentMode switch { HavenMode.Chat => "Chat Group", HavenMode.Study => "Subject", HavenMode.Tasks => "Task Group", _ => "Project" };

    public string WorkspaceEyebrow => CurrentMode switch
    {
        HavenMode.Chat => CurrentChat?.SelectedContainer?.Name ?? "Chat",
        HavenMode.Study => "Lesson",
        HavenMode.Tasks => "Task Group",
        HavenMode.Studio => "Project",
        _ => "Haven"
    };

    public string WorkspaceTitle => CurrentMode == HavenMode.Chat
        ? CurrentChat?.ConversationTitle ?? "Chat"
        : CurrentChat?.SelectedLesson?.Name ?? CurrentChat?.SelectedContainer?.Name ?? "Local workspace";

    public bool ShowTemporaryHeaderAction => CurrentMode == HavenMode.Chat && CurrentChat?.ShowTemporaryHeaderAction == true;
    public bool ShowContextHeaderWidget => CurrentMode == HavenMode.Chat && CurrentChat?.ShowContextHeaderWidget == true;
    public string TemporaryHeaderActionLabel => CurrentChat?.TemporaryHeaderActionLabel ?? string.Empty;
    public int ContextPercent => CurrentChat?.ContextPercent ?? 0;
    public int ContextRemainingPercent => CurrentChat?.ContextRemainingPercent ?? 0;
    public string ContextLabel => CurrentChat?.ContextLabel ?? string.Empty;
    public string ContextRemainingLabel => CurrentChat?.ContextRemainingLabel ?? string.Empty;

    public string RecentHeading => CurrentMode switch { HavenMode.Tasks => "Tasks", HavenMode.Study => "Study chats", HavenMode.Studio when IsProjectOpen => "Project chats", HavenMode.Studio => "Standalone chats", _ => "Chats" };

    public string ContainerSettingsLabel => CurrentMode switch
    {
        HavenMode.Study when CurrentChat?.SelectedLesson is not null => "Lesson settings",
        HavenMode.Study => "Subject settings",
        HavenMode.Tasks => "Task Group settings",
        HavenMode.Chat => "Chat Group settings",
        _ => "Project settings"
    };

    public string OllamaStatus => CurrentChat?.Status ?? string.Empty;
    public string DuoLabel => CurrentChat?.IsDuoPluginActive == true ? CurrentChat?.SelectedDuo.ToString() ?? "Solo" : "Solo";
    public bool HasLiveCall => _callCoordinator.IsActive;
    public string LiveCallLabel => _callCoordinator.State == CallState.Paused ? "Call paused" : $"Live call \u00b7 {_callCoordinator.State}";

    public AsyncRelayCommand NavigateChatCommand { get; }
    public AsyncRelayCommand NavigateStudyCommand { get; }
    public AsyncRelayCommand NavigateTasksCommand { get; }
    public AsyncRelayCommand NavigateStudioCommand { get; }
    public RelayCommand NavigateBrowserCommand { get; }
    public RelayCommand NavigateTrainingCommand { get; }
    public AsyncRelayCommand NavigateHomeCommand { get; }
    public AsyncRelayCommand NavigateCallCommand { get; }
    public AsyncRelayCommand OpenLiveCallCommand { get; }
    public AsyncRelayCommand EndLiveCallCommand { get; }
    public RelayCommand NavigatePlanCommand { get; }
    public RelayCommand NavigateAgentsCommand { get; }
    public RelayCommand NavigateCapabilitiesCommand { get; }
    public RelayCommand NavigatePromptsCommand { get; }
    public RelayCommand NavigateAutomationsCommand { get; }
    public RelayCommand NavigateArchiveCommand { get; }
    public RelayCommand NavigateActivityLogCommand { get; }
    public RelayCommand<Guid> DismissNotificationCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand NavigateContainerSettingsCommand { get; }
    public AsyncRelayCommand NavigateCurrentChatCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public RelayCommand NewContainerCommand { get; }
    public AsyncRelayCommand<ContainerItemViewModel> DeleteContainerCommand { get; }
    public AsyncRelayCommand NewProjectChatCommand { get; }
    public RelayCommand NavigateProjectHomeCommand { get; }
    public AsyncRelayCommand ToggleTemporaryCommand { get; }
    public AsyncRelayCommand RefreshModelsCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public AsyncRelayCommand<ContainerItemViewModel> SelectContainerCommand { get; }
    public RelayCommand OpenCommandPaletteCommand { get; }
    public RelayCommand CloseCommandPaletteCommand { get; }
    public RelayCommand<WorkspaceTabViewModel> SelectTabCommand { get; }
    public RelayCommand<WorkspaceTabViewModel> CloseTabCommand { get; }
    public RelayCommand AddNewTabCommand { get; }
    public RelayCommand NavigateBackCommand { get; }
    public RelayCommand NavigateForwardCommand { get; }
    public AsyncRelayCommand BranchCurrentCommand { get; }
    public AsyncRelayCommand CompactCurrentCommand { get; }
    public AsyncRelayCommand ArchiveCurrentCommand { get; }
    public AsyncRelayCommand TogglePinCurrentCommand { get; }
    public RelayCommand BeginRenameCurrentCommand { get; }
    public AsyncRelayCommand SaveRenameCurrentCommand { get; }
    public RelayCommand CancelRenameCurrentCommand { get; }
    public RelayCommand RequestDeleteCurrentCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCurrentCommand { get; }
    public RelayCommand CancelDeleteCurrentCommand { get; }
    public RelayCommand ConfigureModelCommand { get; }
    public RelayCommand CopyLastResponseCommand { get; }
    public RelayCommand DictateCommand { get; }
    public AsyncRelayCommand UndoCurrentCommand { get; }
    public AsyncRelayCommand RedoCurrentCommand { get; }
    public RelayCommand SaveCurrentCommand { get; }
    public RelayCommand BuildBrowserExtensionCommand { get; }
    public AsyncRelayCommand<string> SwitchSurfaceCommand { get; }
    public RelayCommand NavigateModeLibraryCommand { get; }
#pragma warning restore CS8618

    public async Task InitializeAsync(LegacyMigrationResult migration, CancellationToken cancellationToken)
    {
        await CurrentChat.InitializeAsync(cancellationToken);
        await RefreshRecentsAsync(cancellationToken);
        await PollPlannerRemindersAsync();
        _reminderTimer.Start();
        StartAutomationScheduler();
        _companionDockVm.Start();
        StartupStatus = migration.Imported
            ? $"Imported {migration.ConversationCount} legacy conversations \u00b7 local-only"
            : "Local-only \u00b7 SQLite ready";
    }

    /// <summary>Initialises a secondary shell without starting duplicate application-wide pollers.</summary>
    public async Task InitializeSecondaryWindowAsync(CancellationToken cancellationToken)
    {
        await CurrentChat.InitializeAsync(cancellationToken);
        await RefreshRecentsAsync(cancellationToken);
        _companionDockVm.Start();
        StartupStatus = "Local-only · secondary window";
    }

    public void SetStartupError(string message) => StartupStatus = $"Startup problem: {message}";

    private async Task PollPlannerRemindersAsync()
    {
        if (Interlocked.Exchange(ref _isPollingReminders, 1) != 0) return;
        try
        {
            foreach (var reminder in await _planner.GetDueRemindersAsync(DateTimeOffset.UtcNow, 20, CancellationToken.None))
            {
                _notifications.Show("Planner reminder", reminder.Title, ToastKind.Info, TimeSpan.FromSeconds(12));
                await _planner.MarkReminderDeliveredAsync(reminder, DateTimeOffset.UtcNow, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Planner reminders] {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isPollingReminders, 0);
        }
    }

    public void OpenCommandPalette()
    {
        CommandSearch = string.Empty;
        BuildCommandPalette();
        IsCommandPaletteOpen = false;
        TopRail?.ShowActions();
    }

    private Task OpenHomeAsync()
    {
        if (_edition == HavenShellEdition.New)
            return OpenGoAsync();

        _homePage ??= CreateHomePage();
        AddOrSelectTab("home", "Home", _homePage, false, HavenSurface.Home);
        return _homePage.ActivateAsync(CancellationToken.None);
    }

    private Task OpenGoAsync()
    {
        _goPage ??= CreateGoPage();
        AddOrSelectTab("go", "Go", _goPage, false, HavenSurface.Go);
        _goPage.FocusComposer();
        return Task.CompletedTask;
    }

    private void OpenMail()
    {
        _mailPage ??= new Haven.Desktop.Views.Pages.Mail.MailPage();
        AddOrSelectTab("mail", "Mail", _mailPage, false, HavenSurface.Mail);
        ApplyShellVisualState();
    }
    private void OpenTerminal(bool forceNewTab = false, string? initialDirectory = null)
    {
        var hub = Haven.Desktop.App.Services?.GetService(typeof(TerminalCommandActivityHub)) as TerminalCommandActivityHub;
        var sessionFactory = Haven.Desktop.App.Services?.GetService(typeof(ITerminalSessionFactory)) as ITerminalSessionFactory;
        if (hub is null || sessionFactory is null)
        {
            _notifications.Show("Terminal unavailable", "The Terminal session runtime is not available.", ToastKind.Warning, TimeSpan.FromSeconds(5));
            return;
        }

        var terminalFactory = sessionFactory;

        TerminalPage page;
        string key;
        if (forceNewTab)
        {
            page = new TerminalPage(terminalFactory, _preferences, hub, initialDirectory);
            key = "terminal-" + Guid.NewGuid().ToString("N")[..8];
        }
        else
        {
            _terminalPage ??= new TerminalPage(terminalFactory, _preferences, hub, initialDirectory);
            page = _terminalPage;
            key = "terminal";
        }

        AddOrSelectTab(key, "Terminal", page, forceNewTab, HavenSurface.Terminal, forceNewTab);
        ApplyShellVisualState();
        page.FocusCommandLine();
    }

    private async Task OpenDashboardAsync()
    {
        _newDashboardPage ??= CreateNewDashboardPage();
        AddOrSelectTab("dashboard", "Dashboard", _newDashboardPage, false, HavenSurface.Dashboard);
        await _newDashboardPage.ActivateAsync(CancellationToken.None);
    }

    private async Task OpenNewChatAsync(
        string? instruction = null,
        bool forceNewTab = false,
        TaskAttachmentSnapshot? initialAttachments = null)
    {
        if (forceNewTab)
        {
            var page = CreateNewChatPage();
            // Configure the optional Add catalogue after Chat is visible and submission has started.
            var key = "new-chat-" + Guid.NewGuid().ToString("N")[..8];
            AddOrSelectTab(key, "Chat", page, true, HavenSurface.Chat, forceNewTab: true);
            ApplyShellVisualState();
            await RefreshNativeChatSidebarAsync();
            if (initialAttachments is not null) page.AttachSnapshot(initialAttachments);
            if (!string.IsNullOrWhiteSpace(instruction)) page.Submit(instruction);
            else page.FocusComposer();
            await ConfigureAddMenuAsync(page);
            return;
        }

        if (_newChatPage is null)
        {
            _newChatPage = CreateNewChatPage();
            // Add-menu catalogue configuration runs after Chat is visible.
        }
        AddOrSelectTab("new-chat-general", "Chat", _newChatPage, false, HavenSurface.Chat);
        ApplyShellVisualState();
        await RefreshNativeChatSidebarAsync();

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            if (initialAttachments is not null) _newChatPage.AttachSnapshot(initialAttachments);
            _newChatPage.Submit(instruction);
        }
        else
            _newChatPage.FocusComposer();

        await ConfigureAddMenuAsync(_newChatPage);

    }

    private NewChatPage CreateNewChatPage()
    {
        var page = new NewChatPage(
            _bus,
            _conversations,
            _ollama,
            _sessions,
            _conversationSafety,
            _conversationVersioning,
            _preferences,
            _genUiRouter,
            _genUiInstances,
            _calculatorTemplate,
            _structuredFormTemplate,
            _choicePromptTemplate,
            _checklistTemplate,
            _dataGridTemplate,
            _cardDeckTemplate,
            _graphTemplate,
            _taskListTemplate,
            _dashboardTemplate,
            _assessmentTemplate,
            _workflowTemplate,
            _customTemplate,
            Haven.Desktop.App.Services?.GetService<IMessageAttachmentService>(),
            Haven.Desktop.App.Services?.GetService<IConversationProductionRepository>());
        page.ModelChanged += OnNewChatModelChanged;
        page.ConversationStateChanged += OnNewChatConversationStateChanged;
        page.AddActionSelected += OnNewChatAddActionSelected;
        page.AddCatalogItemSelected += OnNewChatCatalogItemSelected;
        return page;
    }

    private async Task<NewChatPage> OpenScopedNewChatPageAsync(
        HavenMode mode,
        Guid? containerId,
        string key,
        string title,
        HavenSurface surface,
        Conversation? conversation = null,
        string? prompt = null)
    {
        var page = CreateNewChatPage();
        if (mode == HavenMode.Tasks)
            page.ConfigureTaskMode();

        await ConfigureAddMenuAsync(page);
        if (conversation is null)
            await page.StartFreshConversationAsync(mode, containerId);
        else
            await page.LoadConversationAsync(conversation);

        AddOrSelectTab(key, title, page, true, surface);
        if (!string.IsNullOrWhiteSpace(prompt))
            page.Submit(prompt);
        else
            page.FocusComposer();

        _nativeChatSidebar?.SetMode(mode);
        _nativeChatSidebar?.SetActiveConversation(page.ConversationId, containerId);
        ApplyShellVisualState();
        return page;
    }

    private async Task OpenModeWorkspaceAsync(ModeDefinition mode, HavenSurface surface, bool forceNewTab)
    {
        if (IsDocumentWorkspace(mode.Key))
        {
            OpenDocumentWorkspace(mode, surface, forceNewTab);
            return;
        }

        NewChatPage page;
        string key;
        if (forceNewTab)
        {
            page = CreateNewChatPage();
            page.ConfigureMode(mode);
            key = $"app-{mode.Key}-{Guid.NewGuid():N}";
        }
        else if (!_modeWorkspaces.TryGetValue(mode.Key, out page!))
        {
            page = CreateNewChatPage();
            page.ConfigureMode(mode);
            _modeWorkspaces[mode.Key] = page;
            key = $"app-{mode.Key}";
        }
        else
        {
            key = $"app-{mode.Key}";
        }

        await ConfigureAddMenuAsync(page);
        AddOrSelectTab(key, mode.Name, page, forceNewTab, surface, forceNewTab);
        ApplyShellVisualState();
        page.FocusComposer();
    }

    private void OnNewChatModelChanged(object? sender, EventArgs e) => ApplyShellVisualState();

    private void OnNewChatConversationStateChanged(object? sender, EventArgs e)
    {
        ApplyShellVisualState();
        _ = RefreshNativeChatSidebarAsync();
    }

    private void OpenPlay()
    {
        _playPage ??= CreatePlayPage();
        AddOrSelectTab("play", "Play", _playPage, false, HavenSurface.Play);
        _ = _playPage.ActivateAsync(CancellationToken.None);
    }

    private PlayPage CreatePlayPage()
    {
        var page = new PlayPage(_playSessions, _genUiRouter);
        page.CreateRequested += async (_, _) => await OpenNewChatAsync("Help me create an interactive Play experience. Ask what I want to play, then design it with Haven interactive UI and deterministic local state where possible.");
        return page;
    }

    private void OpenPlan()
    {
        // The Plan App opens the authoritative planner/calendar workspace in every shell edition.
        // The native Today/Week projection remains a focused reusable surface, not a replacement for editing.
        OpenLegacyPlan();

    }

    private NativePlanPage CreateNativePlanPage()
    {
        var page = new NativePlanPage(_planner, _containers);
        page.StudyRequested += OnNativePlanStudyRequested;
        return page;
    }

    private void OpenLegacyPlan()
    {
        _planPage ??= new PlanPageViewModel(_planner, _plannerProposals, _calendarProviders, _ollama);
        AddOrSelectTab("plan", "Plan", _planPage, false, HavenSurface.Plan);
    }

    private async Task OpenPlanTaskAsync(Guid taskId)
    {
        OpenLegacyPlan();
        if (_planPage is not null && await _planPage.OpenTaskByIdAsync(taskId)) return;
        _notifications.Show("Task unavailable", "That Planner task could not be opened.", ToastKind.Warning, TimeSpan.FromSeconds(5));
    }

    private async void OnNativePlanStudyRequested(object? sender, PlannerStudyLink link)
    {
        try
        {
            await OpenStudyAssignmentAsync(link);
        }
        catch (Exception exception)
        {
            _notifications.Show("Study unavailable", $"Haven could not open this Study assignment: {exception.Message}", ToastKind.Warning, TimeSpan.FromSeconds(6));
        }
    }

    private async Task OpenStudyAssignmentAsync(PlannerStudyLink link)
    {
        var subjects = await _containers.GetByModeAsync(HavenMode.Study, CancellationToken.None);
        var title = subjects.FirstOrDefault(subject => subject.Id == link.SubjectId)?.Name ?? "Study";
        var page = CreateNewChatPage();
        await ConfigureAddMenuAsync(page);
        await page.StartFreshConversationAsync(HavenMode.Study, link.SubjectId, link.LessonId);
        var lessonKey = link.LessonId?.ToString("N") ?? "subject";
        AddOrSelectTab($"study-plan-{link.SubjectId:N}-{lessonKey}", title, page, true, HavenSurface.Study);
        _nativeChatSidebar?.SetMode(HavenMode.Study);
        _nativeChatSidebar?.SetActiveConversation(page.ConversationId, link.SubjectId);
        ApplyShellVisualState();
        page.FocusComposer();
    }

    private async Task NavigateModeAsync(HavenMode mode, bool showHome)
    {
        if (_edition == HavenShellEdition.New)
        {
            if (mode is HavenMode.Chat or HavenMode.Study)
            {
                await SwitchNativeChatModeAsync(mode);
                return;
            }

            if (mode == HavenMode.Tasks)
            {
                OpenTasksDashboard();
                return;
            }
        }

        var page = await GetOrCreateChatAsync(mode);
        CurrentChat = page;
        if (showHome) await OpenModeHomeAsync();
        else AddOrSelectTab(mode == HavenMode.Study ? "chat-study" : "chat-general", mode == HavenMode.Study ? "Study" : "General chat", page, false, SurfaceForMode(mode));
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task<ChatPage> GetOrCreateChatAsync(HavenMode mode)
    {
        if (_chats.TryGetValue(mode, out var existing))
        {
            await existing.RefreshCatalogAsync(CancellationToken.None);
            return existing;
        }
        var page = CreateChat(mode);
        _chats[mode] = page;
        await page.InitializeAsync(CancellationToken.None);
        if (mode == HavenMode.Studio) page.SelectedContainer = null;
        return page;
    }

    private async Task OpenModeHomeAsync()
    {
        if (CurrentMode is not (HavenMode.Tasks or HavenMode.Studio))
        {
            AddOrSelectTab(CurrentMode == HavenMode.Study ? "chat-study" : "chat-general", ChatTypeLabel, CurrentChat, false, SurfaceForMode(CurrentMode));
            return;
        }
        if (CurrentMode == HavenMode.Studio)
        {
            if (_chats.TryGetValue(HavenMode.Studio, out var standalone))
            {
                standalone.SelectedContainer = null;
                CurrentChat = standalone;
            }
            ClearActiveProject();

            var source = new WorkspaceHomePageViewModel(
                HavenMode.Studio,
                _containers,
                _conversations,
                _automations,
                _workspaceState,
                _projectIntelligence,
                OpenContainerDefinitionAsync,
                OpenProjectCreatorAsync);
            var projects = CreateNativeProjectsPage(source);
            AddOrSelectTab("studio-home", "Projects", projects, false, HavenSurface.Studio);
            return;
        }
        OpenTasksDashboard();
    }

    private void OpenNewContainer()
    {
        if (CurrentMode == HavenMode.Studio)
        {
            _ = OpenProjectCreatorAsync();
            return;
        }
        if (CurrentChat.NewContainerCommand.CanExecute(null)) CurrentChat.NewContainerCommand.Execute(null);
    }

    private Task OpenProjectCreatorAsync()
    {
        var page = new ProjectCreatorPageViewModel(_projectCreator, OpenCreatedProjectAsync);
        AddOrSelectTab("new-project", "New project", page, true, HavenSurface.Studio);
        return Task.CompletedTask;
    }

    private async Task OpenCreatedProjectAsync(ContainerDefinition definition)
    {
        if (!_chats.TryGetValue(HavenMode.Studio, out var standalone)) standalone = await GetOrCreateChatAsync(HavenMode.Studio);
        await standalone.RefreshContainersAsync(CancellationToken.None);
        standalone.SelectedContainer = null;
        await OpenContainerDefinitionAsync(definition);
        await StartProjectChatAsync(string.Empty);
    }

    private async Task<ChatPage> GetOrCreateProjectChatAsync(ContainerDefinition definition)
    {
        if (!_projectChats.TryGetValue(definition.Id, out var chat))
        {
            chat = CreateChat(HavenMode.Studio);
            _projectChats[definition.Id] = chat;
            await chat.InitializeAsync(CancellationToken.None);
        }
        else await chat.RefreshContainersAsync(CancellationToken.None);
        chat.SelectedContainer = chat.Containers.FirstOrDefault(item => item.Id == definition.Id);
        return chat;
    }

    private async Task<ChatPage> GetOrCreateGroupChatAsync(ContainerDefinition definition)
    {
        if (!_groupChats.TryGetValue(definition.Id, out var chat))
        {
            chat = CreateChat(HavenMode.Chat);
            _groupChats[definition.Id] = chat;
            await chat.InitializeAsync(CancellationToken.None);
        }
        else
        {
            await chat.RefreshContainersAsync(CancellationToken.None);
        }
        chat.SelectedContainer = chat.Containers.FirstOrDefault(item => item.Id == definition.Id);
        return chat;
    }

    private ChatGroupPageViewModel GetOrCreateGroupPage(ContainerDefinition definition)
    {
        if (_groupPages.TryGetValue(definition.Id, out var page)) return page;
        page = new ChatGroupPageViewModel(
            definition,
            _conversations,
            _containers,
            _containerResources,
            StartGroupChatAsync,
            OpenGroupedConversationAsync,
            OpenGroupSettingsAsync,
            () => CloseGroupPageAsync(definition.Id));
        _groupPages[definition.Id] = page;
        return page;
    }

    private async Task OpenChatGroupAsync(ContainerDefinition definition)
    {
        var page = GetOrCreateGroupPage(definition);
        await page.InitializeAsync(CancellationToken.None);
        AddOrSelectTab("group-" + definition.Id.ToString("N"), definition.Name, page, true, HavenSurface.Chat);
    }

    private async Task StartGroupChatAsync(ContainerDefinition definition)
    {
        if (_edition == HavenShellEdition.New)
        {
            await OpenScopedNewChatPageAsync(
                HavenMode.Chat,
                definition.Id,
                $"group-chat-{definition.Id:N}-{Guid.NewGuid():N}",
                definition.Name + " chat",
                HavenSurface.Chat);
            await RefreshRecentsAsync(CancellationToken.None);
            return;
        }

        var chat = await GetOrCreateGroupChatAsync(definition);
        CurrentChat = chat;
        chat.NewChat();
        AddOrSelectTab("group-chat-" + definition.Id.ToString("N"), definition.Name + " chat", chat, true, HavenSurface.Chat);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task OpenGroupedConversationAsync(Conversation conversation)
    {
        if (conversation.ContainerId is not Guid groupId) return;
        var definition = (await _containers.GetByModeAsync(HavenMode.Chat, CancellationToken.None))
            .FirstOrDefault(item => item.Id == groupId);
        if (definition is null) return;

        if (_edition == HavenShellEdition.New)
        {
            await OpenScopedNewChatPageAsync(
                HavenMode.Chat,
                groupId,
                $"conversation-{conversation.Id:N}",
                conversation.Title,
                HavenSurface.Chat,
                conversation);
            await RefreshRecentsAsync(CancellationToken.None);
            return;
        }

        var chat = await GetOrCreateGroupChatAsync(definition);
        CurrentChat = chat;
        AddOrSelectTab("group-chat-" + groupId.ToString("N"), conversation.Title, chat, true, HavenSurface.Chat);
        await chat.LoadConversationAsync(conversation.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private Task OpenGroupSettingsAsync(ContainerDefinition definition)
    {
        var item = new ContainerItemViewModel(definition);
        AddOrSelectTab(
            "container-settings-" + definition.Id.ToString("N"),
            "Chat Group settings",
            new ContainerSettingsPage(null, item, _containers, async () =>
            {
                await RefreshAfterSettingsAsync();
                _groupPages.Remove(definition.Id);
            }, () => ReturnToChatGroupAsync(definition.Id)),
            true,
            HavenSurface.Chat);
        return Task.CompletedTask;
    }

    private Task CloseGroupPageAsync(Guid groupId)
    {
        _groupPages.Remove(groupId);
        _groupChats.Remove(groupId);
        var tabs = OpenTabs.Where(item => item.Key.Contains(groupId.ToString("N"), StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var tab in tabs) CloseTab(tab);
        return NavigateModeAsync(HavenMode.Chat, false);
    }

    private StudioProjectPage GetOrCreateProjectPage(ContainerDefinition definition)
    {
        if (_projectPages.TryGetValue(definition.Id, out var page)) return page;
        page = new StudioProjectPage(definition, _conversations, _containers, _automations, _workspaceState, _projectIntelligence,
            file => OpenFileAsync(definition, file), StartProjectChatAsync, _modeRegistry, _catalog, _ollama,
            () => NavigateModeAsync(HavenMode.Studio, true), OpenProjectConversationAsync,
            root => { OpenTerminal(true, root); return Task.CompletedTask; });
        _projectPages[definition.Id] = page;
        return page;
    }

    private void ActivateProject(ContainerDefinition definition, StudioProjectPage? page = null)
    {
        ActiveProject = definition;
        ActiveProjectPage = page ?? GetOrCreateProjectPage(definition);
        RaiseShellProperties();
    }

    private void ClearActiveProject()
    {
        ActiveProject = null;
        ActiveProjectPage = null;
        RaiseShellProperties();
    }

    private void OpenActiveProjectHome()
    {
        if (ActiveProject is null) return;
        var page = ActiveProjectPage ?? GetOrCreateProjectPage(ActiveProject);
        AddOrSelectTab("project-" + ActiveProject.Id.ToString("N"), ActiveProject.Name, page, true);
    }

    private void OpenBrowser()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "browse");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new BrowserPage(_bus, _browser, _browserData, _ollama, _preferences,
            App.Services?.GetService<NotesReadAloudController>());
        AddOrSelectTab("browse", "Browse", page, true);
    }

    private void OpenTraining()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "training");
        if (existing is not null) { SelectedTab = existing; return; }
        var runner = new TrainingRunner(_sessions, _conversations, _ollama, _preferences);
        var page = new TrainingPageViewModel(runner, _trainingRepo, _conversations, _ollama, _preferences,
            msg => System.Diagnostics.Debug.WriteLine($"[Training] {msg}"),
            () =>
            {
                var tab = OpenTabs.FirstOrDefault(item => item.Key == "training");
                if (tab is not null) CloseTab(tab);
            });
        AddOrSelectTab("training", "Training", page, true);
    }

    private void OpenCatalog(CatalogPageKind kind)
    {
        if (kind == CatalogPageKind.Capabilities)
        {
            OpenCapabilities();
            return;
        }
        var pageModel = new CatalogPageViewModel(kind, _catalog, _ollama, true);
        var title = kind switch { CatalogPageKind.Agents => "Agents", CatalogPageKind.Capabilities => "Capabilities", _ => "Instruction Library" };
        if (kind == CatalogPageKind.Agents)
        {
            AddOrSelectTab("catalog-agents", title, new AgentsPage(pageModel, _agentRuntime), true);
            return;
        }

        AddOrSelectTab("catalog-" + kind.ToString().ToLowerInvariant(), title, pageModel, true);
    }

    private void OpenAutomations() => OpenAutomationsDashboard();

    private void OpenArchive() => AddOrSelectTab("archive-" + CurrentMode, "Archive", new ArchivePageViewModel(CurrentMode, _conversations, _containers), true);

    private void OpenActivityLog()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "activity-log");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new ActivityLogPageViewModel(_conversations, id => { });
        AddOrSelectTab("activity-log", "Activity Log", page, true);
    }

    private void OpenActionGraph() => OpenActionGraphTarget(null, null);

    private void OpenActionGraphTarget(Guid? executionId, Guid? actionId)
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "action-graph");
        if (existing is not null)
        {
            SelectedTab = existing;
            if (executionId is { } id && existing.Page is ActionGraphPage existingPage) _ = existingPage.OpenAsync(id, actionId);
            return;
        }
        var services = App.Services ?? throw new InvalidOperationException("Haven services are not available.");
        var page = new ActionGraphPage(
            services.GetRequiredService<ExecutionTraceService>(),
            services.GetRequiredService<IActionFeedbackRepository>(),
            services.GetRequiredService<IRemediationRepository>(),
            services.GetRequiredService<RemediationCoordinator>(),
            services.GetService<ExecutionEventHub>(),
            prompt => _ = OpenNewChatAsync(prompt),
            OpenApplicationSettings);
        AddOrSelectTab("action-graph", "Action Graph", page, true, HavenSurface.Tasks);
        if (executionId is { } requested) _ = page.OpenAsync(requested, actionId);
    }

    private void OpenModeLibrary()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "mode-library");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new ModeLibraryPageViewModel(_modeRegistry, _modeUsage, _pins);
        page.OpenInStudio += () =>
        {
            _ = NavigateModeAsync(HavenMode.Studio, true);
        };
        AddOrSelectTab("mode-library", "App Library", page, true);
    }

    private async Task ShowAppLauncherAsync(bool openInNewTab)
    {
        var appsTask = _modeRegistry.GetModesAsync(CancellationToken.None);
        var pinsTask = _pins.GetPinsAsync(CancellationToken.None);
        var usageTask = _modeUsage.GetRecentUsageAsync(30, CancellationToken.None);
        await Task.WhenAll(appsTask, pinsTask, usageTask);
        var pins = (await pinsTask).OrderBy(pin => pin.SortOrder).ToArray();
        var pinnedIds = pins.Select(pin => pin.ModeId).ToHashSet();
        var pinOrder = pins.Select((pin, index) => (pin.ModeId, index)).ToDictionary(item => item.ModeId, item => item.index);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var scores = (await usageTask)
            .GroupBy(item => item.ModeId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.TurnCount * Math.Pow(0.5, Math.Max(0, today.DayNumber - item.Date.DayNumber) / 14d)));
        var orderedApps = (await appsTask)
            .Where(app => !IsRetiredApp(app))
            .OrderBy(app => pinnedIds.Contains(app.Id) ? 0 : 1)
            .ThenBy(app => pinOrder.GetValueOrDefault(app.Id, int.MaxValue))
            .ThenByDescending(app => scores.GetValueOrDefault(app.Id))
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        TopRail.ShowAppLauncher(
            orderedApps,
            pinnedIds,
            openInNewTab,
            (app, newTab) => _ = LaunchAppAsync(app, newTab),
            OpenModeLibrary);
    }

    private async Task ShowUniversalSearchAsync()
    {
        var generation = Interlocked.Increment(ref _launcherSearchGeneration);
        var immediate = BuildImmediateLauncherItems();
        TopRail.ShowUniversalSearch(immediate, OpenCommandPalette, OpenApplicationSettings);

        try
        {
            var services = App.Services;
            var notesRepository = services?.GetService(typeof(INotesRepository)) as INotesRepository;
            var appsTask = _modeRegistry.GetModesAsync(CancellationToken.None);
            var conversationsTask = _conversations.GetRecentAsync(null, 40, CancellationToken.None);
            var projectsTask = _containers.GetByModeAsync(HavenMode.Studio, CancellationToken.None);
            var documentsTask = notesRepository?.ListAsync(CancellationToken.None)
                ?? Task.FromResult<IReadOnlyList<NotesDocumentSummary>>([]);
            var tasksTask = _planner.GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: false), CancellationToken.None);
            await Task.WhenAll(appsTask, conversationsTask, projectsTask, documentsTask, tasksTask);
            if (generation != _launcherSearchGeneration) return;

            var loaded = new List<UniversalSearchItem>();
            foreach (var app in (await appsTask).Where(item => item.IsEnabled && !IsRetiredApp(item)).Take(18))
            {
                var captured = app;
                loaded.Add(new UniversalSearchItem(
                    "Apps", captured.Name, captured.Description, captured.IconKey, "App",
                    () => _ = LaunchAppAsync(captured, false), SearchKeywords: captured.Key));
            }

            foreach (var conversation in (await conversationsTask).Take(24))
            {
                var captured = conversation;
                loaded.Add(new UniversalSearchItem(
                    "Chats", captured.Title,
                    $"{captured.UpdatedAt.LocalDateTime:g} | {DisplayMode(captured.Mode)}",
                    "chat", "Chat", () => _ = OpenConversationDefinitionAsync(captured)));
            }

            foreach (var project in (await projectsTask).Where(item => !item.IsArchived).OrderByDescending(item => item.UpdatedAt).Take(18))
            {
                var captured = project;
                loaded.Add(new UniversalSearchItem(
                    "Projects", captured.Name, $"Updated {captured.UpdatedAt.LocalDateTime:g}",
                    "studio", "Project", () => _ = OpenContainerDefinitionAsync(captured)));
            }

            foreach (var document in (await documentsTask).OrderByDescending(item => item.UpdatedAt).Take(18))
            {
                var captured = document;
                loaded.Add(new UniversalSearchItem(
                    "Documents", captured.Title, $"Updated {captured.UpdatedAt.LocalDateTime:g} | {captured.WordCount} words",
                    "file", "Document", () => _ = OpenLauncherDocumentAsync(captured.Id)));
            }

            foreach (var task in (await tasksTask).OrderBy(item => item.DueAt ?? DateTimeOffset.MaxValue).Take(18))
            {
                var captured = task;
                var due = captured.DueAt is null ? "No due date" : $"Due {captured.DueAt.Value.LocalDateTime:g}";
                loaded.Add(new UniversalSearchItem(
                    "Tasks", captured.Title, due, "tasks", "Task", () => _ = OpenPlanTaskAsync(captured.Id)));
            }

            var items = loaded.Concat(BuildImmediateLauncherItems().Where(item => item.Group != "Recommended")).ToList();
            var recommendedItems = new List<UniversalSearchItem>();
            foreach (var group in new[] { "Chats", "Projects", "Documents", "Tasks", "Apps", "Tabs" })
            {
                var recommended = items.FirstOrDefault(item => item.IsEnabled && item.Group == group);
                if (recommended is not null) recommendedItems.Add(recommended with { Group = "Recommended" });
            }
            items.InsertRange(0, recommendedItems.Take(5));
            TopRail.UpdateUniversalSearchItems(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Launcher search] {ex}");
            if (generation != _launcherSearchGeneration) return;
            var items = BuildImmediateLauncherItems();
            items.Insert(0, new UniversalSearchItem(
                "Recommended", "Some local results are unavailable",
                "Apps, chats, projects, documents or tasks could not be refreshed. Existing tabs and commands are still available.",
                "warning", "Status", () => { }, false, "Local search refresh failed."));
            TopRail.UpdateUniversalSearchItems(items);
        }
    }

    private List<UniversalSearchItem> BuildImmediateLauncherItems()
    {
        var items = new List<UniversalSearchItem>();
        foreach (var tab in OpenTabs)
        {
            var captured = tab;
            items.Add(new UniversalSearchItem(
                "Tabs", captured.Title, DisplaySurface(captured.Surface), "window", "Tab",
                () => SelectedTab = captured));
        }

        foreach (var command in AllCommandItems.Take(30))
        {
            var captured = command;
            var enabled = captured.RunCommand.CanExecute(null);
            items.Add(new UniversalSearchItem(
                "Commands", captured.Name, captured.Description, ActionIcon(captured.Name), "Command",
                () => Invoke(captured.RunCommand), enabled,
                enabled ? null : "Unavailable in the current context or permission state.", captured.Shortcut));
        }

        foreach (var recommended in items.Where(item => item.IsEnabled).Take(4).Reverse().ToArray())
            items.Insert(0, recommended with { Group = "Recommended" });
        return items;
    }

    private async Task OpenLauncherDocumentAsync(Guid documentId)
    {
        if (await NotesExperienceNavigation.OpenDocumentAsync(this, documentId)) return;
        _notifications.Show("Document unavailable", "That local Write document could not be opened.", ToastKind.Warning, TimeSpan.FromSeconds(5));
    }

    private static string DisplayMode(HavenMode mode) => mode switch
    {
        HavenMode.Study => "Study",
        HavenMode.Tasks => "Tasks",
        HavenMode.Studio => "Studio",
        _ => "Chat"
    };

    private static bool IsRetiredApp(ModeDefinition app) =>
        app.Key.Equals("do", StringComparison.OrdinalIgnoreCase)
        || app.Name.Equals("Do", StringComparison.OrdinalIgnoreCase)
        || app.Key.Equals("teach", StringComparison.OrdinalIgnoreCase)
        || app.Name.Equals("Teach", StringComparison.OrdinalIgnoreCase);

    private static string DisplaySurface(HavenSurface surface) => surface switch
    {
        HavenSurface.Study => "Study",
        HavenSurface.Tasks => "Tasks",
        _ => surface.ToString()
    };

    private async Task LaunchAppAsync(ModeDefinition app, bool openInNewTab)
    {
        var route = HavenAppRoutePolicy.Resolve(app);
        if (route.Surface == HavenSurface.Launcher)
        {
            await ShowUniversalSearchAsync();
        }
        else if (route.Kind == HavenAppRouteKind.Go)
        {
            if (openInNewTab) AddFallbackTab();
            await OpenGoAsync();
        }
        else if (route.Kind == HavenAppRouteKind.Dashboard)
        {
            if (openInNewTab) AddFallbackTab();
            await OpenDashboardAsync();
        }
        else if (route.Kind == HavenAppRouteKind.Browse)
        {
            if (openInNewTab) AddFallbackTab();
            OpenBrowser();
        }
        else if (route.Kind == HavenAppRouteKind.Plan)
        {
            if (openInNewTab) AddFallbackTab();
            OpenPlan();
        }
        else if (route.Kind == HavenAppRouteKind.Training)
        {
            if (openInNewTab) AddFallbackTab();
            OpenTraining();
        }
        else if (route.Kind == HavenAppRouteKind.Write)
        {
            await NotesExperienceNavigation.OpenAsync(this, NotesExperienceKind.Notes, openInNewTab);
        }
        else if (route.Kind is HavenAppRouteKind.Imagine or HavenAppRouteKind.Vision)
        {
            await OpenCreativeModeWorkspaceAsync(app, route.Surface, openInNewTab);
        }
        else if (route.Kind == HavenAppRouteKind.Play)
        {
            if (openInNewTab) AddFallbackTab();
            OpenPlay();
        }
        else if (route.Kind == HavenAppRouteKind.Automations)
        {
            if (openInNewTab) AddFallbackTab();
            OpenAutomationsDashboard();
        }
        else if (route.Kind == HavenAppRouteKind.Translate)
        {
            await OpenTranslateAsync(openInNewTab);
        }
        else if (route.Kind == HavenAppRouteKind.Terminal)
        {
            OpenTerminal(openInNewTab);
        }
        else if (route.Kind == HavenAppRouteKind.Mesh)
        {
            if (openInNewTab) AddNewTab();
            OpenMeshDashboard();
        }
        else if (route.Kind == HavenAppRouteKind.Spaces)
        {
            if (openInNewTab) AddNewTab();
            await OpenSpacesAsync();
        }
        else if (route.Kind == HavenAppRouteKind.Maps)
        {
            if (openInNewTab) AddNewTab();
            OpenMaps();
        }
        else if (route.Kind == HavenAppRouteKind.ModeWorkspace)
        {
            await OpenModeWorkspaceAsync(app, route.Surface, openInNewTab);
        }
        else
        {
            switch (app.BaseMode)
            {
                case HavenMode.Chat:
                    await OpenNewChatAsync(forceNewTab: openInNewTab);
                    break;
                case HavenMode.Study:
                    if (openInNewTab) AddFallbackTab();
                    await NavigateModeAsync(HavenMode.Study, false);
                    break;
                case HavenMode.Tasks:
                    if (openInNewTab) AddFallbackTab();
                    await NavigateModeAsync(HavenMode.Tasks, true);
                    break;
                case HavenMode.Studio:
                    if (openInNewTab) AddFallbackTab();
                    await NavigateModeAsync(HavenMode.Studio, true);
                    break;
            }
        }

        await _modeUsage.RecordUsageAsync(app.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
    }

    private async Task NavigateCurrentChatAsync()
    {
        if (_edition == HavenShellEdition.New)
        {
            await OpenNewChatAsync();
            return;
        }

        OpenCurrentChatTab();
        await CurrentChat.RefreshCatalogAsync(CancellationToken.None);
    }

    private void OpenCurrentChatTab()
    {
        var key = IsProjectOpen ? "project-chat-" + ActiveProject!.Id.ToString("N") : "chat-" + CurrentMode.ToString().ToLowerInvariant();
        AddOrSelectTab(key, IsProjectOpen ? ActiveProjectName + " chat" : ProductName, CurrentChat, IsProjectOpen);
    }

    private async Task SwitchSurfaceAsync(string? surfaceName)
    {
        if (string.IsNullOrWhiteSpace(surfaceName)) return;
        if (Enum.TryParse<HavenSurface>(surfaceName, true, out var surface))
        {
            switch (surface)
            {
                case HavenSurface.Chat:
                    await NavigateModeAsync(HavenMode.Chat, false);
                    break;
                case HavenSurface.Study:
                    await NavigateModeAsync(HavenMode.Study, false);
                    break;
                case HavenSurface.Tasks:
                    await NavigateModeAsync(HavenMode.Tasks, true);
                    break;
                case HavenSurface.Studio:
                    await NavigateModeAsync(HavenMode.Studio, true);
                    break;
                case HavenSurface.Browse:
                    OpenBrowser();
                    break;
                case HavenSurface.Plan:
                    OpenPlan();
                    break;
                case HavenSurface.Play:
                    OpenPlay();
                    break;
                case HavenSurface.Automations:
                    OpenAutomationsDashboard();
                    break;
                case HavenSurface.Terminal:
                    OpenTerminal(false);
                    break;
                case HavenSurface.Training:
                    OpenTraining();
                    break;
                case HavenSurface.Home:
                    await OpenHomeAsync();
                    break;
                case HavenSurface.Go:
                    await OpenGoAsync();
                    break;
                case HavenSurface.Dashboard:
                    await OpenDashboardAsync();
                    break;
                case HavenSurface.Launcher:
                    await ShowUniversalSearchAsync();
                    break;
                case HavenSurface.Translate:
                    await OpenTranslateAsync(false);
                    break;
                case HavenSurface.Mail:
                    OpenMail();
                    break;
                case HavenSurface.Mesh:
                    OpenMeshDashboard();
                    break;
                case HavenSurface.Maps:
                    OpenMaps();
                    break;
                default:
                    var registered = await _modeRegistry.GetModeByKeyAsync(surfaceName.ToLowerInvariant(), CancellationToken.None);
                    if (registered is not null)
                    {
                        var route = HavenAppRoutePolicy.Resolve(registered);
                        if (route.Kind == HavenAppRouteKind.ModeWorkspace)
                            await OpenModeWorkspaceAsync(registered, route.Surface, false);
                    }
                    break;
            }

            if (_modeRegistry is not null)
            {
                var mode = await _modeRegistry.GetModeByKeyAsync(surfaceName.ToLowerInvariant(), CancellationToken.None);
                if (mode is not null)
                    await _modeUsage.RecordUsageAsync(mode.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            }
        }
    }

    private void StartNewConversation()
    {
        if (_edition == HavenShellEdition.New && CurrentPage is NewChatPage newChat)
        {
            newChat.StartFreshConversation();
            ApplyShellVisualState();
            return;
        }

        if (_edition == HavenShellEdition.New)
        {
            if (CurrentMode == HavenMode.Studio && IsProjectOpen)
            {
                _ = StartProjectChatAsync(string.Empty);
                return;
            }

            var mode = CurrentMode is HavenMode.Chat or HavenMode.Study or HavenMode.Tasks
                ? CurrentMode
                : HavenMode.Chat;
            _ = StartNativeConversationAsync(mode, null);
            return;
        }

        if (CurrentMode == HavenMode.Studio && IsProjectOpen)
        {
            _ = StartProjectChatAsync(string.Empty);
            return;
        }
        AddOrSelectTab("chat-" + CurrentMode.ToString().ToLowerInvariant(), ProductName, CurrentChat, false);
        CurrentChat.NewChat();
        _ = RefreshRecentsAsync(CancellationToken.None);
    }

    private void AddNewTab()
    {
        _ = AddConfiguredNewTabAsync();
    }

    private async Task AddConfiguredNewTabAsync()
    {
        var preferredKey = _preferences.DefaultTabAppKey;
        if (string.IsNullOrWhiteSpace(preferredKey))
        {
            AddFallbackTab();
            return;
        }
        var countBeforeLaunch = OpenTabs.Count;
        try
        {
            var preferred = await _modeRegistry.GetModeByKeyAsync(preferredKey, CancellationToken.None);
            if (preferred is null || !preferred.IsEnabled)
            {
                AddFallbackTab();
                return;
            }
            await LaunchAppAsync(preferred, openInNewTab: true);
            if (OpenTabs.Count <= countBeforeLaunch) AddFallbackTab();
        }
        catch
        {
            // Keep the standard new-tab surface and preserve the preference for when the app is available again.
            if (OpenTabs.Count <= countBeforeLaunch) AddFallbackTab();
        }
    }

    private WorkspaceTabViewModel AddFallbackTab()
    {
        if (_edition == HavenShellEdition.New)
        {
            var go = CreateGoPage();
            var goKey = "go-" + Guid.NewGuid().ToString("N")[..8];
            AddOrSelectTab(goKey, "Go", go, true, HavenSurface.Go, forceNewTab: true);
            go.FocusComposer();
            return SelectedTab!;
        }

        var home = CreateHomePage();
        var key = "home-" + Guid.NewGuid().ToString("N")[..8];
        AddOrSelectTab(key, "Home", home, true, HavenSurface.Home, forceNewTab: true);
        _ = home.ActivateAsync(CancellationToken.None);
        return SelectedTab!;
    }

    private GoPage CreateGoPage()
    {
        var page = new GoPage(_bus);
        page.SubmitRequested += async (_, instruction) =>
            await RouteGoSubmissionAsync(page, instruction);
        page.RefreshSuggestionsRequested += (_, _) =>
            QueueGoSuggestionRefresh(page, "The user asked Haven for another set of useful next actions.", TimeSpan.Zero, true);
        page.Disposed += OnGoPageDisposed;
        page.AddRequested += OnGoAddRequested;
        page.AddCatalogItemSelected += OnGoCatalogItemSelected;
        page.AppShortcutInvoked += async (_, app) => await LaunchAppAsync(app, false);
        _ = ConfigureAddMenuAsync(page);
        QueueGoSuggestionRefresh(
            page,
            "The user is viewing the Go workspace and has not entered a new instruction yet.",
            TimeSpan.FromSeconds(3),
            false);
        return page;
    }

    private NewDashboardPage CreateNewDashboardPage()
    {
        var page = new NewDashboardPage(
            _bus, _modeRegistry, _modeUsage, _pins, _conversations, _versionedSettings,
            _dashboard, _dashboardLayout, _dashboardProviders);
        page.EnableDashboardAssistant(new DashboardEditPlanner(_ollama, () => _preferences.DefaultModel));
        page.DashboardActionRequested += async (_, actionKey) =>
        {
            switch (actionKey.Trim().ToLowerInvariant())
            {
                case "new-chat": await OpenNewChatAsync(); break;
                case "call": await OpenVoiceSessionFromActionAsync(); break;
                case "plan": OpenPlan(); break;
                case "browse": OpenBrowser(); break;
                case "automations": OpenAutomations(); break;
                default: await LaunchAppByKeyAsync(actionKey); break;
            }
        };
        page.ModeRequested += async (_, mode) => await LaunchAppAsync(mode, false);
        page.ConversationRequested += async (_, conversation) =>
        {
            await OpenNewChatAsync();
            if (_newChatPage is not null) await _newChatPage.LoadConversationAsync(conversation);
        };
        page.ManageAppsRequested += async (_, _) => await ShowAppLauncherAsync(false);
        return page;
    }

    private async Task ConfigureAddMenuAsync(NewChatPage page)
    {
        var agentsTask = _catalog.GetAgentsAsync(CancellationToken.None);
        var capabilitiesTask = _capabilityRegistry.DiscoverAsync(CurrentCapabilityPlatform, CancellationToken.None);
        var instructionsTask = _catalog.GetPromptsAsync(CancellationToken.None);
        var appsTask = _modeRegistry.GetModesAsync(CancellationToken.None);
        await Task.WhenAll(agentsTask, capabilitiesTask, instructionsTask, appsTask);
#if ANDROID
        var apps = (await appsTask)
            .Concat(await GetInstalledAndroidAppDefinitionsAsync())
            .ToArray();
#else
        var apps = await appsTask;
#endif
        _availableCapabilities = await capabilitiesTask;
        page.SetAddCatalogue(await agentsTask, _availableCapabilities, await instructionsTask, apps);
        RefreshContextualActions();
    }

    private void OpenCapabilities()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "capabilities");
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        AddOrSelectTab(
            "capabilities",
            "Capabilities",
            new CapabilityCatalogPage(_bus, _capabilityRepository, _modeRegistry, _ollama, OpenTemplateLab),
            true,
            HavenSurface.Studio);
    }

    private void OpenGenUiCreationHome()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "genui-create");
        if (existing is not null) { SelectedTab = existing; return; }
        AddOrSelectTab("genui-create", "Create with Generative UI",
            new GenUiCreationHomePage(_bus, _genUiApps, _genUiSessions, _genUiRouter, _genUiInstances, OpenGenUiGenerationAsync),
            true, HavenSurface.Studio);
    }

    private async Task OpenGenUiGenerationAsync(string prompt)
    {
        var request = "Create this as a Haven Generative UI app or interactive surface. Build the complete first-turn workflow rather than describing it in text.\n\n" + prompt;
        await OpenScopedNewChatPageAsync(HavenMode.Studio, null, $"genui-generation-{Guid.NewGuid():N}",
            "Generative UI", HavenSurface.Studio, prompt: request);
    }

    private void OpenTemplateLab()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "genui-template-lab");
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        AddOrSelectTab(
            "genui-template-lab",
            "Template Preview Lab",
            new GenUiTemplatePreviewPage(_bus, _genUiTemplates, _genUiRouter, _genUiInstances, _calculatorTemplate),
            true,
            HavenSurface.Studio);
    }

    private Task OpenProjectPreviewAsync(StudioProjectPage project)
    {
        if (!project.HasRoot)
        {
            _notifications.Show("Project preview", "Connect an existing project folder before opening a live preview.", ToastKind.Warning);
            return Task.CompletedTask;
        }
        var provider = App.Services?.GetServices<IProjectPreviewProvider>().FirstOrDefault(item => item.CanPreview(project.RootPath));
        if (provider is null)
        {
            _notifications.Show("Project preview", "This project does not expose a supported package.json dev/start script or ASP.NET Core web target.", ToastKind.Warning, TimeSpan.FromSeconds(7));
            return Task.CompletedTask;
        }
        var primary = SelectedTab;
        var normalizedRoot = Path.GetFullPath(project.RootPath).ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedRoot)))[..12].ToLowerInvariant();
        var existing = OpenTabs.FirstOrDefault(tab => tab.Key == "project-preview-" + hash);
        if (existing is null)
        {
            AddOrSelectTab("project-preview-" + hash, "Live preview", new ProjectPreviewPage(provider, project.RootPath), true, HavenSurface.Studio, forceNewTab: true);
            existing = SelectedTab;
        }
        if (primary is not null && existing is not null)
        {
            SelectedTab = primary;
            OpenInSplitView(existing);
        }
        return Task.CompletedTask;
    }

    private async Task ConfigureAddMenuAsync(GoPage page)
    {
        var agentsTask = _catalog.GetAgentsAsync(CancellationToken.None);
        var capabilitiesTask = _capabilityRegistry.DiscoverAsync(CurrentCapabilityPlatform, CancellationToken.None);
        var instructionsTask = _catalog.GetPromptsAsync(CancellationToken.None);
        var appsTask = _modeRegistry.GetModesAsync(CancellationToken.None);
        var pinsTask = _pins.GetPinsAsync(CancellationToken.None);
        var usageTask = _modeUsage.GetRecentUsageAsync(30, CancellationToken.None);
        await Task.WhenAll(agentsTask, capabilitiesTask, instructionsTask, appsTask, pinsTask, usageTask);
#if ANDROID
        var apps = (await appsTask)
            .Concat(await GetInstalledAndroidAppDefinitionsAsync())
            .ToArray();
#else
        var apps = await appsTask;
#endif
        _availableCapabilities = await capabilitiesTask;
        page.SetAddCatalogue(await agentsTask, _availableCapabilities, await instructionsTask, apps);
        var installed = apps.Where(app => app.IsEnabled && app.InstallState == ModeInstallState.Installed).ToArray();
        var pinnedIds = (await pinsTask).OrderBy(pin => pin.SortOrder).Select(pin => pin.ModeId).ToArray();
        var pinned = pinnedIds.Select(id => installed.FirstOrDefault(app => app.Id == id)).Where(app => app is not null).Cast<ModeDefinition>().ToArray();
        var suggested = (await usageTask)
            .Where(usage => !pinnedIds.Contains(usage.ModeId))
            .GroupBy(usage => usage.ModeId)
            .OrderByDescending(group => group.Sum(usage => usage.TurnCount + usage.CompletionCount))
            .Select(group => installed.FirstOrDefault(app => app.Id == group.Key))
            .Where(app => app is not null)
            .Cast<ModeDefinition>()
            .Take(Math.Max(0, 8 - pinned.Length))
            .ToArray();
        page.SetAppShortcuts(pinned.Select(app => new GoAppShortcut(app, true)).Concat(suggested.Select(app => new GoAppShortcut(app, false))).ToArray());
        RefreshContextualActions();
    }

    private static CapabilityPlatform CurrentCapabilityPlatform =>
        OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : CapabilityPlatform.Windows;

    private async void OnNewChatAddActionSelected(object? sender, AddMenu.AddMenuAction action)
    {
        if (action == AddMenu.AddMenuAction.File && sender is NewChatPage page)
            await page.AddFileAsync();
    }

    private async void OnNewChatCatalogItemSelected(object? sender, AddMenuSelection selection)
    {
        // NewChatPage applies the selection before raising this event. Apps are
        // task context, so selecting one must not navigate away from the thread.
        await Task.CompletedTask;
    }

    private async void OnGoAddRequested(object? sender, AddMenu.AddMenuAction action)
    {
        if (action != AddMenu.AddMenuAction.File) return;
        // Open file picker directly without forcing navigation to chat
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach files",
            AllowMultiple = true
        });
        if (sender is GoPage page)
            page.AttachFiles(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    private async void OnGoCatalogItemSelected(object? sender, AddMenuSelection selection)
    {
        // GoPage owns the pending task context and attaches Apps locally before
        // raising this event. Nothing selected from Add may force navigation.
        await Task.CompletedTask;
    }

    private void QueueGoSuggestionRefresh(GoPage page, string activity, TimeSpan delay, bool showProgress)
    {
        CancellationTokenSource cancellation;
        lock (_goSuggestionRefreshes)
        {
            if (_goSuggestionRefreshes.Remove(page, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            cancellation = new CancellationTokenSource();
            _goSuggestionRefreshes[page] = cancellation;
        }

        if (showProgress) page.SetRefreshInProgress(true);
        _ = RefreshGoSuggestionsAsync(page, activity, delay, showProgress, cancellation);
    }

    private async Task RefreshGoSuggestionsAsync(
        GoPage page,
        string activity,
        TimeSpan delay,
        bool showProgress,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);

            var suggestions = await _goSuggestions.GenerateAsync(activity, cancellation.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => page.SetSuggestions(suggestions));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (showProgress) page.SetRefreshInProgress(false);
                lock (_goSuggestionRefreshes)
                {
                    if (_goSuggestionRefreshes.TryGetValue(page, out var current) && ReferenceEquals(current, cancellation))
                    {
                        _goSuggestionRefreshes.Remove(page);
                        cancellation.Dispose();
                    }
                }
            });
        }
    }

    private void OnGoPageDisposed(object? sender, EventArgs e)
    {
        if (sender is not GoPage page) return;
        page.Disposed -= OnGoPageDisposed;
        lock (_goSuggestionRefreshes)
        {
            if (!_goSuggestionRefreshes.Remove(page, out var cancellation)) return;
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private HomePage CreateHomePage() => new(
        _bus,
        _dashboard,
        _dashboardLayout,
        _ollama,
        _catalog,
        _dashboardProviders,
        new Dictionary<string, Func<Task>>(StringComparer.OrdinalIgnoreCase)
        {
            ["new-chat"] = async () => { await NavigateModeAsync(HavenMode.Chat, false); StartNewConversation(); },
            ["chat"] = () => NavigateModeAsync(HavenMode.Chat, false),
            ["study"] = () => NavigateModeAsync(HavenMode.Study, false),
            ["tasks"] = () => NavigateModeAsync(HavenMode.Tasks, true),
            ["plan"] = () => { OpenPlan(); return Task.CompletedTask; },
            ["browse"] = () => { OpenBrowser(); return Task.CompletedTask; },
            ["studio"] = () => NavigateModeAsync(HavenMode.Studio, true),
            ["automations"] = () => { OpenAutomations(); return Task.CompletedTask; },
            ["imagine"] = () => LaunchAppByKeyAsync("imagine"),
            ["present"] = () => LaunchAppByKeyAsync("present"),
            ["data"] = () => LaunchAppByKeyAsync("data"),
            ["vision"] = () => LaunchAppByKeyAsync("vision"),
            ["play"] = () => LaunchAppByKeyAsync("play"),
            ["translate"] = () => LaunchAppByKeyAsync("translate"),
            ["launcher"] = () => LaunchAppByKeyAsync("launcher"),
            ["go"] = () => LaunchAppByKeyAsync("go"),
            ["dashboard"] = () => LaunchAppByKeyAsync("dashboard")
        });

    private async Task LaunchAppByKeyAsync(string key)
    {
        var app = await _modeRegistry.GetModeByKeyAsync(key, CancellationToken.None);
        if (app is null)
        {
            _notifications.Show("App unavailable", $"The {key} App is not registered in this profile.", ToastKind.Warning, TimeSpan.FromSeconds(5));
            return;
        }
        await LaunchAppAsync(app, false);
    }

    private async Task SelectContainerAsync(ContainerItemViewModel? item)
    {
        if (item is null) return;
        if (CurrentMode == HavenMode.Chat)
        {
            await OpenChatGroupAsync(item.Definition);
            return;
        }
        CurrentChat.SelectedContainer = item;
        if (CurrentMode == HavenMode.Studio) await OpenContainerDefinitionAsync(item.Definition);
        else AddOrSelectTab("chat-" + CurrentMode.ToString().ToLowerInvariant(), ProductName, CurrentChat, false);
        await RefreshRecentsAsync(CancellationToken.None);
        RaiseShellProperties();
    }

    private async Task OpenContainerDefinitionAsync(ContainerDefinition definition)
    {
        if (definition.Mode == HavenMode.Chat)
        {
            await OpenChatGroupAsync(definition);
        }
        else if (definition.Mode == HavenMode.Studio)
        {
            var chat = await GetOrCreateProjectChatAsync(definition);
            CurrentChat = chat;
            var page = GetOrCreateProjectPage(definition);
            ActivateProject(definition, page);
            AddOrSelectTab("project-" + definition.Id.ToString("N"), definition.Name, page, true);
        }
        else
        {
            var item = CurrentChat.Containers.FirstOrDefault(candidate => candidate.Id == definition.Id);
            if (item is null)
            {
                await CurrentChat.RefreshContainersAsync(CancellationToken.None);
                item = CurrentChat.Containers.FirstOrDefault(candidate => candidate.Id == definition.Id);
            }
            if (item is not null) CurrentChat.SelectedContainer = item;
            AddOrSelectTab("chat-" + CurrentMode.ToString().ToLowerInvariant(), ProductName, CurrentChat, false);
        }
        await RefreshRecentsAsync(CancellationToken.None);
        RaiseShellProperties();
    }

    private async Task StartProjectChatAsync(string prompt)
    {
        if (ActiveProject is null) return;

        if (_edition == HavenShellEdition.New)
        {
            await OpenScopedNewChatPageAsync(
                HavenMode.Studio,
                ActiveProject.Id,
                $"project-chat-{ActiveProject.Id:N}-{Guid.NewGuid():N}",
                ActiveProject.Name + " chat",
                HavenSurface.Studio,
                prompt: prompt);
            await RefreshRecentsAsync(CancellationToken.None);
            return;
        }

        var chat = await GetOrCreateProjectChatAsync(ActiveProject);
        CurrentChat = chat;
        chat.NewChat();
        AddOrSelectTab("project-chat-" + ActiveProject.Id.ToString("N"), ActiveProject.Name + " chat", chat, true);
        if (!string.IsNullOrWhiteSpace(prompt)) chat.UsePrompt(prompt);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task OpenProjectConversationAsync(Conversation conversation)
    {
        if (conversation.ContainerId is not Guid projectId)
        {
            return;
        }

        var project = (await _containers.GetByModeAsync(HavenMode.Studio, CancellationToken.None))
            .FirstOrDefault(item => item.Id == projectId);
        if (project is null)
        {
            return;
        }

        if (_edition == HavenShellEdition.New)
        {
            ActivateProject(project);
            await OpenScopedNewChatPageAsync(
                HavenMode.Studio,
                project.Id,
                $"conversation-{conversation.Id:N}",
                conversation.Title,
                HavenSurface.Studio,
                conversation);
            await RefreshRecentsAsync(CancellationToken.None);
            return;
        }

        CurrentChat = await GetOrCreateProjectChatAsync(project);
        ActivateProject(project);
        AddOrSelectTab(
            "project-chat-" + project.Id.ToString("N"),
            conversation.Title,
            CurrentChat,
            true,
            HavenSurface.Studio);
        await CurrentChat.LoadConversationAsync(conversation.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private Task OpenFileAsync(ContainerDefinition container, WorkspaceFileItemViewModel file) => OpenFileAtAsync(container, file, null);

    private Task OpenFileAtAsync(ContainerDefinition container, WorkspaceFileItemViewModel file, CodeRange? range)
    {
        ActivateProject(container);
        var page = new WorkspaceEditorPage(container, CurrentChat.ConversationId, file, _workspaceTools, _workspaceState, _conversations,
            () => CurrentChat.BranchCurrentAsync(), () => CurrentChat.StopCommand.Execute(null), location => OpenCodeLocationAsync(container, location));
        if (range is not null) page.NavigateTo(range);
        AddOrSelectTab("file-" + container.Id.ToString("N") + "-" + file.RelativePath.ToLowerInvariant(), file.Name, page, true);
        return Task.CompletedTask;
    }

    private Task OpenCodeLocationAsync(ContainerDefinition container, CodeLocation location)
    {
        if (!location.IsInWorkspace || string.IsNullOrWhiteSpace(container.RootPath)) return Task.CompletedTask;
        var file = new WorkspaceFileItemViewModel(container.RootPath, location.DisplayPath);
        return OpenFileAtAsync(container, file, location.Range);
    }

    private void OpenContainerSettings()
    {
        if (CurrentMode == HavenMode.Study && CurrentChat.SelectedLesson is not null)
        {
            AddOrSelectTab("lesson-settings-" + CurrentChat.SelectedLesson.Id.ToString("N"), "Lesson settings",
                new LessonSettingsPageViewModel(CurrentChat.SelectedLesson, _containers, RefreshAfterSettingsAsync), true);
            return;
        }
        if (CurrentChat.SelectedContainer is null) return;
        var selected = CurrentChat.SelectedContainer;
        AddOrSelectTab("container-settings-" + CurrentChat.SelectedContainer.Id.ToString("N"), ContainerSettingsLabel,
            new ContainerSettingsPage(null, selected, _containers, RefreshAfterSettingsAsync,
                selected.Definition.Mode == HavenMode.Chat ? () => ReturnToChatGroupAsync(selected.Id) : null), true);
    }

    private async Task ReturnToChatGroupAsync(Guid groupId)
    {
        var group = (await _containers.GetByModeAsync(HavenMode.Chat, CancellationToken.None))
            .FirstOrDefault(item => item.Id == groupId);
        if (group is not null) await OpenChatGroupAsync(group);
        else if (NavigateBackCommand.CanExecute(null)) NavigateBackCommand.Execute(null);
    }

    private async Task RefreshAfterSettingsAsync()
    {
        await CurrentChat.RefreshContainersAsync(CancellationToken.None);
        if (ActiveProject is not null)
        {
            var refreshed = CurrentChat.Containers.FirstOrDefault(item => item.Id == ActiveProject.Id)?.Definition;
            if (refreshed is not null)
            {
                _projectPages.Remove(refreshed.Id);
                ActivateProject(refreshed, GetOrCreateProjectPage(refreshed));
            }
        }
        RaiseShellProperties();
    }

    private void OpenApplicationSettings()
    {
        AddOrSelectTab(
            "settings-" + CurrentMode,
            "Settings",
            new SettingsHavenPage(
                _bus,
                _preferences,
                _ollama,
                _privacy,
                _modelProviders,
                _providerConfigurations,
                _providerSecrets),
            true);
    }

    private async Task OpenConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await OpenConversationDefinitionAsync(item.Definition);
    }

    private async Task OpenConversationDefinitionAsync(Conversation definition)
    {
        if (definition.Mode == HavenMode.Chat && definition.ContainerId is not null)
        {
            await OpenGroupedConversationAsync(definition);
            return;
        }
        if (definition.Mode == HavenMode.Studio && definition.ContainerId is Guid projectId)
        {
            var project = (await _containers.GetByModeAsync(HavenMode.Studio, CancellationToken.None)).FirstOrDefault(candidate => candidate.Id == projectId);
            if (project is null) return;
            CurrentChat = await GetOrCreateProjectChatAsync(project);
            ActivateProject(project);
        }
        else
        {
            if (CurrentChat.Mode != definition.Mode || CurrentChat.Mode == HavenMode.Studio && CurrentChat.SelectedContainer is not null)
                CurrentChat = await GetOrCreateChatAsync(definition.Mode);
            if (definition.Mode == HavenMode.Studio) ClearActiveProject();
        }

        if (_edition == HavenShellEdition.New)
        {
            await OpenScopedNewChatPageAsync(
                definition.Mode,
                definition.ContainerId,
                $"conversation-{definition.Id:N}",
                definition.Title,
                SurfaceForMode(definition.Mode),
                definition);
            await RefreshRecentsAsync(CancellationToken.None);
            return;
        }

        var key = definition.ContainerId is Guid containerId ? "project-chat-" + containerId.ToString("N") : "chat-" + CurrentChat.Mode.ToString().ToLowerInvariant();
        AddOrSelectTab(key, definition.Title, CurrentChat, definition.ContainerId is not null);
        await CurrentChat.LoadConversationAsync(definition.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task RenameConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DraftTitle)) return;
        var updated = item.Definition with { Title = item.DraftTitle.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        await _conversations.UpsertConversationAsync(updated, CancellationToken.None);
        item.FinishRename(updated);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task TogglePinAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item.Definition with { IsPinned = !item.Definition.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task BranchConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await OpenConversationAsync(item);
        await CurrentChat.BranchCurrentAsync();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task ArchiveConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item.Definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        if (CurrentChat.ConversationId == item.Definition.Id) CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task DeleteConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.DeleteConversationAsync(item.Definition.Id, CancellationToken.None);
        if (CurrentChat.ConversationId == item.Definition.Id) CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task RefreshRecentsAsync(CancellationToken cancellationToken)
    {
        IEnumerable<Conversation> items;
        if (CurrentMode is HavenMode.Chat or HavenMode.Study)
        {
            var scope = CurrentChat.CurrentScope ?? (CurrentMode == HavenMode.Study
                ? ConversationScope.StudyQuickChat
                : ConversationScope.GeneralChat);
            items = await _conversations.GetRecentInScopeAsync(scope, 120, cancellationToken);
        }
        else
        {
            items = await _conversations.GetRecentAsync(CurrentMode, 120, cancellationToken);
        }
        if (CurrentMode == HavenMode.Studio)
        {
            items = ActiveProject is not null ? items.Where(item => item.ContainerId == ActiveProject.Id) : items.Where(item => item.ContainerId is null);
        }
        if (!string.IsNullOrWhiteSpace(SearchQuery)) items = items.Where(item => item.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        PinnedConversations.Clear();
        RecentConversations.Clear();
        foreach (var conversation in items)
        {
            var viewModel = new RecentConversationViewModel(conversation, OpenConversationAsync, RenameConversationAsync, TogglePinAsync,
                BranchConversationAsync, ArchiveConversationAsync, DeleteConversationAsync);
            viewModel.IsActive = conversation.Id == CurrentChat.ConversationId;
            if (conversation.IsPinned) PinnedConversations.Add(viewModel); else RecentConversations.Add(viewModel);
        }
        RaisePropertyChanged(nameof(HasPinnedConversations));
        RaisePropertyChanged(nameof(HasRecentConversations));
        RaisePropertyChanged(nameof(ShowNoProjectChats));
    }

    private async Task TogglePinCurrentAsync()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            await newChat.TogglePinAsync();
            return;
        }

        var item = await _conversations.GetAsync(CurrentChat.ConversationId, CancellationToken.None);
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item with { IsPinned = !item.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task ToggleTemporaryActiveConversationAsync()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            await newChat.ToggleTemporaryAsync();
            return;
        }

        if (CurrentChat.ToggleTemporaryCommand.CanExecute(null))
            CurrentChat.ToggleTemporaryCommand.Execute(null);
    }

    private Task BranchActiveConversationAsync() =>
        CurrentPage is NewChatPage newChat
            ? newChat.BranchLatestAsync()
            : CurrentChat.BranchCurrentAsync();

    private Task CompactActiveConversationAsync() =>
        CurrentPage is NewChatPage newChat
            ? newChat.CompactContextAsync()
            : CurrentChat.CompactContextAsync();

    private async Task UndoActiveAsync()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            await newChat.UndoLatestAsync();
            return;
        }

        if (CurrentPage is WorkspaceEditorPage editor && editor.UndoCommand.CanExecute(null))
            editor.UndoCommand.Execute(null);
    }

    private async Task RedoActiveAsync()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            await newChat.RedoLatestAsync();
            return;
        }

        if (CurrentPage is WorkspaceEditorPage editor && editor.RedoCommand.CanExecute(null))
            editor.RedoCommand.Execute(null);
    }

    private void BeginRenameCurrent()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            newChat.ShowRenameFlyout();
            return;
        }

        RenameDraft = CurrentChat.ConversationTitle;
        IsRenameOpen = true;
    }

    private async Task SaveRenameCurrentAsync()
    {
        var item = await _conversations.GetAsync(CurrentChat.ConversationId, CancellationToken.None);
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item with { Title = RenameDraft.Trim(), UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        IsRenameOpen = false;
        await CurrentChat.LoadConversationAsync(item.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task DeleteCurrentAsync()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            await newChat.DeleteConversationAsync();
            return;
        }

        IsDeleteConfirmationOpen = false;
        await _conversations.DeleteConversationAsync(CurrentChat.ConversationId, CancellationToken.None);
        CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private void CopyLastResponse()
    {
        var content = CurrentPage is NewChatPage newChat
            ? newChat.GetLastAssistantResponse()
            : CurrentChat.Messages.LastOrDefault(item => item.Role == MessageRole.Assistant)?.Content;
        if (!string.IsNullOrWhiteSpace(content)) CopyRequested?.Invoke(this, content);
    }

    private async Task ArchiveActiveConversationAsync()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            await newChat.ArchiveAsync();
            return;
        }

        var item = await _conversations.GetAsync(CurrentChat.ConversationId, CancellationToken.None);
        if (item is null) return;
        await _conversations.UpsertConversationAsync(
            item with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow },
            CancellationToken.None);
        CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private void RequestDeleteCurrent()
    {
        if (CurrentPage is NewChatPage newChat)
        {
            newChat.ShowDeleteConfirmation();
            return;
        }

        IsDeleteConfirmationOpen = CurrentChat.HasMessages;
    }

    private void AddOrSelectTab(
        string key,
        string title,
        object page,
        bool closeable,
        HavenSurface? surface = null,
        bool forceNewTab = false)
    {
        var resolvedSurface = surface ?? InferSurface(page);
        var existing = OpenTabs.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!ReferenceEquals(existing.Page, page)) existing.ReplacePage(page);
            existing.Title = title;
            existing.SetSurface(resolvedSurface);
            if (ReferenceEquals(SelectedTab, existing)) ApplySelectedTab(existing);
            else SelectedTab = existing;
            RefreshTopRailTabs();
            return;
        }

        if (!forceNewTab && SelectedTab is not null)
        {
            if (SelectedTab.Page is IActivatablePage previous) previous.Deactivate();
            SelectedTab.NavigateTo(key, title, page, closeable, resolvedSurface);
            ApplySelectedTab(SelectedTab);
            RefreshTopRailTabs();
            return;
        }

        var tab = new WorkspaceTabViewModel(key, title, page, closeable, resolvedSurface);
        OpenTabs.Add(tab);
        SelectedTab = tab;
        RaisePropertyChanged(nameof(IsHorizontalTabsVisible));
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private HavenSurface InferSurface(object page) => page switch
    {
        GoPage => HavenSurface.Go,
        NewDashboardPage => HavenSurface.Dashboard,
        NewChatPage => HavenSurface.Chat,
        HomePageViewModel => HavenSurface.Home,
        CallPageViewModel => HavenSurface.Chat,
        PlanPageViewModel => HavenSurface.Plan,
        BrowserPage => HavenSurface.Browse,
        TrainingPageViewModel => HavenSurface.Training,
        ChatGroupPageViewModel => HavenSurface.Chat,
        ModeLibraryPageViewModel => HavenSurface.Home,
        ChatPage chat => SurfaceForMode(chat.Mode),
        StudioProjectPage or WorkspaceEditorPage => HavenSurface.Studio,
        _ => SelectedTab?.Surface ?? SurfaceForMode(CurrentMode)
    };

    private static HavenSurface SurfaceForMode(HavenMode mode) => mode switch
    {
        HavenMode.Chat => HavenSurface.Chat,
        HavenMode.Study => HavenSurface.Study,
        HavenMode.Tasks => HavenSurface.Tasks,
        HavenMode.Studio => HavenSurface.Studio,
        _ => HavenSurface.Chat
    };

    private void CloseTab(WorkspaceTabViewModel? item)
    {
        _ = TryCloseTabAsync(item);
    }

    private void NavigateBack()
    {
        if (SelectedTab is null || !SelectedTab.TryGoBack()) return;
        ApplySelectedTab(SelectedTab);
    }

    private void NavigateForward()
    {
        if (SelectedTab is null || !SelectedTab.TryGoForward()) return;
        ApplySelectedTab(SelectedTab);
    }

    private void BuildCommandPalette()
    {
        AllCommandItems =
        [
            Command(FileNewLabel, "Start a clean conversation in the current product.", "Ctrl+N", NewChatCommand),
            Command("Branch chat", "Copy the current conversation and context into an independent branch.", "/Branch", BranchCurrentCommand),
            Command("Temporary chat", "Toggle local history for this conversation.", "/Temporary", ToggleTemporaryCommand),
            Command("Compact context", "Summarise older turns while preserving decisions and requirements.", "/Compact", CompactCurrentCommand),
            Command("Archive current chat", "Remove the chat from recents without destroying it.", string.Empty, ArchiveCurrentCommand),
            Command("Rename chat", "Change the current chat title.", string.Empty, BeginRenameCurrentCommand),
            Command("Delete current chat", "Permanently remove the current conversation after confirmation.", string.Empty, RequestDeleteCurrentCommand),
            Command("Pin or unpin chat", "Toggle the chat in the Pinned section.", string.Empty, TogglePinCurrentCommand),
            Command("Copy last response", "Copy the most recent Haven response.", string.Empty, CopyLastResponseCommand),
            Command("Undo", "Undo the latest chat message or editable workspace change.", "Ctrl+Z", UndoCurrentCommand),
            Command("Redo", "Restore the latest undone chat message or workspace change.", "Ctrl+Y", RedoCurrentCommand),
            Command("Save", "Save the current editable workspace.", "Ctrl+S", SaveCurrentCommand),
            Command("Configure model", "Search models and open advanced generation and safety options.", string.Empty, ConfigureModelCommand),
            Command("Agents", "Create and manage specialised assistants shared with Chat and Go.", string.Empty, NavigateAgentsCommand),
            Command("Instruction Library", "Browse built-in and custom reusable instructions invoked with >.", string.Empty, NavigatePromptsCommand),
            Command("Capabilities", "Browse discoverable, App-owned capabilities and their runtime safety metadata.", string.Empty, NavigateCapabilitiesCommand),
            Command("Create with Generative UI", "Generate, reopen, pin and import persistent interactive Haven surfaces.", string.Empty, new RelayCommand(OpenGenUiCreationHome)),
            Command("Template Preview Lab", "Search registered GenUI templates and exercise trusted structured previews.", string.Empty, new RelayCommand(OpenTemplateLab)),
            Command("Automations", "Create, test, and run reusable, scheduled, recurring, and triggered workflows.", string.Empty, NavigateAutomationsCommand),
            Command("Archive", "Restore archived chats, groups, and projects.", string.Empty, NavigateArchiveCommand),
            Command("Activity Log", "View recent conversations and tool activity across sessions.", string.Empty, NavigateActivityLogCommand),
            Command("Action Graph", "Inspect the real execution trace in Graph or List form, including failures, recovery and action feedback.", string.Empty, new RelayCommand(OpenActionGraph)),
            Command("Haven Browse", "Open the isolated tabbed browser and side assistant.", string.Empty, NavigateBrowserCommand),
            Command("Haven Training", "Run an autonomous agent session and score the result.", string.Empty, NavigateTrainingCommand),
            Command("App Library", "Discover, pin, and create Haven apps.", string.Empty, NavigateModeLibraryCommand),
            Command("Build Browse extension", "Create a scoped Haven extension manifest and content script in Tasks or Studio.", string.Empty, BuildBrowserExtensionCommand),
            Command("Toggle sidebar", "Show or hide the current product sidebar.", string.Empty, ToggleSidebarCommand),
            Command("Refresh models", "Reload the installed Ollama model list.", string.Empty, RefreshModelsCommand),
            Command("Settings", "Appearance, models, permissions, context, and browser options.", string.Empty, NavigateSettingsCommand)
        ];
        FilterCommands();
        RefreshContextualActions();
        TopRail?.SetEditActionsHandler(OpenCapabilities);
    }

    private void RefreshContextualActions()
    {
        if (TopRail is null) return;
        var hasConversation = CurrentPage is NewChatPage newChat && newChat.HasStarted
                              || CurrentPage is ChatPage && CurrentChat.HasMessages;

        var pinned = new DynamicActionToolbar.ToolbarAction[]
        {
            new("Voice", "call", () => _ = OpenVoiceSessionFromActionAsync(),
                Description: "Start or continue a live voice session with the current context.", IsFeatured: true),
            new("New chat", "plus", StartNewConversation,
                Description: "Start a new chat without changing app settings.", IsFeatured: true),
            new("Apps", "rocket", () => _ = ShowAppLauncherAsync(false),
                Description: "Open another Haven app in this tab.", IsFeatured: true),
            new("Settings", "settings", OpenApplicationSettings,
                Description: "Open Haven settings.", IsFeatured: true)
        };

        var contextual = new List<DynamicActionToolbar.ToolbarAction>();
        if (CurrentPage is NewChatPage activeChat)
        {
            contextual.Add(new("Switch agent", "agent", () => OpenCatalog(CatalogPageKind.Agents),
                Category: "Recommended", Description: "Choose the active agent for this conversation."));
            AddCapabilityActions(contextual, activeChat);
            if (hasConversation)
            {
                contextual.Add(new("Branch chat", "branch", () => _ = activeChat.BranchLatestAsync(),
                    Category: "Chat", Description: "Create an independent branch from the current chat."));
                contextual.Add(new("Regenerate last response", "refresh", () => _ = activeChat.RegenerateLatestAsync(),
                    Category: "Chat", Description: "Generate a fresh response from the most recent user message."));
                contextual.Add(new("Undo last message", "chevron-left", () => Invoke(UndoCurrentCommand),
                    Category: "Chat", Description: "Undo the most recent editable chat change."));
                contextual.Add(new("Redo last message", "chevron-right", () => Invoke(RedoCurrentCommand),
                    Category: "Chat", Description: "Restore the most recently undone chat change."));
            }

            AddModeWorkspaceActions(contextual, activeChat);
        }
        else if (CurrentSurface is HavenSurface.Chat or HavenSurface.Study)
        {
            contextual.Add(new("Switch agent", "agent", () => OpenCatalog(CatalogPageKind.Agents),
                Category: "Recommended", Description: "Choose the active agent for this conversation."));
            if (hasConversation)
            {
                contextual.Add(new("Branch chat", "branch", () => _ = CurrentChat.BranchCurrentAsync(),
                    Category: "Chat", Description: "Create an independent branch from the current chat."));
            }
            if (CurrentSurface == HavenSurface.Study)
            {
                contextual.AddRange(
                [
                    new("Quick chats", "chat", () => Invoke(CurrentChat.SelectQuickChatsCommand), Category: "Study", Description: "Switch to study chats that are not attached to a subject or lesson."),
                    new("Create subject", "plus", () => Invoke(CurrentChat.NewContainerCommand), Category: "Study", Description: "Create a subject with its default General lesson."),
                    new("Create lesson", "study", () => Invoke(CurrentChat.NewLessonCommand), Category: "Study", Description: "Add a lesson to the active subject."),
                    new("Subject settings", "settings", OpenContainerSettings, Category: "Study", Description: "Edit instructions, resources and settings for the active subject.")
                ]);
            }
        }
        else if (CurrentPage is BrowserPage browserPage)
        {
            contextual.AddRange(
            [
                new("New tab", "plus", () => Invoke(browserPage.NewTabCommand), Category: "Recommended", Description: "Open an independent browser tab."),
                new("Reload page", "refresh", () => Invoke(browserPage.ReloadCommand), Category: "Recommended", Description: "Reload the active browser tab."),
                new("Back", "chevron-left", () => Invoke(browserPage.BackCommand), Category: "Browser", Description: "Go back in the active browser tab."),
                new("Forward", "chevron-right", () => Invoke(browserPage.ForwardCommand), Category: "Browser", Description: "Go forward in the active browser tab."),
                new("Hard reload", "refresh", () => Invoke(browserPage.HardReloadCommand), Category: "Browser", Description: "Reload the page without using its cached content."),
                new("Private tab", "browse", () => Invoke(browserPage.NewPrivateTabCommand), Category: "Browser", Description: "Open an isolated private tab when the platform supports it."),
                new("Add bookmark", "bookmark", () => Invoke(browserPage.AddBookmarkCommand), Category: "Browser", Description: "Bookmark the current page."),
                new("Browser history", "clock", () => Invoke(browserPage.ToggleHistoryCommand), Category: "Browser", Description: "Show browsing history."),
                new("Browser settings", "settings", () => Invoke(browserPage.ToggleSettingsCommand), Category: "Browser", Description: "Open privacy, downloads, permissions and browser settings."),
                new("Print page", "file", () => Invoke(browserPage.PrintCommand), Category: "Browser", Description: "Print the current page."),
                new("Developer tools", "commands", () => Invoke(browserPage.InspectCommand), Category: "Browser", Description: "Inspect the active page using WebView developer tools."),
                new("Page assistant", "chat", () => Invoke(browserPage.ToggleAssistantCommand), Category: "Browser", Description: "Open the page-aware assistant.")
            ]);
        }
        else if (CurrentSurface == HavenSurface.Studio && ActiveProjectPage is { } project)
        {
            contextual.AddRange(
            [
                new("Refresh project", "refresh", () => Invoke(project.RefreshCommand), Category: "Recommended", Description: "Refresh files, Git state, and project health."),
                new("Build project", "build", () => Invoke(project.BuildCommand), Category: "Recommended", Description: "Build the active project."),
                new("Run tests", "test", () => Invoke(project.TestCommand), Category: "Studio", Description: "Run the active project's tests."),
                new("Live preview", "browse", () => _ = OpenProjectPreviewAsync(project), Category: "Studio", Description: "Open the supported running web preview beside this Project."),
                new("Open terminal", "commands", () => Invoke(project.OpenTerminalCommand), Category: "Studio", Description: "Open a terminal at the project root."),
                new("Project chat", "chat", () => Invoke(project.StartChatCommand), Category: "Studio", Description: "Start a chat scoped to this project."),
                new("Create with Generative UI", "sparkles", OpenGenUiCreationHome, Category: "Studio", Description: "Generate or reopen persistent interactive Haven surfaces."),
                new("Template Preview Lab", "sparkles", OpenTemplateLab, Category: "Studio", Description: "Inspect and exercise trusted structured GenUI templates.")
            ]);
        }
        else if (CurrentSurface == HavenSurface.Plan && _planPage is { } planner)
        {
            contextual.AddRange(
            [
                new("Today", "calendar", () => Invoke(planner.TodayCommand), Category: "Recommended", Description: "Jump to today's plan."),
                new("Refresh plan", "refresh", () => Invoke(planner.RefreshCommand), Category: "Plan", Description: "Refresh tasks, events, and sync state."),
                new("Ask planner", "sparkles", () => Invoke(planner.AskAiCommand), Category: "Plan", Description: "Create a reviewable AI planning proposal.")
            ]);
        }
        else if (CurrentSurface == HavenSurface.Tasks)
        {
            contextual.AddRange(
            [
                new("New task", "plus", StartNewConversation, Category: "Recommended", Description: "Start a new task conversation."),
                new("New task group", "tasks", OpenNewContainer, Category: "Tasks", Description: "Create a group for related tasks and references."),
                new("Automations", "automation", OpenAutomationsDashboard, Category: "Automations", Description: "Create or manage reusable, scheduled, recurring, and triggered workflows."),
                new("Reusable workflows", "automation", OpenAutomationsDashboard, Category: "Automations", Description: "Create, test, run, or edit reusable workflows."),
                new("Activity log", "clock", OpenActivityLog, Category: "Tasks", Description: "Review recent task and tool activity.")
            ]);
        }
        else if (CurrentSurface == HavenSurface.Go && _goPage is { } go)
        {
            AddCapabilityActions(contextual, go);
            contextual.AddRange(
            [
                new("Focus Go", "search", go.FocusComposer, Category: "Recommended", Description: "Focus the universal Go instruction box."),
                new("Refresh suggestions", "refresh", () => QueueGoSuggestionRefresh(go, "Refresh the useful next actions for the current Haven state.", TimeSpan.Zero, true), Category: "Recommended", Description: "Generate a fresh set of useful next actions."),
                new("Add context", "plus", () => _ = ConfigureAddMenuAsync(go), Category: "Tools", Description: "Refresh Agents, Capabilities, Instructions and Apps available to Go.")
            ]);
        }
        else if (CurrentSurface == HavenSurface.Dashboard && _newDashboardPage is { } dashboard)
        {
            contextual.AddRange(
            [
                new("Refresh dashboard", "refresh", () => _ = dashboard.ActivateAsync(CancellationToken.None), Category: "Recommended", Description: "Refresh pins, activity and dashboard content."),
                new("Manage Apps", "rocket", () => _ = ShowAppLauncherAsync(false), Category: "Recommended", Description: "Choose which Apps are pinned and available."),
                new("Customise with Haven", "edit", () => _ = OpenNewChatAsync("Help me customise my Haven dashboard around what I use most."), Category: "View", Description: "Open a guided dashboard customisation chat.")
            ]);
        }
        else if (CurrentSurface == HavenSurface.Home)
        {
            contextual.AddRange(
            [
                new("Refresh Home", "refresh", () => _ = OpenHomeAsync(), Category: "Recommended", Description: "Refresh dashboard statistics and recent work."),
                new("Open Dashboard", "dashboard", () => _ = OpenDashboardAsync(), Category: "Recommended", Description: "Open your custom dashboard pages."),
                new("Manage Apps", "rocket", () => _ = ShowAppLauncherAsync(false), Category: "Tools", Description: "Manage installed and pinned Haven Apps.")
            ]);
        }
        else if (CurrentSurface == HavenSurface.Training)
        {
            contextual.AddRange(
            [
                new("New training session", "training", OpenTraining, Category: "Recommended", Description: "Open a clean autonomous training session."),
                new("Activity log", "clock", OpenActivityLog, Category: "Tools", Description: "Review completed training activity."),
                new("Training settings", "settings", OpenApplicationSettings, Category: "Tools", Description: "Review model, permission and safety defaults.")
            ]);
        }

        contextual.Add(new("Action Graph", "commands", OpenActionGraph, Category: "View", Description: "Inspect what Haven actually did for a request in Graph or List form."));

        var catalogue = AllCommandItems
            .Where(item => !IsConversationOnlyAction(item.Name) || hasConversation)
            .Select(item => new DynamicActionToolbar.ToolbarAction(
                item.Name,
                ActionIcon(item.Name),
                () => Invoke(item.RunCommand),
                Tooltip: item.Description,
                Category: ContextCategory(item.Name),
                Description: item.Description,
                Shortcut: item.Shortcut));
        TopRail.SetActions(pinned.Concat(contextual).Concat(catalogue).DistinctBy(item => item.Label).ToArray());
    }

    private void AddCapabilityActions(
        List<DynamicActionToolbar.ToolbarAction> actions,
        NewChatPage page)
    {
        foreach (var capability in _availableCapabilities.Where(item => item.IsAttachable))
        {
            var captured = capability;
            var attached = page.IsCapabilityAttached(captured.Id);
            actions.Add(new(
                attached ? $"Remove {captured.Name}" : captured.Name,
                captured.IconKey,
                () =>
                {
                    page.ToggleCapability(captured);
                    RefreshContextualActions();
                },
                Category: CapabilityCategory(captured),
                Description: CapabilityDescription(captured, attached)));
        }
    }

    private void AddCapabilityActions(
        List<DynamicActionToolbar.ToolbarAction> actions,
        GoPage page)
    {
        foreach (var capability in _availableCapabilities.Where(item => item.IsAttachable))
        {
            var captured = capability;
            var attached = page.IsCapabilityAttached(captured.Id);
            actions.Add(new(
                attached ? $"Remove {captured.Name}" : captured.Name,
                captured.IconKey,
                () =>
                {
                    page.ToggleCapability(captured);
                    RefreshContextualActions();
                },
                Category: CapabilityCategory(captured),
                Description: CapabilityDescription(captured, attached)));
        }
    }

    private static string CapabilityCategory(CapabilityDefinition capability) => capability.OwnerAppKey switch
    {
        CapabilityRegistryCatalog.GeneralOwner => "General",
        "browse" => "Browser",
        "tasks" => "Tasks",
        "studio" => "Studio",
        _ => "Tools"
    };

    private static string CapabilityDescription(CapabilityDefinition capability, bool attached) =>
        $"{capability.Description} {(attached ? "Attached" : "Attach as relevance")} - {capability.RiskClass} risk - {capability.Availability}. Attachment does not grant permission.";

    private void AddModeWorkspaceActions(List<DynamicActionToolbar.ToolbarAction> actions, NewChatPage page)
    {
        var category = CurrentSurface is HavenSurface.Data ? "Data" :
            CurrentSurface is HavenSurface.Imagine or HavenSurface.Present or HavenSurface.Vision or HavenSurface.Play ? "Media" :
            CurrentSurface is HavenSurface.Tasks ? "Tasks" : "Recommended";
        if (CurrentSurface is HavenSurface.Chat or HavenSurface.Study) return;

        actions.Add(new("Attach file", "plus", page.ShowAddMenu, Category: category,
            Description: "Attach a supported local file or image to this app conversation."));
        actions.Add(new("Start fresh", "plus", () => page.StartFreshConversation(), Category: category,
            Description: $"Start a fresh {DisplaySurface(CurrentSurface)} workspace."));

        var draft = CurrentSurface switch
        {
            HavenSurface.Imagine => "Help me turn this idea into a detailed image prompt with composition, lighting, palette and negative constraints: ",
            HavenSurface.Present => "Create an audience-aware presentation outline with slide titles, key points and speaker notes about: ",
            HavenSurface.Data => "Inspect the attached data, describe its schema and quality issues, then calculate the most useful findings.",
            HavenSurface.Vision => "Inspect the attached image carefully. Describe what is visible, note uncertainty, and answer: ",
            HavenSurface.Play => "Help me design or find a safe interactive local experience for: ",
            HavenSurface.Translate => "Translate the following while preserving tone, formatting and terminology. Target language: ",
            HavenSurface.Launcher => "Find the best installed Haven app, project, command or recent item for: ",
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(draft))
            actions.Add(new("Insert starter", IconForSurface(CurrentSurface), () => page.SetDraft(draft), Category: category,
                Description: "Put a mode-aware starter into the composer without sending it."));

        if (CurrentSurface == HavenSurface.Tasks)
        {
            actions.AddRange(
            [
                new("New task group", "tasks", OpenNewContainer, Category: "Tasks", Description: "Create a group for related tasks and references."),
                new("Automations", "automation", OpenAutomationsDashboard, Category: "Automations", Description: "Create or manage reusable, scheduled, recurring, and triggered workflows."),
                new("Reusable workflows", "automation", OpenAutomationsDashboard, Category: "Automations", Description: "Create, test, run, or edit reusable workflows."),
                new("Activity log", "clock", OpenActivityLog, Category: "Tasks", Description: "Review recent task and tool activity.")
            ]);
        }
    }

    private static bool IsConversationOnlyAction(string name) => name is
        "Branch chat" or "Compact context" or "Archive current chat" or "Rename chat"
        or "Delete current chat" or "Pin or unpin chat" or "Copy last response";

    private string ContextCategory(string name)
    {
        if (name.Contains("chat", StringComparison.OrdinalIgnoreCase)
            || name.Contains("model", StringComparison.OrdinalIgnoreCase)
            || name.Contains("plugin", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Instruction", StringComparison.OrdinalIgnoreCase))
            return CurrentSurface == HavenSurface.Study ? "Study" : "Chat";
        if (name.Contains("project", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Build", StringComparison.OrdinalIgnoreCase)) return "Studio";
        if (name.Contains("Browse", StringComparison.OrdinalIgnoreCase)) return "Browser";
        if (name.Contains("Scheduled", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Reusable", StringComparison.OrdinalIgnoreCase)) return "Tasks";
        if (name.Contains("sidebar", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Action Graph", StringComparison.OrdinalIgnoreCase)) return "View";
        if (name.Contains("New", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Archive", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Save", StringComparison.OrdinalIgnoreCase)) return "File";
        return "Tools";
    }

    private static void Invoke(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private static string ActionIcon(string name)
    {
        var value = name.ToLowerInvariant();
        if (value.Contains("new")) return "plus";
        if (value.Contains("rename")) return "edit";
        if (value.Contains("delete")) return "delete";
        if (value.Contains("archive")) return "archive";
        if (value.Contains("copy")) return "file";
        if (value.Contains("browse")) return "browse";
        if (value.Contains("training")) return "training";
        if (value.Contains("model") || value.Contains("refresh")) return "refresh";
        if (value.Contains("settings")) return "settings";
        if (value.Contains("plugin")) return "plugin";
        if (value.Contains("instruction")) return "prompt";
        if (value.Contains("pin")) return "pin";
        if (value.Contains("project") || value.Contains("build") || value.Contains("extension")) return "studio";
        if (value.Contains("scheduled") || value.Contains("plan")) return "plan";
        if (value.Contains("undo")) return "chevron-left";
        if (value.Contains("redo")) return "chevron-right";
        return "commands";
    }

    private CommandPaletteItemViewModel Command(string name, string description, string shortcut, System.Windows.Input.ICommand command) =>
        new(name, description, shortcut, new RelayCommand(
            () => { IsCommandPaletteOpen = false; if (command.CanExecute(null)) command.Execute(null); },
            () => command.CanExecute(null)));

    private void FilterCommands()
    {
        CommandItems.Clear();
        foreach (var item in AllCommandItems.Where(item => string.IsNullOrWhiteSpace(CommandSearch) ||
                     item.Name.Contains(CommandSearch, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(CommandSearch, StringComparison.OrdinalIgnoreCase)))
            CommandItems.Add(item);
    }

    private void AttachChat(ChatPage chat)
    {
        chat.PropertyChanged += OnChatPropertyChanged;
        chat.ConversationChanged += OnConversationChanged;
    }

    private void DetachChat(ChatPage chat)
    {
        chat.PropertyChanged -= OnChatPropertyChanged;
        chat.ConversationChanged -= OnConversationChanged;
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatPage.Status)
            or nameof(ChatPage.SelectedContainer)
            or nameof(ChatPage.SelectedLesson)
            or nameof(ChatPage.SelectedDuo)
            or nameof(ChatPage.ConversationTitle)
            or nameof(ChatPage.HasMessages)
            or nameof(ChatPage.IsTemporary)
            or nameof(ChatPage.ContextPercent)
            or nameof(ChatPage.ContextRemainingPercent))
            RaiseShellProperties();
    }

    private void OnConversationChanged(object? sender, EventArgs e)
    {
        RaiseShellProperties();
        _ = RefreshRecentsAsync(CancellationToken.None);
    }

    private void OnCallStateChanged(object? sender, CallStateChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RaisePropertyChanged(nameof(HasLiveCall));
        RaisePropertyChanged(nameof(LiveCallLabel));
    });

    private void WireShellControls()
    {
        TopRail.AttachEventBus(_bus);
        TopRail.HomeRequested += OnTopRailHomeRequested;
        TopRail.NewTabRequested += OnTopRailNewTabRequested;
        TopRail.BackRequested += OnTopRailBackRequested;
        TopRail.ForwardRequested += OnTopRailForwardRequested;
        TopRail.AppsRequested += OnTopRailAppsRequested;
        TopRail.ModelRequested += OnTopRailModelRequested;
        TopRail.SearchRequested += OnTopRailSearchRequested;
        TopRail.TabSelected += OnTopRailTabSelected;
        TopRail.TabCloseRequested += OnTopRailTabCloseRequested;
        TopRail.TabRenameRequested += OnTopRailTabRenameRequested;
        TopRail.TabCommandRequested += OnTopRailTabCommandRequested;
        TopRail.NotificationOpenRequested += OnNotificationOpenRequested;

        GoChatButton.Click += OnGoChatClicked;
        GoDashboardButton.Click += OnGoDashboardClicked;
        SavedChatOption.Click += OnSavedChatSelected;
        TemporaryChatOption.Click += OnTemporaryChatSelected;
        SidebarControl.DataContext = this;

        _bus.RegisterElement("Shell.Go.Chat", GoChatButton);
        _bus.WirePointerEvents("Shell.Go.Chat", GoChatButton);
        _bus.RegisterElement("Shell.Go.Dashboard", GoDashboardButton);
        _bus.WirePointerEvents("Shell.Go.Dashboard", GoDashboardButton);
    }

    private async void OnTopRailHomeRequested(object? sender, EventArgs e) => await OpenHomeAsync();
    private void OnTopRailNewTabRequested(object? sender, EventArgs e) => AddNewTab();
    private void OnTopRailBackRequested(object? sender, EventArgs e)
    {
        if (NavigateBackCommand.CanExecute(null)) NavigateBackCommand.Execute(null);
    }
    private void OnTopRailForwardRequested(object? sender, EventArgs e)
    {
        if (NavigateForwardCommand.CanExecute(null)) NavigateForwardCommand.Execute(null);
    }
    private async void OnTopRailAppsRequested(object? sender, EventArgs e) => await ShowAppLauncherAsync(false);
    private async void OnTopRailModelRequested(object? sender, EventArgs e) => await ShowModelSelectorAsync();
    private async void OnTopRailSearchRequested(object? sender, EventArgs e) => await ShowUniversalSearchAsync();

    private void OnNotificationOpenRequested(object? sender, HavenNavigationTarget target)
    {
        if (target.ExecutionId is { } executionId)
        {
            OpenActionGraphTarget(executionId, target.ActionId);
            return;
        }
        if (target.TabId is { } tabId && ResolveTab(tabId.ToString()) is { } tab) SelectedTab = tab;
    }

    private void OnTopRailTabSelected(object? sender, string key)
    {
        var tab = ResolveTab(key);
        if (tab is not null) SelectedTab = tab;
    }

    private void OnTopRailTabCloseRequested(object? sender, string key)
    {
        var tab = ResolveTab(key);
        CloseTab(tab);
    }

    private void OnTopRailTabRenameRequested(object? sender, TabRenameRequestedEventArgs e)
    {
        var tab = ResolveTab(e.Key);
        if (tab is null) return;
        tab.Title = e.Title;
        RefreshTopRailTabs();
    }

    private async void OnGoChatClicked(object? sender, RoutedEventArgs e)
    {
        _bus.Fire("Shell.Go.Chat.Click");
        if (_edition == HavenShellEdition.New)
            await OpenNewChatAsync();
        else
            await NavigateModeAsync(HavenMode.Chat, false);
    }

    private async void OnGoDashboardClicked(object? sender, RoutedEventArgs e)
    {
        _bus.Fire("Shell.Go.Dashboard.Click");
        await (_edition == HavenShellEdition.New ? OpenDashboardAsync() : OpenHomeAsync());
    }

    private void ShowModelSelector() => _ = ShowModelSelectorAsync();

    private async Task ShowModelSelectorAsync()
    {
        TopRail.SetModelSelectorEnabled(false);
        try
        {
            AttachBetaOverlays();
            var models = await _ollama.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var rows = new StackPanel { Spacing = 12 };
                rows.Children.Add(new TextBlock
                {
                    Text = "Model options",
                    FontSize = 18,
                    FontWeight = FontWeight.ExtraBold,
                    Margin = new Avalonia.Thickness(8, 3, 8, 8)
                });

                var selectedName = CurrentPage switch
                {
                    NewChatPage newChatPage => newChatPage.SelectedModelName,
                    TrainingPageViewModel training => training.SelectedModel,
                    _ => CurrentChat?.SelectedModel?.Name
                };
                var modelNames = models.Select(model => model.Name).ToArray();
                var selectedModelIndex = Array.FindIndex(modelNames, name =>
                    name.Equals(selectedName ?? _preferences.DefaultModel, StringComparison.OrdinalIgnoreCase));
                var modelPicker = new HavenSelect
                {
                    ItemsSource = modelNames,
                    SelectedIndex = selectedModelIndex,
                    MinWidth = 178,
                    MaxWidth = 210,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    IsEnabled = models.Count > 0
                };
                modelPicker.SelectionChanged += (_, _) =>
                {
                    if (modelPicker.SelectedIndex < 0 || modelPicker.SelectedIndex >= models.Count) return;
                    var model = models[modelPicker.SelectedIndex];
                    if (CurrentPage is NewChatPage newChat) newChat.SelectModel(model);
                    else if (CurrentPage is TrainingPageViewModel training) training.SelectedModel = model.Name;
                    else if (CurrentChat is { } currentChat) currentChat.SelectedModel = model;
                    _preferences.SetModelDefaults(model.Name, _preferences.DefaultEffort);
                    TopRail.SetModelSummary(model.Name, EffortPercentage(_preferences.DefaultEffort));
                };
                var modelRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                    Margin = new Avalonia.Thickness(8, 2)
                };
                modelRow.Children.Add(new TextBlock
                {
                    Text = "Model",
                    FontWeight = FontWeight.Bold,
                    FontSize = 13,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
                Grid.SetColumn(modelPicker, 1);
                modelRow.Children.Add(modelPicker);
                rows.Children.Add(modelRow);
                if (models.Count == 0)
                {
                    rows.Children.Add(new TextBlock
                    {
                        Text = "No local models are available.",
                        Classes = { "muted" },
                        Margin = new Avalonia.Thickness(8, -6, 8, 0)
                    });
                }
                rows.Children.Add(new Separator { Margin = new Avalonia.Thickness(4, 8) });

                var permissionsMatch = _preferences.FilePermission == _preferences.CommandPermission
                                       && _preferences.CommandPermission == _preferences.BrowserPermission
                                       && _preferences.BrowserPermission == _preferences.ComputerPermission;
                var permissionOptions = new[] { "Custom", "Ask Every Time", "Risk-Free Only", "Full Access" };
                var permissionPicker = new HavenSelect
                {
                    ItemsSource = permissionOptions,
                    SelectedIndex = permissionsMatch
                        ? _preferences.FilePermission switch
                        {
                            PermissionMode.Ask => 1,
                            PermissionMode.AutoSafe => 2,
                            PermissionMode.FullAccess => 3,
                            _ => 0
                        }
                        : 0,
                    MinWidth = 145,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
                };
                permissionPicker.SelectionChanged += (_, _) =>
                {
                    var mode = permissionPicker.SelectedIndex switch
                    {
                        1 => PermissionMode.Ask,
                        2 => PermissionMode.AutoSafe,
                        3 => PermissionMode.FullAccess,
                        _ => (PermissionMode?)null
                    };
                    if (mode is { } selectedMode)
                        _preferences.SetToolPermissions(selectedMode, selectedMode, selectedMode, selectedMode);
                };

                var permissionRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                    Margin = new Avalonia.Thickness(8, 2)
                };
                permissionRow.Children.Add(new TextBlock
                {
                    Text = "Allow Actions",
                    FontWeight = FontWeight.Bold,
                    FontSize = 13,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
                Grid.SetColumn(permissionPicker, 1);
                permissionRow.Children.Add(permissionPicker);
                rows.Children.Add(permissionRow);

                var voices = _globalCallViewModel?.Voices ?? _callCoordinator.Capabilities.Voices;
                var selectedVoice = _globalCallViewModel?.SelectedVoice
                                    ?? voices.FirstOrDefault(item => item.IsDefault)
                                    ?? voices.FirstOrDefault();
                var voicePicker = new HavenSelect
                {
                    ItemsSource = voices.Select(item => item.Name).ToArray(),
                    SelectedIndex = selectedVoice is null ? -1 : voices.ToList().IndexOf(selectedVoice),
                    MinWidth = 145,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    IsEnabled = voices.Count > 0
                };
                voicePicker.SelectionChanged += (_, _) =>
                {
                    if (_globalCallViewModel is not null
                        && voicePicker.SelectedIndex >= 0
                        && voicePicker.SelectedIndex < voices.Count)
                        _globalCallViewModel.SelectedVoice = voices[voicePicker.SelectedIndex];
                };
                var voiceRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                    Margin = new Avalonia.Thickness(8, 2)
                };
                voiceRow.Children.Add(new TextBlock
                {
                    Text = "Voice",
                    FontWeight = FontWeight.Bold,
                    FontSize = 13,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
                Grid.SetColumn(voicePicker, 1);
                voiceRow.Children.Add(voicePicker);
                rows.Children.Add(voiceRow);

                var reasoningValue = new TextBlock
                {
                    Text = $"{EffortPercentage(_preferences.DefaultEffort)}%",
                    FontWeight = FontWeight.Bold,
                    FontSize = 12,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                var reasoningHeader = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Avalonia.Thickness(8, 8, 8, 2)
                };
                reasoningHeader.Children.Add(new TextBlock { Text = "Reasoning", FontWeight = FontWeight.Bold, FontSize = 13 });
                Grid.SetColumn(reasoningValue, 1);
                reasoningHeader.Children.Add(reasoningValue);
                rows.Children.Add(reasoningHeader);

                var reasoning = new HavenSlider
                {
                    Minimum = 20,
                    Maximum = 100,
                    Value = EffortPercentage(_preferences.DefaultEffort),
                    TickFrequency = 20,
                    IsSnapToTickEnabled = true,
                    Margin = new Avalonia.Thickness(8, 0, 8, 2)
                };
                reasoning.ValueChanged += (_, args) =>
                {
                    var percent = Math.Clamp((int)Math.Round(args.NewValue / 20d) * 20, 20, 100);
                    var effort = EffortForPercentage(percent);
                    reasoningValue.Text = $"{percent}%";
                    TopRail.SetModelSummary(
                        CurrentPage is NewChatPage activeNewChat
                            ? activeNewChat.SelectedModelName ?? _preferences.DefaultModel
                            : CurrentChat?.SelectedModel?.Name ?? _preferences.DefaultModel,
                        percent);
                    _preferences.SetModelDefaults(_preferences.DefaultModel, effort);
                    if (CurrentPage is not NewChatPage && CurrentChat is not null)
                        CurrentChat.SelectedEffort = effort;
                };
                rows.Children.Add(reasoning);
                rows.Children.Add(new TextBlock
                {
                    Text = "Higher reasoning can improve difficult answers but may take longer.",
                    Classes = { "muted" },
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(8, 0, 8, 4)
                });

                _modelSelectorFlyout = new HavenDropdown
                {
                    Placement = PlacementMode.BottomEdgeAlignedRight,
                    Content = new HavenDropdownCard
                    {
                        Width = 420,
                        MinWidth = 420,
                        Padding = new Avalonia.Thickness(18),
                        Child = new ScrollViewer
                        {
                            MaxHeight = 570,
                            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                            Content = rows
                        }
                    }
                };
                TopRail.ShowModelFlyout(_modelSelectorFlyout);
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _notifications.Show("Models unavailable", "Haven could not load the local model list.", ToastKind.Warning, TimeSpan.FromSeconds(6)));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => TopRail.SetModelSelectorEnabled(true));
        }
    }

    private static int EffortPercentage(EffortLevel effort) => effort switch
    {
        EffortLevel.Low => 20,
        EffortLevel.Medium => 60,
        EffortLevel.High => 80,
        EffortLevel.Max => 100,
        _ => 60
    };

    private static EffortLevel EffortForPercentage(int percentage) => percentage switch
    {
        <= 20 => EffortLevel.Low,
        <= 60 => EffortLevel.Medium,
        <= 80 => EffortLevel.High,
        _ => EffortLevel.Max
    };

    private async Task RefreshActiveModelsAsync()
    {
        if (CurrentPage is NewChatPage newChat)
            await newChat.RefreshModelsAsync();
        else if (CurrentChat.RefreshModelsCommand.CanExecute(null))
            CurrentChat.RefreshModelsCommand.Execute(null);
        ApplyShellVisualState();
    }

    private void OnSavedChatSelected(object? sender, RoutedEventArgs e)
    {
        _newChatPage?.SetTemporary(false);
        StoredChatLabel.Text = "Saved Chat";
    }

    private void OnTemporaryChatSelected(object? sender, RoutedEventArgs e)
    {
        _newChatPage?.SetTemporary(true);
        StoredChatLabel.Text = "Temporary Chat";
    }

    private void RefreshTopRailTabs()
    {
        if (TopRail is null) return;
        var visibleTabs = OpenTabs.Where(tab => !tab.IsGroupCollapsed || ReferenceEquals(tab, SelectedTab) ||
            (tab.GroupId is { } groupId && ReferenceEquals(tab, OpenTabs.First(item => item.GroupId == groupId))));
        TopRail.SetTabs(visibleTabs.Select(tab => new TopRailTab(
            tab.SessionId.ToString(),
            tab.IsGroupCollapsed && tab.GroupId is { } collapsedGroup
                ? $"{tab.GroupName} ({OpenTabs.Count(item => item.GroupId == collapsedGroup)})"
                : tab.Title,
            IconForSurface(tab.Surface),
            ReferenceEquals(tab, SelectedTab),
            tab.IsCloseable,
            tab.GroupId,
            tab.GroupName,
            tab.IsGroupCollapsed)).ToArray());
        TopRail.SetNavigationAvailability(
            SelectedTab?.CanGoBack == true,
            SelectedTab?.CanGoForward == true);
        if (AllCommandItems.Count > 0) RefreshContextualActions();
    }

    private static string IconForSurface(HavenSurface surface) => surface switch
    {
        HavenSurface.Home => "home",
        HavenSurface.Chat => "chat",
        HavenSurface.Study => "study",
        HavenSurface.Tasks => "tasks",
        HavenSurface.Studio => "studio",
        HavenSurface.Plan => "plan",
        HavenSurface.Browse => "browse",
        HavenSurface.Training => "training",
        _ => "window"
    };

    private void ApplyShellVisualState()
    {
        if (PageContent is null) return;
        PageContent.Content = CurrentPage;
        SidebarControl.IsVisible = _edition == HavenShellEdition.Classic && HasFullSidebar && !IsSplitView;
        NativeSidebarHost.IsVisible = _edition == HavenShellEdition.New
                                      && CurrentSurface == HavenSurface.Chat
                                      && IsSidebarOpen
                                      && !IsSplitView;
        ShellContextBar.IsVisible = false;
        StoredChatDropdown.IsVisible = _edition == HavenShellEdition.New
                                       && CurrentPage is NewChatPage newChatPage
                                       && !newChatPage.HasStarted;
        GoModeLabel.Text = CurrentPage is NewDashboardPage ? "Dashboard" : CurrentPage is NewChatPage ? "Chat" : "Go";
        TopRail.SetModelSummary(
            CurrentPage switch
            {
                NewChatPage newChat => newChat.SelectedModelName ?? _preferences.DefaultModel,
                TrainingPageViewModel training => training.SelectedModel,
                _ => CurrentChat?.SelectedModel?.Name ?? _preferences.DefaultModel
            },
            EffortPercentage(_preferences.DefaultEffort));
        RefreshTopRailTabs();
    }

    private void RaiseShellProperties()
    {
        RaisePropertyChanged(nameof(CurrentSurface));
        RaisePropertyChanged(nameof(CurrentMode));
        RaisePropertyChanged(nameof(IsStudy));
        RaisePropertyChanged(nameof(IsChatProduct));
        RaisePropertyChanged(nameof(HasContainers));
        RaisePropertyChanged(nameof(HasAnyContainers));
        RaisePropertyChanged(nameof(SupportsDuo));
        RaisePropertyChanged(nameof(ProductName));
        RaisePropertyChanged(nameof(NewItemLabel));
        RaisePropertyChanged(nameof(FileNewLabel));
        RaisePropertyChanged(nameof(FileNewContainerLabel));
        RaisePropertyChanged(nameof(ContainerHeading));
        RaisePropertyChanged(nameof(ProjectMenuHeader));
        RaisePropertyChanged(nameof(WorkspaceEyebrow));
        RaisePropertyChanged(nameof(WorkspaceTitle));
        RaisePropertyChanged(nameof(ShowTemporaryHeaderAction));
        RaisePropertyChanged(nameof(ShowContextHeaderWidget));
        RaisePropertyChanged(nameof(TemporaryHeaderActionLabel));
        RaisePropertyChanged(nameof(ContextPercent));
        RaisePropertyChanged(nameof(ContextRemainingPercent));
        RaisePropertyChanged(nameof(ContextLabel));
        RaisePropertyChanged(nameof(ContextRemainingLabel));
        RaisePropertyChanged(nameof(RecentHeading));
        RaisePropertyChanged(nameof(ContainerSettingsLabel));
        RaisePropertyChanged(nameof(OllamaStatus));
        RaisePropertyChanged(nameof(DuoLabel));
        RaisePropertyChanged(nameof(ChatTypeLabel));
        RaisePropertyChanged(nameof(IsProjectOpen));
        RaisePropertyChanged(nameof(ShowNoProjectChats));
        RaisePropertyChanged(nameof(SupportsConversationSidebar));
        RaisePropertyChanged(nameof(SupportsConversationCommands));
        RaisePropertyChanged(nameof(SupportsEditingCommands));
        RaisePropertyChanged(nameof(IsSidebarVisible));
        RaisePropertyChanged(nameof(HasFullSidebar));
        RaisePropertyChanged(nameof(HasCompactSidebar));
        RaisePropertyChanged(nameof(IsBrowseMode));
        RaisePropertyChanged(nameof(IsTrainingMode));
        ApplyShellVisualState();
    }

    private ChatPage CreateChat(HavenMode mode) => new(_bus, mode, _conversations, _containers, _catalog, _ollama, _sessions,
        _preferences, _preflight, _workspaceState, _projectIntelligence, _containerResources);

    public void ShowOverlay(UserControl overlay)
    {
        _previousContent = PageContent.Content;
        OverlayHost.Child = overlay;
        OverlayHost.IsVisible = true;
        PageContent.IsVisible = false;
    }

    public void HideOverlay()
    {
        OverlayHost.IsVisible = false;
        PageContent.Content = _previousContent;
        PageContent.IsVisible = true;
        OverlayHost.Child = null;
        _previousContent = null;
    }

    public async void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (control && e.Key == Key.Space)
        {
            await ShowUniversalSearchAsync();
            e.Handled = true;
        }
        else if (control && e.Key == Key.K)
        {
            OpenCommandPaletteCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.N)
        {
            NewChatCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.S)
        {
            SaveCurrentCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.R
                 && CurrentPage is BrowserPage browser)
        {
            browser.HardReloadCommand.Execute(null);
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _reminderTimer.Stop();
        StopAutomationScheduler();
        lock (_goSuggestionRefreshes)
        {
            foreach (var cancellation in _goSuggestionRefreshes.Values)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
            _goSuggestionRefreshes.Clear();
        }
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _homePage?.Deactivate();
        _newDashboardPage?.Deactivate();
        _newDashboardPage?.Dispose();
        _newChatPage?.Dispose();
        _studyAssignmentsSidebar?.Dispose();
        _nativeChatSidebar?.Dispose();
        _planPage?.Dispose();
        _companionDockVm.Dispose();
        RemoveSplitView();
        foreach (var tab in OpenTabs.ToArray()) tab.Dispose();
        OpenTabs.Clear();
        TopRail.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
