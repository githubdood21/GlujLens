namespace GlujLens.Services;

public sealed class PaddleOnnxOcrModel
{
    public string RootDirectory { get; init; } = string.Empty;

    public string DetectionModelPath { get; init; } = string.Empty;

    public string RecognitionModelPath { get; init; } = string.Empty;

    public string? TextLineOrientationModelPath { get; init; }

    public string DictionaryPath { get; init; } = string.Empty;

    public string RecognitionLanguage { get; init; } = string.Empty;

    public bool IsQuantized { get; init; }
}
