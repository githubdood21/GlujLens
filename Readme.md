# GlujLens

GlujLens is a modern .NET 9 desktop application for text extraction and translation from screenshots. It leverages Avalonia UI for cross-platform compatibility, Microsoft ML.NET for OCR, and Windows Forms NotifyIcon for system tray integration.

## Features

- **Screenshot Capture**: Capture screen regions for text extraction
- **Multi-language OCR**: Extract text in English, German, Russian, Japanese, and more using Microsoft ML.NET
- **Translation**: Translate extracted text to target languages
- **System Tray Icon**: Background-only startup with tray icon and context menu
- **Settings Management**: Configurable via `appsettings.json`
- **Multi-language Support**: Configurable source and target languages

## Technology Stack

- **.NET 9.0** - Latest LTS runtime
- **Avalonia UI 11.2** - Cross-platform desktop UI framework
- **CommunityToolkit.Mvvm** - MVVM framework with source generators
- **Microsoft.Extensions.DependencyInjection** - Built-in DI container
- **System.Windows.Forms (NotifyIcon)** - System tray icon support
- **SkiaSharp** - Graphics rendering
- **Microsoft ML.NET** - Machine learning for OCR
- **System.Drawing.Common** - GDI+ imaging support

## Project Structure

```
GlujLens/
├── Models/
│   ├── AppSettings.cs          # Application configuration model
│   ├── CaptureResult.cs        # Screenshot capture result model
│   ├── TextRegion.cs           # Text region model
│   └── DetectedObject.cs       # Detected object model
├── ViewModels/
│   ├── MainViewModel.cs        # Main application view model
│   ├── CaptureViewModel.cs     # Screenshot capture view model
│   ├── EditorViewModel.cs      # Text editing view model
│   └── SettingsViewModel.cs    # Settings view model
├── Views/
│   ├── MainWindow.axaml        # Main window XAML markup
│   └── MainWindow.axaml.cs     # Main window code-behind
├── Services/
│   ├── IScreenshotService.cs   # Screenshot service interface
│   ├── ScreenshotService.cs    # Screenshot capture implementation
│   ├── ITrayIconService.cs     # Tray icon service interface
│   └── TrayIconService.cs      # Tray icon implementation
├── App.axaml                   # Application resources
├── App.axaml.cs                # Application lifecycle
├── Program.cs                  # Entry point
└── GlujLens.csproj              # Project file
```

## System Tray

The application launches in the background with only a tray icon visible (no main window). Right-click the tray icon to access:

- **Open Application** - Show the main window
- **📷 Capture Screenshot** - Start a new screenshot capture
- **Settings** - Open settings dialog
- **Exit** - Gracefully close the application

When you close the main window, the application minimizes to the tray and shows a notification (throttled to once per 5 seconds).

## Architecture

The application uses a dual-thread approach:
- **Main thread**: Runs Windows Forms message loop for `NotifyIcon`
- **Background thread**: Runs Avalonia UI for the main window

## Building and Running

### Prerequisites

- .NET 9.0 SDK
- Windows 10/11 (for full tray icon support)

### Build

```bash
dotnet restore
dotnet build
```

### Run

```bash
dotnet run --project GlujLens/GlujLens.csproj
```

### Publish

```bash
dotnet publish GlujLens/GlujLens.csproj -c Release -r win-x64 --self-contained false
```

## Configuration

Edit `appsettings.json` to configure:

- `SourceLanguage`: Default source text language (e.g., en-US, de-DE, ru-RU, ja-JP)
- `TargetLanguage`: Default translation target language
- `SavePath`: Directory for saved screenshots

## Architecture Overview

The application follows the MVVM pattern:

1. **Models** - Data structures and configuration
2. **ViewModels** - Business logic and UI state
3. **Views** - XAML-based UI components
4. **Services** - Cross-cutting concerns (screenshot, tray icon)

Dependency injection is configured in `App.axaml.cs` using Microsoft.Extensions.DependencyInjection.

## License

MIT