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

    public MainWindow(ITrayIconService trayIcon)
    {
        InitializeComponent();
        _trayIcon = trayIcon;

        // Handle window closing to minimize to tray instead of quitting
        Closing += MainWindow_Closing;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
        // Balloon tip is shown by App.MainWindow_Closing (throttled)
    }
}
