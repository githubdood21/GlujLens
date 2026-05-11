namespace GlujLens.Models;

/// <summary>
/// Represents an object, logo, or entity detected in an image by the Vision API.
/// </summary>
public class DetectedObject
{
    /// <summary>
    /// The label/name of the detected object.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score of the detection (0.0 to 1.0).
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
    /// The type/category of detection result.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Description or description text from the API.
    /// </summary>
    public string? Description { get; set; }
}