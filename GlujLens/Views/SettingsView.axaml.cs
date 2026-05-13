using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.ComponentModel;
using GlujLens.Services;
using GlujLens.ViewModels;

namespace GlujLens.Views;

public partial class SettingsView : Window
{
    private SettingsViewModel? _settingsVm;

    private readonly MainViewModel? _mainVm;

    public SettingsView(SettingsViewModel settingsViewModel, MainViewModel? mainVm = null)
    {
        InitializeComponent();
        DataContext = settingsViewModel;
        _settingsVm = settingsViewModel;
        _mainVm = mainVm;

        // Listen for key events to capture shortcut
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        _settingsVm.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_settingsVm is null || !_settingsVm.IsRecordingShortcut)
            return;

        e.Handled = true;

        if (IsModifierKey(e.Key))
            return;

        var shortcut = FormatShortcut(e.Key, e.KeyModifiers);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            _settingsVm.OnKeyCaptured(shortcut);
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.CaptureShortcut) || _settingsVm is null || _mainVm is null)
            return;

        if (!HotkeyService.CanParseShortcut(_settingsVm.CaptureShortcut))
        {
            _settingsVm.StatusMessage = "Shortcut is incomplete or unsupported.";
            return;
        }

        var refreshed = _mainVm.RefreshHotkey();
        _settingsVm.StatusMessage = refreshed
            ? $"Shortcut rebound to {_settingsVm.CaptureShortcut}"
            : $"Could not bind {_settingsVm.CaptureShortcut}";
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_settingsVm is null || !_settingsVm.IsRecordingShortcut)
            return;

        // Keep the key release from leaking into focused controls while recording.
        e.Handled = true;
    }

    private static string FormatShortcut(Key key, KeyModifiers modifiers)
    {
        var parts = new System.Collections.Generic.List<string>();

        if ((modifiers & KeyModifiers.Alt) != 0)
            parts.Add("Alt");
        if ((modifiers & KeyModifiers.Control) != 0)
            parts.Add("Ctrl");
        if ((modifiers & KeyModifiers.Shift) != 0)
            parts.Add("Shift");

        if (parts.Count > 2)
        {
            parts.RemoveRange(2, parts.Count - 2);
        }

        var keyName = GetKeyName(key);
        if (string.IsNullOrWhiteSpace(keyName))
            return string.Empty;

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftAlt
            or Key.RightAlt
            or Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;
    }

    private static string GetKeyName(Key key)
    {
        return key switch
        {
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3",
            Key.D4 => "4", Key.D5 => "5", Key.D6 => "6", Key.D7 => "7",
            Key.D8 => "8", Key.D9 => "9",
            Key.A => "A", Key.B => "B", Key.C => "C", Key.D => "D",
            Key.E => "E", Key.F => "F", Key.G => "G", Key.H => "H",
            Key.I => "I", Key.J => "J", Key.K => "K", Key.L => "L",
            Key.M => "M", Key.N => "N", Key.O => "O", Key.P => "P",
            Key.Q => "Q", Key.R => "R", Key.S => "S", Key.T => "T",
            Key.U => "U", Key.V => "V", Key.W => "W", Key.X => "X",
            Key.Y => "Y", Key.Z => "Z",
            Key.Sleep => "Sleep", Key.Multiply => "*", Key.Add => "+",
            Key.Subtract => "-", Key.Divide => "/", Key.Decimal => ".",
            Key.Enter => "Enter", Key.Tab => "Tab", Key.Capital => "CapsLock",
            Key.Escape => "Esc", Key.Space => "Space", Key.PageUp => "PageUp",
            Key.PageDown => "PageDown", Key.End => "End", Key.Home => "Home",
            Key.Left => "Left", Key.Up => "Up", Key.Right => "Right",
            Key.Down => "Down", Key.PrintScreen => "PrintScreen",
            Key.Insert => "Insert", Key.Delete => "Delete",
            Key.NumLock => "NumLock", Key.Pause => "Pause",
            Key.BrowserBack => "BrowserBack", Key.BrowserForward => "BrowserForward",
            Key.BrowserRefresh => "BrowserRefresh", Key.BrowserStop => "BrowserStop",
            Key.BrowserSearch => "BrowserSearch", Key.BrowserFavorites => "BrowserFavorites",
            Key.BrowserHome => "BrowserHome",
            Key.None => string.Empty,
            _ => key.ToString()
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        // 1. Cancel any ongoing key recording FIRST
        if (_settingsVm != null)
        {
            _settingsVm.CancelRecording();
        }

        // 2. Unsubscribe from key events
        KeyDown -= OnKeyDown;
        KeyUp -= OnKeyUp;
        if (_settingsVm != null)
        {
            _settingsVm.PropertyChanged -= OnSettingsPropertyChanged;
        }

        // 3. Save all settings to AppSettings and persist to file
        if (_settingsVm != null)
        {
            var success = _settingsVm.Save();
            System.Diagnostics.Debug.WriteLine($"Settings save result: {(success ? "Success" : "Failed")}");
        }

        // 4. Reload settings and refresh hotkey in the main view model
        var targetVm = _mainVm;
        if (targetVm == null && Owner?.DataContext is GlujLens.ViewModels.MainViewModel mv)
        {
            targetVm = mv;
        }

        if (targetVm != null)
        {
            try
            {
                targetVm.ReloadSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload settings: {ex.Message}");
            }
        }

        // 5. Dispose the view model
        if (_settingsVm is IDisposable disposable)
            disposable.Dispose();
        _settingsVm = null;

        base.OnClosed(e);
    }
}
