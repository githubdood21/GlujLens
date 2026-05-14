using GlujLens.Models;

namespace GlujLens.Services;

public sealed class MlNetOcrService : IOcrService
{
    private readonly AppSettings _settings;
    private readonly MlNetOcrModelCatalog _modelCatalog;
    private readonly PaddleOnnxOcrRunner _paddleOcrRunner;
    private readonly MlNetOcrAutoAcceleratorService _autoAcceleratorService;

    public MlNetOcrService(
        AppSettings settings,
        MlNetOcrModelCatalog modelCatalog,
        PaddleOnnxOcrRunner paddleOcrRunner,
        MlNetOcrAutoAcceleratorService autoAcceleratorService)
    {
        _settings = settings;
        _modelCatalog = modelCatalog;
        _paddleOcrRunner = paddleOcrRunner;
        _autoAcceleratorService = autoAcceleratorService;
    }

    public Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var model = _modelCatalog.GetSelectedModel();
                if (model == null)
                {
                    return new OcrResult
                    {
                        Success = false,
                        ErrorMessage = $"No ML.NET OCR model was found. Add a .onnx or .zip model under {ModelStoragePaths.MlNetOcrDirectory}."
                    };
                }

                var paddleModel = _modelCatalog.GetSelectedPaddleOcrModel();
                if (paddleModel != null)
                {
                    return _paddleOcrRunner.Run(
                        paddleModel,
                        imageData,
                        _autoAcceleratorService.ResolveEffectiveAccelerator(cancellationToken),
                        cancellationToken);
                }

                if (model.OnnxModelPaths.Count == 0)
                {
                    return new OcrResult
                    {
                        Success = false,
                        ErrorMessage = $"{model.DisplayName} does not contain runnable ONNX files yet. ML.NET .zip model loading is planned next."
                    };
                }

                return new OcrResult
                {
                    Success = false,
                    ErrorMessage = $"{model.DisplayName} is not a supported ML.NET OCR layout yet. Select a PaddleOCR ONNX folder containing detection/*/det.onnx and languages/*/rec.onnx plus dict.txt."
                };
            }
            catch (Exception ex)
            {
                return new OcrResult
                {
                    Success = false,
                    ErrorMessage = $"ML.NET OCR inference failed: {ex.Message}"
                };
            }
        }, cancellationToken);
    }
}
