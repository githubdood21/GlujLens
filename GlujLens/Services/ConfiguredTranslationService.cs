using GlujLens.Models;

namespace GlujLens.Services;

public sealed class ConfiguredTranslationService : ITranslationService
{
    private readonly AppSettings _settings;
    private readonly BergamotTranslationService _bergamotTranslationService;

    public ConfiguredTranslationService(
        AppSettings settings,
        BergamotTranslationService bergamotTranslationService)
    {
        _settings = settings;
        _bergamotTranslationService = bergamotTranslationService;
    }

    public Task<TranslationBatchResult> TranslateAsync(
        IReadOnlyList<TextRegion> regions,
        CancellationToken cancellationToken = default)
    {
        var provider = _settings.TranslationProvider?.Trim() ?? "Disabled";
        if (string.Equals(provider, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new TranslationBatchResult
            {
                Success = false,
                ErrorMessage = "Translation is disabled."
            });
        }

        if (string.Equals(provider, "Bergamot", StringComparison.OrdinalIgnoreCase))
        {
            return _bergamotTranslationService.TranslateAsync(regions, cancellationToken);
        }

        return Task.FromResult(new TranslationBatchResult
        {
            Success = false,
            ErrorMessage = $"{provider} translation is not implemented yet."
        });
    }
}
