using GlujLens.Models;
using Tesseract;

namespace GlujLens.Services;

/// <summary>
/// Local OCR provider backed by Tesseract.
/// </summary>
public class TesseractOcrService : IOcrService, IDisposable
{
    private const double HorizontalMergeGapHeightMultiplier = 1.6;
    private const double SameLineCenterToleranceMultiplier = 0.55;

    private readonly AppSettings _settings;
    private readonly object _engineLock = new();
    private TesseractEngine? _engine;
    private string? _engineTessdataPath;
    private string? _engineLanguage;
    private bool _disposed;

    static TesseractOcrService()
    {
        var threadCount = Math.Clamp(Environment.ProcessorCount, 1, 8).ToString();

        SetEnvironmentVariableIfMissing("OMP_THREAD_LIMIT", threadCount);
        SetEnvironmentVariableIfMissing("OMP_NUM_THREADS", threadCount);
        SetEnvironmentVariableIfMissing("OMP_DYNAMIC", "FALSE");
    }

    public TesseractOcrService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        if (!IsTesseractEnabled())
        {
            return Task.FromResult(new OcrResult { Success = true });
        }

        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tessdataPath = GetTessdataPath();
                if (!Directory.Exists(tessdataPath))
                {
                    return new OcrResult
                    {
                        Success = false,
                        ErrorMessage = $"Tesseract tessdata folder not found: {tessdataPath}"
                    };
                }

                var language = ResolveLanguage(tessdataPath, _settings.SourceLanguage);
                var trainedDataFile = Path.Combine(tessdataPath, $"{language}.traineddata");
                if (!File.Exists(trainedDataFile))
                {
                    return new OcrResult
                    {
                        Success = false,
                        ErrorMessage = $"Tesseract language data not found: {trainedDataFile}"
                    };
                }

                using var image = Pix.LoadFromMemory(imageData);

                lock (_engineLock)
                {
                    var engine = GetOrCreateEngine(tessdataPath, language);
                    using var page = engine.Process(image, PageSegMode.Auto);

                    var result = new OcrResult
                    {
                        Success = true,
                        Text = page.GetText()?.Trim() ?? string.Empty
                    };

                    CollectTextRegions(
                        page,
                        result,
                        language,
                        _settings.TesseractHorizontalMergeGap,
                        _settings.TesseractVerticalMergeTolerance);
                    return result;
                }
            }
            catch (Exception ex)
            {
                return new OcrResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }, cancellationToken);
    }

    private bool IsTesseractEnabled()
    {
        var provider = _settings.OcrProvider ?? (_settings.UseLocalTesseract ? "Tesseract" : "Disabled");
        return string.Equals(provider, "Tesseract", StringComparison.OrdinalIgnoreCase);
    }

    private TesseractEngine GetOrCreateEngine(string tessdataPath, string language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_engine != null &&
            string.Equals(_engineTessdataPath, tessdataPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_engineLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            return _engine;
        }

        _engine?.Dispose();
        _engine = new TesseractEngine(tessdataPath, language, EngineMode.LstmOnly);
        _engineTessdataPath = tessdataPath;
        _engineLanguage = language;
        return _engine;
    }

    private string GetTessdataPath()
    {
        return string.IsNullOrWhiteSpace(_settings.TesseractDataPath)
            ? ModelStoragePaths.TesseractDirectory
            : _settings.TesseractDataPath;
    }

    private static string MapLanguage(string? language)
    {
        return (language ?? "en-US").Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" or "en-gb" => "eng",
            "de" or "de-de" => "deu",
            "pl" or "pl-pl" => "pol",
            "ru" or "ru-ru" => "rus",
            "ja" or "ja-jp" => "jpn",
            "es" or "es-es" or "es-mx" => "spa",
            "fr" or "fr-fr" => "fra",
            "it" or "it-it" => "ita",
            "pt" or "pt-br" or "pt-pt" => "por",
            var value when value.Length == 3 => value,
            var value when !string.IsNullOrWhiteSpace(value) => value,
            _ => "eng"
        };
    }

    private static string ResolveLanguage(string tessdataPath, string? sourceLanguage)
    {
        var exactLanguage = sourceLanguage?.Trim();
        if (!string.IsNullOrWhiteSpace(exactLanguage) &&
            File.Exists(Path.Combine(tessdataPath, $"{exactLanguage}.traineddata")))
        {
            return exactLanguage;
        }

        return MapLanguage(sourceLanguage);
    }

    private static void SetEnvironmentVariableIfMissing(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static void CollectTextRegions(
        Page page,
        OcrResult result,
        string language,
        int horizontalMergeGap,
        int verticalMergeTolerance)
    {
        var wordRegions = new List<TextRegion>();
        using var iterator = page.GetIterator();
        iterator.Begin();

        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                continue;

            wordRegions.Add(new TextRegion
            {
                Text = text,
                Confidence = iterator.GetConfidence(PageIteratorLevel.Word) / 100f,
                X = bounds.X1,
                Y = bounds.Y1,
                Width = bounds.X2 - bounds.X1,
                Height = bounds.Y2 - bounds.Y1,
                Language = language
            });
        }
        while (iterator.Next(PageIteratorLevel.Word));

        foreach (var region in MergeWordsIntoPhrases(wordRegions, horizontalMergeGap, verticalMergeTolerance))
        {
            result.TextRegions.Add(region);
        }
    }

    private static IEnumerable<TextRegion> MergeWordsIntoPhrases(
        IEnumerable<TextRegion> wordRegions,
        int horizontalMergeGap,
        int verticalMergeTolerance)
    {
        horizontalMergeGap = Math.Clamp(horizontalMergeGap, 0, 200);
        verticalMergeTolerance = Math.Clamp(verticalMergeTolerance, 0, 100);

        var sortedWords = wordRegions
            .Where(region => !string.IsNullOrWhiteSpace(region.Text) && region.Width > 0 && region.Height > 0)
            .OrderBy(region => region.Y)
            .ThenBy(region => region.X)
            .ToList();

        var currentPhrase = new List<TextRegion>();
        foreach (var word in sortedWords)
        {
            if (currentPhrase.Count == 0)
            {
                currentPhrase.Add(word);
                continue;
            }

            var previousWord = currentPhrase[^1];
            var horizontalGap = word.X - (previousWord.X + previousWord.Width);
            var maxAllowedGap = Math.Max(
                horizontalMergeGap,
                (int)Math.Round(Math.Max(previousWord.Height, word.Height) * HorizontalMergeGapHeightMultiplier));

            if (IsOnSameLine(previousWord, word, verticalMergeTolerance) &&
                horizontalGap >= 0 &&
                horizontalGap <= maxAllowedGap)
            {
                currentPhrase.Add(word);
                continue;
            }

            yield return MergePhrase(currentPhrase);
            currentPhrase.Clear();
            currentPhrase.Add(word);
        }

        if (currentPhrase.Count > 0)
        {
            yield return MergePhrase(currentPhrase);
        }
    }

    private static bool IsOnSameLine(TextRegion left, TextRegion right, int verticalMergeTolerance)
    {
        var leftCenterY = left.Y + left.Height / 2.0;
        var rightCenterY = right.Y + right.Height / 2.0;
        var tolerance = Math.Max(
            verticalMergeTolerance,
            Math.Max(left.Height, right.Height) * SameLineCenterToleranceMultiplier);
        return Math.Abs(leftCenterY - rightCenterY) <= tolerance;
    }

    private static TextRegion MergePhrase(IReadOnlyList<TextRegion> words)
    {
        var x1 = words.Min(word => word.X);
        var y1 = words.Min(word => word.Y);
        var x2 = words.Max(word => word.X + word.Width);
        var y2 = words.Max(word => word.Y + word.Height);

        return new TextRegion
        {
            Text = string.Join(" ", words.Select(word => word.Text)),
            Confidence = words.Average(word => word.Confidence),
            X = x1,
            Y = y1,
            Width = x2 - x1,
            Height = y2 - y1,
            Language = words.FirstOrDefault()?.Language
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_engineLock)
        {
            if (_disposed)
                return;

            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }
}
