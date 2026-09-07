namespace HavenOS.Images;

public static class ImageFilePolicy
{
    private static readonly HashSet<string> SupportedExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp",
    };

    public static IReadOnlyList<string> PickerPatterns { get; } = SupportedExtensionSet
        .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
        .Select(extension => $"*{extension}")
        .ToArray();

    public static bool IsSupportedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return SupportedExtensionSet.Contains(Path.GetExtension(path));
    }
}
