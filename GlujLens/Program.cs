using Avalonia;

namespace GlujLens;

sealed class Program
{
    // Main thread runs Windows Forms message loop for NotifyIcon
    [STAThread]
    public static void Main(string[] args)
    {
        // Start Avalonia app on a separate thread so it doesn't block
        var avoniaThread = new System.Threading.Thread(() =>
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        })
        {
            IsBackground = true,
            Name = "AvaloniaMain"
        };
        avoniaThread.SetApartmentState(System.Threading.ApartmentState.STA);
        avoniaThread.Start();

        // Main thread runs Windows Forms message loop for NotifyIcon
        // This will exit when Application.Exit() is called
        System.Windows.Forms.Application.Run();
    }

    /// <summary>
    /// Call this to gracefully exit the Forms message loop.
    /// </summary>
    public static void ExitFormsLoop()
    {
        System.Windows.Forms.Application.Exit();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}