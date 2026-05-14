using BergamotTranslatorSharp;
using GlujLens.Models;

namespace GlujLens.Services;

public sealed class BergamotTranslationService : ITranslationService
{
    private readonly AppSettings _settings;
    private readonly object _translatorLock = new();
    private BlockingService? _translator;
    private string? _translatorConfigPath;

    public BergamotTranslationService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task<TranslationBatchResult> TranslateAsync(
        IReadOnlyList<TextRegion> regions,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var modelPath = _settings.BergamotModelPath;
                if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
                {
                    return new TranslationBatchResult
                    {
                        Success = false,
                        ErrorMessage = "No valid Bergamot model is selected."
                    };
                }

                var languagePair = BergamotModelConfig.ReadLanguagePair(modelPath);
                if (string.IsNullOrWhiteSpace(languagePair.Source) || string.IsNullOrWhiteSpace(languagePair.Target))
                {
                    return new TranslationBatchResult
                    {
                        Success = false,
                        ErrorMessage = "Could not determine the Bergamot model language pair from its folder name."
                    };
                }

                var result = new TranslationBatchResult { Success = true };
                var translator = GetOrCreateTranslator(modelPath);

                for (var i = 0; i < regions.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var region = regions[i];
                    if (!TranslationLanguageHeuristics.ShouldTranslate(region, languagePair.Source, languagePair.Target))
                    {
                        continue;
                    }

                    var translatedText = translator.Translate(region.Text)?.Trim();
                    if (string.IsNullOrWhiteSpace(translatedText) ||
                        string.Equals(translatedText, region.Text, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Items.Add(new TranslatedTextItem
                    {
                        RegionIndex = i,
                        SourceText = region.Text,
                        TranslatedText = translatedText
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                return new TranslationBatchResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }, cancellationToken);
    }

    private BlockingService GetOrCreateTranslator(string modelPath)
    {
        var configPath = BergamotModelConfig.EnsureConfigFile(modelPath);

        lock (_translatorLock)
        {
            if (_translator != null &&
                string.Equals(_translatorConfigPath, configPath, StringComparison.OrdinalIgnoreCase))
            {
                return _translator;
            }

            _translator?.Dispose();
            _translator = new BlockingService(configPath);
            _translatorConfigPath = configPath;
            return _translator;
        }
    }
}
