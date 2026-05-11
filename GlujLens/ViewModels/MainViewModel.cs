using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlujLens.Models;
using GlujLens.Services;
using System.Collections.ObjectModel;
using System.Windows.Forms;

namespace GlujLens.ViewModels;

/// <summary>
/// Main view model for the application. Handles the primary state and commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ITrayIconService _trayIcon;

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

    public MainViewModel(ITrayIconService trayIcon)
    {
        _trayIcon = trayIcon;

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
    private void ClearResults()
    {
        DetectedObjects.Clear();
        TextRegions.Clear();
        ExtractedText = string.Empty;
        TranslatedText = string.Empty;
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

        StatusText = $"Translating to {TargetLanguage}...";
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
    private void CaptureScreenshot()
    {
        StatusText = "Capturing screenshot...";
        // Screenshot capture will be implemented later
        StatusText = "Screenshot captured (placeholder)";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        StatusText = "Settings opened (placeholder)";
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
                CaptureScreenshotCommand.Execute(null);
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
                CaptureScreenshotCommand.Execute(null);
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
}
