namespace Haven.Desktop.Views.Pages.Studio;

public enum StudioCreationDestinationKind
{
    App,
    ProjectCreator,
    ExperienceBuilder,
    InHouse
}

public sealed record StudioCreationIntent(
    string Id,
    string Name,
    string Description,
    string Category,
    StudioCreationDestinationKind DestinationKind,
    string? AppKey = null,
    string? SeedPrompt = null,
    string? InHouseFlow = null);

public static class StudioCreationCatalog
{
    public static IReadOnlyList<StudioCreationIntent> Items { get; } =
    [
        // Media
        App("image", "Images", "Create or edit an image in Imagine.", "Media", "imagine", "image"),
        App("video", "Videos", "Start a video project in Imagine.", "Media", "imagine", "video"),
        App("audio", "Audio", "Start an audio project in Imagine.", "Media", "imagine", "audio"),
        InHouse("text-to-speech", "Text to Speech", "Turn text into spoken audio with Haven's in-house voice flow.", "Media", "text-to-speech"),

        // Interactive
        Project("windows-app", "Windows Apps", "Build a Windows application as a Haven project.", "Interactive", "Create a Windows application project."),
        Project("android-app", "Android Apps", "Build an Android application as a Haven project.", "Interactive", "Create an Android application project."),
        Project("haven-app", "Haven Apps", "Build an installable Haven application.", "Interactive", "Create a Haven application project."),
        Project("game", "Games", "Create a new game project; Games remains the library and player.", "Interactive", "Create a new game project."),
        Project("website", "Websites", "Build a website as a Haven project.", "Interactive", "Create a website project."),
        Experience("experience", "Experiences", "Create a saved interactive Haven-generated experience.", "Interactive"),

        // Tools
        Project("agent", "Agents", "Create a reusable Haven agent.", "Tools", "Create a Haven agent project."),
        Project("plugin", "Plugins", "Create a Haven plugin project.", "Tools", "Create a Haven plugin project."),
        Project("skill", "Skills", "Create a reusable Haven skill.", "Tools", "Create a Haven skill project."),
        Project("widget", "Widgets", "Create a reusable Haven widget.", "Tools", "Create a Haven widget project."),

        // Documents
        App("text-document", "Text Document", "Start a document in Write.", "Documents", "write", "document"),
        App("pdf", "PDF", "Create PDF-ready content in Write.", "Documents", "write", "pdf"),
        App("note", "Note", "Start a lightweight note in Write.", "Documents", "write", "note"),
        App("presentation", "Presentation", "Start a deck in Present.", "Documents", "present"),
        App("spreadsheet", "Spreadsheet", "Start a workbook in Data.", "Documents", "data", "spreadsheet"),
        App("database", "Database", "Start a structured data workspace in Data.", "Documents", "data", "database"),
        App("canvas", "Canvas", "Open a new visual Canvas.", "Documents", "canvas"),
        App("board", "Board", "Open a new Board.", "Documents", "boards")
    ];

    public static IReadOnlyList<StudioCreationIntent> InCategory(string category) =>
        Items.Where(item => item.Category.Equals(category, StringComparison.Ordinal)).ToArray();

    private static StudioCreationIntent App(string id, string name, string description, string category, string appKey, string? hint = null) =>
        new(id, name, description, category, StudioCreationDestinationKind.App, appKey, hint);

    private static StudioCreationIntent Project(string id, string name, string description, string category, string prompt) =>
        new(id, name, description, category, StudioCreationDestinationKind.ProjectCreator, "projects", prompt);

    private static StudioCreationIntent Experience(string id, string name, string description, string category) =>
        new(id, name, description, category, StudioCreationDestinationKind.ExperienceBuilder, "experiences", "Create a standalone interactive Haven experience.");

    private static StudioCreationIntent InHouse(string id, string name, string description, string category, string flow) =>
        new(id, name, description, category, StudioCreationDestinationKind.InHouse, InHouseFlow: flow);
}
