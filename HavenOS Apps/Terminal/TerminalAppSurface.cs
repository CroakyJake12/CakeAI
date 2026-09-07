/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Terminal/TerminalAppSurface.cs.
 * What: Standalone HavenOS Terminal app boundary over the existing terminal-session contracts.
 * Why: A Terminal app must preserve Haven's persistent session and permission behavior without owning or bypassing the host shell implementation.
 */
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Terminal;

public enum TerminalAppAvailability
{
    Available,
    HostCapabilityUnavailable,
    Disposed
}

public enum TerminalAppCommandState
{
    Succeeded,
    Failed,
    Cancelled,
    RequiresApproval,
    Denied,
    Unavailable
}

public sealed record TerminalAppHostCapabilities(
    ITerminalSessionFactory? SessionFactory,
    Func<PermissionMode>? CommandPermission,
    TerminalCommandActivityHub? ActivityHub = null);

public sealed record TerminalAppCommandResult(
    TerminalAppCommandState State,
    string Command,
    string Message,
    TerminalSessionCommandResult? SessionResult = null);

/// <summary>
/// Owns one live Terminal app session while delegating all shell execution to the host-provided
/// <see cref="ITerminalSessionFactory"/>. If required host capabilities are absent, the surface
/// remains visible but command execution is unavailable; it never falls back to direct process launch.
/// </summary>
public sealed class TerminalAppSurface : IDisposable
{
    private const string MissingCapabilityMessage = "Terminal is unavailable because the host terminal-session capability is not available.";
    private readonly TerminalAppHostCapabilities _host;
    private readonly string _initialDirectory;
    private readonly List<string> _history = [];
    private ITerminalSession? _session;
    private string? _pendingCommand;
    private bool _disposed;

    public TerminalAppSurface(TerminalAppHostCapabilities host, string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _initialDirectory = ResolveStartDirectory(initialDirectory);

        if (host.SessionFactory is null || host.CommandPermission is null)
        {
            Availability = TerminalAppAvailability.HostCapabilityUnavailable;
            UnavailableReason = MissingCapabilityMessage;
            return;
        }

        if (host.ActivityHub is not null)
            host.ActivityHub.ActivityPublished += OnActivityPublished;

        TryCreateInitialSession();
    }

    public TerminalAppAvailability Availability { get; private set; }
    public string? UnavailableReason { get; private set; }
    public bool IsAvailable => Availability == TerminalAppAvailability.Available && _session is not null;
    public bool HasPendingApproval => _pendingCommand is not null;
    public IReadOnlyList<string> History => _history;
    public TerminalSessionMetadata? SessionMetadata => _session?.Metadata;
    public string WorkingDirectory => _session?.Metadata.CurrentWorkingDirectory ?? _initialDirectory;

    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;
    public event EventHandler<TerminalCommandActivity>? AgentActivityObserved;
    public event EventHandler? TranscriptClearRequested;

    public async Task<TerminalAppCommandResult> SubmitAsync(string command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var value = (command ?? string.Empty).Trim();
        if (value.Length == 0)
            return new(TerminalAppCommandState.Failed, string.Empty, "Enter a command to run.");

        var safeCommand = SensitiveTextRedactor.Redact(value, 8_000);
        _history.Add(safeCommand);

        if (IsTranscriptClearCommand(value))
        {
            _pendingCommand = null;
            TranscriptClearRequested?.Invoke(this, EventArgs.Empty);
            return new(TerminalAppCommandState.Succeeded, safeCommand, "Transcript clear requested.");
        }

        if (!TryGetExecutionHost(out var session, out var permission))
            return Unavailable(safeCommand);

        var policy = TerminalCommandPolicy.Evaluate(permission());
        if (policy.Decision == TerminalPermissionDecision.Denied)
        {
            _pendingCommand = null;
            return new(TerminalAppCommandState.Denied, safeCommand, policy.Reason);
        }

        if (policy.Decision == TerminalPermissionDecision.RequiresApproval)
        {
            _pendingCommand = value;
            return new(TerminalAppCommandState.RequiresApproval, safeCommand, policy.Reason);
        }

        _pendingCommand = null;
        return await ExecuteCoreAsync(session, value, safeCommand, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TerminalAppCommandResult> ApprovePendingAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_pendingCommand is null)
            return new(TerminalAppCommandState.Failed, string.Empty, "There is no command awaiting approval.");

        var command = _pendingCommand;
        var safeCommand = SensitiveTextRedactor.Redact(command, 8_000);
        _pendingCommand = null;

        if (!TryGetExecutionHost(out var session, out var permission))
            return Unavailable(safeCommand);

        var policy = TerminalCommandPolicy.Evaluate(permission(), approvedOnce: true);
        if (policy.Decision != TerminalPermissionDecision.Allowed)
            return new(TerminalAppCommandState.Denied, safeCommand, policy.Reason);

        return await ExecuteCoreAsync(session, command, safeCommand, cancellationToken).ConfigureAwait(false);
    }

    public TerminalAppCommandResult DenyPending()
    {
        ThrowIfDisposed();
        if (_pendingCommand is null)
            return new(TerminalAppCommandState.Failed, string.Empty, "There is no command awaiting approval.");

        var safeCommand = SensitiveTextRedactor.Redact(_pendingCommand, 8_000);
        _pendingCommand = null;
        return new(TerminalAppCommandState.Denied, safeCommand, "Command denied by user.");
    }

    public async Task<bool> SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAvailable || _session is null || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            await _session.SetWorkingDirectoryAsync(Path.GetFullPath(path), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public bool NewSession()
    {
        ThrowIfDisposed();
        if (_host.SessionFactory is null || _host.CommandPermission is null)
        {
            SetUnavailable(MissingCapabilityMessage);
            return false;
        }

        ITerminalSession replacement;
        try
        {
            replacement = _host.SessionFactory.Create(_initialDirectory, "Terminal");
        }
        catch (Exception ex)
        {
            SetUnavailable("Terminal host could not create a shell session: " + SensitiveTextRedactor.Redact(ex.Message, 2_000));
            return false;
        }

        var previous = _session;
        Detach(previous);
        _session = replacement;
        Attach(replacement);
        previous?.Dispose();
        _pendingCommand = null;
        _history.Clear();
        Availability = TerminalAppAvailability.Available;
        UnavailableReason = null;
        return true;
    }

    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAvailable || _session is null)
            return;

        await _session.InterruptAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TerminalAppCommandResult> ExecuteCoreAsync(
        ITerminalSession session,
        string command,
        string safeCommand,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await session.ExecuteAsync(Guid.NewGuid(), command, cancellationToken).ConfigureAwait(false);
            var state = result.Cancelled
                ? TerminalAppCommandState.Cancelled
                : result.ExitCode == 0
                    ? TerminalAppCommandState.Succeeded
                    : TerminalAppCommandState.Failed;
            var exit = result.ExitCode?.ToString() ?? "unknown";
            var message = result.Cancelled
                ? "Command interrupted. The host session may have restarted its shell process."
                : $"Command completed with exit code {exit}.";
            return new(state, safeCommand, message, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(TerminalAppCommandState.Cancelled, safeCommand, "Command interrupted.");
        }
        catch (Exception ex)
        {
            return new(TerminalAppCommandState.Failed, safeCommand, SensitiveTextRedactor.Redact(ex.Message, 2_000));
        }
    }

    private bool TryGetExecutionHost(out ITerminalSession session, out Func<PermissionMode> permission)
    {
        if (IsAvailable && _session is not null && _host.CommandPermission is not null)
        {
            session = _session;
            permission = _host.CommandPermission;
            return true;
        }

        session = null!;
        permission = null!;
        return false;
    }

    private TerminalAppCommandResult Unavailable(string safeCommand) =>
        new(TerminalAppCommandState.Unavailable, safeCommand, UnavailableReason ?? MissingCapabilityMessage);

    private void TryCreateInitialSession()
    {
        try
        {
            _session = _host.SessionFactory!.Create(_initialDirectory, "Terminal");
            Attach(_session);
            Availability = TerminalAppAvailability.Available;
            UnavailableReason = null;
        }
        catch (Exception ex)
        {
            SetUnavailable("Terminal host could not create a shell session: " + SensitiveTextRedactor.Redact(ex.Message, 2_000));
        }
    }

    private void SetUnavailable(string reason)
    {
        Detach(_session);
        _session?.Dispose();
        _session = null;
        _pendingCommand = null;
        Availability = TerminalAppAvailability.HostCapabilityUnavailable;
        UnavailableReason = reason;
    }

    private void Attach(ITerminalSession session)
    {
        session.OutputReceived += OnOutputReceived;
        session.MetadataChanged += OnMetadataChanged;
    }

    private void Detach(ITerminalSession? session)
    {
        if (session is null)
            return;
        session.OutputReceived -= OnOutputReceived;
        session.MetadataChanged -= OnMetadataChanged;
    }

    private void OnOutputReceived(object? sender, TerminalSessionOutput output) => OutputReceived?.Invoke(this, output);
    private void OnMetadataChanged(object? sender, TerminalSessionMetadata metadata) => MetadataChanged?.Invoke(this, metadata);
    private void OnActivityPublished(object? sender, TerminalCommandActivity activity)
    {
        if (activity.Origin == TerminalCommandOrigin.Agent)
            AgentActivityObserved?.Invoke(this, activity);
    }

    private static bool IsTranscriptClearCommand(string command) =>
        command.Equals("clear", StringComparison.OrdinalIgnoreCase) || command.Equals("cls", StringComparison.OrdinalIgnoreCase);

    private static string ResolveStartDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return Path.GetFullPath(path);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : Environment.CurrentDirectory;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_host.ActivityHub is not null)
            _host.ActivityHub.ActivityPublished -= OnActivityPublished;
        Detach(_session);
        _session?.Dispose();
        _session = null;
        _pendingCommand = null;
        Availability = TerminalAppAvailability.Disposed;
    }
}
