using System.Windows.Forms;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlujLens.Models;
using GlujLens.Services;

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
        TesseractDataPath = settings.TesseractDataPath ?? ModelStoragePaths.TesseractDirectory;
        GoogleVisionApiKey = settings.GoogleVisionApiKey ?? string.Empty;
        GoogleTranslationApiKey = settings.GoogleTranslationApiKey ?? string.Empty;
        TranslationProvider = settings.TranslationProvider ?? "Disabled";
        BergamotModelsDirectory = settings.BergamotModelsDirectory
            ?? ResolveDefaultBergamotModelsDirectory(settings.BergamotModelPath);
        BergamotModelPath = settings.BergamotModelPath ?? string.Empty;
        DirectMlOnnxModelPath = settings.DirectMlOnnxModelPath ?? ModelStoragePaths.DirectMlOnnxDirectory;
        MlNetOcrModelPath = settings.MlNetOcrModelPath ?? ModelStoragePaths.MlNetOcrDirectory;
        MlNetOcrAccelerator = settings.MlNetOcrAccelerator ?? "Auto";
        SourceLanguage = settings.SourceLanguage ?? "en-US";
        TranslationSourceLanguage = settings.TranslationSourceLanguage ?? "auto";
        TargetLanguage = settings.TargetLanguage ?? "en-US";
        TesseractHorizontalMergeGap = settings.TesseractHorizontalMergeGap;
        TesseractVerticalMergeTolerance = settings.TesseractVerticalMergeTolerance;
        RefreshBergamotModels();
        RefreshMlNetOcrModels();
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
    private string _googleTranslationApiKey;

    [ObservableProperty]
    private string _translationProvider;

    [ObservableProperty]
    private string _bergamotModelsDirectory;

    [ObservableProperty]
    private string? _selectedBergamotModel;

    [ObservableProperty]
    private string _bergamotModelPath;

    [ObservableProperty]
    private string _directMlOnnxModelPath;

    [ObservableProperty]
    private string _mlNetOcrModelPath;

    [ObservableProperty]
    private string? _selectedMlNetOcrModel;

    [ObservableProperty]
    private string _mlNetOcrAccelerator;

    [ObservableProperty]
    private string _sourceLanguage;

    [ObservableProperty]
    private string _translationSourceLanguage;

    [ObservableProperty]
    private string? _selectedTesseractLanguage;

    [ObservableProperty]
    private string _targetLanguage;

    [ObservableProperty]
    private int _tesseractHorizontalMergeGap;

    [ObservableProperty]
    private int _tesseractVerticalMergeTolerance;

    public ObservableCollection<string> AvailableTesseractLanguages { get; } = new();

    public ObservableCollection<string> AvailableBergamotModels { get; } = new();

    public ObservableCollection<string> AvailableMlNetOcrModels { get; } = new();

    public bool IsTesseractProvider => OcrProvider == "Tesseract";

    public bool IsGoogleVisionProvider => OcrProvider == "Google Vision";

    public bool IsMlNetOcrProvider => OcrProvider == "ML.NET OCR";

    public bool IsTranslationEnabled => TranslationProvider != "Disabled";

    public bool IsBergamotTranslationProvider => TranslationProvider == "Bergamot";

    public bool IsDirectMlOnnxTranslationProvider => TranslationProvider == "DirectML ONNX";

    public bool IsGoogleApiTranslationProvider => TranslationProvider == "Google API";

    public bool ShowsTranslationLanguageFields => IsDirectMlOnnxTranslationProvider || IsGoogleApiTranslationProvider;

    public string SelectedBergamotLanguagePair
    {
        get
        {
            var modelName = SelectedBergamotModel ?? Path.GetFileName(BergamotModelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var languagePair = ExtractLanguagePair(modelName);
            return string.IsNullOrWhiteSpace(languagePair)
                ? "Language pair is determined by the selected model."
                : $"Model language pair: {languagePair}";
        }
    }

    public string[] ImageFormatOptions => new[] { "PNG", "JPEG", "BMP" };
    public int[] ImageQualityOptions => new[] { 10, 25, 50, 75, 90, 95, 100 };
    public string[] OcrProviderOptions => new[] { "Disabled", "Tesseract", "ML.NET OCR", "Google Vision" };
    public string[] MlNetOcrAcceleratorOptions => new[] { "Auto", "CPU", "DirectML" };
    public string[] TranslationProviderOptions => new[] { "Disabled", "Bergamot", "DirectML ONNX", "Google API" };
    public string MlNetOcrAutoAcceleratorStatus
    {
        get
        {
            var resolved = _settings.MlNetOcrAutoAccelerator;
            var reason = _settings.MlNetOcrAutoAcceleratorReason;
            return string.IsNullOrWhiteSpace(resolved)
                ? "Auto benchmark has not run yet."
                : $"Auto resolved to {resolved}. {reason}";
        }
    }

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
    private void BrowseMlNetOcrModelPath()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.Description = "Select the ML.NET OCR model folder";
        dialog.InitialDirectory = string.IsNullOrEmpty(MlNetOcrModelPath) || !Directory.Exists(MlNetOcrModelPath)
            ? ModelStoragePaths.MlNetOcrDirectory
            : MlNetOcrModelPath;

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            MlNetOcrModelPath = dialog.SelectedPath;
            RefreshMlNetOcrModels();
        }
    }

    [RelayCommand]
    private void RefreshMlNetOcrModels()
    {
        var previousSelection = SelectedMlNetOcrModel;
        AvailableMlNetOcrModels.Clear();

        var rootDirectory = ResolveMlNetOcrRootDirectory();
        if (Directory.Exists(rootDirectory))
        {
            var modelPaths = ModelFolderScanner.FindModelEntries(
                rootDirectory,
                IsMlNetOcrModelPath,
                SearchOption.TopDirectoryOnly,
                includeFiles: true);

            foreach (var modelPath in modelPaths)
            {
                AvailableMlNetOcrModels.Add(GetMlNetOcrModelDisplayName(modelPath, rootDirectory));
            }
        }

        if (!string.IsNullOrWhiteSpace(MlNetOcrModelPath))
        {
            var savedSelection = GetMlNetOcrModelDisplayName(MlNetOcrModelPath, rootDirectory);
            if (AvailableMlNetOcrModels.Contains(savedSelection))
            {
                SelectedMlNetOcrModel = savedSelection;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(previousSelection) && AvailableMlNetOcrModels.Contains(previousSelection))
        {
            SelectedMlNetOcrModel = previousSelection;
        }
        else
        {
            SelectedMlNetOcrModel = AvailableMlNetOcrModels.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void BrowseBergamotModelsDirectory()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.Description = "Select a folder containing Bergamot model folders";
        dialog.InitialDirectory = string.IsNullOrEmpty(BergamotModelsDirectory) || !Directory.Exists(BergamotModelsDirectory)
            ? ModelStoragePaths.BergamotDirectory
            : BergamotModelsDirectory;

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            BergamotModelsDirectory = dialog.SelectedPath;
            RefreshBergamotModels();
        }
    }

    [RelayCommand]
    private void RefreshBergamotModels()
    {
        var previousSelection = SelectedBergamotModel;
        AvailableBergamotModels.Clear();

        if (Directory.Exists(BergamotModelsDirectory))
        {
            var modelFolders = ModelFolderScanner.FindModelEntries(
                BergamotModelsDirectory,
                IsBergamotModelDirectory,
                SearchOption.AllDirectories);

            foreach (var modelFolder in modelFolders)
            {
                AvailableBergamotModels.Add(GetBergamotModelDisplayName(modelFolder));
            }
        }

        if (!string.IsNullOrWhiteSpace(BergamotModelPath))
        {
            var savedSelection = GetBergamotModelDisplayName(BergamotModelPath);
            if (AvailableBergamotModels.Contains(savedSelection))
            {
                SelectedBergamotModel = savedSelection;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(previousSelection) && AvailableBergamotModels.Contains(previousSelection))
        {
            SelectedBergamotModel = previousSelection;
        }
        else
        {
            SelectedBergamotModel = AvailableBergamotModels.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void BrowseDirectMlOnnxModelPath()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.Description = "Select the DirectML ONNX translation model folder";
        dialog.InitialDirectory = string.IsNullOrEmpty(DirectMlOnnxModelPath) || !Directory.Exists(DirectMlOnnxModelPath)
            ? ModelStoragePaths.DirectMlOnnxDirectory
            : DirectMlOnnxModelPath;

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            DirectMlOnnxModelPath = dialog.SelectedPath;
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
        ModelStoragePaths.EnsureDefaultDirectories();
        TesseractDataPath = ModelStoragePaths.TesseractDirectory;
        RefreshTesseractLanguages();
        GoogleVisionApiKey = string.Empty;
        GoogleTranslationApiKey = string.Empty;
        TranslationProvider = "Disabled";
        BergamotModelsDirectory = ModelStoragePaths.BergamotDirectory;
        BergamotModelPath = string.Empty;
        AvailableBergamotModels.Clear();
        SelectedBergamotModel = null;
        DirectMlOnnxModelPath = ModelStoragePaths.DirectMlOnnxDirectory;
        MlNetOcrModelPath = ModelStoragePaths.MlNetOcrDirectory;
        MlNetOcrAccelerator = "Auto";
        SourceLanguage = SelectedTesseractLanguage ?? "eng";
        TranslationSourceLanguage = "auto";
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
            _settings.GoogleTranslationApiKey = string.IsNullOrWhiteSpace(GoogleTranslationApiKey) ? null : GoogleTranslationApiKey;
            _settings.TranslationProvider = TranslationProvider;
            _settings.BergamotModelsDirectory = string.IsNullOrWhiteSpace(BergamotModelsDirectory) ? ModelStoragePaths.BergamotDirectory : BergamotModelsDirectory;
            _settings.BergamotModelPath = string.IsNullOrWhiteSpace(BergamotModelPath) ? null : BergamotModelPath;
            _settings.DirectMlOnnxModelPath = string.IsNullOrWhiteSpace(DirectMlOnnxModelPath) ? ModelStoragePaths.DirectMlOnnxDirectory : DirectMlOnnxModelPath;
            _settings.MlNetOcrModelPath = string.IsNullOrWhiteSpace(MlNetOcrModelPath) ? ModelStoragePaths.MlNetOcrDirectory : MlNetOcrModelPath;
            _settings.MlNetOcrAccelerator = MlNetOcrAccelerator;
            _settings.SourceLanguage = SourceLanguage;
            _settings.TranslationSourceLanguage = string.IsNullOrWhiteSpace(TranslationSourceLanguage) ? "auto" : TranslationSourceLanguage;
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
        OnPropertyChanged(nameof(IsMlNetOcrProvider));
        SaveIfReady();
    }

    partial void OnTesseractDataPathChanged(string value)
    {
        RefreshTesseractLanguages();
        SaveIfReady();
    }

    partial void OnGoogleVisionApiKeyChanged(string value) => SaveIfReady();

    partial void OnGoogleTranslationApiKeyChanged(string value) => SaveIfReady();

    partial void OnTranslationProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsTranslationEnabled));
        OnPropertyChanged(nameof(IsBergamotTranslationProvider));
        OnPropertyChanged(nameof(IsDirectMlOnnxTranslationProvider));
        OnPropertyChanged(nameof(IsGoogleApiTranslationProvider));
        OnPropertyChanged(nameof(ShowsTranslationLanguageFields));
        SaveIfReady();
    }

    partial void OnBergamotModelsDirectoryChanged(string value)
    {
        RefreshBergamotModels();
        SaveIfReady();
    }

    partial void OnSelectedBergamotModelChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedBergamotLanguagePair));

        if (string.IsNullOrWhiteSpace(value))
        {
            BergamotModelPath = string.Empty;
            return;
        }

        var modelPath = ResolveBergamotModelPath(value);
        if (!string.IsNullOrWhiteSpace(modelPath) && BergamotModelPath != modelPath)
        {
            BergamotModelPath = modelPath;
        }
    }

    partial void OnBergamotModelPathChanged(string value)
    {
        if (!_isInitializing && !string.IsNullOrWhiteSpace(value))
        {
            var displayName = GetBergamotModelDisplayName(value);
            if (AvailableBergamotModels.Contains(displayName) && SelectedBergamotModel != displayName)
            {
                SelectedBergamotModel = displayName;
            }
        }

        OnPropertyChanged(nameof(SelectedBergamotLanguagePair));

        SaveIfReady();
    }

    partial void OnDirectMlOnnxModelPathChanged(string value) => SaveIfReady();

    partial void OnMlNetOcrModelPathChanged(string value)
    {
        if (!_isInitializing && !string.IsNullOrWhiteSpace(value))
        {
            RefreshMlNetOcrModels();
        }

        SaveIfReady();
        OnPropertyChanged(nameof(MlNetOcrAutoAcceleratorStatus));
    }

    partial void OnMlNetOcrAcceleratorChanged(string value)
    {
        SaveIfReady();
        OnPropertyChanged(nameof(MlNetOcrAutoAcceleratorStatus));
    }

    partial void OnSelectedMlNetOcrModelChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var modelPath = ResolveMlNetOcrModelPath(value);
        if (!string.IsNullOrWhiteSpace(modelPath) && MlNetOcrModelPath != modelPath)
        {
            MlNetOcrModelPath = modelPath;
        }
    }

    partial void OnTranslationSourceLanguageChanged(string value) => SaveIfReady();

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

    public string? ResolveSelectedBergamotModelPath()
    {
        if (!string.IsNullOrWhiteSpace(BergamotModelPath) && Directory.Exists(BergamotModelPath))
        {
            return BergamotModelPath;
        }

        return string.IsNullOrWhiteSpace(SelectedBergamotModel)
            ? null
            : ResolveBergamotModelPath(SelectedBergamotModel);
    }

    private string? ResolveBergamotModelPath(string displayName)
    {
        if (!Directory.Exists(BergamotModelsDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(BergamotModelsDirectory, "*", SearchOption.AllDirectories)
            .Where(IsBergamotModelDirectory)
            .FirstOrDefault(path => string.Equals(
                GetBergamotModelDisplayName(path),
                displayName,
                StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveMlNetOcrRootDirectory()
    {
        if (string.IsNullOrWhiteSpace(MlNetOcrModelPath))
        {
            return ModelStoragePaths.MlNetOcrDirectory;
        }

        if (File.Exists(MlNetOcrModelPath))
        {
            return Directory.GetParent(MlNetOcrModelPath)?.FullName ?? ModelStoragePaths.MlNetOcrDirectory;
        }

        if (Directory.Exists(MlNetOcrModelPath))
        {
            if (PathsEqual(MlNetOcrModelPath, ModelStoragePaths.MlNetOcrDirectory))
            {
                return MlNetOcrModelPath;
            }

            return IsMlNetOcrModelPath(MlNetOcrModelPath)
                ? Directory.GetParent(MlNetOcrModelPath)?.FullName ?? ModelStoragePaths.MlNetOcrDirectory
                : MlNetOcrModelPath;
        }

        return ModelStoragePaths.MlNetOcrDirectory;
    }

    private string? ResolveMlNetOcrModelPath(string displayName)
    {
        var rootDirectory = ResolveMlNetOcrRootDirectory();
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        return ModelFolderScanner
            .FindModelEntries(
                rootDirectory,
                IsMlNetOcrModelPath,
                SearchOption.TopDirectoryOnly,
                includeFiles: true)
            .FirstOrDefault(path => string.Equals(
                GetMlNetOcrModelDisplayName(path, rootDirectory),
                displayName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMlNetOcrModelPath(string path)
    {
        if (File.Exists(path))
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        }

        return Directory.Exists(path) &&
               Directory.EnumerateFiles(path, "*.onnx", SearchOption.AllDirectories).Any();
    }

    private static string GetMlNetOcrModelDisplayName(string modelPath, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return string.Empty;
        }

        return ModelFolderScanner.GetDisplayName(rootDirectory, modelPath);
    }

    private static bool PathsEqual(string first, string second)
    {
        return string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBergamotModelDirectory(string directory)
    {
        return Directory.Exists(directory) &&
               Directory.EnumerateFiles(directory, "model.*.bin", SearchOption.TopDirectoryOnly).Any() &&
               Directory.EnumerateFiles(directory, "vocab.*.spm", SearchOption.TopDirectoryOnly).Any() &&
               Directory.EnumerateFiles(directory, "lex.*.bin", SearchOption.TopDirectoryOnly).Any();
    }

    private string GetBergamotModelDisplayName(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return string.Empty;
        }

        return ModelFolderScanner.GetDisplayName(BergamotModelsDirectory, modelPath);
    }

    private static string ResolveDefaultBergamotModelsDirectory(string? modelPath)
    {
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            var parent = Directory.GetParent(modelPath);
            if (parent != null)
            {
                return parent.FullName;
            }
        }

        return ModelStoragePaths.BergamotDirectory;
    }

    private static string? ExtractLanguagePair(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return null;
        }

        var fileName = Path.GetFileName(modelName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parts = fileName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var source = parts[0];
        var target = parts[1];
        return source.Length is >= 2 and <= 8 && target.Length is >= 2 and <= 8
            ? $"{source} -> {target}"
            : null;
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
