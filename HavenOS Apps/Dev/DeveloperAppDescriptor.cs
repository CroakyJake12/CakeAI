/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Dev/DeveloperAppDescriptor.cs, at the Dev app boundary.
 * What: Describes the independent Dev surface and the existing developer destinations it may hand off to.
 * Why: Gives shell integration a stable contract without registering shared routes or duplicating developer executors in this lane.
 */

namespace HavenOS.Apps.Dev;

/// <summary>
/// Stable metadata for the independent HavenOS Dev app surface.
/// </summary>
public static class DeveloperAppDescriptor
{
    public const string AppId = "dev";
    public const string DisplayName = "Dev";
    public const string StudioModeId = "studio";
    public const string TerminalModeId = "terminal";

    /// <summary>
    /// Existing Haven developer modes that own editing, testing, repair, and command execution.
    /// Dev may navigate to these destinations but does not implement a second execution path.
    /// </summary>
    public static IReadOnlyList<string> ExistingExecutionHandoffs { get; } =
        [StudioModeId, TerminalModeId];
}
