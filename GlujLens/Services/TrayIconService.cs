using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace GlujLens.Services;

/// <summary>
/// Windows Forms-based system tray icon service.
/// </summary>
public class TrayIconService : ITrayIconService
{
    private readonly NotifyIcon? _notifyIcon;
    private readonly ILogger<TrayIconService> _logger;
    private bool _isDisposed;

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        _logger = logger;

        // Create NotifyIcon on the current thread
        // Must be called from a thread with a message loop
        try
        {
            _notifyIcon = new NotifyIcon
            {
                Visible = false,
                Text = "GlujLens",
                Icon = SystemIcons.Application,
            };

            // Wire up click events
            _notifyIcon.DoubleClick += OnIconDoubleClick;
            _notifyIcon.Click += OnIconClick;
            _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;

            _logger.LogInformation("NotifyIcon created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create NotifyIcon");
        }
    }

    public event EventHandler? IconClicked;
    public event EventHandler? IconDoubleClicked;
    public event EventHandler? BalloonTipClicked;
    public event EventHandler<MenuItemClickedEventArgs>? MenuItemClicked;

    public void ShowIcon()
    {
        if (_notifyIcon == null)
        {
            _logger.LogError("NotifyIcon is null, cannot show");
            return;
        }

        try
        {
            _notifyIcon.Visible = true;
            _logger.LogInformation("Tray icon shown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show tray icon");
        }
    }

    public void HideIcon()
    {
        if (_notifyIcon == null) return;

        try
        {
            _notifyIcon.Visible = false;
            _logger.LogDebug("Tray icon hidden");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hide tray icon");
        }
    }

    public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_notifyIcon == null) return;

        try
        {
            _notifyIcon.ShowBalloonTip(1000, title, message, icon);
            _logger.LogDebug("Balloon tip shown: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show balloon tip");
        }
    }

    public void SetContextMenu(ContextMenuStrip menu)
    {
        if (_notifyIcon == null) return;

        try
        {
            _notifyIcon.ContextMenuStrip = menu;
            _logger.LogDebug("Context menu set");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set context menu");
        }
    }

    public void SetIcon(Icon icon)
    {
        if (_notifyIcon == null) return;

        try
        {
            _notifyIcon.Icon = icon;
            _logger.LogDebug("Tray icon set");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set tray icon");
        }
    }

    public void SetTooltip(string text)
    {
        if (_notifyIcon == null) return;

        try
        {
            _notifyIcon.Text = text;
            _logger.LogDebug("Tooltip set: {Text}", text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set tooltip");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_notifyIcon != null)
        {
            try
            {
                _notifyIcon.Dispose();
            }
            catch { }
        }
        _logger.LogDebug("Tray icon disposed");
    }

    private void OnIconClick(object? sender, EventArgs e)
    {
        _logger.LogDebug("Tray icon clicked");
        IconClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnIconDoubleClick(object? sender, EventArgs e)
    {
        _logger.LogDebug("Tray icon double-clicked");
        IconDoubleClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e)
    {
        _logger.LogDebug("Balloon tip clicked");
        BalloonTipClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a context menu with the specified items.
    /// </summary>
    public static ContextMenuStrip CreateContextMenu(Action<string>? onMenuItemClicked)
    {
        var menu = new ContextMenuStrip();

        // Open Application
        var openItem = new ToolStripMenuItem("&Open Application", null, (s, e) =>
        {
            onMenuItemClicked?.Invoke("Open");
        });
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        // Screenshot Capture
        var captureItem = new ToolStripMenuItem("Capture Screenshot", null, (s, e) =>
        {
            onMenuItemClicked?.Invoke("Capture");
        });
        menu.Items.Add(captureItem);

        menu.Items.Add(new ToolStripSeparator());

        // Settings
        var settingsItem = new ToolStripMenuItem("&Settings", null, (s, e) =>
        {
            onMenuItemClicked?.Invoke("Settings");
        });
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // Quit
        var quitItem = new ToolStripMenuItem("E&xit", null, (s, e) =>
        {
            onMenuItemClicked?.Invoke("Quit");
        });
        menu.Items.Add(quitItem);

        return menu;
    }
}
