using GlujLens.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GlujLens.Services;

public sealed class PaddleOnnxOcrRunner
{
    private const float DetectionThreshold = 0.30f;
    private const int MaxRecognitionWidth = 960;
    private readonly OnnxRuntimeSessionFactory _sessionFactory;

    public PaddleOnnxOcrRunner(OnnxRuntimeSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public OcrResult Run(
        PaddleOnnxOcrModel model,
        byte[] imageData,
        string? accelerator,
        CancellationToken cancellationToken)
    {
        using var bitmap = SKBitmap.Decode(imageData);
        if (bitmap == null)
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = "Could not decode image for ML.NET OCR."
            };
        }

        using var sessions = _sessionFactory.LoadSessions(
            new[] { model.DetectionModelPath, model.RecognitionModelPath },
            accelerator,
            cancellationToken);

        var detectionSession = sessions.Sessions[0].Session;
        var recognitionSession = sessions.Sessions[1].Session;
        var boxes = DetectTextBoxes(detectionSession, bitmap, cancellationToken);
        var dictionary = LoadDictionary(model.DictionaryPath);
        var result = new OcrResult { Success = true };

        foreach (var box in boxes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var crop = Crop(bitmap, box);
            if (crop == null)
            {
                continue;
            }

            var recognized = RecognizeText(recognitionSession, crop, dictionary);
            if (string.IsNullOrWhiteSpace(recognized.Text))
            {
                continue;
            }

            result.TextRegions.Add(new TextRegion
            {
                Text = recognized.Text,
                Confidence = recognized.Confidence,
                X = box.Left,
                Y = box.Top,
                Width = box.Width,
                Height = box.Height,
                Language = model.RecognitionLanguage
            });
        }

        result.Text = string.Join(Environment.NewLine, result.TextRegions.Select(region => region.Text));
        return result;
    }

    private static IReadOnlyList<SKRectI> DetectTextBoxes(
        InferenceSession session,
        SKBitmap bitmap,
        CancellationToken cancellationToken)
    {
        var inputName = session.InputMetadata.Keys.First();
        var width = RoundUp(Math.Max(32, bitmap.Width), 32);
        var height = RoundUp(Math.Max(32, bitmap.Height), 32);
        using var resized = bitmap.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.Medium);
        var inputBitmap = resized ?? bitmap;
        var tensor = CreateImageTensor(inputBitmap, height, width, normalizeMinusOneToOne: false);
        var input = NamedOnnxValue.CreateFromTensor(inputName, tensor);

        using var outputs = session.Run(new[] { input });
        var output = outputs.First().AsTensor<float>();
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length < 4)
        {
            return Array.Empty<SKRectI>();
        }

        var mapHeight = dimensions[^2];
        var mapWidth = dimensions[^1];
        var probabilities = output.ToArray();
        var boxes = ExtractConnectedBoxes(probabilities, mapWidth, mapHeight);
        var scaleX = (float)bitmap.Width / mapWidth;
        var scaleY = (float)bitmap.Height / mapHeight;

        return boxes
            .Select(box => ScaleBox(box, scaleX, scaleY, bitmap.Width, bitmap.Height))
            .Where(box => box.Width >= 3 && box.Height >= 3)
            .OrderBy(box => box.Top)
            .ThenBy(box => box.Left)
            .ToList();
    }

    private static IReadOnlyList<SKRectI> ExtractConnectedBoxes(float[] map, int width, int height)
    {
        var visited = new bool[map.Length];
        var boxes = new List<SKRectI>();
        var queue = new Queue<(int X, int Y)>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (visited[index] || map[index] < DetectionThreshold)
                {
                    continue;
                }

                var left = x;
                var right = x;
                var top = y;
                var bottom = y;
                visited[index] = true;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    left = Math.Min(left, current.X);
                    right = Math.Max(right, current.X);
                    top = Math.Min(top, current.Y);
                    bottom = Math.Max(bottom, current.Y);

                    Enqueue(current.X + 1, current.Y);
                    Enqueue(current.X - 1, current.Y);
                    Enqueue(current.X, current.Y + 1);
                    Enqueue(current.X, current.Y - 1);
                }

                if ((right - left) * (bottom - top) >= 8)
                {
                    boxes.Add(new SKRectI(left, top, right + 1, bottom + 1));
                }
            }
        }

        return MergeNearbyBoxes(boxes);

        void Enqueue(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (visited[index] || map[index] < DetectionThreshold)
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue((x, y));
        }
    }

    private static IReadOnlyList<SKRectI> MergeNearbyBoxes(IReadOnlyList<SKRectI> boxes)
    {
        var merged = new List<SKRectI>();
        foreach (var box in boxes.OrderBy(box => box.Top).ThenBy(box => box.Left))
        {
            var expanded = Expand(box, 2, int.MaxValue, int.MaxValue);
            var index = merged.FindIndex(existing => expanded.IntersectsWith(existing));
            if (index >= 0)
            {
                merged[index] = SKRectI.Union(merged[index], box);
            }
            else
            {
                merged.Add(box);
            }
        }

        return merged;
    }

    private static (string Text, float Confidence) RecognizeText(
        InferenceSession session,
        SKBitmap crop,
        IReadOnlyList<string> dictionary)
    {
        var inputName = session.InputMetadata.Keys.First();
        var inputHeight = ResolveRecognitionInputHeight(session);
        var width = Math.Clamp((int)Math.Ceiling(crop.Width * ((double)inputHeight / Math.Max(1, crop.Height))), inputHeight, MaxRecognitionWidth);
        width = RoundUp(width, 8);

        using var resized = crop.Resize(new SKImageInfo(width, inputHeight, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.Medium);
        var inputBitmap = resized ?? crop;
        var tensor = CreateImageTensor(inputBitmap, inputHeight, width, normalizeMinusOneToOne: true);
        var input = NamedOnnxValue.CreateFromTensor(inputName, tensor);

        using var outputs = session.Run(new[] { input });
        var output = outputs.First().AsTensor<float>();
        return DecodeCtc(output, dictionary);
    }

    private static int ResolveRecognitionInputHeight(InferenceSession session)
    {
        var metadata = session.InputMetadata.Values.FirstOrDefault(value =>
            value.Dimensions.Length == 4 &&
            (value.Dimensions[1] == 3 || value.Dimensions[3] == 3));

        if (metadata == null)
        {
            return 48;
        }

        var dimensions = metadata.Dimensions;
        var isNchw = dimensions[1] == 3;
        var height = isNchw ? dimensions[2] : dimensions[1];
        return height > 0 ? height : 48;
    }

    private static (string Text, float Confidence) DecodeCtc(Tensor<float> output, IReadOnlyList<string> dictionary)
    {
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length != 3)
        {
            return (string.Empty, 0);
        }

        var sequenceLength = dimensions[1];
        var classCount = dimensions[2];
        var previousClass = -1;
        var text = new List<string>();
        var confidences = new List<float>();

        for (var step = 0; step < sequenceLength; step++)
        {
            var bestClass = 0;
            var bestLogit = float.NegativeInfinity;
            var logitSum = 0.0;

            for (var cls = 0; cls < classCount; cls++)
            {
                var value = output[0, step, cls];
                if (value > bestLogit)
                {
                    bestLogit = value;
                    bestClass = cls;
                }

                logitSum += Math.Exp(Math.Clamp(value, -30, 30));
            }

            if (bestClass != 0 && bestClass != previousClass)
            {
                var dictionaryIndex = bestClass - 1;
                if (dictionaryIndex >= 0 && dictionaryIndex < dictionary.Count)
                {
                    text.Add(dictionary[dictionaryIndex]);
                    confidences.Add((float)(Math.Exp(Math.Clamp(bestLogit, -30, 30)) / Math.Max(logitSum, double.Epsilon)));
                }
            }

            previousClass = bestClass;
        }

        return (string.Concat(text), confidences.Count == 0 ? 0 : confidences.Average());
    }

    private static DenseTensor<float> CreateImageTensor(SKBitmap bitmap, int height, int width, bool normalizeMinusOneToOne)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                WriteChannel(0, color.Red);
                WriteChannel(1, color.Green);
                WriteChannel(2, color.Blue);

                void WriteChannel(int channel, byte value)
                {
                    var normalized = value / 255f;
                    tensor[0, channel, y, x] = normalizeMinusOneToOne
                        ? (normalized - 0.5f) / 0.5f
                        : normalized;
                }
            }
        }

        return tensor;
    }

    private static SKBitmap? Crop(SKBitmap bitmap, SKRectI box)
    {
        var expanded = Expand(box, 2, bitmap.Width, bitmap.Height);
        if (expanded.Width <= 0 || expanded.Height <= 0)
        {
            return null;
        }

        var crop = new SKBitmap(expanded.Width, expanded.Height);
        using var canvas = new SKCanvas(crop);
        canvas.DrawBitmap(bitmap, expanded, new SKRect(0, 0, expanded.Width, expanded.Height));
        return crop;
    }

    private static SKRectI ScaleBox(SKRectI box, float scaleX, float scaleY, int maxWidth, int maxHeight)
    {
        return Expand(
            new SKRectI(
                (int)MathF.Floor(box.Left * scaleX),
                (int)MathF.Floor(box.Top * scaleY),
                (int)MathF.Ceiling(box.Right * scaleX),
                (int)MathF.Ceiling(box.Bottom * scaleY)),
            2,
            maxWidth,
            maxHeight);
    }

    private static SKRectI Expand(SKRectI box, int padding, int maxWidth, int maxHeight)
    {
        return new SKRectI(
            Math.Max(0, box.Left - padding),
            Math.Max(0, box.Top - padding),
            Math.Min(maxWidth, box.Right + padding),
            Math.Min(maxHeight, box.Bottom + padding));
    }

    private static IReadOnlyList<string> LoadDictionary(string path)
    {
        return File.ReadAllLines(path)
            .Select(line => line.TrimEnd('\r', '\n'))
            .ToList();
    }

    private static int RoundUp(int value, int multiple)
    {
        return ((value + multiple - 1) / multiple) * multiple;
    }
}
