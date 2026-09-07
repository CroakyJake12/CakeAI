#if !ANDROID
using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Overlay;

public enum OverlayVisionStatus
{
    Idle,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Real compact Vision execution state; progress is intentionally not fabricated.</summary>
public sealed record OverlayVisionState(
    OverlayVisionStatus Status,
    string? SourcePath,
    string? Prompt,
    string? Response,
    string? Model,
    string? Error,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public static OverlayVisionState Empty { get; } = new(
        OverlayVisionStatus.Idle, null, null, null, null, null, null, null);

    public bool IsRunning => Status == OverlayVisionStatus.Running;
}

public sealed class OverlayVisionStateChangedEventArgs(OverlayVisionState state) : EventArgs
{
    public OverlayVisionState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}

/// <summary>
/// Coordinates one compact Vision request through the same provider execution
/// service used by the full Vision page.
/// </summary>
public sealed class OverlayVisionCoordinator : IDisposable
{
    private readonly VisionAnalysisService _analysis;
    private readonly object _gate = new();
    private OverlayVisionState _state = OverlayVisionState.Empty;
    private CancellationTokenSource? _activeCancellation;
    private bool _disposed;

    public OverlayVisionCoordinator(VisionAnalysisService analysis)
    {
        _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
    }

    public event EventHandler<OverlayVisionStateChangedEventArgs>? StateChanged;

    public OverlayVisionState State
    {
        get { lock (_gate) return _state; }
    }

    public async Task<OverlayVisionState> AnalyzeAsync(
        string sourcePath,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var request = new VisionAnalysisRequest(sourcePath, prompt);
        CancellationTokenSource linked;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state.IsRunning)
                throw new InvalidOperationException("A Vision analysis is already running.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linked;
            PublishLocked(new OverlayVisionState(
                OverlayVisionStatus.Running,
                request.SourcePath,
                request.Prompt,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                null));
        }

        try
        {
            var result = await _analysis.AnalyzeAsync(request, linked.Token).ConfigureAwait(false);
            lock (_gate)
            {
                var completed = new OverlayVisionState(
                    OverlayVisionStatus.Completed,
                    result.SourcePath,
                    result.Prompt,
                    result.Response,
                    result.Model,
                    null,
                    _state.StartedAt,
                    DateTimeOffset.UtcNow);
                PublishLocked(completed);
                return completed;
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            lock (_gate)
            {
                var cancelled = new OverlayVisionState(
                    OverlayVisionStatus.Cancelled,
                    request.SourcePath,
                    request.Prompt,
                    null,
                    null,
                    "Vision analysis cancelled.",
                    _state.StartedAt,
                    DateTimeOffset.UtcNow);
                PublishLocked(cancelled);
                return cancelled;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            lock (_gate)
            {
                var failed = new OverlayVisionState(
                    OverlayVisionStatus.Failed,
                    request.SourcePath,
                    request.Prompt,
                    null,
                    null,
                    string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message,
                    _state.StartedAt,
                    DateTimeOffset.UtcNow);
                PublishLocked(failed);
                return failed;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeCancellation, linked))
                    _activeCancellation = null;
            }
            linked.Dispose();
        }
    }

    public bool Stop()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_state.IsRunning || _activeCancellation is null) return false;
            _activeCancellation.Cancel();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _activeCancellation?.Cancel();
        }
    }

    private void PublishLocked(OverlayVisionState state)
    {
        _state = state;
        StateChanged?.Invoke(this, new OverlayVisionStateChangedEventArgs(state));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OverlayVisionCoordinator));
    }
}
#endif
