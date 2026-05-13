using System.Windows.Forms;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GlujLens.Models;
using GlujLens.Services;
using GlujLens.ViewModels;
using GlujLens.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GlujLens;

public partial class App : Avalonia.Application
{
    private readonly ServiceProvider _services;
    private ITrayIconService? _trayIcon;
    private HotkeyService? _hotkeyService;
    private MainViewModel? _mainVm;
    private AppSettings? _settings;
    private ManualResetEvent _formsLoopExit;
    private bool _isShuttingDown;
    private DateTime _lastBalloonTime = DateTime.MinValue;
    private readonly object _balloonLock = new object();
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    // Static callback for notification popup to show main window
    public static Action<MainViewModel>? ShowMainWindowCallback { get; set; }

    public App()
    {
        var serviceCollection = new ServiceCollection();

        // Configure services
        serviceCollection.AddSingleton<ITrayIconService, TrayIconService>();
        serviceCollection.AddSingleton<IScreenshotService, ScreenshotService>();
        serviceCollection.AddSingleton<AppSettings>();

        // Add logging (minimal console logging for now)
        serviceCollection.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        _services = serviceCollection.BuildServiceProvider();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _formsLoopExit = new ManualResetEvent(false);
        
        _desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        
        if (_desktop != null)
        {
            // Create tray icon service FIRST
            _trayIcon = _services.GetRequiredService<ITrayIconService>();
            _settings = _services.GetRequiredService<AppSettings>();

            // Create context menu - uses ShowMainWindow method directly
            var contextMenu = TrayIconService.CreateContextMenu(menuItemName =>
            {
                if (menuItemName == "Quit")
                {
                    // Gracefully shut down the entire application
                    ShutdownApp();
                }
                else if (menuItemName == "Open" || menuItemName == "Capture" || menuItemName == "Settings")
                {
                    // Use ShowMainWindow for Open, trigger capture/settings for others
                    if (menuItemName == "Open")
                    {
                        ShowMainWindow();
                    }
                    else if (_mainVm != null)
                    {
                        _mainVm.HandleMenuItem(menuItemName);
                    }
                }
            });

            _trayIcon.SetContextMenu(contextMenu);
            _trayIcon.SetTooltip("GlujLens");

            // Show tray icon immediately
            _trayIcon.ShowIcon();

            // Create MainViewModel and register HotkeyService at app startup
            // This ensures hotkeys work immediately without needing to open the main window
            _mainVm = new MainViewModel(_trayIcon, _services.GetRequiredService<IScreenshotService>(), _settings, _services);
            _hotkeyService = new HotkeyService(_mainVm, _settings);
            _hotkeyService.RegisterHotkey();
            System.Diagnostics.Debug.WriteLine($"Hotkey registration at startup: {_settings.CaptureShortcut}");

            // Register the callback for showing main window from notification popup
            // This uses the SAME method as the tray icon "Open" menu item
            ShowMainWindowCallback = vm =>
            {
                // This is called from a background thread, so we need to marshal to the Avalonia UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ShowMainWindow();
                }, Avalonia.Threading.DispatcherPriority.Normal);
            };

            // Handle main window closing to go back to tray
            if (_desktop.MainWindow != null)
            {
                _desktop.MainWindow.Closing += MainWindow_Closing;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow()
    {
        if (_desktop == null || _mainVm == null || _trayIcon == null || _settings == null)
            return;
            
        if (_desktop.MainWindow == null)
        {
            // Create new main window
            var mainWindow = new MainWindow(_trayIcon);
            mainWindow.DataContext = _mainVm; // Use the already-created MainViewModel
            _desktop.MainWindow = mainWindow;
            mainWindow.Closing += MainWindow_Closing;
            mainWindow.Show();
        }
        else
        {
            // Window exists - show and activate it
            var window = _desktop.MainWindow;
            window.Show();
            
            // Use Win32 to properly activate the window
            var handle = GetWindowHandle(window);
            if (handle != IntPtr.Zero)
            {
                WindowHelper.ActivateWindow(handle);
            }
        }
    }

    private static IntPtr GetWindowHandle(Avalonia.Controls.Window window)
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.MainWindowHandle;
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // Don't quit on close, minimize to tray
        e.Cancel = true;
        if (sender is Avalonia.Controls.Window window)
        {
            window.Hide();

            // Throttle balloon tips to once per 5 seconds
            lock (_balloonLock)
            {
                var now = DateTime.Now;
                if ((now - _lastBalloonTime).TotalSeconds >= 5)
                {
                    _trayIcon?.ShowBalloonTip(
                        "GlujLens",
                        "GlujLens is running in the background. Right-click the tray icon to access options.",
                        ToolTipIcon.Info);
                    _lastBalloonTime = now;
                }
            }
        }
    }

    private void ShutdownApp()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        // Dispose hotkey service
        _hotkeyService?.Dispose();
        _hotkeyService = null;

        // Dispose tray icon
        _trayIcon?.Dispose();
        _trayIcon = null;

        // Shutdown the Avalonia main window if it exists
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Close();
            desktop.Shutdown();
        }

        // Signal the Forms message loop to exit
        _formsLoopExit.Set();

        // Wait a moment for cleanup
        System.Threading.Thread.Sleep(200);

        // Exit the Forms message loop via static method
        Program.ExitFormsLoop();
    }

    /// <summary>
    /// Call this from Program.cs after Avalonia shuts down to exit the Forms loop.
    /// </summary>
    public void ExitFormsLoop()
    {
        _formsLoopExit.Set();
    }
}