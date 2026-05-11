using System.Drawing;
using System.Windows.Forms;

namespace GlujLens.Services;

/// <summary>
/// Service interface for managing the system tray icon.
/// </summary>
public interface ITrayIconService
{
    /// <summary>
    /// Shows the tray icon.
    /// </summary>
    void ShowIcon();

    /// <summary>
    /// Hides the tray icon.
    /// </summary>
    void HideIcon();

    /// <summary>
    /// Shows a balloon tip notification.
    /// </summary>
    /// <param name="title">Title of the notification.</param>
    /// <param name="message">Message text.</param>
    /// <param name="icon">Icon type for the notification.</param>
    void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info);

    /// <summary>
    /// Sets the context menu for the tray icon.
    /// </summary>
    void SetContextMenu(ContextMenuStrip menu);

    /// <summary>
    /// Sets the icon displayed in the tray.
    /// </summary>
    void SetIcon(Icon icon);

    /// <summary>
    /// Sets the tooltip text shown on hover.
    /// </summary>
    void SetTooltip(string text);

    /// <summary>
    /// Event raised when the tray icon is clicked.
    /// </summary>
    event EventHandler IconClicked;

    /// <summary>
    /// Event raised when the tray icon is double-clicked.
    /// </summary>
    event EventHandler IconDoubleClicked;

    /// <summary>
    /// Event raised when a menu item is clicked.
    /// </summary>
    event EventHandler<MenuItemClickedEventArgs> MenuItemClicked;

    /// <summary>
    /// Disposes the tray icon resources.
    /// </summary>
    void Dispose();
}

/// <summary>
/// Event arguments for menu item clicks.
/// </summary>
public class MenuItemClickedEventArgs : EventArgs
{
    public string MenuItemName { get; }

    public MenuItemClickedEventArgs(string menuItemName)
    {
        MenuItemName = menuItemName;
    }
}