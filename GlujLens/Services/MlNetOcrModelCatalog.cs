using GlujLens.Models;

namespace GlujLens.Services;

public sealed class MlNetOcrModelCatalog
{
    private readonly AppSettings _settings;

    public MlNetOcrModelCatalog(AppSettings settings)
    {
        _settings = settings;
    }

    public MlNetOcrModelInfo? GetSelectedModel()
    {
        var path = _settings.MlNetOcrModelPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = ModelStoragePaths.MlNetOcrDirectory;
        }

        if (File.Exists(path) && IsSupportedModelFile(path))
        {
            return new MlNetOcrModelInfo
            {
                ModelPath = path,
                DirectoryPath = Path.GetDirectoryName(path) ?? ModelStoragePaths.MlNetOcrDirectory,
                OnnxModelPaths = Path.GetExtension(path).Equals(".onnx", StringComparison.OrdinalIgnoreCase)
                    ? new[] { path }
                    : Array.Empty<string>()
            };
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        var directModel = TryCreateModelDirectoryInfo(path);
        if (directModel != null)
        {
            return directModel;
        }

        return Directory
            .EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
            .Select(TryCreateModelDirectoryInfo)
            .Where(model => model != null)
            .Cast<MlNetOcrModelInfo>()
            .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? Directory
            .EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedModelFile)
            .OrderBy(modelPath => Path.GetRelativePath(path, modelPath), StringComparer.OrdinalIgnoreCase)
            .Select(modelPath => new MlNetOcrModelInfo
            {
                ModelPath = modelPath,
                DirectoryPath = Path.GetDirectoryName(modelPath) ?? path,
                OnnxModelPaths = Path.GetExtension(modelPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase)
                    ? new[] { modelPath }
                    : Array.Empty<string>()
            })
            .FirstOrDefault();
    }

    public PaddleOnnxOcrModel? GetSelectedPaddleOcrModel()
    {
        var selected = GetSelectedModel();
        if (selected == null || !Directory.Exists(selected.ModelPath))
        {
            return null;
        }

        var root = selected.ModelPath;
        var detectionModel = FindPreferredFile(root, new[]
        {
            Path.Combine("detection", "v5", "det.onnx"),
            Path.Combine("detection", "v3", "det.onnx")
        }) ?? Directory
            .EnumerateFiles(root, "det.onnx", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var languageDirectory = FindPreferredLanguageDirectory(root);
        if (string.IsNullOrWhiteSpace(detectionModel) || string.IsNullOrWhiteSpace(languageDirectory))
        {
            return null;
        }

        var recognitionModel = Path.Combine(languageDirectory, "rec.onnx");
        var dictionary = Path.Combine(languageDirectory, "dict.txt");
        if (!File.Exists(recognitionModel) || !File.Exists(dictionary))
        {
            return null;
        }

        return new PaddleOnnxOcrModel
        {
            RootDirectory = root,
            DetectionModelPath = detectionModel,
            RecognitionModelPath = recognitionModel,
            DictionaryPath = dictionary,
            RecognitionLanguage = Path.GetFileName(languageDirectory)
        };
    }

    private static MlNetOcrModelInfo? TryCreateModelDirectoryInfo(string directory)
    {
        var onnxFiles = Directory
            .EnumerateFiles(directory, "*.onnx", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (onnxFiles.Count == 0)
        {
            return null;
        }

        return new MlNetOcrModelInfo
        {
            ModelPath = directory,
            DirectoryPath = directory,
            OnnxModelPaths = onnxFiles
        };
    }

    private static string? FindPreferredLanguageDirectory(string root)
    {
        var languagesRoot = Path.Combine(root, "languages");
        if (!Directory.Exists(languagesRoot))
        {
            return null;
        }

        foreach (var language in new[] { "latin", "english", "chinese", "korean", "eslav", "greek", "thai" })
        {
            var path = Path.Combine(languagesRoot, language);
            if (File.Exists(Path.Combine(path, "rec.onnx")) &&
                File.Exists(Path.Combine(path, "dict.txt")))
            {
                return path;
            }
        }

        return Directory
            .EnumerateDirectories(languagesRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
                File.Exists(Path.Combine(path, "rec.onnx")) &&
                File.Exists(Path.Combine(path, "dict.txt")))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? FindPreferredFile(string root, IReadOnlyList<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var path = Path.Combine(root, relativePath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool IsSupportedModelFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }
}
