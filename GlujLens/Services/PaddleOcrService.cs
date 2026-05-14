using GlujLens.Models;
using PaddleOCRSharp;
using System.Drawing;
using System.Drawing.Imaging;
using PaddleSharpResult = PaddleOCRSharp.OCRResult;

namespace GlujLens.Services;

/// <summary>
/// Local CPU OCR provider backed by PaddleOCRSharp.
/// </summary>
public sealed class PaddleOcrService : IOcrService, IDisposable
{
    private const int MaxTileDepth = 4;
    private const int MinimumTileSize = 220;

    private readonly AppSettings _settings;
    private readonly object _engineLock = new();
    private PaddleOCREngine? _engine;
    private string? _engineModelPath;
    private bool _disposed;

    public PaddleOcrService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                lock (_engineLock)
                {
                    var engine = GetOrCreateEngine();
                    return DetectText(engine, imageData, _settings.SourceLanguage);
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

    private static OcrResult DetectText(PaddleOCREngine engine, byte[] imageData, string? language)
    {
        try
        {
            var paddleResult = engine.DetectText(imageData);
            return ConvertResult(paddleResult, language);
        }
        catch (Exception ex) when (IsCommunityBoxLimitException(ex))
        {
            using var stream = new MemoryStream(imageData);
            using var source = new Bitmap(stream);
            var fullImageBounds = new Rectangle(0, 0, source.Width, source.Height);
            return DetectTextInTiles(engine, source, fullImageBounds, language, depth: 0);
        }
    }

    private static OcrResult DetectTextInTiles(
        PaddleOCREngine engine,
        Bitmap source,
        Rectangle bounds,
        string? language,
        int depth)
    {
        try
        {
            var tileBytes = EncodeTile(source, bounds);
            var paddleResult = engine.DetectText(tileBytes);
            return ConvertResult(paddleResult, language, bounds.X, bounds.Y);
        }
        catch (Exception ex) when (IsCommunityBoxLimitException(ex) && CanSplit(bounds, depth))
        {
            return MergeTileResults(
                SplitBounds(bounds)
                    .Select(tile => DetectTextInTiles(engine, source, tile, language, depth + 1)));
        }
        catch (Exception ex) when (IsCommunityBoxLimitException(ex))
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = "PaddleOCRSharp community edition refused this screenshot because one tile still contains 100 or more detected text boxes. Capture a smaller area or use Tesseract for this image."
            };
        }
    }

    private static bool IsCommunityBoxLimitException(Exception ex)
    {
        return ex.Message.Contains("box sizes <100", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanSplit(Rectangle bounds, int depth)
    {
        return depth < MaxTileDepth &&
               bounds.Width >= MinimumTileSize * 2 &&
               bounds.Height >= MinimumTileSize * 2;
    }

    private static IEnumerable<Rectangle> SplitBounds(Rectangle bounds)
    {
        var leftWidth = bounds.Width / 2;
        var rightWidth = bounds.Width - leftWidth;
        var topHeight = bounds.Height / 2;
        var bottomHeight = bounds.Height - topHeight;

        yield return new Rectangle(bounds.X, bounds.Y, leftWidth, topHeight);
        yield return new Rectangle(bounds.X + leftWidth, bounds.Y, rightWidth, topHeight);
        yield return new Rectangle(bounds.X, bounds.Y + topHeight, leftWidth, bottomHeight);
        yield return new Rectangle(bounds.X + leftWidth, bounds.Y + topHeight, rightWidth, bottomHeight);
    }

    private static byte[] EncodeTile(Bitmap source, Rectangle bounds)
    {
        using var tile = source.Clone(bounds, PixelFormat.Format32bppArgb);
        using var output = new MemoryStream();
        tile.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private PaddleOCREngine GetOrCreateEngine()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var modelPath = NormalizeModelPath(_settings.PaddleOcrModelPath);
        if (_engine != null && string.Equals(_engineModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
        {
            return _engine;
        }

        _engine?.Dispose();
        _engine = new PaddleOCREngine(CreateModelConfig(modelPath), CreateCpuParameter());
        _engineModelPath = modelPath;
        return _engine;
    }

    private static OCRParameter CreateCpuParameter()
    {
        return new OCRParameter
        {
            use_gpu = false,
            cpu_math_library_num_threads = Math.Clamp(Environment.ProcessorCount, 1, 10),
            enable_mkldnn = true,
            det = true,
            rec = true,
            cls = true,
            use_angle_cls = true,
            max_side_len = 1600,
            det_db_box_thresh = 0.5f,
            det_db_unclip_ratio = 1.6f,
            det_db_score_mode = true,
            visualize = false,
            show_img_vis = false,
            use_tensorrt = false
        };
    }

    private static OCRModelConfig CreateModelConfig(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return OCRModelConfig.Default;
        }

        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException($"PaddleOCR model folder not found: {modelPath}");
        }

        var detPath = FindModelDirectory(modelPath, "det");
        var clsPath = FindModelDirectory(modelPath, "cls");
        var recPath = FindModelDirectory(modelPath, "rec");
        var keysPath = FindKeysFile(modelPath);

        return new OCRModelConfig(detPath, clsPath, recPath, keysPath);
    }

    private static string? NormalizeModelPath(string? modelPath)
    {
        return string.IsNullOrWhiteSpace(modelPath)
            ? null
            : Path.GetFullPath(modelPath.Trim());
    }

    private static string FindModelDirectory(string rootPath, string modelKind)
    {
        var candidates = Directory
            .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains(modelKind, StringComparison.OrdinalIgnoreCase))
            .Where(ContainsPaddleModelFiles)
            .OrderBy(path => path.Length)
            .ToList();

        return candidates.FirstOrDefault()
            ?? throw new FileNotFoundException($"PaddleOCR {modelKind} model folder was not found under: {rootPath}");
    }

    private static bool ContainsPaddleModelFiles(string directory)
    {
        return File.Exists(Path.Combine(directory, "inference.pdiparams")) ||
               File.Exists(Path.Combine(directory, "inference.json"));
    }

    private static string FindKeysFile(string rootPath)
    {
        var keysPath = Directory
            .EnumerateFiles(rootPath, "*keys*.txt", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        return keysPath ?? string.Empty;
    }

    private static OcrResult MergeTileResults(IEnumerable<OcrResult> tileResults)
    {
        var results = tileResults.ToList();
        var failedResult = results.FirstOrDefault(result => !result.Success);
        if (failedResult != null)
        {
            return failedResult;
        }

        var merged = new OcrResult { Success = true };
        foreach (var result in results)
        {
            merged.TextRegions.AddRange(result.TextRegions);
        }

        merged.TextRegions = merged.TextRegions
            .OrderBy(region => region.Y)
            .ThenBy(region => region.X)
            .ToList();
        merged.Text = string.Join(Environment.NewLine, merged.TextRegions.Select(region => region.Text));
        return merged;
    }

    private static OcrResult ConvertResult(PaddleSharpResult? paddleResult, string? language, int offsetX = 0, int offsetY = 0)
    {
        var result = new OcrResult
        {
            Success = true,
            Text = paddleResult?.Text?.Trim() ?? string.Empty
        };

        if (paddleResult?.TextBlocks == null)
        {
            return result;
        }

        foreach (var block in paddleResult.TextBlocks)
        {
            var text = block.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var region = ToTextRegion(block, language, offsetX, offsetY);
            if (region.Width > 0 && region.Height > 0)
            {
                result.TextRegions.Add(region);
            }
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            result.Text = string.Join(Environment.NewLine, result.TextRegions.Select(region => region.Text));
        }

        return result;
    }

    private static TextRegion ToTextRegion(TextBlock block, string? language, int offsetX, int offsetY)
    {
        var points = block.BoxPoints ?? new List<OCRPoint>();
        if (points.Count == 0)
        {
            return new TextRegion
            {
                Text = block.Text?.Trim() ?? string.Empty,
                Confidence = ClampConfidence(block.Score),
                X = offsetX,
                Y = offsetY,
                Language = language
            };
        }

        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);

        return new TextRegion
        {
            Text = block.Text?.Trim() ?? string.Empty,
            Confidence = ClampConfidence(block.Score),
            X = minX + offsetX,
            Y = minY + offsetY,
            Width = maxX - minX,
            Height = maxY - minY,
            Language = language
        };
    }

    private static float ClampConfidence(float confidence)
    {
        return Math.Clamp(confidence, 0f, 1f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_engineLock)
        {
            if (_disposed)
            {
                return;
            }

            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }
}
