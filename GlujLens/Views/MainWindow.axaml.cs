using System.Windows.Forms;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GlujLens.Services;
using GlujLens.ViewModels;

namespace GlujLens.Views;

public partial class MainWindow : Window
{
    private readonly ITrayIconService _trayIcon;
    private bool _isMinimizing;

    public MainWindow(ITrayIconService trayIcon)
    {
        InitializeComponent();
        _trayIcon = trayIcon;

        // Handle window closing to minimize to tray instead of quitting
        Closing += MainWindow_Closing;

        DataContext = new MainViewModel(trayIcon);
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