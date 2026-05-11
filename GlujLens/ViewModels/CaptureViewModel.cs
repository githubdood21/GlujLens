using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlujLens.Models;
using Avalonia.Media.Imaging;

namespace GlujLens.ViewModels;

/// <summary>
/// View model for screenshot capture operations.
/// </summary>
public partial class CaptureViewModel : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _capturedImage;

    [ObservableProperty]
    private CaptureMode _selectedCaptureMode;

    [ObservableProperty]
    private string _statusMessage = "Select capture mode";

    [ObservableProperty]
    private bool _isCapturing;

    public CaptureViewModel()
    {
    }

    public enum CaptureMode
    {
        FullScreen,
        Region,
        Window,
        Delayed
    }

    [RelayCommand]
    private async Task CaptureAsync()
    {
        IsCapturing = true;
        StatusMessage = "Capturing...";

        try
        {
            // Capture logic will be delegated to ScreenshotService
            StatusMessage = "Capture complete";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Capture failed: {ex.Message}";
        }
        finally
        {
            IsCapturing = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(string filePath)
    {
        if (CapturedImage == null)
        {
            StatusMessage = "No image to save";
            return;
        }

        try
        {
            // Save bitmap to file
            StatusMessage = $"Saving to {filePath}...";
            StatusMessage = "Saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyToClipboardAsync()
    {
        if (CapturedImage == null)
        {
            StatusMessage = "No image to copy";
            return;
        }

        try
        {
            StatusMessage = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Copy failed: {ex.Message}";
        }
    }
}