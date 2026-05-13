using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GlujLens.Models;
using GlujLens.Services;
using GlujLens.ViewModels;
using WinScreen = System.Windows.Forms.Screen;

namespace GlujLens.Views;

/// <summary>
/// A custom notification popup that displays a captured screenshot preview.
/// Slides in from the top-right with a fade animation.
/// </summary>
public class NotificationPopup : Window
{
    private readonly MainViewModel _mainVm;
    private readonly AppSettings _settings;
    private DispatcherTimer? _autoCloseTimer;
    private bool _isUserClick;
    private readonly int _popupWidth;
    private readonly int _popupHeight;

    public NotificationPopup(MainViewModel mainVm, ITrayIconService trayIcon, AppSettings settings, byte[] imageData, int width, int height)
    {
        _mainVm = mainVm;
        _settings = settings;

        // Smaller notification size
        _popupWidth = 320;
        _popupHeight = 280;
        
        Width = _popupWidth;
        Height = _popupHeight;
        MinWidth = 280;
        MinHeight = 220;
        MaxWidth = 360;
        MaxHeight = 320;
        
        // No resize, no taskbar entry, always on top
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        
        // Translucent background for modern look
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30), 0);

        // Calculate position (top-right corner of primary screen)
        var primaryScreen = WinScreen.PrimaryScreen;
        var rightEdge = primaryScreen.WorkingArea.Right - 20;
        var topEdge = primaryScreen.WorkingArea.Top + 20;
        Position = new PixelPoint(rightEdge - _popupWidth, topEdge);

        // Store initial state for animation
        var initialOpacity = 0.0;
        var initialTop = Position.Y - 50;

        // Set initial invisible state
        Opacity = initialOpacity;
        // Start below target position for slide-up effect
        Position = new PixelPoint(Position.X, initialTop);

        // Create the UI
        CreateContent(imageData, width, height);

        // Click handler to open main window
        PointerPressed += OnPointerPressed;

        // Create and start animation
        CreateAndStartAnimation(initialTop);
    }

    private void CreateContent(byte[] imageData, int width, int height)
    {
        var rootPanel = new Panel
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
        };

        var contentPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8)
        };

        var innerPanel = new StackPanel { Margin = new Thickness(12) };

        // Header row with title and close button
        var headerRow = new StackPanel();
        headerRow.Orientation = Orientation.Horizontal;
        headerRow.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;

        var titleText = new TextBlock
        {
            Text = "Screenshot captured",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        headerRow.Children.Add(titleText);

        // Small "X" close button
        var closeBtn = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 22,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Padding = new Thickness(0)
        };
        closeBtn.Click += (s, e) => { _isUserClick = true; Close(); };
        headerRow.Children.Add(closeBtn);

        innerPanel.Children.Add(headerRow);

        // Dimensions message
        var messageText = new TextBlock
        {
            Text = $"{width} × {height}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Margin = new Thickness(0, 2, 0, 8)
        };
        innerPanel.Children.Add(messageText);

        // Image Preview
        Bitmap? capturedBitmap = null;
        try
        {
            using var ms = new MemoryStream(imageData);
            capturedBitmap = new Bitmap(ms);
        }
        catch { /* Ignore */ }

        var imageBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8),
            Height = 160
        };

        var previewImage = new Avalonia.Controls.Image
        {
            Source = capturedBitmap,
            Stretch = Avalonia.Media.Stretch.UniformToFill,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        imageBorder.Child = previewImage;
        innerPanel.Children.Add(imageBorder);

        // Bottom hint bar
        var bottomBar = new StackPanel();
        bottomBar.Orientation = Orientation.Horizontal;
        bottomBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;

        var hintText = new TextBlock
        {
            Text = "Click to open",
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        bottomBar.Children.Add(hintText);

        innerPanel.Children.Add(bottomBar);

        contentPanel.Child = innerPanel;
        rootPanel.Children.Add(contentPanel);

        Content = rootPanel;
    }

    private void CreateAndStartAnimation(double initialTop)
    {
        // Start fade in animation via timer
        var startTime = DateTime.Now;
        var fadeDuration = TimeSpan.FromMilliseconds(300);
        var targetOpacity = 0.92;
        
        var fadeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Normal, (s, e) =>
        {
            var elapsed = DateTime.Now - startTime;
            var progress = Math.Min(1.0, elapsed.TotalMilliseconds / fadeDuration.TotalMilliseconds);
            
            // Ease out quad
            progress = 1.0 - (1.0 - progress) * (1.0 - progress);
            
            Opacity = targetOpacity * progress;

            if (progress >= 1.0)
            {
                ((DispatcherTimer)s!).Stop();
                StartAutoCloseTimer();
            }
        });
        fadeTimer.Start();

        // Position animation (slide up)
        AnimateSlideUp(initialTop);
    }

    private void AnimateSlideUp(double initialTop)
    {
        var targetTop = Position.Y + 50; // The final target position
        var currentTop = initialTop;
        var elapsed = TimeSpan.Zero;
        var duration = TimeSpan.FromMilliseconds(350);
        var startTime = DateTime.Now;

        var animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Normal, (s, e) =>
        {
            var now = DateTime.Now;
            elapsed = now - startTime;
            var progress = Math.Min(1.0, elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            
            // Ease out quad
            progress = 1.0 - (1.0 - progress) * (1.0 - progress);
            
            currentTop = initialTop + (targetTop - initialTop) * progress;
            Position = new PixelPoint(Position.X, (int)currentTop);

            if (progress >= 1.0)
            {
                ((DispatcherTimer)s!).Stop();
                StartAutoCloseTimer();
            }
        });
        animationTimer.Start();
    }

    private void StartAutoCloseTimer()
    {
        _autoCloseTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (s, e) =>
        {
            if (!_isUserClick)
            {
                Dispatcher.UIThread.Post(() => Close(), DispatcherPriority.Normal);
            }
        });
        _autoCloseTimer.Start();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
    private const int SW_SHOWNORMAL = 1;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isUserClick = true;
        
        // Stop the auto-close timer
        _autoCloseTimer?.Stop();
        
        // Close this popup first
        Close();
        
        // Use the static callback to show the main window
        // This properly handles waking from tray and minimized states
        if (App.ShowMainWindowCallback != null)
        {
            try
            {
                App.ShowMainWindowCallback(_mainVm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show main window: {ex.Message}");
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        base.OnClosed(e);
    }
}