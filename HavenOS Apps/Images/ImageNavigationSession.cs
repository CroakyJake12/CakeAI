namespace HavenOS.Images;

public sealed class ImageNavigationSession
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly string[] _paths;

    private ImageNavigationSession(string[] paths, int index)
    {
        _paths = paths;
        Index = index;
    }

    public int Index { get; private set; }

    public string CurrentPath => _paths[Index];

    public bool CanMovePrevious => Index > 0;

    public bool CanMoveNext => Index < _paths.Length - 1;

    public static ImageNavigationSession FromSelection(string selectedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);

        var selectedFullPath = Path.GetFullPath(selectedPath);
        if (!ImageFilePolicy.IsSupportedPath(selectedFullPath))
        {
            throw new NotSupportedException($"The selected file type is not supported by the Images picker: {Path.GetExtension(selectedFullPath)}");
        }

        var directory = Path.GetDirectoryName(selectedFullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new ImageNavigationSession([selectedFullPath], 0);
        }

        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(directory)
                .Where(ImageFilePolicy.IsSupportedPath)
                .Select(Path.GetFullPath)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, PathComparer)
                .ToArray();
        }
        catch (IOException)
        {
            paths = [selectedFullPath];
        }
        catch (UnauthorizedAccessException)
        {
            paths = [selectedFullPath];
        }

        if (!paths.Contains(selectedFullPath, PathComparer))
        {
            paths = [.. paths, selectedFullPath];
            Array.Sort(paths, ComparePaths);
        }

        var index = Array.FindIndex(paths, path => PathComparer.Equals(path, selectedFullPath));
        return new ImageNavigationSession(paths, index < 0 ? 0 : index);
    }

    public string? MovePrevious()
    {
        if (!CanMovePrevious)
        {
            return null;
        }

        Index--;
        return CurrentPath;
    }

    public string? MoveNext()
    {
        if (!CanMoveNext)
        {
            return null;
        }

        Index++;
        return CurrentPath;
    }

    private static int ComparePaths(string left, string right)
    {
        var byName = StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(left), Path.GetFileName(right));
        return byName != 0 ? byName : PathComparer.Compare(left, right);
    }
}
