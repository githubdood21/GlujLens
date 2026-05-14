using GlujLens.Models;

namespace GlujLens.Services;

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken = default);
}
