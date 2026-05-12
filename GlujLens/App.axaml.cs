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
    private readonly ManualResetEvent _formsLoopExit = new ManualResetEvent(false);
    private bool _isShuttingDown;
    private DateTime _lastBalloonTime = DateTime.MinValue;
    private readonly object _balloonLock = new object();

    public App()
    {
        var serviceCollection = new ServiceCollection();

        // Configure services
        serviceCollection.AddSingleton<ITrayIconService, TrayIconService>();
        serviceCollection.AddSingleton<IScreenshotService, ScreenshotService>();
        serviceCollection.AddSingleton<AppSettings>();
        serviceCollection.AddSingleton<MainViewModel>();

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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create tray icon service FIRST
            _trayIcon = _services.GetRequiredService<ITrayIconService>();

            // Create context menu
            var contextMenu = TrayIconService.CreateContextMenu(menuItemName =>
            {
                if (menuItemName == "Quit")
                {
                    // Gracefully shut down the entire application
                    ShutdownApp();
                }
                else if (menuItemName == "Open")
                {
                    // Show main window
                    ShowMainWindow(desktop);
                }
                else if (menuItemName == "Capture")
                {
                    // Trigger capture
                    if (desktop.MainWindow?.DataContext is MainViewModel vm)
                        vm.HandleMenuItem(menuItemName);
                }
                else if (menuItemName == "Settings")
                {
                    if (desktop.MainWindow?.DataContext is MainViewModel vm)
                        vm.HandleMenuItem(menuItemName);
                }
            });

            _trayIcon.SetContextMenu(contextMenu);
            _trayIcon.SetTooltip("GlujLens");

            // Show tray icon immediately
            _trayIcon.ShowIcon();

            // Only create main window if user clicks "Open" from tray
            // Don't show it by default - app runs in background only

            // Handle main window closing to go back to tray
            if (desktop.MainWindow != null)
            {
                desktop.MainWindow.Closing += MainWindow_Closing;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow == null)
        {
            var trayIcon = _services.GetRequiredService<ITrayIconService>();
            var screenshotService = _services.GetRequiredService<IScreenshotService>();
            var settings = _services.GetRequiredService<AppSettings>();
            var serviceProvider = _services;
            var mainWindow = new MainWindow(trayIcon, screenshotService, settings, serviceProvider);
            mainWindow.DataContext = _services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = mainWindow;
            mainWindow.Closing += MainWindow_Closing;
            mainWindow.Show();
        }
        else
        {
            desktop.MainWindow.Show();
            desktop.MainWindow.WindowState = WindowState.Normal;
        }
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