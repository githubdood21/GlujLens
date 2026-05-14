# GlujLens

GlujLens is a Windows desktop screenshot utility for capturing screens, running OCR, translating detected text, and selecting extracted text directly from the screenshot preview. It uses Avalonia for the UI, a Windows tray icon for background use, and configurable OCR/translation providers.

## Current Features

- **Tray-first workflow**: GlujLens can run in the background with a tray icon and context menu.
- **Configurable global hotkey**: Record a capture shortcut from Settings.
- **Mouse-aware monitor capture**: A normal capture grabs the display that currently contains the mouse cursor.
- **All-display long press**: Hold the capture hotkey for 3 seconds to capture all active displays as one image, preserving their virtual desktop positions.
- **Screenshot preview**: Captured images are shown in the main window with capture dimensions and elapsed capture time.
- **Save and clipboard actions**: Save screenshots to the configured directory or copy the latest screenshot to the clipboard.
- **Multiple OCR providers**: Run OCR using Tesseract, ML.NET OCR, or Google Vision.
- **OCR text overlays**: Detected text regions are highlighted on top of the screenshot; clicking a highlight selects and copies that text.
- **Translation overlay**: Translate detected foreign-language regions and render translated text over the original screenshot location.
- **Selection inspector**: Click a detected text region to inspect the original text and manually translate that region without changing the screenshot overlay.
- **Phrase merging controls**: Tune Tesseract horizontal word gap and vertical line tolerance from Settings.
- **Automatic settings persistence**: Settings are saved to `settings.json` in the app output folder.

## OCR Notes

The implemented OCR providers are Tesseract, ML.NET OCR, and Google Vision.

Tesseract runs through the `Tesseract` NuGet package and native `tesseract50.dll` / `leptonica-1.82.0.dll` binaries. It is the lightweight fallback provider. GlujLens runs Tesseract through one serialized local engine. GPU acceleration is not currently used.

ML.NET OCR is a managed local OCR backend that loads PaddleOCR ONNX model artifacts from `models/ocr/mlnet`. The recommended model source is [monkt/paddleocr-onnx](https://huggingface.co/monkt/paddleocr-onnx). The OCR accelerator can be set to `Auto`, `CPU`, or `DirectML`. `Auto` skips DirectML below 2 GB detected adapter RAM, otherwise benchmarks CPU versus DirectML once per hardware/model signature and caches the faster choice in settings.

Google Vision uses the Google Cloud Vision API `images:annotate` endpoint with `DOCUMENT_TEXT_DETECTION`. It requires a Google Vision API key in Settings and sends the captured image to Google for OCR.

Model and language data are centralized under the app's default `models` folder next to the running app executable. GlujLens creates the default model folders at startup if they do not exist.

```text
models/
  ocr/
    tesseract/
      tessdata/
    mlnet/
  translation/
    bergamot/
    directml-onnx/
  language-detection/
```

Tesseract language data is discovered from `models/ocr/tesseract/tessdata` by scanning for `*.traineddata` files. Exact traineddata names such as `eng_std` are supported.

## Translation Notes

Translation is provider-based. Bergamot runs through `BergamotTranslatorSharp`; DirectML ONNX is planned as the Windows GPU-friendly local backend.

Bergamot is local and CPU-friendly. It uses a selected model folder to determine the source and target language pair, for example `ja-en-base`. GlujLens can scan a parent models folder, populate a model dropdown, and run a small Bergamot test window before using the model in the screenshot workflow.

The screenshot-wide translation action translates only OCR regions that appear to match the selected model's source language, then renders the translated text over the detected region. The original screenshot pixels are not permanently modified yet; GlujLens currently uses a visual replacement overlay with sampled background color and fitted text size.

The inspector panel can translate only the currently selected OCR region. This manual translation does not affect the screenshot overlay.

DirectML ONNX support will use ONNX Runtime with the DirectML execution provider. The default DirectML ONNX folder is `models/translation/directml-onnx` and is intended to contain exported ONNX translation models plus matching tokenizer files.

When DirectML ONNX translation is implemented, the source language can be set to `auto` so GlujLens can detect the source language from grouped OCR text before choosing a matching local model.

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
- ML.NET OCR model folder and selected model
- ML.NET OCR accelerator (`Auto`, `CPU`, `DirectML`)
- Translation provider
- Bergamot models folder and selected model
- DirectML ONNX model folder
- Translation source language (`auto` planned for DirectML ONNX)
- Google Translation API key placeholder

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
- **Microsoft.ML.OnnxRuntime.DirectML**
- **BergamotTranslatorSharp 0.3.4 NuGet package**
- **SkiaSharp**

## Planned / Experimental

- **ML.NET OCR backend**: PaddleOCR ONNX detection/recognition inference is wired through ONNX Runtime with `Auto`, `CPU`, and `DirectML` acceleration. The first pass uses DB-map connected-component boxes, axis-aligned crops, and CTC decoding from `dict.txt`; rotated quadrilateral crops and stronger DB post-processing are planned next.
- **DirectML ONNX translation**: Planned local translation backend using ONNX Runtime and DirectML for broader Windows GPU support.
- **Additional translation providers**: Google API translation is present in Settings as a planned provider.
- **Model download manager**: A built-in manager for downloading, organizing, and selecting model files for multiple OCR and translation providers.
- **Better visual replacement**: Improved text removal/replacement, including stronger background cleanup, blur/inpaint-style fills, better color sampling, and better multi-line text fitting.

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

Apache-2.0
