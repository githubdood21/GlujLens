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
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace GlujLens.ViewModels;

/// <summary>
/// Main view model for the application. Handles the primary state and commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ITrayIconService _trayIcon;
    private readonly IScreenshotService _screenshotService;
    private readonly IOcrService _ocrService;
    private readonly ITranslationService _translationService;
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
    private bool _isTranslationProcessing;

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
    private string _selectedTranslatedText = string.Empty;

    [ObservableProperty]
    private string _captureDuration = string.Empty;

    private byte[]? _lastCapturedImage;

    private readonly IServiceProvider _serviceProvider;
    private HotkeyService? _hotkeyService;

    partial void OnCapturedImageChanged(Bitmap? value)
    {
        HasCapturedImage = value != null;
    }

    public MainViewModel(ITrayIconService trayIcon, IScreenshotService screenshotService, IOcrService ocrService, ITranslationService translationService, AppSettings settings, IServiceProvider serviceProvider)
    {
        _trayIcon = trayIcon;
        _screenshotService = screenshotService;
        _ocrService = ocrService;
        _translationService = translationService;
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
        SelectedTranslatedText = string.Empty;
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
        SelectedTranslatedText = region.TranslatedText;
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
    private async Task TranslateScreenshotAsync()
    {
        if (TextRegions.Count == 0)
        {
            StatusText = "No OCR text regions to translate";
            return;
        }

        if (IsProcessing)
            return;

        IsProcessing = true;
        IsTranslationProcessing = true;
        try
        {
            StatusText = $"Translating with {_settings.TranslationProvider ?? "translation provider"}...";
            ClearTranslatedRegions();

            var result = await _translationService.TranslateAsync(TextRegions.ToList());
            if (!result.Success)
            {
                TranslatedText = string.Empty;
                StatusText = $"Translation failed: {result.ErrorMessage ?? "Unknown error"}";
                return;
            }

            ApplyTranslatedRegions(result.Items);
            TranslatedText = string.Join(
                Environment.NewLine,
                result.Items.Select(item => item.TranslatedText));
            StatusText = result.Items.Count == 0
                ? "Translation complete; no foreign-language regions matched the selected model"
                : $"Translation complete; replaced {result.Items.Count} region(s)";
        }
        finally
        {
            IsTranslationProcessing = false;
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task TranslateSelectedTextAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedOcrText))
        {
            StatusText = "Select an OCR region first";
            return;
        }

        var selectedRegion = TextRegions.FirstOrDefault(region => region.Text == SelectedOcrText);
        if (selectedRegion == null)
        {
            StatusText = "Selected OCR region is no longer available";
            return;
        }

        IsTranslationProcessing = true;
        try
        {
            StatusText = "Translating selected region...";
            var result = await _translationService.TranslateAsync(new[] { selectedRegion });
            if (!result.Success)
            {
                SelectedTranslatedText = string.Empty;
                StatusText = $"Selected translation failed: {result.ErrorMessage ?? "Unknown error"}";
                return;
            }

            SelectedTranslatedText = result.Items.FirstOrDefault()?.TranslatedText ?? string.Empty;
            StatusText = string.IsNullOrWhiteSpace(SelectedTranslatedText)
                ? "Selected region did not match the selected translation model"
                : "Selected region translated";
        }
        finally
        {
            IsTranslationProcessing = false;
        }
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

        var provider = string.IsNullOrWhiteSpace(_settings.OcrProvider) ? "OCR" : _settings.OcrProvider;
        StatusText = $"Running {provider} OCR...";
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
        SelectedTranslatedText = string.Empty;
        TextRegions.Clear();
        foreach (var region in ocrResult.TextRegions)
        {
            TextRegions.Add(region);
        }

        StatusText = string.IsNullOrWhiteSpace(ExtractedText)
            ? "Screenshot captured; OCR found no text"
            : $"Screenshot captured; OCR extracted {TextRegions.Count} text regions";
    }

    private void ClearTranslatedRegions()
    {
        foreach (var region in TextRegions)
        {
            region.TranslatedText = string.Empty;
        }

        RefreshTextRegions();
    }

    private void ApplyTranslatedRegions(IReadOnlyList<TranslatedTextItem> translatedItems)
    {
        using var sourceBitmap = TryLoadLastCaptureAsBitmap();

        foreach (var item in translatedItems)
        {
            if (item.RegionIndex < 0 || item.RegionIndex >= TextRegions.Count)
            {
                continue;
            }

            var region = TextRegions[item.RegionIndex];
            region.TranslatedText = item.TranslatedText;
            region.TranslationFontSize = EstimateFontSize(region, item.TranslatedText);
            var colors = EstimateOverlayColors(sourceBitmap, region);
            region.TranslationBackground = colors.Background;
            region.TranslationForeground = colors.Foreground;
        }

        RefreshTextRegions();
    }

    private void RefreshTextRegions()
    {
        var regions = TextRegions.ToList();
        TextRegions.Clear();
        foreach (var region in regions)
        {
            TextRegions.Add(region);
        }
    }

    private DrawingBitmap? TryLoadLastCaptureAsBitmap()
    {
        if (_lastCapturedImage == null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(_lastCapturedImage);
            return new DrawingBitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static double EstimateFontSize(TextRegion region, string translatedText)
    {
        if (region.Height <= 0)
        {
            return 12;
        }

        var baseSize = Math.Max(8, region.Height * 0.72);
        if (string.IsNullOrWhiteSpace(translatedText) || region.Width <= 0)
        {
            return baseSize;
        }

        var roughWidth = translatedText.Length * baseSize * 0.55;
        if (roughWidth <= region.Width)
        {
            return baseSize;
        }

        return Math.Max(7, baseSize * region.Width / roughWidth);
    }

    private static (string Background, string Foreground) EstimateOverlayColors(DrawingBitmap? bitmap, TextRegion region)
    {
        if (bitmap == null)
        {
            return ("#DDFFFFFF", "#FF000000");
        }

        var bounds = ClampRegion(bitmap, region, padding: 2);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return ("#DDFFFFFF", "#FF000000");
        }

        long red = 0;
        long green = 0;
        long blue = 0;
        var count = 0;
        var stepX = Math.Max(1, bounds.Width / 14);
        var stepY = Math.Max(1, bounds.Height / 8);

        for (var y = bounds.Top; y < bounds.Bottom; y += stepY)
        for (var x = bounds.Left; x < bounds.Right; x += stepX)
        {
            var pixel = bitmap.GetPixel(x, y);
            red += pixel.R;
            green += pixel.G;
            blue += pixel.B;
            count++;
        }

        if (count == 0)
        {
            return ("#DDFFFFFF", "#FF000000");
        }

        var averageRed = (int)(red / count);
        var averageGreen = (int)(green / count);
        var averageBlue = (int)(blue / count);
        var luminance = 0.2126 * averageRed + 0.7152 * averageGreen + 0.0722 * averageBlue;
        var foreground = luminance > 140 ? "#FF111111" : "#FFFFFFFF";
        var background = $"#E6{averageRed:X2}{averageGreen:X2}{averageBlue:X2}";
        return (background, foreground);
    }

    private static DrawingRectangle ClampRegion(DrawingBitmap bitmap, TextRegion region, int padding)
    {
        var x = Math.Clamp(region.X - padding, 0, bitmap.Width - 1);
        var y = Math.Clamp(region.Y - padding, 0, bitmap.Height - 1);
        var right = Math.Clamp(region.X + region.Width + padding, x + 1, bitmap.Width);
        var bottom = Math.Clamp(region.Y + region.Height + padding, y + 1, bitmap.Height);
        return DrawingRectangle.FromLTRB(x, y, right, bottom);
    }
}
