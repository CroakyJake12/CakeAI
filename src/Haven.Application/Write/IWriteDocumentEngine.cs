namespace Haven.Application;

/// <summary>
/// Defines the platform-neutral document-engine boundary used by Haven Write.
/// Implementations own document layout/editing state; HUI remains the visible UI.
/// </summary>
public interface IWriteDocumentEngine : IAsyncDisposable
{
    WriteEngineCapabilities Capabilities { get; }
    WriteEngineDocumentState? CurrentDocument { get; }

    Task<WriteEngineDocumentState> OpenAsync(
        WriteEngineOpenRequest request,
        CancellationToken cancellationToken = default);

    Task<WriteEngineTile> RenderTileAsync(
        WriteEngineTileRequest request,
        CancellationToken cancellationToken = default);

    Task PostTextInputAsync(
        string text,
        CancellationToken cancellationToken = default);

    Task PostKeyAsync(
        WriteEngineKeyEvent keyEvent,
        CancellationToken cancellationToken = default);

    Task PostPointerAsync(
        WriteEnginePointerEvent pointerEvent,
        CancellationToken cancellationToken = default);

    Task ExecuteCommandAsync(
        WriteEngineCommand command,
        CancellationToken cancellationToken = default);

    Task<WriteEngineAccessibilitySnapshot> GetAccessibilitySnapshotAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        WriteEngineSaveRequest request,
        CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

public sealed record WriteEngineCapabilities(
    string EngineId,
    string EngineVersion,
    bool SupportsTiledRendering,
    bool SupportsTextInput,
    bool SupportsPointerInput,
    bool SupportsUnoCommands,
    bool SupportsAccessibilitySnapshot,
    bool UsesUnstableUpstreamApi);

public sealed record WriteEngineOpenRequest(
    string SourcePath,
    bool ReadOnly = false,
    string? UserProfileDirectory = null);

public sealed record WriteEngineDocumentState(
    string SourcePath,
    string DocumentType,
    long WidthTwips,
    long HeightTwips,
    int ViewId,
    bool ReadOnly);

public sealed record WriteEngineTileRequest(
    int PixelWidth,
    int PixelHeight,
    int XTwips,
    int YTwips,
    int WidthTwips,
    int HeightTwips);

public sealed record WriteEngineTile(
    int PixelWidth,
    int PixelHeight,
    WriteEnginePixelFormat PixelFormat,
    byte[] Pixels);

public enum WriteEnginePixelFormat
{
    Bgra8888 = 0,
    Rgba8888 = 1
}

public sealed record WriteEngineKeyEvent(
    WriteEngineKeyEventType Type,
    int CharacterCode,
    int KeyCode);

public enum WriteEngineKeyEventType
{
    KeyInput = 0,
    KeyUp = 1
}

public sealed record WriteEnginePointerEvent(
    WriteEnginePointerEventType Type,
    int XTwips,
    int YTwips,
    int ClickCount,
    int Buttons,
    int Modifiers);

public enum WriteEnginePointerEventType
{
    ButtonDown = 0,
    ButtonUp = 1,
    Move = 2
}

/// <summary>
/// Semantic engine command. The implementation validates command names and arguments;
/// callers must not use this as an unrestricted native-command escape hatch.
/// </summary>
public sealed record WriteEngineCommand(
    string Name,
    string? ArgumentsJson = null,
    bool NotifyWhenFinished = true);

public sealed record WriteEngineAccessibilitySnapshot(
    bool Available,
    string FocusedParagraph,
    int CaretPosition,
    string? SelectionText = null);

public sealed record WriteEngineSaveRequest(
    string DestinationPath,
    string? Format = null,
    string? FilterOptions = null);
