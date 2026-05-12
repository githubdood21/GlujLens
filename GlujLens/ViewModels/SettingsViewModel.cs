using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlujLens.Models;

namespace GlujLens.ViewModels;

/// <summary>
/// View model for the settings window. Simple two-way binding with explicit Save.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private bool _isRecordingShortcut;
    private bool _isDisposed;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;

        // Initialize from settings
        DefaultSaveDirectory = settings.DefaultSaveDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        CaptureShortcut = settings.CaptureShortcut ?? "B";
        RecordedShortcutText = CaptureShortcut;
        HardwareAcceleration = settings.HardwareAcceleration ?? "Auto";
        ImageFormat = settings.ImageFormat ?? "PNG";
        ImageQuality = settings.ImageQuality;
        CopyToClipboardAfterCapture = settings.CopyToClipboardAfterCapture;
        ShowNotificationAfterCapture = settings.ShowNotificationAfterCapture;
    }

    [ObservableProperty]
    private string _defaultSaveDirectory;

    [ObservableProperty]
    private string _captureShortcut;

    [ObservableProperty]
    private string _recordedShortcutText;

    [ObservableProperty]
    private string _hardwareAcceleration;

    [ObservableProperty]
    private string _imageFormat;

    [ObservableProperty]
    private int _imageQuality;

    [ObservableProperty]
    private bool _copyToClipboardAfterCapture;

    [ObservableProperty]
    private bool _showNotificationAfterCapture;

    public string[] HardwareAccelerationOptions => new[] { "Auto", "GPU", "CPU (AVX)", "CPU (No AVX)" };
    public string[] ImageFormatOptions => new[] { "PNG", "JPEG", "BMP" };
    public int[] ImageQualityOptions => new[] { 10, 25, 50, 75, 90, 95, 100 };

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
    private void StartRecordingShortcut()
    {
        IsRecordingShortcut = true;
        RecordedShortcutText = "Recording...";
    }

    /// <summary>
    /// Called when a key is pressed during recording.
    /// </summary>
    public void OnKeyCaptured(string keyName)
    {
        if (_isRecordingShortcut)
        {
            CaptureShortcut = keyName;
            RecordedShortcutText = keyName;
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
            _settings.HardwareAcceleration = HardwareAcceleration;
            _settings.ImageFormat = ImageFormat;
            _settings.ImageQuality = ImageQuality;
            _settings.CopyToClipboardAfterCapture = CopyToClipboardAfterCapture;
            _settings.ShowNotificationAfterCapture = ShowNotificationAfterCapture;
            _settings.Save();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            return false;
        }
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
