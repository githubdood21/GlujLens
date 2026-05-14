using GlujLens.Models;

namespace GlujLens.Services;

public interface ITranslationService
{
    Task<TranslationBatchResult> TranslateAsync(
        IReadOnlyList<TextRegion> regions,
        CancellationToken cancellationToken = default);
}
