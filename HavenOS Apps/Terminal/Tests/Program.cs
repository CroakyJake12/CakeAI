/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Terminal/Tests/Program.cs.
 * What: Focused executable specifications for Terminal app host gating, permission approval, persistent sessions, cwd, and restart behavior.
 * Why: The migration must prove it reuses the shared terminal contracts and fails closed without a host execution capability.
 */
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Terminal;

await TerminalAppSurfaceSpecs.RunAsync();
Console.WriteLine("Terminal app surface specs passed.");

internal static class TerminalAppSurfaceSpecs
{
    public static async Task RunAsync()
    {
        await MissingSessionCapabilityFailsClosedAsync();
        await MissingPermissionCapabilityFailsClosedAsync();
        await AskPermissionRequiresApprovalWithoutExecutingAsync();
        await ApprovalUsesTheSamePersistentSessionAsync();
        await WorkingDirectoryAndNewSessionUseHostSessionContractAsync();
        CreationFailureFailsClosedAndRedactsReason();
    }

    private static async Task MissingSessionCapabilityFailsClosedAsync()
    {
        using var surface = new TerminalAppSurface(new(null, () => PermissionMode.FullAccess));
        Check(surface.Availability == TerminalAppAvailability.HostCapabilityUnavailable, "missing session factory must be unavailable");
        var result = await surface.SubmitAsync("echo blocked");
        Check(result.State == TerminalAppCommandState.Unavailable, "missing session factory must not execute");
    }

    private static async Task MissingPermissionCapabilityFailsClosedAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, null));
        var result = await surface.SubmitAsync("echo blocked");
        Check(result.State == TerminalAppCommandState.Unavailable, "missing permission source must not execute");
        Check(factory.CreateCount == 0, "surface must not create a shell when a required host capability is missing");
    }

    private static async Task AskPermissionRequiresApprovalWithoutExecutingAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.Ask));
        var result = await surface.SubmitAsync("echo hello token=super-secret");
        Check(result.State == TerminalAppCommandState.RequiresApproval, "Ask must require one-time approval");
        Check(factory.LastSession!.ExecuteCount == 0, "approval gate must run before shell execution");
        Check(surface.History.Count == 1, "submitted command should enter visible history");
        Check(surface.History[0].Contains("<redacted>", StringComparison.Ordinal), "visible history must redact secrets");
        Check(!surface.History[0].Contains("super-secret", StringComparison.Ordinal), "visible history must not retain raw secrets");
    }

    private static async Task ApprovalUsesTheSamePersistentSessionAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.Ask));
        var first = await surface.SubmitAsync("set-state one");
        Check(first.State == TerminalAppCommandState.RequiresApproval, "first command should await approval");
        var approved = await surface.ApprovePendingAsync();
        Check(approved.State == TerminalAppCommandState.Succeeded, "approved command should execute");

        var second = await surface.SubmitAsync("set-state two");
        Check(second.State == TerminalAppCommandState.RequiresApproval, "second command should independently await approval");
        var approvedSecond = await surface.ApprovePendingAsync();
        Check(approvedSecond.State == TerminalAppCommandState.Succeeded, "second approved command should execute");
        Check(factory.CreateCount == 1, "multiple commands must reuse one persistent host session");
        Check(factory.LastSession!.ExecuteCount == 2, "both commands must execute through that session");
    }

    private static async Task WorkingDirectoryAndNewSessionUseHostSessionContractAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.FullAccess));
        var expected = Path.GetFullPath(Environment.CurrentDirectory);
        Check(await surface.SetWorkingDirectoryAsync(expected), "existing working directory should be accepted");
        Check(surface.WorkingDirectory == expected, "working directory must come from host session metadata");

        var priorId = surface.SessionMetadata!.SessionId;
        Check(surface.NewSession(), "new session should be created through host factory");
        Check(factory.CreateCount == 2, "new-session action must request a second host session");
        Check(surface.SessionMetadata!.SessionId != priorId, "new-session action must replace the prior session");
    }

    private static void CreationFailureFailsClosedAndRedactsReason()
    {
        using var surface = new TerminalAppSurface(new(new ThrowingSessionFactory(), () => PermissionMode.FullAccess));
        Check(surface.Availability == TerminalAppAvailability.HostCapabilityUnavailable, "host creation failure must fail closed");
        Check(surface.UnavailableReason!.Contains("<redacted>", StringComparison.Ordinal), "host failure reason should be redacted");
        Check(!surface.UnavailableReason.Contains("secret-value", StringComparison.Ordinal), "raw secret from host failure must not reach UI state");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Spec failed: " + message);
    }
}

internal sealed class FakeSessionFactory : ITerminalSessionFactory
{
    public int CreateCount { get; private set; }
    public FakeSession? LastSession { get; private set; }

    public ITerminalSession Create(string initialDirectory, string? displayName = null)
    {
        CreateCount++;
        LastSession = new FakeSession(initialDirectory, displayName ?? "Terminal");
        return LastSession;
    }
}

internal sealed class ThrowingSessionFactory : ITerminalSessionFactory
{
    public ITerminalSession Create(string initialDirectory, string? displayName = null) =>
        throw new InvalidOperationException("token=secret-value");
}

internal sealed class FakeSession : ITerminalSession
{
    private TerminalSessionMetadata _metadata;

    public FakeSession(string initialDirectory, string displayName)
    {
        _metadata = new(
            Guid.NewGuid(),
            "fake-shell",
            displayName,
            initialDirectory,
            initialDirectory,
            TerminalSessionLifecycleState.Ready,
            DateTimeOffset.UtcNow,
            0,
            false);
    }

    public int ExecuteCount { get; private set; }
    public TerminalSessionMetadata Metadata => _metadata;
    public int? ProcessId => 1234;
    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;

    public Task<TerminalSessionCommandResult> ExecuteAsync(Guid commandId, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecuteCount++;
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Running };
        MetadataChanged?.Invoke(this, _metadata);
        OutputReceived?.Invoke(this, new(_metadata.SessionId, commandId, TerminalOutputStream.StandardOutput, command, DateTimeOffset.UtcNow));
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Ready };
        MetadataChanged?.Invoke(this, _metadata);
        return Task.FromResult(new TerminalSessionCommandResult(
            _metadata.SessionId,
            commandId,
            0,
            false,
            TimeSpan.FromMilliseconds(1),
            _metadata.CurrentWorkingDirectory,
            true,
            false));
    }

    public Task InterruptAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _metadata = _metadata with { CurrentWorkingDirectory = path };
        MetadataChanged?.Invoke(this, _metadata);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Disposed };
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
