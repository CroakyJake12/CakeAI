using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class OverlayCompactExecutionTests
{
    [Fact]
    public async Task Translate_uses_production_service_and_publishes_running_then_completed_state()
    {
        using var paths = new TemporaryPaths();
        var model = new FakeOllamaClient
        {
            Response = "{\"translatedText\":\"Hola\",\"detectedSourceLanguage\":\"English\",\"detectedSourceLanguageCode\":\"en\",\"ambiguities\":[]}"
        };
        var translator = new TranslateService(model, new UserPreferencesService(paths), null!);
        using var coordinator = new OverlayTranslateCoordinator(translator);
        var states = new List<OverlayTranslateState>();
        coordinator.StateChanged += (_, args) => states.Add(args.State);

        var request = new TranslateRequest("en", "English", "es", "Spanish", "Hello", "Natural", "");
        var result = await coordinator.TranslateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OverlayTranslateStatus.Completed, result.Status);
        Assert.Equal("Hola", result.Result?.TranslatedText);
        Assert.Null(result.Error);
        Assert.Equal([OverlayTranslateStatus.Running, OverlayTranslateStatus.Completed], states.Select(state => state.Status));
        Assert.NotNull(model.LastRequest);
        Assert.Contains(request.Text, model.LastRequest!.Messages.Single().Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_preserves_real_provider_error_without_claiming_success()
    {
        using var paths = new TemporaryPaths();
        var model = new FakeOllamaClient { Available = false };
        var translator = new TranslateService(model, new UserPreferencesService(paths), null!);
        using var coordinator = new OverlayTranslateCoordinator(translator);

        var state = await coordinator.TranslateAsync(new TranslateRequest("auto", "Auto-detect", "fr", "French", "Hello", "Natural", ""), TestContext.Current.CancellationToken);

        Assert.Equal(OverlayTranslateStatus.Failed, state.Status);
        Assert.Equal("The configured local model provider is offline.", state.Error);
        Assert.Null(state.Result);
    }

    [Fact]
    public async Task Translate_stop_cancels_provider_and_publishes_cancelled_state()
    {
        using var paths = new TemporaryPaths();
        var model = new FakeOllamaClient { WaitForCancellation = true };
        var translator = new TranslateService(model, new UserPreferencesService(paths), null!);
        using var coordinator = new OverlayTranslateCoordinator(translator);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            if (args.State.Status == OverlayTranslateStatus.Running) running.TrySetResult();
        };

        var operation = coordinator.TranslateAsync(new TranslateRequest("en", "English", "de", "German", "Hello", "Natural", ""), TestContext.Current.CancellationToken);
        await running.Task;
        Assert.True(coordinator.Stop());

        var state = await operation;
        Assert.Equal(OverlayTranslateStatus.Cancelled, state.Status);
        Assert.Equal("Translation cancelled.", state.Error);
        Assert.False(coordinator.Stop());
    }

    [Fact]
    public void Calculator_uses_deterministic_evaluator_and_bounds_history()
    {
        var calculator = new OverlayCalculatorCoordinator(historyLimit: 2);
        var first = calculator.Evaluate("2 + 3 * 4");
        var second = calculator.Evaluate("sqrt(81)");
        var third = calculator.Evaluate("pi");

        Assert.Equal(OverlayCalculatorStatus.Completed, first.Status);
        Assert.Equal("14", first.FormattedResult);
        Assert.Equal("9", second.FormattedResult);
        Assert.Equal("3.14159265358979", third.FormattedResult);
        Assert.Equal(2, third.History.Length);
        Assert.Equal(["pi", "sqrt(81)"], third.History.Select(item => item.Expression));
    }

    [Fact]
    public void Calculator_invalid_expression_is_a_real_error_and_does_not_add_history()
    {
        var calculator = new OverlayCalculatorCoordinator();
        var state = calculator.Evaluate("10 / 0");

        Assert.Equal(OverlayCalculatorStatus.Failed, state.Status);
        Assert.Equal("Division by zero at position 7.", state.Error);
        Assert.Null(state.FormattedResult);
        Assert.Empty(state.History);
    }

    [Fact]
    public void Calculator_clear_restores_empty_immutable_state_and_notifies()
    {
        var calculator = new OverlayCalculatorCoordinator();
        var states = new List<OverlayCalculatorState>();
        calculator.StateChanged += (_, args) => states.Add(args.State);
        calculator.Evaluate("8 * 8");

        var cleared = calculator.Clear();

        Assert.Equal(OverlayCalculatorStatus.Idle, cleared.Status);
        Assert.Empty(cleared.History);
        Assert.Equal([OverlayCalculatorStatus.Completed, OverlayCalculatorStatus.Idle], states.Select(state => state.Status));
    }

    private sealed class FakeOllamaClient : IOllamaClient
    {
        public bool Available { get; init; } = true;
        public bool WaitForCancellation { get; init; }
        public string Response { get; init; } = "{}";
        public OllamaChatRequest? LastRequest { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(Available);

        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([
                new ModelDescriptor("overlay-test-model", 1, "test", "1B", "Q4", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow)
            ]);

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Response;
        }

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public TemporaryPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-overlay-compact-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
