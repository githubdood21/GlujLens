using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GlujLens.Models;

/// <summary>
/// Application settings stored in JSON config. Provides Load/Save/Reload methods.
/// </summary>
public class AppSettings : INotifyPropertyChanged
{
    private readonly string _settingsFilePath;
    private string? _defaultSaveDirectory;
    private string? _captureShortcut;
    private string? _hardwareAcceleration;
    private string? _imageFormat;
    private int _imageQuality;
    private bool _copyToClipboardAfterCapture;
    private bool _showNotificationAfterCapture;
    private bool _useLocalTesseract;
    private string? _tesseractDataPath;
    private string? _googleVisionApiKey;
    private string? _googleTranslationApiKey;
    private string? _sourceLanguage;
    private string? _targetLanguage;

    public AppSettings()
    {
        _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        // Initialize with defaults - do NOT call Load() in constructor
        _defaultSaveDirectory = null;
        _captureShortcut = "B";
        _hardwareAcceleration = "Auto";
        _imageFormat = "PNG";
        _imageQuality = 90;
        _copyToClipboardAfterCapture = false;
        _showNotificationAfterCapture = true;
        _useLocalTesseract = false;
        _tesseractDataPath = null;
        _googleVisionApiKey = null;
        _googleTranslationApiKey = null;
        _sourceLanguage = "en-US";
        _targetLanguage = "en-US";
    }

    /// <summary>
    /// Loads settings from the settings.json file. Throws on error.
    /// </summary>
    public void Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            Save(); // Create file with defaults
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var dto = System.Text.Json.JsonSerializer.Deserialize<SettingsDto>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto != null)
            {
                DefaultSaveDirectory = dto.DefaultSaveDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                CaptureShortcut = dto.CaptureShortcut ?? "B";
                HardwareAcceleration = dto.HardwareAcceleration ?? "Auto";
                ImageFormat = dto.ImageFormat ?? "PNG";
                ImageQuality = dto.ImageQuality;
                CopyToClipboardAfterCapture = dto.CopyToClipboardAfterCapture;
                ShowNotificationAfterCapture = dto.ShowNotificationAfterCapture;
                UseLocalTesseract = dto.UseLocalTesseract;
                TesseractDataPath = dto.TesseractDataPath;
                GoogleVisionApiKey = dto.GoogleVisionApiKey;
                GoogleTranslationApiKey = dto.GoogleTranslationApiKey;
                SourceLanguage = dto.SourceLanguage ?? "en-US";
                TargetLanguage = dto.TargetLanguage ?? "en-US";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            // Keep current values
        }
    }

    /// <summary>
    /// Saves current settings to the settings.json file. Throws on error.
    /// </summary>
    public void Save()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Reloads settings from file and copies them to existing instance.
    /// </summary>
    public void Reload()
    {
        var backup = new SettingsDto
        {
            DefaultSaveDirectory = DefaultSaveDirectory,
            CaptureShortcut = CaptureShortcut,
            HardwareAcceleration = HardwareAcceleration,
            ImageFormat = ImageFormat,
            ImageQuality = ImageQuality,
            CopyToClipboardAfterCapture = CopyToClipboardAfterCapture,
            ShowNotificationAfterCapture = ShowNotificationAfterCapture,
            UseLocalTesseract = UseLocalTesseract,
            TesseractDataPath = TesseractDataPath,
            GoogleVisionApiKey = GoogleVisionApiKey,
            GoogleTranslationApiKey = GoogleTranslationApiKey,
            SourceLanguage = SourceLanguage,
            TargetLanguage = TargetLanguage
        };

        try
        {
            Load();
        }
        catch
        {
            // Restore backup on failure
            DefaultSaveDirectory = backup.DefaultSaveDirectory;
            CaptureShortcut = backup.CaptureShortcut;
            HardwareAcceleration = backup.HardwareAcceleration;
            ImageFormat = backup.ImageFormat;
            ImageQuality = backup.ImageQuality;
            CopyToClipboardAfterCapture = backup.CopyToClipboardAfterCapture;
            ShowNotificationAfterCapture = backup.ShowNotificationAfterCapture;
            UseLocalTesseract = backup.UseLocalTesseract;
            TesseractDataPath = backup.TesseractDataPath;
            GoogleVisionApiKey = backup.GoogleVisionApiKey;
            GoogleTranslationApiKey = backup.GoogleTranslationApiKey;
            SourceLanguage = backup.SourceLanguage;
            TargetLanguage = backup.TargetLanguage;
        }
    }

    public string? DefaultSaveDirectory
    {
        get => _defaultSaveDirectory;
        set { _defaultSaveDirectory = value; OnPropertyChanged(); }
    }

    public string? CaptureShortcut
    {
        get => _captureShortcut;
        set { _captureShortcut = value; OnPropertyChanged(); }
    }

    public string? HardwareAcceleration
    {
        get => _hardwareAcceleration;
        set { _hardwareAcceleration = value; OnPropertyChanged(); }
    }

    public string? ImageFormat
    {
        get => _imageFormat;
        set { _imageFormat = value; OnPropertyChanged(); }
    }

    public int ImageQuality
    {
        get => _imageQuality;
        set { _imageQuality = value; OnPropertyChanged(); }
    }

    public bool CopyToClipboardAfterCapture
    {
        get => _copyToClipboardAfterCapture;
        set { _copyToClipboardAfterCapture = value; OnPropertyChanged(); }
    }

    public bool ShowNotificationAfterCapture
    {
        get => _showNotificationAfterCapture;
        set { _showNotificationAfterCapture = value; OnPropertyChanged(); }
    }

    public bool UseLocalTesseract
    {
        get => _useLocalTesseract;
        set { _useLocalTesseract = value; OnPropertyChanged(); }
    }

    public string? TesseractDataPath
    {
        get => _tesseractDataPath;
        set { _tesseractDataPath = value; OnPropertyChanged(); }
    }

    public string? GoogleVisionApiKey
    {
        get => _googleVisionApiKey;
        set { _googleVisionApiKey = value; OnPropertyChanged(); }
    }

    public string? GoogleTranslationApiKey
    {
        get => _googleTranslationApiKey;
        set { _googleTranslationApiKey = value; OnPropertyChanged(); }
    }

    public string? SourceLanguage
    {
        get => _sourceLanguage;
        set { _sourceLanguage = value; OnPropertyChanged(); }
    }

    public string? TargetLanguage
    {
        get => _targetLanguage;
        set { _targetLanguage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// DTO for deserializing settings from JSON (avoids circular constructor calls).
/// </summary>
internal class SettingsDto
{
    public string? DefaultSaveDirectory { get; set; }
    public string? CaptureShortcut { get; set; }
    public string? HardwareAcceleration { get; set; }
    public string? ImageFormat { get; set; }
    public int ImageQuality { get; set; }
    public bool CopyToClipboardAfterCapture { get; set; }
    public bool ShowNotificationAfterCapture { get; set; }
    public bool UseLocalTesseract { get; set; }
    public string? TesseractDataPath { get; set; }
    public string? GoogleVisionApiKey { get; set; }
    public string? GoogleTranslationApiKey { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
}

/// <summary>
/// Available hardware acceleration methods.
/// </summary>
public enum HardwareAccelerationMethod
{
    GPU,
    CPU_AVX,
    CPU_NO_AVX
}