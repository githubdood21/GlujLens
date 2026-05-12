using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GlujLens.Models;
using GlujLens.Services;
using GlujLens.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GlujLens.Views;

public partial class MainWindow : Window
{
    private readonly ITrayIconService _trayIcon;
    private readonly IScreenshotService _screenshotService;
    private readonly AppSettings _settings;
    private HotkeyService? _hotkeyService;
    private bool _isMinimizing;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public MainWindow(ITrayIconService trayIcon, IScreenshotService screenshotService, AppSettings settings, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _trayIcon = trayIcon;
        _screenshotService = screenshotService;
        _settings = settings;

        // Handle window closing to minimize to tray instead of quitting
        Closing += MainWindow_Closing;

        // Use DI to get the same MainViewModel instance that's used elsewhere
        DataContext = serviceProvider.GetRequiredService<MainViewModel>();

        // Initialize hotkey service after window is shown
        Opened += OnOpenedInternal;
    }

    private void OnOpenedInternal(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _hotkeyService = new HotkeyService(vm, _settings);
            var success = _hotkeyService.RegisterHotkey();
            System.Diagnostics.Debug.WriteLine($"Hotkey registration: {(success ? "Success" : "Failed")} - {_settings.CaptureShortcut}");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Subscribe to layout updated to check for minimize
        LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnClosed(EventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        base.OnClosed(e);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_isMinimizing)
            return;

        if (WindowState == WindowState.Minimized)
        {
            _isMinimizing = true;
            Hide();
            _trayIcon.ShowIcon();
            _isMinimizing = false;
        }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
        // Balloon tip is shown by App.MainWindow_Closing (throttled)
    }
}