# HavenOS Dev app surface

This directory owns the bounded, independent HavenOS **Dev** app surface. Its first functional journey is read-only inspection of an already-approved workspace through Haven's existing `ICodeIntelligenceService`: language-server status, diagnostics, and symbol search.

## Ownership boundary

Dev coordinates existing developer tooling; it does not replace it.

- Workspace selection/trust remains owned by the existing Haven project/workspace flow. `DeveloperWorkspaceTarget` only rejects obviously unscoped or traversal-shaped input and does not grant access.
- Editing, test/repair workflows, and command execution remain owned by the existing `studio` and `terminal` modes and their central permission/execution planning.
- The debug-only Desktop visual inspector remains in `src/Haven.Desktop/DeveloperTools`; this app does not import or duplicate it.
- Shared Haven UI runtime and platform services remain at the OS root. This slice adds no Avalonia, WPF, Win32, or platform-specific dependency.
- Shell/mode registration is intentionally not changed in this lane because those are shared integration files. `DeveloperAppDescriptor` exposes stable app metadata and existing handoff IDs for a coordinated shell integration.

## Visual Studio extension provenance boundary

The Dev app does **not** own Visual Studio extension source or provenance. No Visual Studio SDK/package reference, VSIX implementation, editor-extension registration, or extension build asset is copied into or referenced by this project. Visual Studio integration must remain in its established provenance/adapter boundary and may only supply data through Haven-owned contracts.

## Focused validation

From the repository root:

```powershell
dotnet build "HavenOS Apps/Dev/HavenOS.Dev.csproj"
dotnet test "HavenOS Apps/Dev/Tests/HavenOS.Dev.Tests.csproj"
```

The focused tests guard read-only delegation, path-scope validation, Studio/Terminal handoff metadata, and the absence of Visual Studio SDK assembly references from the Dev app assembly.
