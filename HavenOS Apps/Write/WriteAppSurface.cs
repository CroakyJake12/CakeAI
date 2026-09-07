using System;
using Haven.Desktop.Views.Pages.Write;

namespace Haven.Desktop.Apps.Write;

/// <summary>
/// Defines the standalone HavenOS Write app surface while preserving the existing editor implementation.
/// </summary>
public static class WriteAppSurface
{
    public const string AppKey = "write";
    public const string DisplayName = "Write";

    /// <summary>
    /// The existing DI-created editor remains the single Write editing surface.
    /// </summary>
    public static Type EditorPageType => typeof(WritePage);
}
