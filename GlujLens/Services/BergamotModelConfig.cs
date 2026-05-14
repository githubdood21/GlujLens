namespace GlujLens.Services;

public static class BergamotModelConfig
{
    public static string EnsureConfigFile(string modelFolder)
    {
        if (string.IsNullOrWhiteSpace(modelFolder) || !Directory.Exists(modelFolder))
        {
            throw new DirectoryNotFoundException($"Bergamot model folder not found: {modelFolder}");
        }

        var existingConfig = Directory
            .EnumerateFiles(modelFolder, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(modelFolder, "*.yaml", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(existingConfig))
        {
            return existingConfig;
        }

        var modelFile = FindSingleFile(modelFolder, "model.*.bin");
        var vocabFile = FindSingleFile(modelFolder, "vocab.*.spm");
        var shortlistFile = FindSingleFile(modelFolder, "lex.*.bin");
        var configPath = Path.Combine(modelFolder, "config.yml");
        var gemmPrecision = modelFile.Contains("alphas", StringComparison.OrdinalIgnoreCase)
            ? "int8shiftAlphaAll"
            : "int8shiftAll";

        var config = string.Join(Environment.NewLine, new[]
        {
            "relative-paths: true",
            "models:",
            $"- {Path.GetFileName(modelFile)}",
            "vocabs:",
            $"- {Path.GetFileName(vocabFile)}",
            $"- {Path.GetFileName(vocabFile)}",
            "shortlist:",
            $"- {Path.GetFileName(shortlistFile)}",
            "- false",
            "beam-size: 1",
            "normalize: 1.0",
            "word-penalty: 0",
            "max-length-break: 128",
            "mini-batch-words: 1024",
            "workspace: 128",
            "max-length-factor: 2.0",
            "skip-cost: true",
            "cpu-threads: 0",
            "quiet: true",
            "quiet-translation: true",
            $"gemm-precision: {gemmPrecision}"
        });

        File.WriteAllText(configPath, config);
        return configPath;
    }

    public static (string Source, string Target) ReadLanguagePair(string modelFolder)
    {
        var modelName = Path.GetFileName(modelFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parts = modelName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
            ? (parts[0], parts[1])
            : (string.Empty, string.Empty);
    }

    private static string FindSingleFile(string folder, string pattern)
    {
        return Directory
            .EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path.Length)
            .FirstOrDefault()
            ?? throw new FileNotFoundException($"Required Bergamot file not found: {pattern}", folder);
    }
}
