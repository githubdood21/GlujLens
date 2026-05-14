namespace GlujLens.Models;

/// <summary>
/// Represents a region of text detected in an image.
/// </summary>
public class TextRegion
{
    /// <summary>
    /// The detected text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score of the OCR detection (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Bounding box rectangle (x, y, width, height) in image coordinates.
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Bounding box rectangle (x, y, width, height) in image coordinates.
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// Bounding box rectangle (x, y, width, height) in image coordinates.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Bounding box rectangle (x, y, width, height) in image coordinates.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Detected language code (e.g., "en", "de", "pl").
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Translated text rendered over this OCR region.
    /// </summary>
    public string TranslatedText { get; set; } = string.Empty;

    /// <summary>
    /// Whether this region should render a translated replacement overlay.
    /// </summary>
    public bool HasTranslatedText => !string.IsNullOrWhiteSpace(TranslatedText);

    /// <summary>
    /// Estimated replacement text size in image coordinates.
    /// </summary>
    public double TranslationFontSize { get; set; }

    /// <summary>
    /// Estimated replacement foreground color.
    /// </summary>
    public string TranslationForeground { get; set; } = "#FF000000";

    /// <summary>
    /// Estimated background fill over the original text.
    /// </summary>
    public string TranslationBackground { get; set; } = "#CCFFFFFF";
}
