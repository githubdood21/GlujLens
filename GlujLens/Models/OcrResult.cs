namespace GlujLens.Models;

/// <summary>
/// Result returned by an OCR provider.
/// </summary>
public class OcrResult
{
    public bool Success { get; set; }

    public string Text { get; set; } = string.Empty;

    public List<TextRegion> TextRegions { get; set; } = new();

    public string? ErrorMessage { get; set; }
}
