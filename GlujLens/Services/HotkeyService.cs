using System.Runtime.InteropServices;
using GlujLens.Models;
using GlujLens.ViewModels;

namespace GlujLens.Services;

/// <summary>
/// Service for registering global hotkeys using a low-level keyboard hook.
/// This does NOT consume the key - the key still reaches other apps.
/// </summary>
public class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // Scan codes for Alt and right Alt
    private const uint SCAN_ALT = 0x38;       // Scan code for left Alt
    private const uint SCAN_ALT_EXTENDED = 0xE038; // Right Alt (AltGr)
    private const uint KHF_ALTDOWN = 0x40;    // Context code bit indicating ALT was held

    private readonly WeakReference _mainViewModelRef;
    private readonly AppSettings _settings;
    private readonly int _hotkeyId = 0xA000;
    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelKeyboardProc _keyboardProc;
    private bool _isDisposed;
    private DateTime _lastTriggerTime = DateTime.MinValue;

    /// <summary>
    /// Event raised when the hotkey is pressed.
    /// </summary>
    public event Action? HotKeyPressed;

    public HotkeyService(object mainViewModel, AppSettings settings)
    {
        _mainViewModelRef = new WeakReference(mainViewModel);
        _settings = settings;
        _keyboardProc = KeyboardHookProc;
    }

    /// <summary>
    /// Registers the low-level keyboard hook.
    /// </summary>
    public bool RegisterHotkey()
    {
        UnregisterHotkey();

        // Get the module handle for the hook
        var handle = GetModuleHandle(IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        // Install the hook - must be called from a thread with a message queue
        _hookHandle = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _keyboardProc,
            handle,
            0); // 0 = hook all threads

        if (_hookHandle == IntPtr.Zero)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Unregisters the keyboard hook.
    /// </summary>
    public void UnregisterHotkey()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Low-level keyboard hook procedure.
    /// Returns 0 to allow the key to pass through, non-zero to suppress it.
    /// We return 0 so the key still reaches other applications.
    /// </summary>
    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _isDisposed)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
        if (!isKeyDown)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var keyboardHookStruct = (KeyboardHookStruct)Marshal.PtrToStructure(lParam, typeof(KeyboardHookStruct))!;

        // Extract flags and scan code directly from the struct memory layout.
        // KBDLLHOOKSTRUCT layout: vkCode (uint), scanCode (uint), flags (uint), time (uint), dwExtraInfo (IntPtr)
        // sizeof(uint) = 4, so:
        //   vkCode at offset 0
        //   scanCode at offset 4
        //   flags at offset 8
        //   time at offset 12
        //   dwExtraInfo at offset 16
        uint scanCode = keyboardHookStruct.scanCode;
        uint flags = keyboardHookStruct.flags;

        // Check if the pressed key matches the configured hotkey
        // Pass the message type, scan code, and flags for accurate modifier detection
        if (IsHotkeyPressed(keyboardHookStruct.vkCode, scanCode, flags, wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            // Throttle triggers to prevent too many rapid-fire calls
            var now = DateTime.Now;
            if ((now - _lastTriggerTime).TotalMilliseconds > 500)
            {
                _lastTriggerTime = now;

                // Invoke the hotkey event on the UI thread
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
                        Console.WriteLine($"Hotkey trigger error: {ex.Message}");
                    }
                }
            }
        }

        // IMPORTANT: Return 0 to LET the key pass through to other applications
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Checks if the current key press matches the configured hotkey.
    /// </summary>
    /// <param name="vkCode">The virtual key code of the key that was pressed.</param>
    /// <param name="scanCode">The scan code of the key that was pressed.</param>
    /// <param name="flags">The flags field from the KBDLLHOOKSTRUCT structure.</param>
    /// <param name="isSysKeyDown">True if WM_SYSKEYDOWN was received.</param>
    private bool IsHotkeyPressed(uint vkCode, uint scanCode, uint flags, bool isSysKeyDown)
    {
        var shortcut = _settings.CaptureShortcut ?? "B";

        // Parse which modifiers the shortcut requires
        var shortcutRequiresCtrl = shortcut.IndexOf("ctrl", StringComparison.OrdinalIgnoreCase) >= 0
                              || shortcut.IndexOf("control", StringComparison.OrdinalIgnoreCase) >= 0;
        var shortcutRequiresShift = shortcut.IndexOf("shift", StringComparison.OrdinalIgnoreCase) >= 0;
        var shortcutRequiresAlt = shortcut.IndexOf("alt", StringComparison.OrdinalIgnoreCase) >= 0;
        var shortcutRequiresWin = shortcut.IndexOf("win", StringComparison.OrdinalIgnoreCase) >= 0
                            || shortcut.IndexOf("command", StringComparison.OrdinalIgnoreCase) >= 0;

        // Extract the key part (remove modifier prefixes and plus signs)
        var extractedKey = shortcut;
        foreach (var mod in new[] { "ctrl+", "control+", "shift+", "alt+", "win+", "command+", "+" })
        {
            extractedKey = extractedKey.Replace(mod, "", StringComparison.OrdinalIgnoreCase);
        }

        // Get the expected virtual key code for the main key
        var expectedVk = MapKeyToVirtualKey(extractedKey);
        if (expectedVk == 0)
            return false;

        // For single-key shortcuts (no modifiers), just match the key
        if (!shortcutRequiresCtrl && !shortcutRequiresShift && !shortcutRequiresAlt && !shortcutRequiresWin)
        {
            return expectedVk == vkCode;
        }

        // For modifier+key shortcuts, check if all required modifiers are currently held down.

        // Check Ctrl
        if (shortcutRequiresCtrl && (GetKeyState(VK_CONTROL) < 0) == false)
            return false;

        // Check Shift
        if (shortcutRequiresShift && (GetKeyState(VK_SHIFT) < 0) == false)
            return false;

        // Check Win
        if (shortcutRequiresWin && (GetKeyState(VK_LWIN) < 0 || GetKeyState(VK_RWIN) < 0) == false)
            return false;

        // Special handling for ALT detection:
        // Low-level keyboard hooks have a quirk: ALT+key triggers WM_SYSKEYDOWN instead of WM_KEYDOWN,
        // and GetKeyState(VK_MENU) may not reflect the ALT state because the system is processing
        // the ALT as part of a "system key" event. We use multiple strategies to detect ALT:
        //
        // 1. If WM_SYSKEYDOWN was received and the shortcut requires Alt, ALT was likely the trigger.
        // 2. Check the KBDLLHOOKSTRUCT flags field's context code (bit 0x40 indicates ALT held).
        // 3. Check if the scan code is 0x38 (which is the scan code for Alt when it's the triggering key).
        // 4. Fall back to GetKeyState(VK_MENU).
        bool altDetected = false;
        if (shortcutRequiresAlt)
        {
            // Strategy 1: WM_SYSKEYDOWN with Alt required usually means Alt triggered this
            if (isSysKeyDown)
                altDetected = true;

            // Strategy 2: Check the context code bit in the high byte of flags (KHF_ALTDOWN = 0x40)
            if (!altDetected && ((flags & KHF_ALTDOWN) != 0))
                altDetected = true;

            // Strategy 3: If the scan code matches Alt's scan code (0x38), this IS the Alt key press itself
            if (!altDetected && scanCode == SCAN_ALT)
                altDetected = true;

            // Strategy 4: Fall back to GetKeyState
            if (!altDetected && (GetKeyState(VK_MENU) < 0))
                altDetected = true;
        }

        if (shortcutRequiresAlt && !altDetected)
            return false;

        // All required modifiers are held and the main key matches
        return expectedVk == vkCode;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern int GetKeyState(int vKey);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

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
            _ => 0
        };
    }

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private struct KeyboardHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            UnregisterHotkey();
            _isDisposed = true;
        }
    }
}