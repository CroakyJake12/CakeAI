using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class VisionAnalysisServiceTests
{
    [Fact]
    public async Task Shared_service_selects_only_vision_capable_model_and_builds_real_image_request()
    {
        using var source = TemporaryImage.Create();
        var client = new FakeProviderModelClient
        {
            Models =
            [
                new ModelDescriptor("text-only", 1, "test", "1B", "Q4", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow),
                new ModelDescriptor("vision-model", 1, "test", "7B", "Q4", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Vision }, DateTimeOffset.UtcNow)
            ],
            Response = "A real image result."
        };
        var service = new VisionAnalysisService(client);

        var model = await service.GetVisionModelAsync(TestContext.Current.CancellationToken);
        var result = await service.AnalyzeAsync(
            new VisionAnalysisRequest(source.Path, "Describe the image."),
            TestContext.Current.CancellationToken);

        Assert.Equal("vision-model", model?.Name);
        Assert.Equal("vision-model", result.Model);
        Assert.Equal("A real image result.", result.Response);
        Assert.Equal(source.Path, client.LastRequest?.Messages.Single().Images?.Single());
        Assert.DoesNotContain("text-only", client.LastRequest?.Model ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("Describe the image.", client.LastRequest?.Messages.Single().Content);
    }

    [Fact]
    public async Task Shared_service_rejects_profiles_without_a_vision_model()
    {
        using var source = TemporaryImage.Create();
        var client = new FakeProviderModelClient
        {
            Models = [new ModelDescriptor("text-only", 1, "test", "1B", "Q4", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow)]
        };
        var service = new VisionAnalysisService(client);

        var exception = await Assert.ThrowsAsync<VisionModelUnavailableException>(() =>
            service.AnalyzeAsync(new VisionAnalysisRequest(source.Path, "Inspect it."), TestContext.Current.CancellationToken));

        Assert.Contains("not sent to a text-only model", exception.Message, StringComparison.Ordinal);
        Assert.Null(client.LastRequest);
    }

    [Fact]
    public async Task Shared_service_rejects_an_explicit_text_only_model_without_calling_provider()
    {
        using var source = TemporaryImage.Create();
        var client = new FakeProviderModelClient
        {
            Models =
            [
                new ModelDescriptor("text-only", 1, "test", "1B", "Q4", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow),
                new ModelDescriptor("vision-model", 1, "test", "7B", "Q4", new HashSet<ToolCapability> { ToolCapability.Vision }, DateTimeOffset.UtcNow)
            ]
        };
        var service = new VisionAnalysisService(client);

        var exception = await Assert.ThrowsAsync<VisionModelUnavailableException>(() =>
            service.AnalyzeAsync(new VisionAnalysisRequest(source.Path, "Inspect it.", "text-only"), TestContext.Current.CancellationToken));

        Assert.Contains("does not advertise Vision support", exception.Message, StringComparison.Ordinal);
        Assert.Null(client.LastRequest);
    }

    [Fact]
    public async Task Overlay_coordinator_publishes_real_result_and_stop_cancels_same_request()
    {
        using var source = TemporaryImage.Create();
        var client = new FakeProviderModelClient
        {
            Models = [new ModelDescriptor("vision-model", 1, "test", "7B", "Q4", new HashSet<ToolCapability> { ToolCapability.Vision }, DateTimeOffset.UtcNow)],
            WaitForCancellation = true
        };
        using var coordinator = new OverlayVisionCoordinator(new VisionAnalysisService(client));
        var states = new List<OverlayVisionState>();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            states.Add(args.State);
            if (args.State.Status == OverlayVisionStatus.Running) running.TrySetResult();
        };

        var operation = coordinator.AnalyzeAsync(source.Path, "Read the image.", TestContext.Current.CancellationToken);
        await running.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(coordinator.Stop());

        var result = await operation;
        Assert.Equal(OverlayVisionStatus.Cancelled, result.Status);
        Assert.Equal(source.Path, result.SourcePath);
        Assert.Equal("Read the image.", result.Prompt);
        Assert.Equal("Vision analysis cancelled.", result.Error);
        Assert.Equal([OverlayVisionStatus.Running, OverlayVisionStatus.Cancelled], states.Select(item => item.Status));
        Assert.False(coordinator.Stop());
    }

    private sealed class FakeProviderModelClient : IProviderModelClient
    {
        public IReadOnlyList<ModelDescriptor> Models { get; init; } = [];
        public string Response { get; init; } = "result";
        public bool WaitForCancellation { get; init; }
        public OllamaChatRequest? LastRequest { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Models);

        public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Response;
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return Response;
        }

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
    }

    private sealed class TemporaryImage : IDisposable
    {
        private TemporaryImage(string path) => Path = path;
        public string Path { get; }

        public static TemporaryImage Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "haven-vision-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, [137, 80, 78, 71]);
            return new TemporaryImage(path);
        }

        public void Dispose()
        {
            try { File.Delete(Path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
