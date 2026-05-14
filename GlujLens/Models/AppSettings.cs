using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GlujLens.Models;

/// <summary>
/// Application settings stored in JSON config. Provides Load/Save/Reload methods.
/// </summary>
public class AppSettings : INotifyPropertyChanged
{
    public const int DefaultTesseractHorizontalMergeGap = 32;
    public const int DefaultTesseractVerticalMergeTolerance = 12;

    private readonly string _settingsFilePath;
    private string? _defaultSaveDirectory;
    private string? _captureShortcut;
    private string? _hardwareAcceleration;
    private string? _imageFormat;
    private int _imageQuality;
    private bool _copyToClipboardAfterCapture;
    private bool _showNotificationAfterCapture;
    private bool _useLocalTesseract;
    private string? _ocrProvider;
    private string? _tesseractDataPath;
    private string? _paddleOcrModelPath;
    private string? _googleVisionApiKey;
    private string? _googleTranslationApiKey;
    private string? _translationProvider;
    private string? _bergamotModelsDirectory;
    private string? _bergamotModelPath;
    private string? _cTranslate2ModelPath;
    private string? _sourceLanguage;
    private string? _targetLanguage;
    private int _tesseractHorizontalMergeGap;
    private int _tesseractVerticalMergeTolerance;

    public AppSettings()
    {
        _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        // Initialize with defaults - startup calls Load() once the app is ready.
        _defaultSaveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        _captureShortcut = "Alt+Ctrl+Q";
        _hardwareAcceleration = "Auto";
        _imageFormat = "PNG";
        _imageQuality = 90;
        _copyToClipboardAfterCapture = false;
        _showNotificationAfterCapture = true;
        _useLocalTesseract = false;
        _ocrProvider = "Tesseract";
        _tesseractDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        _paddleOcrModelPath = null;
        _googleVisionApiKey = null;
        _googleTranslationApiKey = null;
        _translationProvider = "Disabled";
        _bergamotModelsDirectory = null;
        _bergamotModelPath = null;
        _cTranslate2ModelPath = null;
        _sourceLanguage = "en-US";
        _targetLanguage = "en-US";
        _tesseractHorizontalMergeGap = DefaultTesseractHorizontalMergeGap;
        _tesseractVerticalMergeTolerance = DefaultTesseractVerticalMergeTolerance;
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
                CaptureShortcut = dto.CaptureShortcut ?? "Alt+Ctrl+Q";
                HardwareAcceleration = dto.HardwareAcceleration ?? "Auto";
                ImageFormat = dto.ImageFormat ?? "PNG";
                ImageQuality = dto.ImageQuality ?? 90;
                CopyToClipboardAfterCapture = dto.CopyToClipboardAfterCapture ?? false;
                ShowNotificationAfterCapture = dto.ShowNotificationAfterCapture ?? true;
                UseLocalTesseract = dto.UseLocalTesseract ?? false;
                OcrProvider = dto.OcrProvider ?? "Tesseract";
                TesseractDataPath = dto.TesseractDataPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                PaddleOcrModelPath = dto.PaddleOcrModelPath;
                GoogleVisionApiKey = dto.GoogleVisionApiKey;
                GoogleTranslationApiKey = dto.GoogleTranslationApiKey;
                TranslationProvider = dto.TranslationProvider ?? "Disabled";
                BergamotModelsDirectory = dto.BergamotModelsDirectory;
                BergamotModelPath = dto.BergamotModelPath;
                CTranslate2ModelPath = dto.CTranslate2ModelPath;
                SourceLanguage = dto.SourceLanguage ?? "en-US";
                TargetLanguage = dto.TargetLanguage ?? "en-US";
                TesseractHorizontalMergeGap = dto.TesseractHorizontalMergeGap ?? DefaultTesseractHorizontalMergeGap;
                TesseractVerticalMergeTolerance = dto.TesseractVerticalMergeTolerance ?? DefaultTesseractVerticalMergeTolerance;
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
            OcrProvider = OcrProvider,
            TesseractDataPath = TesseractDataPath,
            PaddleOcrModelPath = PaddleOcrModelPath,
            GoogleVisionApiKey = GoogleVisionApiKey,
            GoogleTranslationApiKey = GoogleTranslationApiKey,
            TranslationProvider = TranslationProvider,
            BergamotModelsDirectory = BergamotModelsDirectory,
            BergamotModelPath = BergamotModelPath,
            CTranslate2ModelPath = CTranslate2ModelPath,
            SourceLanguage = SourceLanguage,
            TargetLanguage = TargetLanguage,
            TesseractHorizontalMergeGap = TesseractHorizontalMergeGap,
            TesseractVerticalMergeTolerance = TesseractVerticalMergeTolerance
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
            ImageQuality = backup.ImageQuality ?? 90;
            CopyToClipboardAfterCapture = backup.CopyToClipboardAfterCapture ?? false;
            ShowNotificationAfterCapture = backup.ShowNotificationAfterCapture ?? true;
            UseLocalTesseract = backup.UseLocalTesseract ?? false;
            OcrProvider = backup.OcrProvider;
            TesseractDataPath = backup.TesseractDataPath;
            PaddleOcrModelPath = backup.PaddleOcrModelPath;
            GoogleVisionApiKey = backup.GoogleVisionApiKey;
            GoogleTranslationApiKey = backup.GoogleTranslationApiKey;
            TranslationProvider = backup.TranslationProvider;
            BergamotModelsDirectory = backup.BergamotModelsDirectory;
            BergamotModelPath = backup.BergamotModelPath;
            CTranslate2ModelPath = backup.CTranslate2ModelPath;
            SourceLanguage = backup.SourceLanguage;
            TargetLanguage = backup.TargetLanguage;
            TesseractHorizontalMergeGap = backup.TesseractHorizontalMergeGap ?? DefaultTesseractHorizontalMergeGap;
            TesseractVerticalMergeTolerance = backup.TesseractVerticalMergeTolerance ?? DefaultTesseractVerticalMergeTolerance;
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

    public string? OcrProvider
    {
        get => _ocrProvider;
        set { _ocrProvider = value; OnPropertyChanged(); }
    }

    public string? TesseractDataPath
    {
        get => _tesseractDataPath;
        set { _tesseractDataPath = value; OnPropertyChanged(); }
    }

    public string? PaddleOcrModelPath
    {
        get => _paddleOcrModelPath;
        set { _paddleOcrModelPath = value; OnPropertyChanged(); }
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

    public string? TranslationProvider
    {
        get => _translationProvider;
        set { _translationProvider = value; OnPropertyChanged(); }
    }

    public string? BergamotModelsDirectory
    {
        get => _bergamotModelsDirectory;
        set { _bergamotModelsDirectory = value; OnPropertyChanged(); }
    }

    public string? BergamotModelPath
    {
        get => _bergamotModelPath;
        set { _bergamotModelPath = value; OnPropertyChanged(); }
    }

    public string? CTranslate2ModelPath
    {
        get => _cTranslate2ModelPath;
        set { _cTranslate2ModelPath = value; OnPropertyChanged(); }
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

    public int TesseractHorizontalMergeGap
    {
        get => _tesseractHorizontalMergeGap;
        set { _tesseractHorizontalMergeGap = Math.Clamp(value, 0, 200); OnPropertyChanged(); }
    }

    public int TesseractVerticalMergeTolerance
    {
        get => _tesseractVerticalMergeTolerance;
        set { _tesseractVerticalMergeTolerance = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
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
    public int? ImageQuality { get; set; }
    public bool? CopyToClipboardAfterCapture { get; set; }
    public bool? ShowNotificationAfterCapture { get; set; }
    public bool? UseLocalTesseract { get; set; }
    public string? OcrProvider { get; set; }
    public string? TesseractDataPath { get; set; }
    public string? PaddleOcrModelPath { get; set; }
    public string? GoogleVisionApiKey { get; set; }
    public string? GoogleTranslationApiKey { get; set; }
    public string? TranslationProvider { get; set; }
    public string? BergamotModelsDirectory { get; set; }
    public string? BergamotModelPath { get; set; }
    public string? CTranslate2ModelPath { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
    public int? TesseractHorizontalMergeGap { get; set; }
    public int? TesseractVerticalMergeTolerance { get; set; }
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
