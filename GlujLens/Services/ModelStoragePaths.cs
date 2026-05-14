namespace GlujLens.Services;

public static class ModelStoragePaths
{
    public static string RootDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

    public static string OcrDirectory =>
        Path.Combine(RootDirectory, "ocr");

    public static string TesseractDirectory =>
        Path.Combine(OcrDirectory, "tesseract", "tessdata");

    public static string MlNetOcrDirectory =>
        Path.Combine(OcrDirectory, "mlnet");

    public static string TranslationDirectory =>
        Path.Combine(RootDirectory, "translation");

    public static string BergamotDirectory =>
        Path.Combine(TranslationDirectory, "bergamot");

    public static string DirectMlOnnxDirectory =>
        Path.Combine(TranslationDirectory, "directml-onnx");

    public static string LanguageDetectionDirectory =>
        Path.Combine(RootDirectory, "language-detection");

    public static void EnsureDefaultDirectories()
    {
        Directory.CreateDirectory(TesseractDirectory);
        Directory.CreateDirectory(MlNetOcrDirectory);
        Directory.CreateDirectory(BergamotDirectory);
        Directory.CreateDirectory(DirectMlOnnxDirectory);
        Directory.CreateDirectory(LanguageDetectionDirectory);
    }
}
