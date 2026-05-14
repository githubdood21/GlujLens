namespace GlujLens.Services;

public static class ModelFolderScanner
{
    public static IReadOnlyList<string> FindModelEntries(
        string rootDirectory,
        Func<string, bool> isModelEntry,
        SearchOption directorySearchOption = SearchOption.TopDirectoryOnly,
        bool includeFiles = false)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return Array.Empty<string>();
        }

        var directories = Directory
            .EnumerateDirectories(rootDirectory, "*", directorySearchOption)
            .Where(isModelEntry);

        var files = includeFiles
            ? Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.TopDirectoryOnly).Where(isModelEntry)
            : Enumerable.Empty<string>();

        return directories
            .Concat(files)
            .Where(path => !PathsEqual(path, rootDirectory))
            .OrderBy(path => Path.GetRelativePath(rootDirectory, path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetDisplayName(string rootDirectory, string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return string.Empty;
        }

        return Directory.Exists(rootDirectory)
            ? Path.GetRelativePath(rootDirectory, modelPath)
            : GetFallbackName(modelPath);
    }

    private static string GetFallbackName(string modelPath)
    {
        return Directory.Exists(modelPath)
            ? Path.GetFileName(modelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(modelPath);
    }

    private static bool PathsEqual(string first, string second)
    {
        return string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
    }
}
