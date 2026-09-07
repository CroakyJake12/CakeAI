/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Dev/DeveloperAppSurface.cs, the bounded functional Dev journey.
 * What: Exposes read-only code-intelligence inspection and symbol search over an already-approved workspace.
 * Why: Reuses Haven's existing developer tooling while keeping file mutation, command execution, permission planning, and editor integration in their current owners.
 */

using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Dev;

/// <summary>
/// Identifies a file inside a workspace that was selected and approved by Haven's existing workspace flow.
/// This value does not grant filesystem access or establish workspace trust.
/// </summary>
public sealed record DeveloperWorkspaceTarget
{
    public DeveloperWorkspaceTarget(string workspaceRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (!Path.IsPathRooted(workspaceRoot))
        {
            throw new ArgumentException("Workspace root must be an absolute path supplied by the existing workspace flow.", nameof(workspaceRoot));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("The inspected file path must remain relative to the approved workspace.", nameof(relativePath));
        }

        string[] segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("The inspected file path cannot contain traversal segments.", nameof(relativePath));
        }

        WorkspaceRoot = workspaceRoot;
        RelativePath = relativePath;
    }

    public string WorkspaceRoot { get; }

    public string RelativePath { get; }
}

/// <summary>
/// Read-only developer information suitable for presentation by a HavenOS Dev host.
/// </summary>
public sealed record DeveloperInspectionSnapshot(
    DeveloperWorkspaceTarget Target,
    CodeIntelligenceStatus Status,
    IReadOnlyList<CodeDiagnostic> Diagnostics);

/// <summary>
/// Coordinates the first bounded Dev app journey using Haven's existing code-intelligence service.
/// </summary>
public sealed class DeveloperAppSurface
{
    private readonly ICodeIntelligenceService _codeIntelligence;

    public DeveloperAppSurface(ICodeIntelligenceService codeIntelligence)
    {
        _codeIntelligence = codeIntelligence ?? throw new ArgumentNullException(nameof(codeIntelligence));
    }

    /// <summary>
    /// Loads language-server status and diagnostics without previewing or applying edits.
    /// </summary>
    public async Task<DeveloperInspectionSnapshot> InspectAsync(
        DeveloperWorkspaceTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        CodeIntelligenceStatus status = await _codeIntelligence.GetStatusAsync(
            target.WorkspaceRoot,
            target.RelativePath,
            cancellationToken);
        IReadOnlyList<CodeDiagnostic> diagnostics = await _codeIntelligence.GetDiagnosticsAsync(
            target.WorkspaceRoot,
            target.RelativePath,
            cancellationToken);

        return new DeveloperInspectionSnapshot(target, status, diagnostics);
    }

    /// <summary>
    /// Searches workspace symbols through the existing code-intelligence implementation.
    /// </summary>
    public Task<IReadOnlyList<CodeSymbol>> SearchSymbolsAsync(
        DeveloperWorkspaceTarget target,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return _codeIntelligence.SearchSymbolsAsync(target.WorkspaceRoot, query, cancellationToken);
    }
}
