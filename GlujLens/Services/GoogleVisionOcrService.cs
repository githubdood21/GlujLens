using System.Net.Http;
using System.Text;
using System.Text.Json;
using GlujLens.Models;

namespace GlujLens.Services;

/// <summary>
/// Cloud OCR provider backed by Google Cloud Vision API.
/// </summary>
public sealed class GoogleVisionOcrService : IOcrService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly AppSettings _settings;

    public GoogleVisionOcrService(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.GoogleVisionApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = "Google Vision API key is not configured."
            };
        }

        try
        {
            using var requestContent = new StringContent(
                BuildRequestJson(imageData, _settings.SourceLanguage),
                Encoding.UTF8,
                "application/json");

            var requestUri = $"https://vision.googleapis.com/v1/images:annotate?key={Uri.EscapeDataString(apiKey)}";
            using var response = await HttpClient.PostAsync(requestUri, requestContent, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new OcrResult
                {
                    Success = false,
                    ErrorMessage = TryReadGoogleError(responseJson) ?? $"Google Vision request failed: {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }

            return ParseResponse(responseJson, _settings.SourceLanguage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string BuildRequestJson(byte[] imageData, string? sourceLanguage)
    {
        var languageHint = NormalizeLanguageHint(sourceLanguage);
        var request = new
        {
            requests = new[]
            {
                new
                {
                    image = new
                    {
                        content = Convert.ToBase64String(imageData)
                    },
                    features = new[]
                    {
                        new
                        {
                            type = "DOCUMENT_TEXT_DETECTION",
                            maxResults = 1
                        }
                    },
                    imageContext = string.IsNullOrWhiteSpace(languageHint)
                        ? null
                        : new
                        {
                            languageHints = new[] { languageHint }
                        }
                }
            }
        };

        return JsonSerializer.Serialize(request);
    }

    private static OcrResult ParseResponse(string responseJson, string? fallbackLanguage)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var response = root.GetProperty("responses").EnumerateArray().FirstOrDefault();

        if (response.ValueKind == JsonValueKind.Undefined)
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = "Google Vision response did not contain an OCR result."
            };
        }

        if (response.TryGetProperty("error", out var error))
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = ReadStatusMessage(error) ?? "Google Vision returned an OCR error."
            };
        }

        var result = new OcrResult { Success = true };
        if (response.TryGetProperty("fullTextAnnotation", out var fullTextAnnotation))
        {
            if (fullTextAnnotation.TryGetProperty("text", out var fullText))
            {
                result.Text = fullText.GetString()?.Trim() ?? string.Empty;
            }

            AddWordRegions(fullTextAnnotation, result, fallbackLanguage);
        }

        if (result.TextRegions.Count == 0 && response.TryGetProperty("textAnnotations", out var textAnnotations))
        {
            AddTextAnnotationRegions(textAnnotations, result, fallbackLanguage);
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            result.Text = string.Join(Environment.NewLine, result.TextRegions.Select(region => region.Text));
        }

        return result;
    }

    private static void AddWordRegions(JsonElement fullTextAnnotation, OcrResult result, string? fallbackLanguage)
    {
        if (!fullTextAnnotation.TryGetProperty("pages", out var pages))
        {
            return;
        }

        foreach (var page in pages.EnumerateArray())
        foreach (var block in EnumerateArrayProperty(page, "blocks"))
        foreach (var paragraph in EnumerateArrayProperty(block, "paragraphs"))
        foreach (var word in EnumerateArrayProperty(paragraph, "words"))
        {
            var text = ReadWordText(word);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var bounds = ReadBoundingBox(word);
            if (bounds is null)
            {
                continue;
            }

            result.TextRegions.Add(new TextRegion
            {
                Text = text,
                Confidence = ReadConfidence(word),
                X = bounds.Value.X,
                Y = bounds.Value.Y,
                Width = bounds.Value.Width,
                Height = bounds.Value.Height,
                Language = ReadDetectedLanguage(word) ?? NormalizeLanguageHint(fallbackLanguage)
            });
        }
    }

    private static void AddTextAnnotationRegions(JsonElement textAnnotations, OcrResult result, string? fallbackLanguage)
    {
        var index = 0;
        foreach (var annotation in textAnnotations.EnumerateArray())
        {
            var text = annotation.TryGetProperty("description", out var description)
                ? description.GetString()?.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (index++ == 0)
            {
                result.Text = text;
                continue;
            }

            var bounds = ReadBoundingBox(annotation);
            if (bounds is null)
            {
                continue;
            }

            result.TextRegions.Add(new TextRegion
            {
                Text = text,
                Confidence = annotation.TryGetProperty("score", out var score) ? score.GetSingle() : 1f,
                X = bounds.Value.X,
                Y = bounds.Value.Y,
                Width = bounds.Value.Width,
                Height = bounds.Value.Height,
                Language = NormalizeLanguageHint(fallbackLanguage)
            });
        }
    }

    private static IEnumerable<JsonElement> EnumerateArrayProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
    }

    private static string ReadWordText(JsonElement word)
    {
        if (!word.TryGetProperty("symbols", out var symbols))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var symbol in symbols.EnumerateArray())
        {
            if (symbol.TryGetProperty("text", out var text))
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString().Trim();
    }

    private static OcrBounds? ReadBoundingBox(JsonElement element)
    {
        if (!element.TryGetProperty("boundingBox", out var boundingBox) &&
            !element.TryGetProperty("boundingPoly", out boundingBox))
        {
            return null;
        }

        if (!boundingBox.TryGetProperty("vertices", out var vertices))
        {
            return null;
        }

        var points = vertices
            .EnumerateArray()
            .Select(vertex => new
            {
                X = vertex.TryGetProperty("x", out var x) ? x.GetInt32() : 0,
                Y = vertex.TryGetProperty("y", out var y) ? y.GetInt32() : 0
            })
            .ToList();

        if (points.Count == 0)
        {
            return null;
        }

        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);

        return new OcrBounds(minX, minY, maxX - minX, maxY - minY);
    }

    private static float ReadConfidence(JsonElement element)
    {
        return element.TryGetProperty("confidence", out var confidence)
            ? Math.Clamp(confidence.GetSingle(), 0f, 1f)
            : 1f;
    }

    private static string? ReadDetectedLanguage(JsonElement element)
    {
        if (!element.TryGetProperty("property", out var property) ||
            !property.TryGetProperty("detectedLanguages", out var detectedLanguages))
        {
            return null;
        }

        return detectedLanguages
            .EnumerateArray()
            .Select(language => language.TryGetProperty("languageCode", out var code) ? code.GetString() : null)
            .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code));
    }

    private static string? TryReadGoogleError(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            return document.RootElement.TryGetProperty("error", out var error)
                ? ReadStatusMessage(error)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadStatusMessage(JsonElement status)
    {
        return status.TryGetProperty("message", out var message)
            ? message.GetString()
            : null;
    }

    private static string? NormalizeLanguageHint(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var trimmed = language.Trim();
        var dashIndex = trimmed.IndexOf('-');
        return dashIndex > 0 ? trimmed[..dashIndex] : trimmed;
    }

    private readonly record struct OcrBounds(int X, int Y, int Width, int Height);
}
