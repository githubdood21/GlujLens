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
            .OrderByDescending(model => IsPreferredPaddleOcrDirectory(model.ModelPath))
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
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

    private static bool IsPreferredPaddleOcrDirectory(string path)
    {
        return File.Exists(Path.Combine(path, "PP-OCRv5_server_det_infer.onnx")) &&
               File.Exists(Path.Combine(path, "PP-OCRv5_server_rec_infer.onnx")) &&
               File.Exists(Path.Combine(path, "ppocrv5_dict.txt"));
    }

    public PaddleOnnxOcrModel? GetSelectedPaddleOcrModel()
    {
        var selected = GetSelectedModel();
        if (selected == null || !Directory.Exists(selected.ModelPath))
        {
            return null;
        }

        var root = selected.ModelPath;
        var flatV5Model = TryCreateFlatPaddleOcrModel(root);
        if (flatV5Model != null)
        {
            return flatV5Model;
        }

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

    private static PaddleOnnxOcrModel? TryCreateFlatPaddleOcrModel(string root)
    {
        var detectionModel = FindPreferredFile(root, new[]
        {
            "PP-OCRv5_server_det_infer.onnx",
            "PP-OCRv5_mobile_det_infer.onnx",
            "PP-OCRv4_server_det_infer.onnx",
            "PP-OCRv4_mobile_det_infer.onnx"
        }) ?? Directory
            .EnumerateFiles(root, "*det*.onnx", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var recognitionModel = FindPreferredFile(root, new[]
        {
            "PP-OCRv5_server_rec_infer.onnx",
            "PP-OCRv5_mobile_rec_infer.onnx",
            "PP-OCRv4_server_rec_infer.onnx",
            "PP-OCRv4_mobile_rec_infer.onnx"
        }) ?? Directory
            .EnumerateFiles(root, "*rec*.onnx", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var dictionary = FindPreferredFile(root, new[]
        {
            "ppocrv5_dict.txt",
            "dict.txt",
            "en_dict.txt"
        }) ?? Directory
            .EnumerateFiles(root, "*dict*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(detectionModel) ||
            string.IsNullOrWhiteSpace(recognitionModel) ||
            string.IsNullOrWhiteSpace(dictionary))
        {
            return null;
        }

        return new PaddleOnnxOcrModel
        {
            RootDirectory = root,
            DetectionModelPath = detectionModel,
            RecognitionModelPath = recognitionModel,
            TextLineOrientationModelPath = Directory
                .EnumerateFiles(root, "*textline*ori*.onnx", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(),
            DictionaryPath = dictionary,
            RecognitionLanguage = "ppocrv5"
        };
    }

    private static MlNetOcrModelInfo? TryCreateModelDirectoryInfo(string directory)
    {
        if (!LooksLikeModelDirectory(directory))
        {
            return null;
        }

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

    private static bool LooksLikeModelDirectory(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.onnx", SearchOption.TopDirectoryOnly).Any() ||
               Directory.Exists(Path.Combine(directory, "detection")) ||
               Directory.Exists(Path.Combine(directory, "languages"));
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
