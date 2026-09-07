/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Dev/Tests/DeveloperAppSurfaceTests.cs, focused regression coverage for the Dev surface.
 * What: Verifies read-only delegation, workspace-boundary validation, symbol search, handoff metadata, and the Visual Studio reference boundary.
 * Why: Protects the first Dev journey from becoming a second mutation/execution path or importing editor-extension dependencies.
 */

using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperAppSurfaceTests
{
    [Fact]
    public async Task InspectAsync_UsesOnlyReadOnlyCodeIntelligenceOperations()
    {
        var codeIntelligence = new RecordingCodeIntelligenceService();
        var surface = new DeveloperAppSurface(codeIntelligence);
        var target = CreateTarget("src/Haven.Core/Sample.cs");

        DeveloperInspectionSnapshot snapshot = await surface.InspectAsync(target);

        Assert.Same(target, snapshot.Target);
        Assert.Equal(1, codeIntelligence.StatusCalls);
        Assert.Equal(1, codeIntelligence.DiagnosticsCalls);
        Assert.Equal(0, codeIntelligence.PreviewFormatCalls);
        Assert.Equal(0, codeIntelligence.ApplyFormatCalls);
    }

    [Fact]
    public void WorkspaceTarget_RejectsRelativeWorkspaceRoot()
    {
        Assert.Throws<ArgumentException>(() => new DeveloperWorkspaceTarget("relative-workspace", "src/Sample.cs"));
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("src/../outside.cs")]
    [InlineData("src/./Sample.cs")]
    public void WorkspaceTarget_RejectsTraversalSegments(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => CreateTarget(relativePath));
    }

    [Fact]
    public async Task SearchSymbolsAsync_DelegatesToExistingCodeIntelligenceService()
    {
        var codeIntelligence = new RecordingCodeIntelligenceService();
        var surface = new DeveloperAppSurface(codeIntelligence);
        var target = CreateTarget("src/Haven.Core/Sample.cs");

        await surface.SearchSymbolsAsync(target, "Sample");

        Assert.Equal(1, codeIntelligence.SymbolSearchCalls);
        Assert.Equal(target.WorkspaceRoot, codeIntelligence.LastWorkspaceRoot);
        Assert.Equal("Sample", codeIntelligence.LastQuery);
    }

    [Fact]
    public void Descriptor_HandsExecutionToExistingStudioAndTerminalModes()
    {
        Assert.Equal(["studio", "terminal"], DeveloperAppDescriptor.ExistingExecutionHandoffs);
    }

    [Fact]
    public void DevAssembly_DoesNotReferenceVisualStudioSdkAssemblies()
    {
        string?[] references = typeof(DeveloperAppSurface).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain(references, static name =>
            name?.Contains("VisualStudio", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static DeveloperWorkspaceTarget CreateTarget(string relativePath)
    {
        string workspaceRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "haven-dev-surface-tests"));
        return new DeveloperWorkspaceTarget(workspaceRoot, relativePath);
    }

    private sealed class RecordingCodeIntelligenceService : ICodeIntelligenceService
    {
        public int StatusCalls { get; private set; }
        public int DiagnosticsCalls { get; private set; }
        public int SymbolSearchCalls { get; private set; }
        public int PreviewFormatCalls { get; private set; }
        public int ApplyFormatCalls { get; private set; }
        public string? LastWorkspaceRoot { get; private set; }
        public string? LastQuery { get; private set; }

        public Task<CodeIntelligenceStatus> GetStatusAsync(
            string workspaceRoot,
            string relativePath,
            CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult<CodeIntelligenceStatus>(default!);
        }

        public Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(
            string workspaceRoot,
            string relativePath,
            CancellationToken cancellationToken)
        {
            DiagnosticsCalls++;
            return Task.FromResult<IReadOnlyList<CodeDiagnostic>>(Array.Empty<CodeDiagnostic>());
        }

        public Task<IReadOnlyList<CodeSymbol>> SearchSymbolsAsync(
            string workspaceRoot,
            string query,
            CancellationToken cancellationToken)
        {
            SymbolSearchCalls++;
            LastWorkspaceRoot = workspaceRoot;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<CodeSymbol>>(Array.Empty<CodeSymbol>());
        }

        public Task<CodeFormatPreview> PreviewFormatAsync(
            string workspaceRoot,
            string relativePath,
            int tabSize,
            bool insertSpaces,
            CancellationToken cancellationToken)
        {
            PreviewFormatCalls++;
            return Task.FromResult<CodeFormatPreview>(default!);
        }

        public Task<CodeFormatApplyResult> ApplyFormatAsync(
            string workspaceRoot,
            CodeFormatPreview preview,
            CancellationToken cancellationToken)
        {
            ApplyFormatCalls++;
            return Task.FromResult<CodeFormatApplyResult>(default!);
        }
    }
}
