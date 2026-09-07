using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>One provider/model execution contract shared by full Vision and compact Vision.</summary>
public sealed record VisionAnalysisRequest(
    string SourcePath,
    string Prompt,
    string? Model = null);

/// <summary>The real result returned by a vision-capable model.</summary>
public sealed record VisionAnalysisResult(
    string SourcePath,
    string Prompt,
    string Response,
    string Model);

/// <summary>Raised when the configured providers do not expose a vision-capable model.</summary>
public sealed class VisionModelUnavailableException : InvalidOperationException
{
    public VisionModelUnavailableException()
        : base("No compatible vision model is available. The image was not sent to a text-only model.")
    {
    }

    public VisionModelUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Owns the provider/model selection and request construction for Vision.
/// Presentation surfaces must use this service rather than constructing an
/// image request themselves, so full Vision and Overlay cannot drift apart.
/// </summary>
public sealed class VisionAnalysisService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private const string DefaultSystemPrompt =
        "Act as Haven Vision. Analyse only the supplied image, distinguish observation from inference, transcribe visible text accurately, and state uncertainty.";

    private readonly IProviderModelClient _models;

    public VisionAnalysisService(IProviderModelClient models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    /// <summary>Returns the first model explicitly advertising Vision support.</summary>
    public async Task<ModelDescriptor?> GetVisionModelAsync(CancellationToken cancellationToken = default)
    {
        var available = await _models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        return available.FirstOrDefault(item => item.Supports(ToolCapability.Vision));
    }

    public async Task<VisionAnalysisResult> AnalyzeAsync(
        VisionAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = new CancellationTokenSource(DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var normalizedRequest = NormalizeRequest(request);
            var available = await _models.GetModelsAsync(linked.Token).ConfigureAwait(false);
            var modelName = normalizedRequest.Model?.Trim();
            var model = string.IsNullOrWhiteSpace(modelName)
                ? available.FirstOrDefault(item => item.Supports(ToolCapability.Vision))
                : available.FirstOrDefault(item =>
                    string.Equals(item.Name, modelName, StringComparison.OrdinalIgnoreCase)
                    && item.Supports(ToolCapability.Vision));
            if (model is null)
            {
                if (!string.IsNullOrWhiteSpace(modelName))
                    throw new VisionModelUnavailableException($"The selected model '{modelName}' does not advertise Vision support. The image was not sent to a text-only model.");
                throw new VisionModelUnavailableException();
            }

            return await AnalyzeResolvedAsync(normalizedRequest, model, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Vision analysis timed out after 60 seconds.");
        }
    }

    /// <summary>
    /// Runs a model already selected by this service. Full Vision uses this
    /// after computing its cache key, avoiding a second model enumeration.
    /// </summary>
    internal async Task<VisionAnalysisResult> AnalyzeResolvedAsync(
        VisionAnalysisRequest request,
        ModelDescriptor model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(model);
        if (!model.Supports(ToolCapability.Vision))
            throw new VisionModelUnavailableException($"The selected model '{model.Name}' does not advertise Vision support. The image was not sent to a text-only model.");

        var normalizedRequest = NormalizeRequest(request with { Model = model.Name });
        using var timeout = new CancellationTokenSource(DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var providerRequest = new OllamaChatRequest(
                model.Name,
                [new OllamaMessage("user", normalizedRequest.Prompt, [normalizedRequest.SourcePath])],
                EffortLevel.Medium,
                DefaultSystemPrompt);
            var response = await _models.CompleteAsync(providerRequest, linked.Token).ConfigureAwait(false);
            return new VisionAnalysisResult(normalizedRequest.SourcePath, normalizedRequest.Prompt, response, model.Name);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Vision analysis timed out after 60 seconds.");
        }
    }

    private static VisionAnalysisRequest NormalizeRequest(VisionAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath))
            throw new FileNotFoundException("The Vision source image is unavailable.", request.SourcePath);

        var sourcePath = Path.GetFullPath(request.SourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The Vision source image is unavailable.", request.SourcePath);

        var prompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? "Describe this image carefully, including important objects, visible text, layout and uncertainty."
            : request.Prompt.Trim();
        return request with { SourcePath = sourcePath, Prompt = prompt };
    }
}
