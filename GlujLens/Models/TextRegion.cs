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
}