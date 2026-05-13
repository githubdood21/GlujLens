using System.Runtime.InteropServices;

namespace GlujLens.Services;

/// <summary>
/// Win32 helper for finding and activating windows with proper foreground switch support.
/// </summary>
public static class WindowHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private const int SW_SHOWNORMAL = 1;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    /// <summary>
    /// Activates (focuses) a window by its handle using the best available technique.
    /// Uses thread attachment to force foreground switch.
    /// </summary>
    public static bool ActivateWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        // Check if window is minimized
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
        }
        else
        {
            ShowWindow(hWnd, SW_SHOWNORMAL);
        }

        // Get current and target thread IDs
        var currentThreadId = GetCurrentThreadId();
        var windowThreadId = GetWindowThreadProcessId(hWnd, out _);

        // Try thread attachment method first (works when our app currently has focus)
        if (currentThreadId != windowThreadId)
        {
            AttachThreadInput(currentThreadId, windowThreadId, true);
            var result = SetForegroundWindow(hWnd);
            AttachThreadInput(currentThreadId, windowThreadId, false);
            if (result)
                return true;
        }

        // Fallback: direct SetForegroundWindow
        return SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// Finds the main window handle from the current process and activates it.
    /// </summary>
    public static bool ActivateMainWindow()
    {
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var hWnd = currentProcess.MainWindowHandle;

        if (hWnd != IntPtr.Zero)
        {
            return ActivateWindow(hWnd);
        }
        return false;
    }

    /// <summary>
    /// Sets the window as topmost temporarily to ensure it's visible.
    /// </summary>
    public static void SetTopMost(IntPtr hWnd, bool topMost)
    {
        if (hWnd == IntPtr.Zero)
            return;

        var hWndInsertAfter = topMost ? HWND_TOPMOST : HWND_NOTOPMOST;
        SetWindowPos(hWnd, hWndInsertAfter, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}