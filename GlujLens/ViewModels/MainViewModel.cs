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
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _appName = "GlujLens";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isProcessing;

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
    private string _captureDuration = string.Empty;

    private byte[]? _lastCapturedImage;

    private readonly IServiceProvider _serviceProvider;

    public MainViewModel(ITrayIconService trayIcon, IScreenshotService screenshotService, AppSettings settings, IServiceProvider serviceProvider)
    {
        _trayIcon = trayIcon;
        _screenshotService = screenshotService;
        _settings = settings;
        _serviceProvider = serviceProvider;

        // Wire up tray icon menu item clicks
        _trayIcon.MenuItemClicked += OnMenuItemClicked;
        _trayIcon.IconDoubleClicked += OnIconDoubleClicked;
    }

    ~MainViewModel()
    {
        _trayIcon.MenuItemClicked -= OnMenuItemClicked;
        _trayIcon.IconDoubleClicked -= OnIconDoubleClicked;
    }

    [RelayCommand]
    private async Task CaptureAsync()
    {
        if (IsProcessing) return;

        IsProcessing = true;
        StatusText = "Capturing screenshot...";
        CaptureDuration = string.Empty;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await _screenshotService.CaptureFullScreenAsync();
            stopwatch.Stop();

            if (result.Success && result.ImageData != null)
            {
                _lastCapturedImage = result.ImageData;

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

                StatusText = $"Screenshot captured successfully ({result.Width}x{result.Height})";
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
        _lastCapturedImage = null;
        StatusText = "Ready";
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
        IsWindowVisible = true;
        StatusText = "Window shown";
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
            var settingsWindow = new SettingsView(settingsViewModel);
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
    /// </summary>
    public void ReloadSettings()
    {
        try
        {
            _settings.Reload();
            StatusText = $"Settings reloaded. Capture shortcut: {_settings.CaptureShortcut ?? "Not set"}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to reload settings: {ex.Message}";
        }
    }

    /// <summary>
    /// Refreshes the hotkey from current settings.
    /// Called by MainWindow when settings change.
    /// </summary>
    public void RefreshHotkey()
    {
        StatusText = $"Hotkey refreshed. Capture shortcut: {_settings.CaptureShortcut ?? "Not set"}";
    }
}
