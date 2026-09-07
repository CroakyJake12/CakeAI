#if !ANDROID
using System.Reflection;
using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class OverlayCompactSurfacesTests
{
    [Fact]
    public void Route_projection_shows_only_the_selected_surface_and_home_uses_go()
    {
        using var surfaces = new OverlayCompactSurfaces();

        surfaces.ApplyRoute(OverlayCompactAppRoute.Home);
        Assert.Equal(HavenVisibility.Visible, surfaces.GoPanel.GetValue(HavenProperties.Visibility));
        Assert.All(new[] { surfaces.ChatPanel, surfaces.TranslatePanel, surfaces.VisionPanel, surfaces.CalculatorPanel, surfaces.TasksPanel },
            panel => Assert.Equal(HavenVisibility.Collapsed, panel.GetValue(HavenProperties.Visibility)));

        surfaces.ApplyRoute(new OverlayCompactAppRoute("vision", "Vision", "Vision"));
        Assert.Equal(HavenVisibility.Visible, surfaces.VisionPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, surfaces.GoPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, surfaces.ChatPanel.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Go_shortcuts_are_dynamic_and_raise_the_registered_key()
    {
        using var surfaces = new OverlayCompactSurfaces();
        string? opened = null;
        surfaces.GoShortcutRequested += (_, key) => opened = key;

        surfaces.SetGoShortcuts([new OverlayCompactShortcut("translate", "Translate"), new OverlayCompactShortcut("vision", "Vision")]);

        var buttons = surfaces.GoShortcuts.Children.OfType<Button>().ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.Equal("Translate", buttons[0].Content);
        Invoke(buttons[1]);
        Assert.Equal("vision", opened);
    }

    [Fact]
    public void Chat_projection_renders_real_messages_and_submit_stop_events()
    {
        using var surfaces = new OverlayCompactSurfaces();
        string? submitted = null;
        var stopped = false;
        surfaces.ChatSubmitRequested += (_, text) => submitted = text;
        surfaces.ChatStopRequested += (_, _) => stopped = true;
        var state = new ChatProjectionState(
            Guid.NewGuid(), "Build check", "Chat", "qwen3", "Haven", true, "Running tests…",
            [new ChatProjectionMessage(Guid.NewGuid(), MessageRole.Assistant, "Actual result", "Haven", "qwen3", false, [], DateTimeOffset.UtcNow)],
            DateTimeOffset.UtcNow);

        surfaces.ApplyChat(state);
        Assert.Same(state, surfaces.ChatState);
        Assert.Contains(surfaces.ChatMessages.Children, child => child.DescendantsAndSelf().OfType<Text>().Any(text => text.Content == "Actual result"));
        Assert.Equal(HavenVisibility.Visible, surfaces.ChatStopButton.GetValue(HavenProperties.Visibility));
        Assert.False(surfaces.ChatSendButton.GetValue(HavenProperties.Enabled));

        surfaces.ChatInput.Text = "  inspect the result  ";
        surfaces.ApplyChat(state with { IsSending = false, StatusText = null });
        Invoke(surfaces.ChatSendButton);
        Assert.Equal("inspect the result", submitted);
        Invoke(surfaces.ChatStopButton);
        Assert.True(stopped);
    }

    [Fact]
    public void Translate_vision_and_calculator_projection_preserve_state_and_raise_typed_requests()
    {
        using var surfaces = new OverlayCompactSurfaces();
        TranslateRequest? translation = null;
        VisionAnalysisRequest? vision = null;
        string? expression = null;
        surfaces.TranslateRequested += (_, request) => translation = request;
        surfaces.VisionRequested += (_, request) => vision = request;
        surfaces.CalculatorEvaluateRequested += (_, value) => expression = value;

        surfaces.TranslateText.Text = "hello";
        Invoke(surfaces.TranslateRunButton);
        Assert.NotNull(translation);
        Assert.Equal("hello", translation!.Text);

        surfaces.VisionSourcePath.Text = "C:\\image.png";
        surfaces.VisionQuestion.Text = "What is visible?";
        Invoke(surfaces.VisionAnalyseButton);
        Assert.Equal("C:\\image.png", vision!.SourcePath);
        Assert.Equal("What is visible?", vision.Prompt);

        surfaces.CalculatorExpression.Text = "2 + 2";
        Invoke(surfaces.CalculatorEvaluateButton);
        Assert.Equal("2 + 2", expression);

        var translationState = OverlayTranslateState.Empty with
        {
            Status = OverlayTranslateStatus.Completed,
            Result = new TranslateResult("hola", "English", "en", [], "qwen3")
        };
        surfaces.ApplyTranslation(translationState);
        Assert.Equal("hola", surfaces.TranslateResult.Content);

        var visionState = OverlayVisionState.Empty with
        {
            Status = OverlayVisionStatus.Completed,
            SourcePath = "C:\\image.png",
            Prompt = "What is visible?",
            Response = "A landscape",
            Model = "vision-qwen"
        };
        surfaces.ApplyVision(visionState);
        Assert.Equal("A landscape", surfaces.VisionResult.Content);

        var calculatorState = OverlayCalculatorState.Empty with
        {
            Status = OverlayCalculatorStatus.Completed,
            Expression = "2 + 2",
            FormattedResult = "4"
        };
        surfaces.ApplyCalculator(calculatorState);
        Assert.Equal("4", surfaces.CalculatorResult.Content);
    }

    private static void Invoke(HavenElement element)
    {
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(element, null);
    }
}
#endif
