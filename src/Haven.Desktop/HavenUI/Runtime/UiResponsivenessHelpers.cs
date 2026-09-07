namespace Haven.Desktop.HavenUI.Runtime;

/// <summary>
/// Trailing-edge debouncer for expensive reactions to rapid text entry.
/// The observed value updates immediately; only the expensive follow-up work is
/// deferred until typing settles, and superseded schedules never fire.
/// Inject a <see cref="TimeProvider"/> in tests to make timing deterministic.
/// </summary>
public sealed class TrailingDebouncer(TimeSpan delay, TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private long _scheduled;
    private bool _disposed;

    /// <summary>Schedules the callback to run once after <paramref name="delay"/> of quiet time.</summary>
    public void Schedule(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        long token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token = ++_scheduled;
        }
        _ = RunAsync(token, callback);
    }

    /// <summary>Drops any pending callback without running it.</summary>
    public void Cancel()
    {
        lock (_gate) _scheduled++;
    }

    private async Task RunAsync(long token, Action callback)
    {
        try
        {
            await Task.Delay(delay, _timeProvider).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        lock (_gate)
        {
            if (_disposed || token != _scheduled) return;
        }
        callback();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _scheduled++;
        }
    }
}

/// <summary>
/// Generation gate so stale async reads can never overwrite newer page state.
/// Take a token before starting I/O and discard results unless the token is
/// still active; navigation or a newer load invalidates every older read.
/// </summary>
public sealed class LatestOperationGate
{
    private int _current;

    /// <summary>Starts a new operation and invalidates all previous tokens.</summary>
    public int Begin() => Interlocked.Increment(ref _current);

    /// <summary>True only while <paramref name="token"/> names the newest operation.</summary>
    public bool IsActive(int token) => token == Volatile.Read(ref _current);

    /// <summary>Invalidates all outstanding tokens without starting a new operation.</summary>
    public void Invalidate() => Interlocked.Increment(ref _current);
}
