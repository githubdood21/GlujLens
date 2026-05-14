using System.Windows.Forms;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlujLens.Models;

namespace GlujLens.ViewModels;

/// <summary>
/// View model for the settings window. Changes are written to AppSettings immediately.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private bool _isRecordingShortcut;
    private bool _isDisposed;
    private bool _isInitializing;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        _isInitializing = true;

        // Initialize from settings
        DefaultSaveDirectory = settings.DefaultSaveDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        CaptureShortcut = settings.CaptureShortcut ?? "Alt+Ctrl+Q";
        RecordedShortcutText = CaptureShortcut;
        ImageFormat = settings.ImageFormat ?? "PNG";
        ImageQuality = settings.ImageQuality;
        CopyToClipboardAfterCapture = settings.CopyToClipboardAfterCapture;
        ShowNotificationAfterCapture = settings.ShowNotificationAfterCapture;
        OcrProvider = settings.OcrProvider ?? "Tesseract";
        TesseractDataPath = settings.TesseractDataPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        GoogleVisionApiKey = settings.GoogleVisionApiKey ?? string.Empty;
        PaddleOcrModelPath = settings.PaddleOcrModelPath ?? string.Empty;
        SourceLanguage = settings.SourceLanguage ?? "en-US";
        TargetLanguage = settings.TargetLanguage ?? "en-US";
        TesseractHorizontalMergeGap = settings.TesseractHorizontalMergeGap;
        TesseractVerticalMergeTolerance = settings.TesseractVerticalMergeTolerance;
        RefreshTesseractLanguages();
        StatusMessage = "Settings are saved automatically.";
        _isInitializing = false;
    }

    [ObservableProperty]
    private string _defaultSaveDirectory;

    [ObservableProperty]
    private string _captureShortcut;

    [ObservableProperty]
    private string _recordedShortcutText;

    [ObservableProperty]
    private string _imageFormat;

    [ObservableProperty]
    private int _imageQuality;

    [ObservableProperty]
    private bool _copyToClipboardAfterCapture;

    [ObservableProperty]
    private bool _showNotificationAfterCapture;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private string _ocrProvider;

    [ObservableProperty]
    private string _tesseractDataPath;

    [ObservableProperty]
    private string _googleVisionApiKey;

    [ObservableProperty]
    private string _paddleOcrModelPath;

    [ObservableProperty]
    private string _sourceLanguage;

    [ObservableProperty]
    private string? _selectedTesseractLanguage;

    [ObservableProperty]
    private string _targetLanguage;

    [ObservableProperty]
    private int _tesseractHorizontalMergeGap;

    [ObservableProperty]
    private int _tesseractVerticalMergeTolerance;

    public ObservableCollection<string> AvailableTesseractLanguages { get; } = new();

    public bool IsTesseractProvider => OcrProvider == "Tesseract";

    public bool IsGoogleVisionProvider => OcrProvider == "Google Vision";

    public bool IsPaddleOcrProvider => OcrProvider == "PaddleOCR";

    public string[] ImageFormatOptions => new[] { "PNG", "JPEG", "BMP" };
    public int[] ImageQualityOptions => new[] { 10, 25, 50, 75, 90, 95, 100 };
    public string[] OcrProviderOptions => new[] { "Disabled", "Tesseract", "Google Vision", "PaddleOCR" };

    public bool IsRecordingShortcut
    {
        get => _isRecordingShortcut;
        private set
        {
            if (_isRecordingShortcut != value)
            {
                _isRecordingShortcut = value;
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void BrowseSaveDirectory()
    {
        using var dialog = new FolderBrowserDialog();
        if (string.IsNullOrEmpty(DefaultSaveDirectory) || !Directory.Exists(DefaultSaveDirectory))
        {
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }
        else
        {
            dialog.InitialDirectory = DefaultSaveDirectory;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            DefaultSaveDirectory = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void BrowseTesseractDataPath()
    {
        using var dialog = new FolderBrowserDialog();
        if (string.IsNullOrEmpty(TesseractDataPath) || !Directory.Exists(TesseractDataPath))
        {
            dialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }
        else
        {
            dialog.InitialDirectory = TesseractDataPath;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            TesseractDataPath = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void RefreshTesseractLanguages()
    {
        var previousSelection = SelectedTesseractLanguage;
        var savedLanguage = SourceLanguage?.Trim();
        var mappedSavedLanguage = MapLanguageToTesseractCode(SourceLanguage);
        AvailableTesseractLanguages.Clear();

        if (Directory.Exists(TesseractDataPath))
        {
            var languages = Directory
                .EnumerateFiles(TesseractDataPath, "*.traineddata", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(language => !string.IsNullOrWhiteSpace(language))
                .Select(language => language!)
                .OrderBy(language => language, StringComparer.OrdinalIgnoreCase);

            foreach (var language in languages)
            {
                AvailableTesseractLanguages.Add(language);
            }
        }

        if (!string.IsNullOrWhiteSpace(savedLanguage) && AvailableTesseractLanguages.Contains(savedLanguage))
        {
            SelectedTesseractLanguage = savedLanguage;
        }
        else if (AvailableTesseractLanguages.Contains(mappedSavedLanguage))
        {
            SelectedTesseractLanguage = mappedSavedLanguage;
        }
        else if (previousSelection != null && AvailableTesseractLanguages.Contains(previousSelection))
        {
            SelectedTesseractLanguage = previousSelection;
        }
        else
        {
            SelectedTesseractLanguage = AvailableTesseractLanguages.FirstOrDefault();
        }

        StatusMessage = AvailableTesseractLanguages.Count == 0
            ? "No Tesseract traineddata files found."
            : $"Found {AvailableTesseractLanguages.Count} Tesseract language file(s).";
    }

    [RelayCommand]
    private void BrowsePaddleOcrModelPath()
    {
        using var dialog = new FolderBrowserDialog();
        if (string.IsNullOrEmpty(PaddleOcrModelPath) || !Directory.Exists(PaddleOcrModelPath))
        {
            dialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }
        else
        {
            dialog.InitialDirectory = PaddleOcrModelPath;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            PaddleOcrModelPath = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void StartRecordingShortcut()
    {
        IsRecordingShortcut = true;
        RecordedShortcutText = "Recording...";
    }

    [RelayCommand]
    private void StopRecordingShortcut()
    {
        CancelRecording();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        DefaultSaveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        CaptureShortcut = "Alt+Ctrl+Q";
        RecordedShortcutText = CaptureShortcut;
        ImageFormat = "PNG";
        ImageQuality = 90;
        CopyToClipboardAfterCapture = false;
        ShowNotificationAfterCapture = true;
        OcrProvider = "Tesseract";
        TesseractDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        RefreshTesseractLanguages();
        GoogleVisionApiKey = string.Empty;
        PaddleOcrModelPath = string.Empty;
        SourceLanguage = SelectedTesseractLanguage ?? "eng";
        TargetLanguage = "en-US";
        TesseractHorizontalMergeGap = AppSettings.DefaultTesseractHorizontalMergeGap;
        TesseractVerticalMergeTolerance = AppSettings.DefaultTesseractVerticalMergeTolerance;
        IsRecordingShortcut = false;
    }

    /// <summary>
    /// Called when a key is pressed during recording.
    /// </summary>
    public void OnKeyCaptured(string keyName)
    {
        if (_isRecordingShortcut)
        {
            var shortcut = NormalizeShortcut(keyName);
            CaptureShortcut = shortcut;
            RecordedShortcutText = shortcut;
            IsRecordingShortcut = false;
        }
    }

    /// <summary>
    /// Cancels recording and restores original value.
    /// </summary>
    public void CancelRecording()
    {
        if (_isRecordingShortcut)
        {
            RecordedShortcutText = CaptureShortcut;
            IsRecordingShortcut = false;
        }
    }

    /// <summary>
    /// Saves all current view model values to AppSettings and persists to file.
    /// Returns false on error.
    /// </summary>
    public bool Save()
    {
        try
        {
            _settings.DefaultSaveDirectory = DefaultSaveDirectory;
            _settings.CaptureShortcut = CaptureShortcut;
            _settings.ImageFormat = ImageFormat;
            _settings.ImageQuality = ImageQuality;
            _settings.CopyToClipboardAfterCapture = CopyToClipboardAfterCapture;
            _settings.ShowNotificationAfterCapture = ShowNotificationAfterCapture;
            _settings.OcrProvider = OcrProvider;
            _settings.UseLocalTesseract = OcrProvider == "Tesseract";
            _settings.TesseractDataPath = TesseractDataPath;
            _settings.GoogleVisionApiKey = string.IsNullOrWhiteSpace(GoogleVisionApiKey) ? null : GoogleVisionApiKey;
            _settings.PaddleOcrModelPath = string.IsNullOrWhiteSpace(PaddleOcrModelPath) ? null : PaddleOcrModelPath;
            _settings.SourceLanguage = SourceLanguage;
            _settings.TargetLanguage = TargetLanguage;
            _settings.TesseractHorizontalMergeGap = TesseractHorizontalMergeGap;
            _settings.TesseractVerticalMergeTolerance = TesseractVerticalMergeTolerance;
            _settings.Save();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            return false;
        }
    }

    partial void OnDefaultSaveDirectoryChanged(string value) => SaveIfReady();

    partial void OnCaptureShortcutChanged(string value)
    {
        if (!_isRecordingShortcut && RecordedShortcutText != value)
        {
            RecordedShortcutText = value;
        }

        SaveIfReady();
    }

    partial void OnRecordedShortcutTextChanged(string value)
    {
        var shortcut = NormalizeShortcut(value);
        if (!_isRecordingShortcut && CaptureShortcut != shortcut)
        {
            CaptureShortcut = shortcut;
        }
    }

    partial void OnImageFormatChanged(string value) => SaveIfReady();

    partial void OnImageQualityChanged(int value) => SaveIfReady();

    partial void OnCopyToClipboardAfterCaptureChanged(bool value) => SaveIfReady();

    partial void OnShowNotificationAfterCaptureChanged(bool value) => SaveIfReady();

    partial void OnOcrProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsTesseractProvider));
        OnPropertyChanged(nameof(IsGoogleVisionProvider));
        OnPropertyChanged(nameof(IsPaddleOcrProvider));
        SaveIfReady();
    }

    partial void OnTesseractDataPathChanged(string value)
    {
        RefreshTesseractLanguages();
        SaveIfReady();
    }

    partial void OnGoogleVisionApiKeyChanged(string value) => SaveIfReady();

    partial void OnPaddleOcrModelPathChanged(string value) => SaveIfReady();

    partial void OnSourceLanguageChanged(string value)
    {
        var tesseractLanguage = MapLanguageToTesseractCode(value);
        if (AvailableTesseractLanguages.Contains(tesseractLanguage) && SelectedTesseractLanguage != tesseractLanguage)
        {
            SelectedTesseractLanguage = tesseractLanguage;
        }

        SaveIfReady();
    }

    partial void OnSelectedTesseractLanguageChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && SourceLanguage != value)
        {
            SourceLanguage = value;
        }
    }

    partial void OnTargetLanguageChanged(string value) => SaveIfReady();

    partial void OnTesseractHorizontalMergeGapChanged(int value) => SaveIfReady();

    partial void OnTesseractVerticalMergeToleranceChanged(int value) => SaveIfReady();

    private void SaveIfReady()
    {
        if (_isInitializing || _isDisposed)
            return;

        StatusMessage = Save()
            ? $"Saved at {DateTime.Now:T}"
            : "Failed to save settings.";
    }

    private static string NormalizeShortcut(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return "Alt+Ctrl+Q";

        var tokens = shortcut
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();

        if (tokens.Count <= 3)
            return string.Join("+", tokens);

        var mainKey = tokens[^1];
        return string.Join("+", tokens.Take(2).Append(mainKey));
    }

    private static string MapLanguageToTesseractCode(string? language)
    {
        return (language ?? "eng").Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" or "en-gb" => "eng",
            "de" or "de-de" => "deu",
            "pl" or "pl-pl" => "pol",
            "ru" or "ru-ru" => "rus",
            "ja" or "ja-jp" => "jpn",
            "es" or "es-es" or "es-mx" => "spa",
            "fr" or "fr-fr" => "fra",
            "it" or "it-it" => "ita",
            "pt" or "pt-br" or "pt-pt" => "por",
            var value when value.Length == 3 => value,
            var value when !string.IsNullOrWhiteSpace(value) => value,
            _ => "eng"
        };
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            // Note: CancelRecording should be called by SettingsView.OnClosed before Dispose
            _isDisposed = true;
        }
    }
}
