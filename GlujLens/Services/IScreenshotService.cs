using GlujLens.Models;

namespace GlujLens.Services;

/// <summary>
/// Service interface for capturing screenshots.
/// </summary>
public interface IScreenshotService
{
    /// <summary>
    /// Captures the full screen.
    /// </summary>
    Task<CaptureResult> CaptureFullScreenAsync();

    /// <summary>
    /// Captures every active display in its virtual desktop position.
    /// </summary>
    Task<CaptureResult> CaptureAllDisplaysAsync();

    /// <summary>
    /// Captures a specific region of the screen.
    /// </summary>
    /// <param name="x">X coordinate of the region top-left corner.</param>
    /// <param name="y">Y coordinate of the region top-left corner.</param>
    /// <param name="width">Width of the capture region.</param>
    /// <param name="height">Height of the capture region.</param>
    Task<CaptureResult> CaptureRegionAsync(int x, int y, int width, int height);

    /// <summary>
    /// Captures a specific window.
    /// </summary>
    /// <param name="windowHandle">Handle of the window to capture.</param>
    Task<CaptureResult> CaptureWindowAsync(IntPtr windowHandle);

    /// <summary>
    /// Captures with a delay (in seconds).
    /// </summary>
    /// <param name="delaySeconds">Delay before capture in seconds.</param>
    Task<CaptureResult> CaptureDelayedAsync(int delaySeconds);

    /// <summary>
    /// Shows a region selection overlay for manual capture.
    /// </summary>
    Task<CaptureResult> CaptureRegionOverlayAsync();
}
