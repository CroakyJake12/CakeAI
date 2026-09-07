#if !ANDROID
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using Haven.UI.Components;
using HavenElement = Haven.UI.HavenElement;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Overlay;

/// <summary>
/// HUI-only presentation surfaces for the compact Overlay routes.
///
/// This type owns no provider, Chat, Vision, translation, or calculator
/// execution. It projects the state owned by those production services and
/// raises typed requests for the existing controller/coordinators to handle.
/// Inactive routes are collapsed so the shell never renders a dead surface.
/// </summary>
internal sealed class OverlayCompactSurfaces : IDisposable
{
    private readonly Dictionary<string, Container> _routePanels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HavenButton> _goShortcutButtons = [];
    private bool _disposed;

    public OverlayCompactSurfaces()
    {
        Root = new Container { Name = "Overlay.CompactSurfaces", Layout = HavenLayout.Vertical };
        Set(Root, HavenProperties.Width, HavenLength.Percent(100));
        Set(Root, HavenProperties.Gap, HavenLength.Px(8));
        Set(Root, HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Accessibility.AccessibleName = "Compact Haven app surface";

        GoPanel = Panel("Overlay.Compact.Go", "Go");
        GoStatus = Text("Overlay.Compact.Go.Status", string.Empty, TextLevel.Caption);
        GoShortcuts = new Container { Name = "Overlay.Compact.Go.Shortcuts", Layout = HavenLayout.Wrap };
        Set(GoShortcuts, HavenProperties.Width, HavenLength.Percent(100));
        Set(GoShortcuts, HavenProperties.Gap, HavenLength.Px(6));
        GoShortcuts.Accessibility.AccessibleName = "Installed Haven app shortcuts";
        GoPanel.Add(GoShortcuts);
        GoPanel.Add(GoStatus);

        ChatPanel = Panel("Overlay.Compact.Chat", "Chat");
        ChatHeader = Text("Overlay.Compact.Chat.Header", "New chat", TextLevel.Caption);
        ChatMessages = new Container { Name = "Overlay.Compact.Chat.Messages", Layout = HavenLayout.Vertical };
        Set(ChatMessages, HavenProperties.Width, HavenLength.Percent(100));
        Set(ChatMessages, HavenProperties.Gap, HavenLength.Px(6));
        Set(ChatMessages, HavenProperties.Overflow, HavenOverflow.Scroll);
        ChatMessages.Accessibility.AccessibleName = "Chat messages";
        ChatStatus = Text("Overlay.Compact.Chat.Status", string.Empty, TextLevel.Caption);
        ChatInput = new Input { Name = "Overlay.Compact.Chat.Input", Placeholder = "Ask Haven anything", SubmitOnEnter = true };
        Set(ChatInput, HavenProperties.Width, HavenLength.Percent(100));
        ChatInput.Accessibility.AccessibleName = "Chat message";
        ChatSendButton = Action("Overlay.Compact.Chat.Send", "Send", ButtonVariant.Primary);
        ChatStopButton = Action("Overlay.Compact.Chat.Stop", "Stop", ButtonVariant.Danger);
        ChatStopButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        ChatPanel.Add(ChatHeader);
        ChatPanel.Add(ChatMessages);
        ChatPanel.Add(ChatStatus);
        ChatPanel.Add(ChatInput);
        var chatActions = Row("Overlay.Compact.Chat.Actions");
        chatActions.Add(ChatSendButton);
        chatActions.Add(ChatStopButton);
        ChatPanel.Add(chatActions);

        TranslatePanel = Panel("Overlay.Compact.Translate", "Translate");
        TranslateSourceLanguage = Field("Overlay.Compact.Translate.SourceLanguage", "Source language code", "auto");
        TranslateSourceLanguageName = Field("Overlay.Compact.Translate.SourceLanguageName", "Source language name", "Auto-detect");
        TranslateTargetLanguage = Field("Overlay.Compact.Translate.TargetLanguage", "Target language code", "es");
        TranslateTargetLanguageName = Field("Overlay.Compact.Translate.TargetLanguageName", "Target language name", "Spanish");
        TranslateTone = Field("Overlay.Compact.Translate.Tone", "Tone", "Natural");
        TranslateContext = Field("Overlay.Compact.Translate.Context", "Context (optional)", string.Empty);
        TranslateText = new Input { Name = "Overlay.Compact.Translate.Text", Placeholder = "Text to translate", Multiline = true };
        Set(TranslateText, HavenProperties.MinHeight, HavenLength.Px(84));
        Set(TranslateText, HavenProperties.Width, HavenLength.Percent(100));
        TranslateText.Accessibility.AccessibleName = "Text to translate";
        TranslateStatus = Text("Overlay.Compact.Translate.Status", string.Empty, TextLevel.Caption);
        TranslateResult = Text("Overlay.Compact.Translate.Result", string.Empty, TextLevel.Paragraph);
        TranslateRunButton = Action("Overlay.Compact.Translate.Run", "Translate", ButtonVariant.Primary);
        TranslateStopButton = Action("Overlay.Compact.Translate.Stop", "Stop", ButtonVariant.Danger);
        TranslateStopButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AddAll(TranslatePanel, TranslateSourceLanguage, TranslateSourceLanguageName, TranslateTargetLanguage,
            TranslateTargetLanguageName, TranslateTone, TranslateContext, TranslateText, TranslateStatus, TranslateResult);
        var translateActions = Row("Overlay.Compact.Translate.Actions");
        translateActions.Add(TranslateRunButton);
        translateActions.Add(TranslateStopButton);
        TranslatePanel.Add(translateActions);

        VisionPanel = Panel("Overlay.Compact.Vision", "Vision");
        VisionSourcePath = Field("Overlay.Compact.Vision.Source", "Image source path", string.Empty);
        VisionQuestion = new Input { Name = "Overlay.Compact.Vision.Question", Placeholder = "Ask about this image", Multiline = true };
        Set(VisionQuestion, HavenProperties.MinHeight, HavenLength.Px(64));
        Set(VisionQuestion, HavenProperties.Width, HavenLength.Percent(100));
        VisionQuestion.Accessibility.AccessibleName = "Vision question";
        VisionPreview = new VisionPreviewElement { Name = "Overlay.Compact.Vision.Preview" };
        Set(VisionPreview, HavenProperties.Width, HavenLength.Percent(100));
        Set(VisionPreview, HavenProperties.Height, HavenLength.Px(140));
        VisionStatus = Text("Overlay.Compact.Vision.Status", string.Empty, TextLevel.Caption);
        VisionResult = Text("Overlay.Compact.Vision.Result", string.Empty, TextLevel.Paragraph);
        VisionAnalyseButton = Action("Overlay.Compact.Vision.Analyse", "Analyse", ButtonVariant.Primary);
        VisionOcrButton = Action("Overlay.Compact.Vision.Ocr", "Read text", ButtonVariant.Secondary);
        VisionStopButton = Action("Overlay.Compact.Vision.Stop", "Stop", ButtonVariant.Danger);
        VisionStopButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AddAll(VisionPanel, VisionSourcePath, VisionPreview, VisionQuestion, VisionStatus, VisionResult);
        var visionActions = Row("Overlay.Compact.Vision.Actions");
        visionActions.Add(VisionAnalyseButton);
        visionActions.Add(VisionOcrButton);
        visionActions.Add(VisionStopButton);
        VisionPanel.Add(visionActions);

        CalculatorPanel = Panel("Overlay.Compact.Calculator", "Calculator");
        CalculatorExpression = Field("Overlay.Compact.Calculator.Expression", "Expression", string.Empty);
        CalculatorResult = Text("Overlay.Compact.Calculator.Result", string.Empty, TextLevel.H2);
        CalculatorStatus = Text("Overlay.Compact.Calculator.Status", string.Empty, TextLevel.Caption);
        CalculatorEvaluateButton = Action("Overlay.Compact.Calculator.Evaluate", "Evaluate", ButtonVariant.Primary);
        CalculatorClearButton = Action("Overlay.Compact.Calculator.Clear", "Clear", ButtonVariant.Ghost);
        CalculatorHistory = new Container { Name = "Overlay.Compact.Calculator.History", Layout = HavenLayout.Vertical };
        Set(CalculatorHistory, HavenProperties.Width, HavenLength.Percent(100));
        Set(CalculatorHistory, HavenProperties.Gap, HavenLength.Px(4));
        Set(CalculatorHistory, HavenProperties.Overflow, HavenOverflow.Scroll);
        AddAll(CalculatorPanel, CalculatorExpression, CalculatorResult, CalculatorStatus, CalculatorHistory);
        var calculatorActions = Row("Overlay.Compact.Calculator.Actions");
        calculatorActions.Add(CalculatorEvaluateButton);
        calculatorActions.Add(CalculatorClearButton);
        CalculatorPanel.Add(calculatorActions);

        TasksPanel = Panel("Overlay.Compact.Tasks", "Tasks");
        TasksStatus = Text("Overlay.Compact.Tasks.Status", "", TextLevel.Caption);
        TasksPanel.Add(TasksStatus);

        RegisterRoute("go", GoPanel);
        RegisterRoute("chat", ChatPanel);
        RegisterRoute("tasks", TasksPanel);
        RegisterRoute("translate", TranslatePanel);
        RegisterRoute("vision", VisionPanel);
        RegisterRoute("calculator", CalculatorPanel);

        foreach (var panel in _routePanels.Values)
        {
            Set(panel, HavenProperties.Visibility, HavenVisibility.Collapsed);
            Root.Add(panel);
        }

        ChatSendButton.Invoked += (_, _) => SubmitChat();
        ChatStopButton.Invoked += (_, _) => ChatStopRequested?.Invoke(this, EventArgs.Empty);
        TranslateRunButton.Invoked += (_, _) => RequestTranslation();
        TranslateStopButton.Invoked += (_, _) => TranslateStopRequested?.Invoke(this, EventArgs.Empty);
        VisionAnalyseButton.Invoked += (_, _) => RequestVision(false);
        VisionOcrButton.Invoked += (_, _) => RequestVision(true);
        VisionStopButton.Invoked += (_, _) => VisionStopRequested?.Invoke(this, EventArgs.Empty);
        CalculatorEvaluateButton.Invoked += (_, _) => CalculatorEvaluateRequested?.Invoke(this, CalculatorExpression.Text.Trim());
        CalculatorClearButton.Invoked += (_, _) => CalculatorClearRequested?.Invoke(this, EventArgs.Empty);
    }

    public Container Root { get; }
    public Container GoPanel { get; }
    public Container GoShortcuts { get; }
    public Text GoStatus { get; }
    public Container ChatPanel { get; }
    public Text ChatHeader { get; }
    public Container ChatMessages { get; }
    public Text ChatStatus { get; }
    public Input ChatInput { get; }
    public HavenButton ChatSendButton { get; }
    public HavenButton ChatStopButton { get; }
    public Container TranslatePanel { get; }
    public Input TranslateSourceLanguage { get; }
    public Input TranslateSourceLanguageName { get; }
    public Input TranslateTargetLanguage { get; }
    public Input TranslateTargetLanguageName { get; }
    public Input TranslateTone { get; }
    public Input TranslateContext { get; }
    public Input TranslateText { get; }
    public Text TranslateStatus { get; }
    public Text TranslateResult { get; }
    public HavenButton TranslateRunButton { get; }
    public HavenButton TranslateStopButton { get; }
    public Container VisionPanel { get; }
    public Input VisionSourcePath { get; }
    public VisionPreviewElement VisionPreview { get; }
    public Input VisionQuestion { get; }
    public Text VisionStatus { get; }
    public Text VisionResult { get; }
    public HavenButton VisionAnalyseButton { get; }
    public HavenButton VisionOcrButton { get; }
    public HavenButton VisionStopButton { get; }
    public Container CalculatorPanel { get; }
    public Input CalculatorExpression { get; }
    public Text CalculatorResult { get; }
    public Text CalculatorStatus { get; }
    public Container CalculatorHistory { get; }
    public HavenButton CalculatorEvaluateButton { get; }
    public HavenButton CalculatorClearButton { get; }
    public Container TasksPanel { get; }
    public Text TasksStatus { get; }
    public OverlayCompactAppRoute? CurrentRoute { get; private set; }
    public ChatProjectionState ChatState { get; private set; } = ChatProjectionState.Empty;
    public OverlayTranslateState TranslateState { get; private set; } = OverlayTranslateState.Empty;
    public OverlayVisionState VisionState { get; private set; } = OverlayVisionState.Empty;
    public OverlayCalculatorState CalculatorState { get; private set; } = OverlayCalculatorState.Empty;

    public event EventHandler<string>? GoShortcutRequested;
    public event EventHandler<string>? ChatSubmitRequested;
    public event EventHandler? ChatStopRequested;
    public event EventHandler<TranslateRequest>? TranslateRequested;
    public event EventHandler? TranslateStopRequested;
    public event EventHandler<VisionAnalysisRequest>? VisionRequested;
    public event EventHandler? VisionStopRequested;
    public event EventHandler? VisionOcrRequested;
    public event EventHandler<string>? CalculatorEvaluateRequested;
    public event EventHandler? CalculatorClearRequested;

    public void ApplyRoute(OverlayCompactAppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        CurrentRoute = route;
        foreach (var (key, panel) in _routePanels)
        {
            var visible = route.IsHome
                ? key.Equals("go", StringComparison.OrdinalIgnoreCase)
                : key.Equals(route.Key, StringComparison.OrdinalIgnoreCase);
            Set(panel, HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        }
    }

    public void SetGoStatus(string status) => GoStatus.Content = status?.Trim() ?? string.Empty;

    public void SetGoShortcuts(IEnumerable<OverlayCompactShortcut> shortcuts)
    {
        foreach (var button in _goShortcutButtons) GoShortcuts.Remove(button);
        _goShortcutButtons.Clear();
        foreach (var shortcut in shortcuts ?? [])
        {
            if (string.IsNullOrWhiteSpace(shortcut.Key) || string.IsNullOrWhiteSpace(shortcut.Title)) continue;
            var button = Action($"Overlay.Compact.Go.Shortcut.{shortcut.Key.Trim()}", shortcut.Title.Trim(), ButtonVariant.Secondary);
            button.Accessibility.AccessibleName = "Open " + shortcut.Title.Trim();
            var key = shortcut.Key.Trim();
            button.Invoked += (_, _) => GoShortcutRequested?.Invoke(this, key);
            GoShortcuts.Add(button);
            _goShortcutButtons.Add(button);
        }
    }

    public void ApplyChat(ChatProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ChatState = state;
        ChatHeader.Content = string.IsNullOrWhiteSpace(state.SelectedModelName)
            ? state.ConversationTitle
            : $"{state.ConversationTitle} · {state.SelectedModelName}";
        foreach (var child in ChatMessages.Children.ToArray()) ChatMessages.Remove(child);
        foreach (var message in state.Messages)
        {
            var bubble = new Container { Name = $"Overlay.Compact.Chat.Message.{message.Id:N}", Layout = HavenLayout.Vertical };
            Set(bubble, HavenProperties.Width, HavenLength.Percent(100));
            Set(bubble, HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
            Set(bubble, HavenProperties.Background, message.Role == MessageRole.User ? "SurfaceRaised" : "Overlay");
            Set(bubble, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
            var role = Text($"Overlay.Compact.Chat.Message.{message.Id:N}.Role", message.Role.ToString(), TextLevel.Caption);
            var content = Text($"Overlay.Compact.Chat.Message.{message.Id:N}.Content", message.Content, TextLevel.Paragraph);
            bubble.Add(role);
            bubble.Add(content);
            foreach (var activity in message.ToolActivities)
                bubble.Add(Text($"Overlay.Compact.Chat.Tool.{activity.Id:N}", activity.Title + ": " + activity.Detail, TextLevel.Caption));
            ChatMessages.Add(bubble);
        }
        ChatStatus.Content = state.StatusText ?? (state.IsSending ? "Working" : string.Empty);
        Set(ChatStopButton, HavenProperties.Visibility, state.IsSending ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Set(ChatSendButton, HavenProperties.Enabled, !state.IsSending);
    }

    public void ApplyTranslation(OverlayTranslateState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        TranslateState = state;
        TranslateStatus.Content = state.Error ?? state.Status.ToString();
        TranslateResult.Content = state.Result?.TranslatedText ?? string.Empty;
        Set(TranslateStopButton, HavenProperties.Visibility, state.IsRunning ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Set(TranslateRunButton, HavenProperties.Enabled, !state.IsRunning);
    }

    public void ApplyVision(OverlayVisionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        VisionState = state;
        if (!string.IsNullOrWhiteSpace(state.SourcePath)) SetVisionSource(state.SourcePath);
        if (state.Prompt is not null && !string.Equals(VisionQuestion.Text, state.Prompt, StringComparison.Ordinal)) VisionQuestion.Text = state.Prompt;
        VisionStatus.Content = state.Error ?? state.Status.ToString();
        VisionResult.Content = state.Response ?? string.Empty;
        Set(VisionStopButton, HavenProperties.Visibility, state.IsRunning ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Set(VisionAnalyseButton, HavenProperties.Enabled, !state.IsRunning);
        Set(VisionOcrButton, HavenProperties.Enabled, !state.IsRunning);
    }

    public void ApplyCalculator(OverlayCalculatorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        CalculatorState = state;
        CalculatorExpression.Text = state.Expression;
        CalculatorResult.Content = state.FormattedResult ?? string.Empty;
        CalculatorStatus.Content = state.Error ?? state.Status.ToString();
        foreach (var child in CalculatorHistory.Children.ToArray()) CalculatorHistory.Remove(child);
        foreach (var entry in state.History)
            CalculatorHistory.Add(Text($"Overlay.Compact.Calculator.History.{entry.EvaluatedAt.UtcTicks}", $"{entry.Expression} = {entry.FormattedResult}", TextLevel.Caption));
    }

    public void SetVisionSource(string? sourcePath)
    {
        var value = sourcePath?.Trim() ?? string.Empty;
        VisionSourcePath.Text = value;
        VisionPreview.Source = string.IsNullOrWhiteSpace(value) || !File.Exists(value) ? null : value;
    }

    private void SubmitChat()
    {
        var text = ChatInput.Text.Trim();
        if (text.Length == 0 || ChatState.IsSending) return;
        ChatInput.Text = string.Empty;
        ChatSubmitRequested?.Invoke(this, text);
    }

    private void RequestTranslation()
    {
        TranslateRequested?.Invoke(this, new TranslateRequest(
            TranslateSourceLanguage.Text.Trim(), TranslateSourceLanguageName.Text.Trim(),
            TranslateTargetLanguage.Text.Trim(), TranslateTargetLanguageName.Text.Trim(),
            TranslateText.Text, TranslateTone.Text.Trim(), TranslateContext.Text));
    }

    private void RequestVision(bool ocr)
    {
        var path = VisionSourcePath.Text.Trim();
        if (ocr) VisionOcrRequested?.Invoke(this, EventArgs.Empty);
        else VisionRequested?.Invoke(this, new VisionAnalysisRequest(path, VisionQuestion.Text.Trim()));
    }

    private void RegisterRoute(string key, Container panel) => _routePanels.Add(key, panel);

    private static Container Panel(string name, string title)
    {
        var panel = new Container { Name = name, Layout = HavenLayout.Vertical };
        Set(panel, HavenProperties.Width, HavenLength.Percent(100));
        Set(panel, HavenProperties.Gap, HavenLength.Px(6));
        Set(panel, HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
        Set(panel, HavenProperties.Background, "SurfaceRaised");
        Set(panel, HavenProperties.BorderColor, "Border");
        Set(panel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(panel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        panel.Accessibility.AccessibleName = title + " compact app";
        panel.Add(Text(name + ".Title", title, TextLevel.H3));
        return panel;
    }

    private static Container Row(string name) => new() { Name = name, Layout = HavenLayout.Wrap };

    private static Input Field(string name, string placeholder, string value)
    {
        var input = new Input { Name = name, Placeholder = placeholder, Text = value };
        Set(input, HavenProperties.Width, HavenLength.Percent(100));
        input.Accessibility.AccessibleName = placeholder;
        return input;
    }

    private static Text Text(string name, string content, TextLevel level)
        => new(content) { Name = name, Level = level };

    private static HavenButton Action(string name, string content, ButtonVariant variant)
        => new() { Name = name, Content = content, Variant = variant };

    private static void AddAll(Container parent, params HavenElement[] children)
    {
        foreach (var child in children) parent.Add(child);
    }

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value)
        => element.SetValue(property, value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var button in _goShortcutButtons) GoShortcuts.Remove(button);
        _goShortcutButtons.Clear();
    }
}

internal sealed record OverlayCompactShortcut(string Key, string Title);
#endif
