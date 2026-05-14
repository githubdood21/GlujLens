using Avalonia.Controls;
using BergamotTranslatorSharp;

namespace GlujLens.Views;

public partial class BergamotTestWindow : Window
{
    private readonly string _modelFolder;

    public BergamotTestWindow(string modelFolder)
    {
        InitializeComponent();

        _modelFolder = modelFolder;
        ModelPathTextBox.Text = string.IsNullOrWhiteSpace(modelFolder)
            ? "No Bergamot model selected in Settings."
            : modelFolder;
        InputTextBox.Text = IsJapaneseToEnglishModel(modelFolder)
            ? "これは翻訳テストです。"
            : "This is a translation test.";
        StatusTextBlock.Text = "Using the Bergamot model selected in Settings.";
    }

    private async void OnTranslateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var input = InputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(_modelFolder) || !Directory.Exists(_modelFolder))
        {
            StatusTextBlock.Text = "No valid Bergamot model is selected in Settings.";
            OutputTextBox.Text = $"Model path: {_modelFolder}";
            return;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            StatusTextBlock.Text = "Enter text to translate.";
            return;
        }

        TranslateButton.IsEnabled = false;
        OutputTextBox.Text = string.Empty;
        StatusTextBlock.Text = "Loading Bergamot model and translating...";

        try
        {
            var translated = await Task.Run(() =>
            {
                var configPath = EnsureConfigFile(_modelFolder);
                using var translator = new BlockingService(configPath);
                return translator.Translate(input);
            });

            OutputTextBox.Text = translated;
            StatusTextBlock.Text = "Translation completed.";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = ex.ToString();
            StatusTextBlock.Text = "Bergamot test failed.";
        }
        finally
        {
            TranslateButton.IsEnabled = true;
        }
    }

    private static string EnsureConfigFile(string modelFolder)
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

    private static string FindSingleFile(string folder, string pattern)
    {
        return Directory
            .EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path.Length)
            .FirstOrDefault()
            ?? throw new FileNotFoundException($"Required Bergamot file not found: {pattern}", folder);
    }

    private static bool IsJapaneseToEnglishModel(string modelFolder)
    {
        var modelName = Path.GetFileName(modelFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return modelName.StartsWith("ja-en", StringComparison.OrdinalIgnoreCase);
    }
}
