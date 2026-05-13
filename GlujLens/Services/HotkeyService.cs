using System.Runtime.InteropServices;
using System.Windows.Forms;
using GlujLens.Models;
using GlujLens.ViewModels;

namespace GlujLens.Services;

/// <summary>
/// Service for registering global hotkeys using Windows RegisterHotKey API.
/// This registers the key combination as a whole - individual keys are not consumed.
/// </summary>
public class HotkeyService : IDisposable
{
    private readonly WeakReference _mainViewModelRef;
    private readonly AppSettings _settings;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly int _hotkeyId = 0xA000;
    private readonly HashSet<int> _registeredHotkeyIds = new();
    private bool _isDisposed;
    private DateTime _lastTriggerTime = DateTime.MinValue;

    public HotkeyService(object mainViewModel, AppSettings settings)
    {
        _mainViewModelRef = new WeakReference(mainViewModel);
        _settings = settings;
        _hotkeyWindow = new HotkeyWindow(this);
    }

    /// <summary>
    /// Registers the hotkey using Windows RegisterHotKey API.
    /// Must be called from a thread with a message loop.
    /// </summary>
    public bool RegisterHotkey()
    {
        UnregisterAllHotkeys();

        var shortcut = _settings.CaptureShortcut ?? "Alt+Ctrl+Q";

        if (!TryParseShortcut(shortcut, out var modifiers, out var virtualKey))
        {
            System.Diagnostics.Debug.WriteLine($"[HotkeyService] Failed to parse virtual key from shortcut: {shortcut}");
            return false;
        }

        var registered = User32.RegisterHotKey(_hotkeyWindow.Handle, _hotkeyId, modifiers, virtualKey);

        if (!registered)
        {
            var error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"[HotkeyService] RegisterHotKey failed (error {error}) for shortcut: {shortcut}");
            return false;
        }

        _registeredHotkeyIds.Add(_hotkeyId);
        System.Diagnostics.Debug.WriteLine($"[HotkeyService] RegisterHotKey succeeded for shortcut: {shortcut} (MOD=0x{modifiers:X2}, VK=0x{virtualKey:X2})");
        return true;
    }

    /// <summary>
    /// Unregisters every hotkey this service has registered.
    /// </summary>
    public void UnregisterAllHotkeys()
    {
        if (_hotkeyWindow.Handle == IntPtr.Zero || _registeredHotkeyIds.Count == 0)
            return;

        foreach (var hotkeyId in _registeredHotkeyIds.ToArray())
        {
            if (!User32.UnregisterHotKey(_hotkeyWindow.Handle, hotkeyId))
            {
                var error = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"[HotkeyService] UnregisterHotKey failed (error {error}) for id: {hotkeyId}");
            }
        }

        _registeredHotkeyIds.Clear();
    }

    /// <summary>
    /// Called by HotkeyWindow when WM_HOTKEY is received.
    /// </summary>
    internal void OnHotKeyPressed()
    {
        var now = DateTime.Now;
        if ((now - _lastTriggerTime).TotalMilliseconds < 500)
            return;

        _lastTriggerTime = now;

        var vm = _mainViewModelRef.Target;
        if (vm is MainViewModel mainVm)
        {
            try
            {
                if (mainVm.CaptureCommand.CanExecute(null))
                {
                    mainVm.CaptureCommand.Execute(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkey trigger error: {ex.Message}");
            }
        }
    }

    public bool RefreshHotkey()
    {
        return RegisterHotkey();
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            UnregisterAllHotkeys();
            _hotkeyWindow.Dispose();
            _isDisposed = true;
        }
    }

    #region Parsing

    private static uint ParseModifiers(string shortcut)
    {
        uint modifiers = 0;
        var upper = shortcut.ToUpperInvariant();

        if (upper.Contains("ALT"))
            modifiers |= User32.MOD_ALT;
        if (upper.Contains("SHIFT"))
            modifiers |= User32.MOD_SHIFT;
        if (upper.Contains("CTRL") || upper.Contains("CONTROL"))
            modifiers |= User32.MOD_CONTROL;
        if (upper.Contains("WIN") || upper.Contains("COMMAND"))
            modifiers |= User32.MOD_WIN;

        return modifiers;
    }

    private static uint ParseVirtualKey(string shortcut)
    {
        var extractedKey = shortcut;
        foreach (var mod in new[] { "CTRL+", "CONTROL+", "SHIFT+", "ALT+", "WIN+", "COMMAND+", "+" })
        {
            extractedKey = extractedKey.Replace(mod, "", StringComparison.OrdinalIgnoreCase);
        }
        return MapKeyToVirtualKey(extractedKey.Trim());
    }

    public static bool CanParseShortcut(string shortcut)
    {
        return TryParseShortcut(shortcut, out _, out _);
    }

    private static bool TryParseShortcut(string shortcut, out uint modifiers, out uint virtualKey)
    {
        modifiers = ParseModifiers(shortcut);
        virtualKey = ParseVirtualKey(shortcut);
        return virtualKey != 0;
    }

    private static uint MapKeyToVirtualKey(string keyName)
    {
        return keyName.ToUpperInvariant() switch
        {
            "F1" => 0x71, "F2" => 0x72, "F3" => 0x73, "F4" => 0x74,
            "F5" => 0x75, "F6" => 0x76, "F7" => 0x77, "F8" => 0x78,
            "F9" => 0x79, "F10" => 0x7A, "F11" => 0x7B, "F12" => 0x7C,
            "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44,
            "E" => 0x45, "F" => 0x46, "G" => 0x47, "H" => 0x48,
            "I" => 0x49, "J" => 0x4A, "K" => 0x4B, "L" => 0x4C,
            "M" => 0x4D, "N" => 0x4E, "O" => 0x4F, "P" => 0x50,
            "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
            "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58,
            "Y" => 0x59, "Z" => 0x5A,
            "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33,
            "4" => 0x34, "5" => 0x35, "6" => 0x36, "7" => 0x37,
            "8" => 0x38, "9" => 0x39,
            "PRINTSCREEN" => 0x2C, "PRTSC" => 0x2C,
            "BACKSPACE" => 0x08, "TAB" => 0x09,
            "RETURN" => 0x0D, "ENTER" => 0x0D,
            "ESCAPE" => 0x1B, "SPACE" => 0x20,
            "INSERT" => 0x2D, "DELETE" => 0x2E,
            "NUMPAD0" => 0x60, "NUMPAD1" => 0x61, "NUMPAD2" => 0x62,
            "NUMPAD3" => 0x63, "NUMPAD4" => 0x64, "NUMPAD5" => 0x65,
            "NUMPAD6" => 0x66, "NUMPAD7" => 0x67, "NUMPAD8" => 0x68,
            "NUMPAD9" => 0x69,
            _ => 0
        };
    }

    #endregion

    #region Win32 Native Methods

    private static class User32
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint ldvk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);
    }

    #endregion

    #region Hidden Window for Message Pump

    private class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly HotkeyService _service;
        private bool _disposed;

        public HotkeyWindow(HotkeyService service)
        {
            _service = service;
            var hWnd = User32.CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            AssignHandle(hWnd);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == User32.WM_HOTKEY)
            {
                _service.OnHotKeyPressed();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (!_disposed && Handle != IntPtr.Zero)
            {
                var handle = Handle;
                ReleaseHandle();
                User32.DestroyWindow(handle);
                _disposed = true;
            }
        }
    }

    #endregion
}
