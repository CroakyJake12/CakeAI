using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    private const int QueuedComposerMessageLimit = 8;
    private readonly Queue<string> _queuedComposerMessages = new();
    private DispatcherTimer? _queuedSendPump;
    private bool _queuedSendingEnabled;

    /// <summary>
    /// Replaces the streaming send-button handler with a queue-aware variant while preserving
    /// the existing empty-composer Stop behavior. The hook is installed once during Chat wiring.
    /// </summary>
    private void EnsureQueuedSendingEnabled()
    {
        if (_queuedSendingEnabled) return;
        _queuedSendingEnabled = true;
        SendButton.Invoked -= OnSendInvoked;
        SendButton.Invoked += OnQueueAwareSendInvoked;
    }

    private void OnQueueAwareSendInvoked(object? sender, EventArgs e)
    {
        if (!_isSending)
        {
            SendRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var draft = Instruction.Text.Trim();
        if (string.IsNullOrWhiteSpace(draft))
        {
            StopRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_queuedComposerMessages.Count >= QueuedComposerMessageLimit)
        {
            SetStatus($"Message queue is full ({QueuedComposerMessageLimit}). Wait for a queued message to send or stop the current response.");
            return;
        }

        _queuedComposerMessages.Enqueue(draft);
        Instruction.Text = string.Empty;
        SetStatus(_queuedComposerMessages.Count == 1
            ? "Message queued. Clear the composer and press Stop to cancel the active response."
            : $"{_queuedComposerMessages.Count} messages queued.");
        StartQueuedSendPump();
    }

    private void StartQueuedSendPump()
    {
        if (_queuedSendPump is not null)
        {
            if (!_queuedSendPump.IsEnabled) _queuedSendPump.Start();
            return;
        }

        _queuedSendPump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _queuedSendPump.Tick += OnQueuedSendPumpTick;
        _queuedSendPump.Start();
    }

    private void OnQueuedSendPumpTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            _queuedSendPump?.Stop();
            return;
        }
        if (_isSending || _safetyLocked) return;
        if (_queuedComposerMessages.Count == 0)
        {
            _queuedSendPump?.Stop();
            return;
        }

        // Never overwrite a draft the user typed after queueing. The queued item waits until
        // the composer is empty, then uses the exact same SendRequested path as a normal send.
        if (!string.IsNullOrWhiteSpace(Instruction.Text)) return;

        var next = _queuedComposerMessages.Dequeue();
        Instruction.Text = next;
        SetStatus(_queuedComposerMessages.Count == 0
            ? "Sending queued message…"
            : $"Sending queued message… {_queuedComposerMessages.Count} still queued.");
        SendRequested?.Invoke(this, EventArgs.Empty);

        if (_queuedComposerMessages.Count == 0) _queuedSendPump?.Stop();
    }
}
