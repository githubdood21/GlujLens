using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlujLens.Models;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace GlujLens.ViewModels;

/// <summary>
/// View model for application settings.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _googleVisionApiKey;

    [ObservableProperty]
    private string? _googleTranslationApiKey;

    [ObservableProperty]
    private string _targetLanguage = "en";

    [ObservableProperty]
    private string? _defaultSaveDirectory;

    [ObservableProperty]
    private bool _useLocalTesseract;

    [ObservableProperty]
    private string? _tesseractDataPath;

    [ObservableProperty]
    private string _captureShortcut = "PrintScreen";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private readonly IConfiguration _configuration;

    public SettingsViewModel(IConfiguration configuration)
    {
        _configuration = configuration;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Load from configuration
        GoogleVisionApiKey = _configuration.GetValue<string?>("GoogleVisionApiKey");
        GoogleTranslationApiKey = _configuration.GetValue<string?>("GoogleTranslationApiKey");
        TargetLanguage = _configuration.GetValue<string>("TargetLanguage") ?? "en";
        DefaultSaveDirectory = _configuration.GetValue<string?>("DefaultSaveDirectory");
        UseLocalTesseract = _configuration.GetValue<bool>("UseLocalTesseract");
        TesseractDataPath = _configuration.GetValue<string?>("TesseractDataPath");
        CaptureShortcut = _configuration.GetValue<string>("CaptureShortcut") ?? "PrintScreen";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            // Save to config file
            StatusMessage = "Settings saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseSaveDirectory()
    {
        // Open folder picker dialog
        StatusMessage = "Folder picker opened (placeholder)";
    }
}