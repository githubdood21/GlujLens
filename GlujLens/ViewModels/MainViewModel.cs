using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlujLens.Models;
using GlujLens.Services;
using GlujLens.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows.Forms;

namespace GlujLens.ViewModels;

/// <summary>
/// Main view model for the application. Handles the primary state and commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ITrayIconService _trayIcon;
    private readonly IScreenshotService _screenshotService;
    private readonly IOcrService _ocrService;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _appName = "GlujLens";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isOcrProcessing;

    [ObservableProperty]
    private ObservableCollection<DetectedObject> _detectedObjects = new();

    [ObservableProperty]
    private ObservableCollection<TextRegion> _textRegions = new();

    [ObservableProperty]
    private string _extractedText = string.Empty;

    [ObservableProperty]
    private string _translatedText = string.Empty;

    [ObservableProperty]
    private string _targetLanguage = "en";

    [ObservableProperty]
    private bool _isWindowVisible;

    [ObservableProperty]
    private Bitmap? _capturedImage;

    [ObservableProperty]
    private bool _hasCapturedImage;

    [ObservableProperty]
    private int _capturedImageWidth;

    [ObservableProperty]
    private int _capturedImageHeight;

    [ObservableProperty]
    private string _selectedOcrText = string.Empty;

    [ObservableProperty]
    private string _captureDuration = string.Empty;

    private byte[]? _lastCapturedImage;

    private readonly IServiceProvider _serviceProvider;
    private HotkeyService? _hotkeyService;

    partial void OnCapturedImageChanged(Bitmap? value)
    {
        HasCapturedImage = value != null;
    }

    public MainViewModel(ITrayIconService trayIcon, IScreenshotService screenshotService, IOcrService ocrService, AppSettings settings, IServiceProvider serviceProvider)
    {
        _trayIcon = trayIcon;
        _screenshotService = screenshotService;
        _ocrService = ocrService;
        _settings = settings;
        _serviceProvider = serviceProvider;

        // Wire up tray icon menu item clicks
        _trayIcon.MenuItemClicked += OnMenuItemClicked;
        _trayIcon.IconDoubleClicked += OnIconDoubleClicked;
        _trayIcon.BalloonTipClicked += OnBalloonTipClicked;
    }

    ~MainViewModel()
    {
        _trayIcon.MenuItemClicked -= OnMenuItemClicked;
        _trayIcon.IconDoubleClicked -= OnIconDoubleClicked;
        _trayIcon.BalloonTipClicked -= OnBalloonTipClicked;
    }

    [RelayCommand]
    private async Task CaptureAsync()
    {
        await CaptureAsync(captureAllDisplays: false);
    }

    [RelayCommand]
    private async Task CaptureAllDisplaysAsync()
    {
        await CaptureAsync(captureAllDisplays: true);
    }

    private async Task CaptureAsync(bool captureAllDisplays)
    {
        if (IsProcessing) return;

        IsProcessing = true;
        StatusText = captureAllDisplays
            ? "Capturing all displays..."
            : "Capturing screenshot...";
        CaptureDuration = string.Empty;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = captureAllDisplays
                ? await _screenshotService.CaptureAllDisplaysAsync()
                : await _screenshotService.CaptureFullScreenAsync();
            stopwatch.Stop();

            if (result.Success && result.ImageData != null)
            {
                _lastCapturedImage = result.ImageData;
                CapturedImageWidth = result.Width;
                CapturedImageHeight = result.Height;
                TextRegions.Clear();
                ExtractedText = string.Empty;
                SelectedOcrText = string.Empty;

                // Convert byte[] to Avalonia Bitmap for preview
                using var ms = new MemoryStream(result.ImageData);
                CapturedImage = new Bitmap(ms);

                var elapsed = stopwatch.Elapsed;
                if (elapsed.TotalMilliseconds < 1)
                {
                    CaptureDuration = $"Capture time: {(elapsed.TotalMilliseconds * 1000000):F0} ns";
                }
                else if (elapsed.TotalMilliseconds < 1000)
                {
                    CaptureDuration = $"Capture time: {elapsed.TotalMilliseconds:F1} ms";
                }
                else
                {
                    CaptureDuration = $"Capture time: {elapsed.TotalSeconds:F2} s";
                }

                StatusText = captureAllDisplays
                    ? $"All displays captured successfully ({result.Width}x{result.Height})"
                    : $"Screenshot captured successfully ({result.Width}x{result.Height})";

                // Show notification popup if enabled
                if (_settings.ShowNotificationAfterCapture)
                {
                    ShowCaptureNotification(result.ImageData, result.Width, result.Height);
                }
            }
            else
            {
                StatusText = $"Capture failed: {result.ErrorMessage ?? "Unknown error"}";
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task CopyToClipboardAsync()
    {
        if (_lastCapturedImage == null)
        {
            StatusText = "No screenshot captured yet";
            return;
        }

        try
        {
            // Use WinForms Clipboard for simplicity (since we're already using WinForms for tray)
            using var ms = new MemoryStream(_lastCapturedImage);
            using var tempBitmap = new System.Drawing.Bitmap(ms);
            Clipboard.Clear();
            Clipboard.SetImage(tempBitmap);

            StatusText = "Screenshot copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusText = $"Copy failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveImageAsync()
    {
        if (_lastCapturedImage == null)
        {
            StatusText = "No screenshot captured yet";
            return;
        }

        try
        {
            var saveDir = _settings.DefaultSaveDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var ext = _settings.ImageFormat?.ToUpper() ?? "PNG";
            switch (ext)
            {
                case "JPEG": ext = "jpg"; break;
                case "BMP": ext = "bmp"; break;
                case "PNG": default: ext = "png"; break;
            }
            var filePath = Path.Combine(saveDir, $"screenshot_{timestamp}.{ext}");

            await File.WriteAllBytesAsync(filePath, _lastCapturedImage);
            StatusText = $"Screenshot saved to {filePath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearResults()
    {
        DetectedObjects.Clear();
        TextRegions.Clear();
        ExtractedText = string.Empty;
        TranslatedText = string.Empty;
        CapturedImage = null;
        CapturedImageWidth = 0;
        CapturedImageHeight = 0;
        _lastCapturedImage = null;
        SelectedOcrText = string.Empty;
        StatusText = "Ready";
    }

    [RelayCommand]
    private async Task RunOcrAsync()
    {
        if (_lastCapturedImage == null)
        {
            StatusText = "No screenshot captured yet";
            return;
        }

        if (IsProcessing) return;

        IsProcessing = true;
        IsOcrProcessing = true;
        try
        {
            await RunOcrAsync(_lastCapturedImage);
        }
        finally
        {
            IsOcrProcessing = false;
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void SelectTextRegion(TextRegion? region)
    {
        if (region == null)
            return;

        SelectedOcrText = region.Text;
        StatusText = $"Selected OCR text: {region.Text}";

        try
        {
            Clipboard.SetText(region.Text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to copy selected OCR text: {ex.Message}");
        }
    }

    [RelayCommand]
    private void TranslateText()
    {
        if (string.IsNullOrWhiteSpace(ExtractedText))
        {
            StatusText = "No text to translate";
            return;
        }

        StatusText = $"Translating to {_targetLanguage}...";
        // Translation will be done by the service
        StatusText = "Translation complete (placeholder)";
    }

    [RelayCommand]
    private void ShowWindow()
    {
        // Try to activate the main window using Win32 APIs
        try
        {
            var success = WindowHelper.ActivateMainWindow();
            if (success)
            {
                StatusText = "Window activated";
            }
            else
            {
                StatusText = "Window activation attempted";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Window activation failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void HideWindow()
    {
        IsWindowVisible = false;
        StatusText = "Minimized to tray";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        try
        {
            // Create a NEW SettingsViewModel each time to ensure a fresh instance
            var settings = _serviceProvider.GetRequiredService<AppSettings>();
            var settingsViewModel = new SettingsViewModel(settings);
            var settingsWindow = new SettingsView(settingsViewModel, this);
            settingsWindow.Show();
            StatusText = "Settings window opened";
        }
        catch (Exception ex)
        {
            StatusText = $"Error opening settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Quit()
    {
        _trayIcon.HideIcon();
        // Application quit will be handled by App
        StatusText = "Quitting...";
    }

    private void OnMenuItemClicked(object? sender, MenuItemClickedEventArgs e)
    {
        StatusText = $"Menu item clicked: {e.MenuItemName}";

        switch (e.MenuItemName)
        {
            case "Capture":
                CaptureCommand.Execute(null);
                break;
            case "Open":
                ShowWindowCommand.Execute(null);
                break;
            case "Settings":
                OpenSettingsCommand.Execute(null);
                break;
            case "Quit":
                QuitCommand.Execute(null);
                break;
        }
    }

    private void OnIconDoubleClicked(object? sender, EventArgs e)
    {
        ShowWindowCommand.Execute(null);
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e)
    {
        // When the notification is clicked, show/focus the main window
        ShowWindowCommand.Execute(null);
    }

    /// <summary>
    /// Shows a custom notification popup with the captured screenshot preview.
    /// Uses Avalonia's dispatcher to run on the UI thread.
    /// </summary>
    private void ShowCaptureNotification(byte[] imageData, int width, int height)
    {
        try
        {
            var imageDataCopy = imageData;
            var w = width;
            var h = height;
            
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var popup = new NotificationPopup(this, _trayIcon, _settings, imageDataCopy, w, h);
                    popup.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationPopup] Error: {ex.Message}");
                }
            }, Avalonia.Threading.DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationPopup] Failed to schedule: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles menu item clicks from the tray icon context menu.
    /// </summary>
    public void HandleMenuItem(string menuItemName)
    {
        StatusText = $"Menu item clicked: {menuItemName}";

        switch (menuItemName)
        {
            case "Capture":
                CaptureCommand.Execute(null);
                break;
            case "Open":
                ShowWindowCommand.Execute(null);
                break;
            case "Settings":
                OpenSettingsCommand.Execute(null);
                break;
            case "Quit":
                QuitCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Reloads settings from the settings file.
    /// Should be called when the settings window closes.
    /// Also refreshes the hotkey registration with the new shortcut.
    /// </summary>
    public void ReloadSettings()
    {
        try
        {
            _settings.Reload();
            // Refresh the hotkey registration with the new shortcut
            RefreshHotkey();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to reload settings: {ex.Message}";
        }
    }

    /// <summary>
    /// Sets the hotkey service reference (called by MainWindow after creation).
    /// </summary>
    public void SetHotkeyService(HotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
    }

    /// <summary>
    /// Refreshes the hotkey from current settings.
    /// Called when settings change.
    /// </summary>
    public bool RefreshHotkey()
    {
        var shortcut = _settings.CaptureShortcut ?? "Not set";
        if (_hotkeyService == null)
        {
            StatusText = $"Hotkey refresh failed. Hotkey service is not available for: {shortcut}";
            System.Diagnostics.Debug.WriteLine("[MainViewModel] Hotkey refresh requested before HotkeyService was attached.");
            return false;
        }

        var refreshed = _hotkeyService.RefreshHotkey();
        StatusText = refreshed
            ? $"Hotkey refreshed. Capture shortcut: {shortcut}"
            : $"Hotkey refresh failed. Capture shortcut: {shortcut}";
        return refreshed;
    }

    private async Task RunOcrAsync(byte[] imageData)
    {
        if (string.Equals(_settings.OcrProvider, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            ExtractedText = string.Empty;
            TextRegions.Clear();
            return;
        }

        if (!string.Equals(_settings.OcrProvider, "Tesseract", StringComparison.OrdinalIgnoreCase))
        {
            ExtractedText = string.Empty;
            TextRegions.Clear();
            StatusText = $"{_settings.OcrProvider} OCR is not implemented yet";
            return;
        }

        StatusText = "Running Tesseract OCR...";
        var ocrResult = await _ocrService.ExtractTextAsync(imageData);
        if (!ocrResult.Success)
        {
            ExtractedText = string.Empty;
            TextRegions.Clear();
            StatusText = $"OCR failed: {ocrResult.ErrorMessage ?? "Unknown error"}";
            return;
        }

        ExtractedText = ocrResult.Text;
        SelectedOcrText = string.Empty;
        TextRegions.Clear();
        foreach (var region in ocrResult.TextRegions)
        {
            TextRegions.Add(region);
        }

        StatusText = string.IsNullOrWhiteSpace(ExtractedText)
            ? "Screenshot captured; OCR found no text"
            : $"Screenshot captured; OCR extracted {TextRegions.Count} text regions";
    }
}
