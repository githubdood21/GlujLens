using System.Drawing;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Windows.Forms;
using GlujLens.Models;
using GlujLens.Services;
using GlujLens.ViewModels;
using WinScreen = System.Windows.Forms.Screen;

namespace GlujLens.Views;

/// <summary>
/// WinForms notification popup that displays a captured screenshot preview.
/// Keeping this out of Avalonia avoids desktop lifetime shutdown when the tray-only app has no main window open.
/// </summary>
public class NotificationPopup : Form
{
    private readonly MainViewModel _mainVm;
    private readonly System.Windows.Forms.Timer _autoCloseTimer;
    private readonly Image? _previewImage;
    private bool _isUserClick;

    public NotificationPopup(MainViewModel mainVm, ITrayIconService trayIcon, AppSettings settings, byte[] imageData, int width, int height)
    {
        _mainVm = mainVm;
        _previewImage = CreatePreviewImage(imageData);

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(30, 30, 30);
        ClientSize = new Size(320, 280);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        PositionNearTray();
        BuildContent(width, height);

        _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            if (!_isUserClick)
            {
                Close();
            }
        };

        Shown += (_, _) => _autoCloseTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;

            var createParams = base.CreateParams;
            createParams.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return createParams;
        }
    }

    private void PositionNearTray()
    {
        var workingArea = WinScreen.PrimaryScreen?.WorkingArea ?? WinScreen.AllScreens[0].WorkingArea;
        Location = new Point(workingArea.Right - Width - 20, workingArea.Top + 20);
    }

    private void BuildContent(int width, int height)
    {
        var shell = new RoundedPanel
        {
            BackColor = Color.FromArgb(30, 30, 30),
            BorderColor = Color.FromArgb(60, 60, 60),
            BorderRadius = 8,
            Bounds = new Rectangle(8, 8, ClientSize.Width - 16, ClientSize.Height - 16),
            Cursor = Cursors.Hand
        };
        shell.Click += OnPopupClick;
        Controls.Add(shell);

        var title = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(14, 12),
            Size = new Size(230, 22),
            Text = "Screenshot captured",
            TextAlign = ContentAlignment.MiddleLeft
        };
        title.Click += OnPopupClick;
        shell.Controls.Add(title);

        var closeButton = new Button
        {
            BackColor = Color.FromArgb(50, 50, 50),
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(170, 170, 170),
            Location = new Point(shell.Width - 36, 12),
            Size = new Size(22, 22),
            TabStop = false,
            Text = "X"
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.Click += (_, _) =>
        {
            _isUserClick = true;
            Close();
        };
        shell.Controls.Add(closeButton);

        var dimensions = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.FromArgb(180, 180, 180),
            Location = new Point(14, 34),
            Size = new Size(230, 18),
            Text = $"{width} x {height}",
            TextAlign = ContentAlignment.MiddleLeft
        };
        dimensions.Click += OnPopupClick;
        shell.Controls.Add(dimensions);

        var previewBox = new PictureBox
        {
            BackColor = Color.FromArgb(40, 40, 40),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Image = _previewImage,
            Location = new Point(14, 60),
            Size = new Size(shell.Width - 28, 160),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        previewBox.Click += OnPopupClick;
        shell.Controls.Add(previewBox);

        var hint = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.FromArgb(130, 130, 130),
            Location = new Point(14, 228),
            Size = new Size(230, 18),
            Text = "Click to open",
            TextAlign = ContentAlignment.MiddleLeft
        };
        hint.Click += OnPopupClick;
        shell.Controls.Add(hint);
    }

    private static Image? CreatePreviewImage(byte[] imageData)
    {
        try
        {
            using var stream = new MemoryStream(imageData);
            using var sourceImage = Image.FromStream(stream);
            return new Bitmap(sourceImage);
        }
        catch
        {
            return null;
        }
    }

    private void OnPopupClick(object? sender, EventArgs e)
    {
        _isUserClick = true;
        _autoCloseTimer.Stop();
        Close();

        try
        {
            App.ShowMainWindowCallback?.Invoke(_mainVm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show main window: {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoCloseTimer.Dispose();
            _previewImage?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class RoundedPanel : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BorderRadius { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectangle(ClientRectangle, BorderRadius);
            using var borderPen = new Pen(BorderColor);

            Region = new Region(path);
            e.Graphics.DrawPath(borderPen, path);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
