using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GlujLens.Models;

namespace GlujLens.Services;

/// <summary>
/// Windows-specific screenshot service using GDI and Win32 APIs.
/// </summary>
public class ScreenshotService : IScreenshotService
{
    private readonly AppSettings _settings;

    public ScreenshotService(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<CaptureResult> CaptureFullScreenAsync()
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bitmap.Size);

            var result = await ConvertToCaptureResultAsync(bitmap);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CaptureResult> CaptureRegionAsync(int x, int y, int width, int height)
    {
        try
        {
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));

            var result = await ConvertToCaptureResultAsync(bitmap);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CaptureResult> CaptureWindowAsync(IntPtr windowHandle)
    {
        try
        {
            var handle = User32.GetForegroundWindow();
            var rect = new User32.RECT();
            User32.GetWindowRect(handle, out rect);

            var width = rect.right - rect.left;
            var height = rect.bottom - rect.top;

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rect.left, rect.top, 0, 0, new Size(width, height));

            var result = await ConvertToCaptureResultAsync(bitmap);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CaptureResult> CaptureDelayedAsync(int delaySeconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            return await CaptureFullScreenAsync();
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CaptureResult> CaptureRegionOverlayAsync()
    {
        // TODO: Implement region selection overlay
        // For now, fall back to full screen capture
        return await CaptureFullScreenAsync();
    }

    private async Task<CaptureResult> ConvertToCaptureResultAsync(Bitmap bitmap)
    {
        return new CaptureResult
        {
            Width = bitmap.Width,
            Height = bitmap.Height,
            CapturedAt = DateTime.Now,
            ImageData = BitmapToBytes(bitmap),
            // Note: Bitmap conversion will be handled separately for Avalonia
        };
    }

    private byte[] BitmapToBytes(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        var format = _settings.ImageFormat?.ToUpper() ?? "PNG";
        ImageFormat? imageFormat = format switch
        {
            "JPEG" => ImageFormat.Jpeg,
            "BMP" => ImageFormat.Bmp,
            "PNG" or _ => ImageFormat.Png
        };
        bitmap.Save(ms, imageFormat);
        return ms.ToArray();
    }
}

/// <summary>
/// Win32 API interop for window capture.
/// </summary>
internal static class User32
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}

/// <summary>
/// Extension to get primary screen bounds.
/// </summary>
// Screen class is now from System.Windows.Forms
