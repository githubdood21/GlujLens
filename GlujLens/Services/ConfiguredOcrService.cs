using GlujLens.Models;

namespace GlujLens.Services;

/// <summary>
/// Routes OCR requests to the provider selected in settings.
/// </summary>
public sealed class ConfiguredOcrService : IOcrService
{
    private readonly AppSettings _settings;
    private readonly TesseractOcrService _tesseractOcrService;
    private readonly MlNetOcrService _mlNetOcrService;
    private readonly GoogleVisionOcrService _googleVisionOcrService;

    public ConfiguredOcrService(
        AppSettings settings,
        TesseractOcrService tesseractOcrService,
        MlNetOcrService mlNetOcrService,
        GoogleVisionOcrService googleVisionOcrService)
    {
        _settings = settings;
        _tesseractOcrService = tesseractOcrService;
        _mlNetOcrService = mlNetOcrService;
        _googleVisionOcrService = googleVisionOcrService;
    }

    public Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        var provider = _settings.OcrProvider ?? (_settings.UseLocalTesseract ? "Tesseract" : "Disabled");
        provider = provider.Trim();

        if (string.Equals(provider, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new OcrResult { Success = true });
        }

        if (string.Equals(provider, "Tesseract", StringComparison.OrdinalIgnoreCase))
        {
            return _tesseractOcrService.ExtractTextAsync(imageData, cancellationToken);
        }

        if (string.Equals(provider, "ML.NET OCR", StringComparison.OrdinalIgnoreCase))
        {
            return _mlNetOcrService.ExtractTextAsync(imageData, cancellationToken);
        }

        if (string.Equals(provider, "Google Vision", StringComparison.OrdinalIgnoreCase))
        {
            return _googleVisionOcrService.ExtractTextAsync(imageData, cancellationToken);
        }

        return Task.FromResult(new OcrResult
        {
            Success = false,
            ErrorMessage = $"{provider} OCR is not implemented yet"
        });
    }
}
