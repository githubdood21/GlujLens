namespace GlujLens.Services;

public sealed class MlNetOcrModelInfo
{
    public string ModelPath { get; init; } = string.Empty;

    public string DirectoryPath { get; init; } = string.Empty;

    public IReadOnlyList<string> OnnxModelPaths { get; init; } = Array.Empty<string>();

    public bool IsModelDirectory => Directory.Exists(ModelPath);

    public string DisplayName => IsModelDirectory
        ? Path.GetFileName(ModelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        : Path.GetFileNameWithoutExtension(ModelPath);
}
