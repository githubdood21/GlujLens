using System.Windows.Forms;
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
        HardwareAcceleration = settings.HardwareAcceleration ?? "Auto";
        ImageFormat = settings.ImageFormat ?? "PNG";
        ImageQuality = settings.ImageQuality;
        CopyToClipboardAfterCapture = settings.CopyToClipboardAfterCapture;
        ShowNotificationAfterCapture = settings.ShowNotificationAfterCapture;
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
    private string _hardwareAcceleration;

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
        HardwareAcceleration = "Auto";
        ImageFormat = "PNG";
        ImageQuality = 90;
        CopyToClipboardAfterCapture = false;
        ShowNotificationAfterCapture = true;
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

    partial void OnHardwareAccelerationChanged(string value) => SaveIfReady();

    partial void OnImageFormatChanged(string value) => SaveIfReady();

    partial void OnImageQualityChanged(int value) => SaveIfReady();

    partial void OnCopyToClipboardAfterCaptureChanged(bool value) => SaveIfReady();

    partial void OnShowNotificationAfterCaptureChanged(bool value) => SaveIfReady();

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

    public void Dispose()
    {
        if (!_isDisposed)
        {
            // Note: CancelRecording should be called by SettingsView.OnClosed before Dispose
            _isDisposed = true;
        }
    }
}
