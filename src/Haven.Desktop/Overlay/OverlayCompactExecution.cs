#if !ANDROID
using System.Collections.Immutable;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Overlay;

/// <summary>Describes the lifecycle of one compact translation request.</summary>
public enum OverlayTranslateStatus
{
    Idle,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Immutable projection of compact translation execution. It contains the
/// actual request/result or the production error returned by TranslateService;
/// it does not expose synthetic progress.
/// </summary>
public sealed record OverlayTranslateState(
    OverlayTranslateStatus Status,
    TranslateRequest? Request,
    TranslateResult? Result,
    string? Error,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public static OverlayTranslateState Empty { get; } = new(
        OverlayTranslateStatus.Idle,
        null,
        null,
        null,
        null,
        null);

    public bool IsRunning => Status == OverlayTranslateStatus.Running;
}

public sealed class OverlayTranslateStateChangedEventArgs(OverlayTranslateState state) : EventArgs
{
    public OverlayTranslateState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}

/// <summary>
/// Coordinates a single compact translation request through the production
/// TranslateService. It owns only cancellation and immutable presentation
/// state; model selection, provider I/O, parsing, and error policy remain in
/// TranslateService.
/// </summary>
public sealed class OverlayTranslateCoordinator : IDisposable
{
    private readonly TranslateService _translator;
    private readonly object _gate = new();
    private OverlayTranslateState _state = OverlayTranslateState.Empty;
    private CancellationTokenSource? _activeCancellation;
    private bool _disposed;

    public OverlayTranslateCoordinator(TranslateService translator)
    {
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
    }

    public event EventHandler<OverlayTranslateStateChangedEventArgs>? StateChanged;

    public OverlayTranslateState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public async Task<OverlayTranslateState> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CancellationTokenSource linkedCancellation;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state.IsRunning)
                throw new InvalidOperationException("A translation is already running.");

            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linkedCancellation;
            PublishLocked(new OverlayTranslateState(
                OverlayTranslateStatus.Running,
                request,
                null,
                null,
                DateTimeOffset.UtcNow,
                null));
        }

        try
        {
            var result = await _translator.TranslateAsync(request, linkedCancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                var completed = new OverlayTranslateState(
                    OverlayTranslateStatus.Completed,
                    request,
                    result,
                    null,
                    _state.StartedAt,
                    DateTimeOffset.UtcNow);
                PublishLocked(completed);
                return completed;
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            lock (_gate)
            {
                var cancelled = new OverlayTranslateState(
                    OverlayTranslateStatus.Cancelled,
                    request,
                    null,
                    "Translation cancelled.",
                    _state.StartedAt,
                    DateTimeOffset.UtcNow);
                PublishLocked(cancelled);
                return cancelled;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // TranslateService deliberately normalises provider failures to
            // understandable InvalidOperationException messages. Preserve
            // that message verbatim for the compact UI rather than claiming a
            // result or fabricating a generic progress/failure state.
            lock (_gate)
            {
                var failed = new OverlayTranslateState(
                    OverlayTranslateStatus.Failed,
                    request,
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
                if (ReferenceEquals(_activeCancellation, linkedCancellation))
                    _activeCancellation = null;
            }

            linkedCancellation.Dispose();
        }
    }

    /// <summary>Requests cancellation of the active provider operation.</summary>
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

    private void PublishLocked(OverlayTranslateState state)
    {
        _state = state;
        StateChanged?.Invoke(this, new OverlayTranslateStateChangedEventArgs(state));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OverlayTranslateCoordinator));
    }
}

/// <summary>Describes the lifecycle of a compact deterministic calculation.</summary>
public enum OverlayCalculatorStatus
{
    Idle,
    Completed,
    Failed
}

public sealed record OverlayCalculatorHistoryEntry(
    string Expression,
    string FormattedResult,
    DateTimeOffset EvaluatedAt);

/// <summary>Immutable compact calculator state backed by DeterministicCalculator.</summary>
public sealed record OverlayCalculatorState(
    OverlayCalculatorStatus Status,
    string Expression,
    string? FormattedResult,
    string? Error,
    ImmutableArray<OverlayCalculatorHistoryEntry> History)
{
    public static OverlayCalculatorState Empty { get; } = new(
        OverlayCalculatorStatus.Idle,
        string.Empty,
        null,
        null,
        ImmutableArray<OverlayCalculatorHistoryEntry>.Empty);
}

public sealed class OverlayCalculatorStateChangedEventArgs(OverlayCalculatorState state) : EventArgs
{
    public OverlayCalculatorState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}

/// <summary>
/// Coordinates compact calculator requests through Haven's deterministic
/// evaluator. It never executes scripts or interprets arbitrary commands.
/// </summary>
public sealed class OverlayCalculatorCoordinator
{
    public const int DefaultHistoryLimit = 20;

    private readonly object _gate = new();
    private readonly int _historyLimit;
    private OverlayCalculatorState _state = OverlayCalculatorState.Empty;

    public OverlayCalculatorCoordinator(int historyLimit = DefaultHistoryLimit)
    {
        _historyLimit = Math.Clamp(historyLimit, 1, 100);
    }

    public event EventHandler<OverlayCalculatorStateChangedEventArgs>? StateChanged;

    public OverlayCalculatorState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public OverlayCalculatorState Evaluate(string expression)
    {
        expression = expression?.Trim() ?? string.Empty;
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return PublishLocked(_state with
                {
                    Status = OverlayCalculatorStatus.Failed,
                    Expression = string.Empty,
                    FormattedResult = null,
                    Error = "Enter an expression."
                });

            try
            {
                var formatted = DeterministicCalculator.Format(DeterministicCalculator.Evaluate(expression));
                var history = _state.History.Insert(
                    0,
                    new OverlayCalculatorHistoryEntry(expression, formatted, DateTimeOffset.UtcNow));
                if (history.Length > _historyLimit)
                    history = history.RemoveRange(_historyLimit, history.Length - _historyLimit);

                return PublishLocked(new OverlayCalculatorState(
                    OverlayCalculatorStatus.Completed,
                    expression,
                    formatted,
                    null,
                    history));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return PublishLocked(_state with
                {
                    Status = OverlayCalculatorStatus.Failed,
                    Expression = expression,
                    FormattedResult = null,
                    Error = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message
                });
            }
        }
    }

    public OverlayCalculatorState Clear()
    {
        lock (_gate)
        {
            return PublishLocked(OverlayCalculatorState.Empty);
        }
    }

    private OverlayCalculatorState PublishLocked(OverlayCalculatorState state)
    {
        _state = state;
        StateChanged?.Invoke(this, new OverlayCalculatorStateChangedEventArgs(state));
        return state;
    }
}
#endif
