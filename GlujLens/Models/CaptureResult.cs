using Avalonia.Media.Imaging;

namespace GlujLens.Models;

/// <summary>
/// Represents a captured screenshot with its metadata.
/// </summary>
public class CaptureResult
{
    /// <summary>
    /// The captured bitmap image.
    /// </summary>
    public Bitmap? Image { get; set; }

    /// <summary>
    /// The raw byte array of the image.
    /// </summary>
    public byte[]? ImageData { get; set; }

    /// <summary>
    /// Width of the captured image in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Height of the captured image in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Timestamp when the screenshot was captured.
    /// </summary>
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Whether the capture was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if capture failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}