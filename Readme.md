# GlujLens

GlujLens is a Windows desktop screenshot utility for capturing screens, running local OCR, and selecting extracted text directly from the screenshot preview. It uses Avalonia for the UI, a Windows tray icon for background use, and Tesseract for OCR.

## Current Features

- **Tray-first workflow**: GlujLens can run in the background with a tray icon and context menu.
- **Configurable global hotkey**: Record a capture shortcut from Settings.
- **Mouse-aware monitor capture**: A normal capture grabs the display that currently contains the mouse cursor.
- **All-display long press**: Hold the capture hotkey for 3 seconds to capture all active displays as one image, preserving their virtual desktop positions.
- **Screenshot preview**: Captured images are shown in the main window with capture dimensions and elapsed capture time.
- **Save and clipboard actions**: Save screenshots to the configured directory or copy the latest screenshot to the clipboard.
- **Local Tesseract OCR**: Run OCR on the latest screenshot using `.traineddata` files from the configured tessdata folder.
- **OCR text overlays**: Detected text regions are highlighted on top of the screenshot; clicking a highlight selects and copies that text.
- **Phrase merging controls**: Tune Tesseract horizontal word gap and vertical line tolerance from Settings.
- **Automatic settings persistence**: Settings are saved to `settings.json` in the app output folder.

## OCR Notes

The implemented OCR provider is Tesseract. Google Vision and PaddleOCR appear as placeholders in Settings, but they are not implemented yet.

Tesseract runs through the `Tesseract` NuGet package and native `tesseract50.dll` / `leptonica-1.82.0.dll` binaries. GlujLens caches the Tesseract engine between OCR runs and sets OpenMP thread-count hints at startup. GPU acceleration is not currently used.

Language data is discovered from the configured tessdata folder by scanning for `*.traineddata` files. Exact traineddata names such as `eng_std` are supported.

## Hotkey Behavior

- **Short press**: Capture the monitor under the mouse cursor.
- **Hold for 3 seconds**: Capture all active monitors into one virtual-desktop image.

Default shortcut:

```text
Alt+Ctrl+Q
```

You can change it in `Settings > Capture`.

## Settings

Settings are available from the main window or tray menu. Key options include:

- Default save directory
- Image format and quality
- Capture shortcut
- Copy to clipboard after capture
- Show notification after capture
- OCR provider
- Tesseract tessdata folder
- Tesseract language data
- Tesseract text merge controls
- Target language placeholder

The settings file is written to:

```text
GlujLens/bin/<configuration>/<target-framework>/settings.json
```

## Technology Stack

- **.NET 9**
- **Avalonia UI 11**
- **CommunityToolkit.Mvvm**
- **Microsoft.Extensions.DependencyInjection**
- **System.Windows.Forms NotifyIcon**
- **System.Drawing / GDI screen capture**
- **Tesseract 5.2.0 NuGet package**
- **SkiaSharp**

## Project Structure

```text
GlujLens/
|-- Models/
|   |-- AppSettings.cs
|   |-- CaptureResult.cs
|   |-- OcrResult.cs
|   `-- TextRegion.cs
|-- Services/
|   |-- HotkeyService.cs
|   |-- ScreenshotService.cs
|   |-- TesseractOcrService.cs
|   `-- TrayIconService.cs
|-- ViewModels/
|   |-- MainViewModel.cs
|   `-- SettingsViewModel.cs
|-- Views/
|   |-- MainWindow.axaml
|   |-- NotificationPopup.axaml
|   `-- SettingsView.axaml
|-- App.axaml
|-- Program.cs
`-- GlujLens.csproj
```

## Build

Prerequisites:

- Windows 10/11
- .NET 9 SDK

Restore and build:

```powershell
dotnet restore
dotnet build GlujLens.sln
```

Run:

```powershell
dotnet run --project GlujLens/GlujLens.csproj
```

Publish:

```powershell
dotnet publish GlujLens/GlujLens.csproj -c Release -r win-x64 --self-contained false
```

## License

MIT
