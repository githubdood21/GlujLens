using GlujLens.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GlujLens.Services;

public sealed class PaddleOnnxOcrRunner
{
    private static readonly OcrInferenceProfile SharedProfile = new(
        DetectionLimitSideLength: 1152,
        DetectionBitmapThreshold: 0.30f,
        DetectionBoxThreshold: 0.55f,
        DetectionUnclipRatio: 1.5f,
        MinimumBoxSize: 4,
        CropPadding: 3,
        VerticalCropPadding: 6,
        MaxRecognitionWidth: 2048,
        LowConfidenceRetryThreshold: 0.54f,
        AlwaysRunContrastDetectionPass: false,
        EnableRecognitionRetries: true,
        MaximumGroupedLineWidth: 1500,
        MaximumGroupedVerticalHeight: 2200);

    private static readonly float[] DetectionMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] DetectionStd = { 0.229f, 0.224f, 0.225f };
    private static readonly float[] RecognitionMean = { 0.5f, 0.5f, 0.5f };
    private static readonly float[] RecognitionStd = { 0.5f, 0.5f, 0.5f };
    private static readonly ConcurrentDictionary<string, CachedDictionary> DictionaryCache = new(StringComparer.OrdinalIgnoreCase);

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
        var timing = new OcrInferenceTiming();
        var totalStopwatch = Stopwatch.StartNew();
        var stageStopwatch = Stopwatch.StartNew();
        using var bitmap = SKBitmap.Decode(imageData);
        timing.Decode = stageStopwatch.Elapsed;
        if (bitmap == null)
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = "Could not decode image for ML.NET OCR."
            };
        }

        var result = new OcrResult { Success = true };
        var profile = SharedProfile;
        IReadOnlyList<DetectedTextBox> boxes;

        stageStopwatch.Restart();
        OnnxSessionBundle detectionSessions;
        detectionSessions = _sessionFactory.LoadSessions(
            new[] { model.DetectionModelPath },
            accelerator,
            cancellationToken,
            optimizeForLowMemory: true);
        timing.DetectionSessionLoad = stageStopwatch.Elapsed;

        stageStopwatch.Restart();
        using (detectionSessions)
        {
            boxes = DetectTextBoxes(detectionSessions.Sessions[0].Session, bitmap, profile, cancellationToken);
        }
        timing.DetectionRunAndDispose = stageStopwatch.Elapsed;
        timing.DetectedBoxes = boxes.Count;

        stageStopwatch.Restart();
        TrimInferenceMemory();
        timing.FirstTrim = stageStopwatch.Elapsed;

        if (boxes.Count == 0)
        {
            totalStopwatch.Stop();
            LogOcrTiming(timing, totalStopwatch.Elapsed, accelerator, model);
            return result;
        }

        stageStopwatch.Restart();
        var textItems = new List<TextRecognitionItem>();
        foreach (var box in boxes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var textBoundsList = SplitIntoTextBounds(bitmap, box.Bounds, profile);
            textItems.AddRange(textBoundsList.Select(textBounds =>
                new TextRecognitionItem(textBounds.Bounds, textBounds.Orientation, box.Score)));
        }

        timing.Split = stageStopwatch.Elapsed;
        timing.TextCandidates = textItems.Count;
        textItems = GroupHorizontalTextLines(textItems, profile).ToList();
        timing.GroupedTextCandidates = textItems.Count;

        if (textItems.Count == 0)
        {
            totalStopwatch.Stop();
            LogOcrTiming(timing, totalStopwatch.Elapsed, accelerator, model);
            return result;
        }

        stageStopwatch.Restart();
        var dictionary = LoadDictionary(model.DictionaryPath);
        timing.DictionaryLoad = stageStopwatch.Elapsed;

        var recognitionAccelerator = ResolveRecognitionAccelerator(accelerator);
        timing.RecognitionAccelerator = recognitionAccelerator;

        stageStopwatch.Restart();
        OnnxSessionBundle recognitionSessions;
        recognitionSessions = _sessionFactory.LoadSessions(
            new[] { model.RecognitionModelPath },
            recognitionAccelerator,
            cancellationToken,
            optimizeForLowMemory: true);
        timing.RecognitionSessionLoad = stageStopwatch.Elapsed;

        stageStopwatch.Restart();
        using (recognitionSessions)
        {
            var recognitionSession = recognitionSessions.Sessions[0].Session;
            var recognitionContext = CreateRecognitionContext(recognitionSession);

            foreach (var textItem in textItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cropStopwatch = Stopwatch.StartNew();
                using var crop = Crop(bitmap, textItem.Bounds, ResolveCropPadding(profile, textItem.Orientation));
                timing.Crop += cropStopwatch.Elapsed;
                if (crop == null)
                {
                    continue;
                }

                var recognitionStopwatch = Stopwatch.StartNew();
                var recognized = RecognizeText(recognitionContext, crop, dictionary, profile, textItem.Orientation);
                timing.Recognition += recognitionStopwatch.Elapsed;
                timing.RecognitionCalls++;
                if (string.IsNullOrWhiteSpace(recognized.Text))
                {
                    continue;
                }

                result.TextRegions.Add(new TextRegion
                {
                    Text = recognized.Text,
                    Confidence = Math.Min(textItem.DetectionScore, recognized.Confidence),
                    X = textItem.Bounds.Left,
                    Y = textItem.Bounds.Top,
                    Width = textItem.Bounds.Width,
                    Height = textItem.Bounds.Height,
                    Language = model.RecognitionLanguage
                });
            }
        }
        timing.RecognitionLoopAndDispose = stageStopwatch.Elapsed;

        stageStopwatch.Restart();
        TrimInferenceMemory();
        timing.SecondTrim = stageStopwatch.Elapsed;

        stageStopwatch.Restart();
        var orderedRegions = OrderTextRegionsForReading(result.TextRegions).ToList();
        result.TextRegions.Clear();
        result.TextRegions.AddRange(orderedRegions);
        result.Text = string.Join(Environment.NewLine, result.TextRegions.Select(region => region.Text));
        timing.ResultOrdering = stageStopwatch.Elapsed;
        timing.OutputRegions = result.TextRegions.Count;

        totalStopwatch.Stop();
        LogOcrTiming(timing, totalStopwatch.Elapsed, accelerator, model);
        return result;
    }

    private static IReadOnlyList<DetectedTextBox> DetectTextBoxes(
        InferenceSession session,
        SKBitmap bitmap,
        OcrInferenceProfile profile,
        CancellationToken cancellationToken)
    {
        var inputName = session.InputMetadata.Keys.First();
        var targetSize = ResolveDetectionInputSize(bitmap.Width, bitmap.Height, profile.DetectionLimitSideLength);
        using var resizedOriginal = bitmap.Resize(
            new SKImageInfo(targetSize.Width, targetSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul),
            SKFilterQuality.Medium);
        var inputBitmap = resizedOriginal ?? bitmap;
        var boxes = RunDetectionPass(session, inputName, inputBitmap, bitmap.Width, bitmap.Height, profile).ToList();
        if (ShouldRunContrastDetectionPass(boxes, bitmap, profile))
        {
            using var normalized = CreateContrastNormalizedBitmap(inputBitmap);
            boxes.AddRange(RunDetectionPass(session, inputName, normalized, bitmap.Width, bitmap.Height, profile));
            boxes = MergeOverlappingBoxes(boxes).ToList();
        }

        return boxes
            .Where(box => box.Bounds.Width >= profile.MinimumBoxSize && box.Bounds.Height >= profile.MinimumBoxSize)
            .OrderBy(box => box.Bounds.Top)
            .ThenBy(box => box.Bounds.Left)
            .ToList();
    }

    private static bool ShouldRunContrastDetectionPass(
        IReadOnlyList<DetectedTextBox> boxes,
        SKBitmap bitmap,
        OcrInferenceProfile profile)
    {
        if (profile.AlwaysRunContrastDetectionPass || boxes.Count == 0)
        {
            return true;
        }

        var totalArea = Math.Max(1, bitmap.Width * bitmap.Height);
        var detectedArea = boxes.Sum(box => Math.Max(0, box.Bounds.Width) * Math.Max(0, box.Bounds.Height));
        var coverage = detectedArea / (double)totalArea;
        return boxes.Count <= 2 && coverage < 0.015;
    }

    private static void TrimInferenceMemory()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true);
        GC.WaitForPendingFinalizers();
    }

    private static string ResolveRecognitionAccelerator(string? accelerator)
    {
        var normalized = accelerator?.Trim();
        if (string.Equals(normalized, "CPU", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU";
        }

        return string.Equals(normalized, "DirectML", StringComparison.OrdinalIgnoreCase)
            ? "DirectML"
            : "Auto";
    }

    private static void LogOcrTiming(
        OcrInferenceTiming timing,
        TimeSpan total,
        string? accelerator,
        PaddleOnnxOcrModel model)
    {
        var message =
            "PaddleOCR timing " +
            $"accelerator={accelerator ?? "Auto"} " +
            $"recAccelerator={timing.RecognitionAccelerator ?? accelerator ?? "Auto"} " +
            $"model={Path.GetFileName(model.RootDirectory)} " +
            $"boxes={timing.DetectedBoxes} candidates={timing.TextCandidates} groupedCandidates={timing.GroupedTextCandidates} regions={timing.OutputRegions} calls={timing.RecognitionCalls} " +
            $"total={FormatMs(total)} decode={FormatMs(timing.Decode)} " +
            $"detLoad={FormatMs(timing.DetectionSessionLoad)} detRunDispose={FormatMs(timing.DetectionRunAndDispose)} " +
            $"trim1={FormatMs(timing.FirstTrim)} dict={FormatMs(timing.DictionaryLoad)} " +
            $"recLoad={FormatMs(timing.RecognitionSessionLoad)} split={FormatMs(timing.Split)} crop={FormatMs(timing.Crop)} " +
            $"recognize={FormatMs(timing.Recognition)} recLoopDispose={FormatMs(timing.RecognitionLoopAndDispose)} " +
            $"trim2={FormatMs(timing.SecondTrim)} order={FormatMs(timing.ResultOrdering)}";

        Debug.WriteLine(message);
        Trace.WriteLine(message);
    }

    private static string FormatMs(TimeSpan elapsed)
    {
        return $"{elapsed.TotalMilliseconds:0}ms";
    }

    private static IReadOnlyList<DetectedTextBox> RunDetectionPass(
        InferenceSession session,
        string inputName,
        SKBitmap inputBitmap,
        int originalWidth,
        int originalHeight,
        OcrInferenceProfile profile)
    {
        var tensor = CreateImageTensor(inputBitmap, inputBitmap.Height, inputBitmap.Width, DetectionMean, DetectionStd);
        var input = NamedOnnxValue.CreateFromTensor(inputName, tensor);

        using var outputs = session.Run(new[] { input });
        var output = outputs.First().AsTensor<float>();
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length < 4)
        {
            return Array.Empty<DetectedTextBox>();
        }

        var mapHeight = dimensions[^2];
        var mapWidth = dimensions[^1];
        var probabilities = output.ToArray();
        var boxes = ExtractDbBoxes(probabilities, mapWidth, mapHeight, profile);
        var scaleX = (float)originalWidth / mapWidth;
        var scaleY = (float)originalHeight / mapHeight;

        return boxes
            .Select(box => box with { Bounds = ScaleBox(box.Bounds, scaleX, scaleY, originalWidth, originalHeight) })
            .ToList();
    }

    private static IReadOnlyList<DetectedTextBox> ExtractDbBoxes(
        float[] map,
        int width,
        int height,
        OcrInferenceProfile profile)
    {
        var visited = new bool[map.Length];
        var boxes = new List<DetectedTextBox>();
        var queue = new Queue<(int X, int Y)>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (visited[index] || map[index] < profile.DetectionBitmapThreshold)
                {
                    continue;
                }

                var left = x;
                var right = x;
                var top = y;
                var bottom = y;
                var scoreSum = 0.0;
                var pixelCount = 0;

                visited[index] = true;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var currentIndex = current.Y * width + current.X;
                    var score = map[currentIndex];

                    left = Math.Min(left, current.X);
                    right = Math.Max(right, current.X);
                    top = Math.Min(top, current.Y);
                    bottom = Math.Max(bottom, current.Y);
                    scoreSum += score;
                    pixelCount++;

                    Enqueue(current.X + 1, current.Y);
                    Enqueue(current.X - 1, current.Y);
                    Enqueue(current.X, current.Y + 1);
                    Enqueue(current.X, current.Y - 1);
                }

                var box = new SKRectI(left, top, right + 1, bottom + 1);
                var averageScore = pixelCount == 0 ? 0 : (float)(scoreSum / pixelCount);
                if (averageScore < profile.DetectionBoxThreshold ||
                    box.Width < profile.MinimumBoxSize ||
                    box.Height < profile.MinimumBoxSize)
                {
                    continue;
                }

                boxes.Add(new DetectedTextBox(UnclipBox(box, profile.DetectionUnclipRatio, width, height), averageScore));
            }
        }

        return MergeOverlappingBoxes(boxes);

        void Enqueue(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (visited[index] || map[index] < profile.DetectionBitmapThreshold)
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue((x, y));
        }
    }

    private static IReadOnlyList<DetectedTextBox> MergeOverlappingBoxes(IReadOnlyList<DetectedTextBox> boxes)
    {
        var merged = new List<DetectedTextBox>();
        foreach (var box in boxes.OrderBy(box => box.Bounds.Top).ThenBy(box => box.Bounds.Left))
        {
            var index = merged.FindIndex(existing => ShouldMergeDetectedBoxes(existing.Bounds, box.Bounds));
            if (index < 0)
            {
                merged.Add(box);
                continue;
            }

            var existing = merged[index];
            merged[index] = new DetectedTextBox(
                SKRectI.Union(existing.Bounds, box.Bounds),
                Math.Max(existing.Score, box.Score));
        }

        return merged;
    }

    private static bool ShouldMergeDetectedBoxes(SKRectI existing, SKRectI candidate)
    {
        if (!existing.IntersectsWith(candidate))
        {
            return false;
        }

        var intersection = SKRectI.Intersect(existing, candidate);
        var smallerArea = Math.Max(1, Math.Min(existing.Width * existing.Height, candidate.Width * candidate.Height));
        var intersectionArea = intersection.Width * intersection.Height;
        if (intersectionArea / (double)smallerArea >= 0.55)
        {
            return true;
        }

        var verticalOverlap = intersection.Height / (double)Math.Max(1, Math.Min(existing.Height, candidate.Height));
        var existingCenterY = existing.Top + existing.Height / 2.0;
        var candidateCenterY = candidate.Top + candidate.Height / 2.0;
        var sameLineTolerance = Math.Max(existing.Height, candidate.Height) * 0.35;
        return verticalOverlap >= 0.65 &&
               Math.Abs(existingCenterY - candidateCenterY) <= sameLineTolerance;
    }

    private static IEnumerable<TextRegion> OrderTextRegionsForReading(IReadOnlyList<TextRegion> regions)
    {
        var verticalCount = regions.Count(IsVerticalTextRegion);
        if (verticalCount > regions.Count / 2)
        {
            return regions
                .OrderByDescending(region => region.X + region.Width / 2.0)
                .ThenBy(region => region.Y);
        }

        return regions
            .OrderBy(region => region.Y)
            .ThenBy(region => region.X);
    }

    private static bool IsVerticalTextRegion(TextRegion region)
    {
        return region.Height >= Math.Max(32, region.Width * 1.8) &&
               region.Width <= Math.Max(96, region.Height * 0.65);
    }

    private static IReadOnlyList<TextRecognitionItem> GroupHorizontalTextLines(
        IReadOnlyList<TextRecognitionItem> items,
        OcrInferenceProfile profile)
    {
        var verticalCandidates = ExtractVerticalColumnCandidates(items, profile);
        var verticalCandidateSet = verticalCandidates
            .SelectMany(group => group)
            .ToHashSet();

        var horizontal = items
            .Where(item => item.Orientation == TextOrientation.Horizontal)
            .Where(item => !verticalCandidateSet.Contains(item))
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .ToList();
        var vertical = items
            .Where(item => item.Orientation == TextOrientation.Vertical)
            .Where(item => !verticalCandidateSet.Contains(item))
            .ToList();

        if (horizontal.Count <= 1)
        {
            return horizontal.Concat(GroupVerticalTextColumns(vertical, profile)).ToList();
        }

        var rows = BuildTextRows(horizontal);
        var grouped = new List<TextRecognitionItem>();
        foreach (var row in rows)
        {
            grouped.AddRange(GroupTextRow(row, profile));
        }

        grouped.AddRange(verticalCandidates.Select(group => MergeVerticalCandidateGroup(group)));
        grouped.AddRange(GroupVerticalTextColumns(vertical, profile));
        return grouped
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .ToList();
    }

    private static IReadOnlyList<List<TextRecognitionItem>> ExtractVerticalColumnCandidates(
        IReadOnlyList<TextRecognitionItem> items,
        OcrInferenceProfile profile)
    {
        var narrowItems = items
            .Where(item => IsVerticalColumnCandidate(item.Bounds))
            .OrderByDescending(item => item.Bounds.Left)
            .ThenBy(item => item.Bounds.Top)
            .ToList();
        if (narrowItems.Count < 3)
        {
            return Array.Empty<List<TextRecognitionItem>>();
        }

        var columns = BuildTextColumns(narrowItems);
        return columns
            .Select(column => column
                .OrderBy(item => item.Bounds.Top)
                .ToList())
            .Where(column => LooksLikeVerticalColumnGroup(column, profile))
            .ToList();
    }

    private static bool IsVerticalColumnCandidate(SKRectI bounds)
    {
        return bounds.Width <= 96 &&
               bounds.Height <= 180 &&
               bounds.Width <= bounds.Height * 1.45;
    }

    private static bool LooksLikeVerticalColumnGroup(
        IReadOnlyList<TextRecognitionItem> column,
        OcrInferenceProfile profile)
    {
        if (column.Count < 3)
        {
            return false;
        }

        var bounds = column
            .Select(item => item.Bounds)
            .Aggregate(SKRectI.Union);
        if (bounds.Height < Math.Max(48, bounds.Width * 1.7) ||
            bounds.Height > profile.MaximumGroupedVerticalHeight)
        {
            return false;
        }

        var medianWidth = ResolveMedianWidth(column);
        var maxGap = Math.Clamp((int)Math.Round(medianWidth * 4.0), 22, 96);
        for (var i = 1; i < column.Count; i++)
        {
            var gap = column[i].Bounds.Top - column[i - 1].Bounds.Bottom;
            if (gap > maxGap)
            {
                return false;
            }
        }

        return true;
    }

    private static TextRecognitionItem MergeVerticalCandidateGroup(IReadOnlyList<TextRecognitionItem> column)
    {
        var bounds = column
            .Select(item => item.Bounds)
            .Aggregate(SKRectI.Union);
        return new TextRecognitionItem(
            bounds,
            TextOrientation.Vertical,
            column.Min(item => item.DetectionScore));
    }

    private static IReadOnlyList<TextRecognitionItem> GroupVerticalTextColumns(
        IReadOnlyList<TextRecognitionItem> items,
        OcrInferenceProfile profile)
    {
        var ordered = items
            .OrderByDescending(item => item.Bounds.Left)
            .ThenBy(item => item.Bounds.Top)
            .ToList();
        if (ordered.Count <= 1)
        {
            return ordered;
        }

        var columns = BuildTextColumns(ordered);
        var grouped = new List<TextRecognitionItem>();
        foreach (var column in columns)
        {
            grouped.AddRange(GroupTextColumn(column, profile));
        }

        return grouped;
    }

    private static IReadOnlyList<List<TextRecognitionItem>> BuildTextColumns(IReadOnlyList<TextRecognitionItem> items)
    {
        var columns = new List<List<TextRecognitionItem>>();
        foreach (var item in items)
        {
            var column = columns.FirstOrDefault(existing => BelongsToTextColumn(existing, item.Bounds));
            if (column == null)
            {
                columns.Add(new List<TextRecognitionItem> { item });
                continue;
            }

            column.Add(item);
        }

        return columns
            .Select(column => column.OrderBy(item => item.Bounds.Top).ToList())
            .OrderByDescending(column => column.Max(item => item.Bounds.Right))
            .ToList();
    }

    private static bool BelongsToTextColumn(IReadOnlyList<TextRecognitionItem> column, SKRectI bounds)
    {
        var columnBounds = column
            .Select(item => item.Bounds)
            .Aggregate(SKRectI.Union);
        return AreSameTextColumn(columnBounds, bounds);
    }

    private static IReadOnlyList<TextRecognitionItem> GroupTextColumn(
        IReadOnlyList<TextRecognitionItem> column,
        OcrInferenceProfile profile)
    {
        if (column.Count <= 1)
        {
            return column;
        }

        var grouped = new List<TextRecognitionItem>();
        var current = column[0];
        var medianWidth = ResolveMedianWidth(column);

        for (var i = 1; i < column.Count; i++)
        {
            var next = column[i];
            if (ShouldExtendTextColumnChunk(current, next, medianWidth, profile))
            {
                current = MergeTextItems(current, next, TextOrientation.Vertical);
                continue;
            }

            grouped.Add(current);
            current = next;
        }

        grouped.Add(current);
        return grouped;
    }

    private static bool ShouldExtendTextColumnChunk(
        TextRecognitionItem current,
        TextRecognitionItem next,
        int medianWidth,
        OcrInferenceProfile profile)
    {
        if (!AreSameTextColumn(current.Bounds, next.Bounds))
        {
            return false;
        }

        var merged = SKRectI.Union(current.Bounds, next.Bounds);
        if (merged.Height > profile.MaximumGroupedVerticalHeight)
        {
            return false;
        }

        if (LooksLikeMultiColumnBlock(current.Bounds, medianWidth) ||
            LooksLikeMultiColumnBlock(next.Bounds, medianWidth))
        {
            return false;
        }

        var gap = next.Bounds.Top - current.Bounds.Bottom;
        if (gap < -Math.Min(current.Bounds.Height, next.Bounds.Height) / 5)
        {
            return false;
        }

        var maxGap = Math.Clamp((int)Math.Round(medianWidth * 4.5), 28, 140);
        return gap <= maxGap;
    }

    private static IReadOnlyList<List<TextRecognitionItem>> BuildTextRows(IReadOnlyList<TextRecognitionItem> items)
    {
        var rows = new List<List<TextRecognitionItem>>();
        foreach (var item in items)
        {
            var row = rows.FirstOrDefault(existing => BelongsToTextRow(existing, item.Bounds));
            if (row == null)
            {
                rows.Add(new List<TextRecognitionItem> { item });
                continue;
            }

            row.Add(item);
        }

        return rows
            .Select(row => row.OrderBy(item => item.Bounds.Left).ToList())
            .OrderBy(row => row.Min(item => item.Bounds.Top))
            .ToList();
    }

    private static bool BelongsToTextRow(IReadOnlyList<TextRecognitionItem> row, SKRectI bounds)
    {
        var rowBounds = row
            .Select(item => item.Bounds)
            .Aggregate(SKRectI.Union);
        return AreSameTextRow(rowBounds, bounds);
    }

    private static IReadOnlyList<TextRecognitionItem> GroupTextRow(
        IReadOnlyList<TextRecognitionItem> row,
        OcrInferenceProfile profile)
    {
        if (row.Count <= 1)
        {
            return row;
        }

        var grouped = new List<TextRecognitionItem>();
        var current = row[0];
        var medianHeight = ResolveMedianHeight(row);

        for (var i = 1; i < row.Count; i++)
        {
            var next = row[i];
            if (ShouldExtendTextLineChunk(current, next, medianHeight, profile))
            {
                current = MergeTextItems(current, next, TextOrientation.Horizontal);
                continue;
            }

            grouped.Add(current);
            current = next;
        }

        grouped.Add(current);
        return grouped;
    }

    private static bool ShouldExtendTextLineChunk(
        TextRecognitionItem current,
        TextRecognitionItem next,
        int medianHeight,
        OcrInferenceProfile profile)
    {
        if (!AreSameTextRow(current.Bounds, next.Bounds))
        {
            return false;
        }

        var merged = SKRectI.Union(current.Bounds, next.Bounds);
        if (merged.Width > profile.MaximumGroupedLineWidth)
        {
            return false;
        }

        if (LooksLikeMultiLineBlock(current.Bounds, medianHeight) ||
            LooksLikeMultiLineBlock(next.Bounds, medianHeight))
        {
            return false;
        }

        var gap = next.Bounds.Left - current.Bounds.Right;
        if (gap < -Math.Min(current.Bounds.Width, next.Bounds.Width) / 5)
        {
            return false;
        }

        var maxGap = Math.Clamp((int)Math.Round(medianHeight * 2.2), 18, 72);
        return gap <= maxGap;
    }

    private static bool AreSameTextRow(SKRectI left, SKRectI right)
    {
        var intersectionTop = Math.Max(left.Top, right.Top);
        var intersectionBottom = Math.Min(left.Bottom, right.Bottom);
        var overlap = Math.Max(0, intersectionBottom - intersectionTop) /
                      (double)Math.Max(1, Math.Min(left.Height, right.Height));
        var centerDelta = Math.Abs((left.Top + left.Bottom) / 2.0 - (right.Top + right.Bottom) / 2.0);
        return overlap >= 0.76 &&
               centerDelta <= Math.Max(left.Height, right.Height) * 0.22;
    }

    private static bool AreSameTextColumn(SKRectI top, SKRectI bottom)
    {
        var intersectionLeft = Math.Max(top.Left, bottom.Left);
        var intersectionRight = Math.Min(top.Right, bottom.Right);
        var overlap = Math.Max(0, intersectionRight - intersectionLeft) /
                      (double)Math.Max(1, Math.Min(top.Width, bottom.Width));
        var centerDelta = Math.Abs((top.Left + top.Right) / 2.0 - (bottom.Left + bottom.Right) / 2.0);
        return overlap >= 0.62 &&
               centerDelta <= Math.Max(top.Width, bottom.Width) * 0.35;
    }

    private static int ResolveMedianHeight(IReadOnlyList<TextRecognitionItem> row)
    {
        var heights = row
            .Select(item => item.Bounds.Height)
            .Order()
            .ToList();
        return heights.Count == 0 ? 1 : Math.Max(1, heights[heights.Count / 2]);
    }

    private static int ResolveMedianWidth(IReadOnlyList<TextRecognitionItem> column)
    {
        var widths = column
            .Select(item => item.Bounds.Width)
            .Order()
            .ToList();
        return widths.Count == 0 ? 1 : Math.Max(1, widths[widths.Count / 2]);
    }

    private static bool LooksLikeMultiLineBlock(SKRectI bounds, int medianHeight)
    {
        return bounds.Height >= Math.Max(72, medianHeight * 2.8);
    }

    private static bool LooksLikeMultiColumnBlock(SKRectI bounds, int medianWidth)
    {
        return bounds.Width >= Math.Max(72, medianWidth * 2.8);
    }

    private static TextRecognitionItem MergeTextItems(
        TextRecognitionItem left,
        TextRecognitionItem right,
        TextOrientation orientation)
    {
        return new TextRecognitionItem(
            SKRectI.Union(left.Bounds, right.Bounds),
            orientation,
            Math.Min(left.DetectionScore, right.DetectionScore));
    }

    private static RecognitionContext CreateRecognitionContext(InferenceSession session)
    {
        return new RecognitionContext(
            session,
            session.InputMetadata.Keys.First(),
            ResolveRecognitionInputHeight(session));
    }

    private static (string Text, float Confidence) RecognizeText(
        RecognitionContext context,
        SKBitmap crop,
        IReadOnlyList<string> dictionary,
        OcrInferenceProfile profile,
        TextOrientation orientation)
    {
        return orientation == TextOrientation.Vertical
            ? RecognizeVerticalText(context, crop, dictionary, profile)
            : RecognizeHorizontalText(context, crop, dictionary, profile);
    }

    private static (string Text, float Confidence) RecognizeHorizontalText(
        RecognitionContext context,
        SKBitmap crop,
        IReadOnlyList<string> dictionary,
        OcrInferenceProfile profile)
    {
        var original = RecognizeTextVariant(context, crop, dictionary, profile.MaxRecognitionWidth);
        if (!ShouldRunRecognitionRetries(crop, original, profile))
        {
            return original;
        }

        return RetryHorizontalRecognition(context, crop, dictionary, profile, original);
    }

    private static (string Text, float Confidence) RetryHorizontalRecognition(
        RecognitionContext context,
        SKBitmap crop,
        IReadOnlyList<string> dictionary,
        OcrInferenceProfile profile,
        (string Text, float Confidence) original)
    {
        using var colorSeparated = TryCreateColorSeparatedBitmap(crop);
        if (colorSeparated == null)
        {
            return original;
        }

        var separated = RecognizeTextVariant(context, colorSeparated, dictionary, profile.MaxRecognitionWidth);
        var best = IsBetterColorSeparatedRecognition(separated, original) ? separated : original;
        if (best.Confidence >= profile.LowConfidenceRetryThreshold)
        {
            return best;
        }

        using var sharpened = TryCreateSharpenedBitmap(crop);
        if (sharpened == null)
        {
            return best;
        }

        var sharpenedResult = RecognizeTextVariant(context, sharpened, dictionary, profile.MaxRecognitionWidth);
        return IsBetterFontRetryRecognition(sharpenedResult, best) ? sharpenedResult : best;
    }

    private static (string Text, float Confidence) RecognizeVerticalText(
        RecognitionContext context,
        SKBitmap crop,
        IReadOnlyList<string> dictionary,
        OcrInferenceProfile profile)
    {
        var original = RecognizeTextVariant(context, crop, dictionary, profile.MaxRecognitionWidth);

        using var counterClockwise = RotateBitmapCounterClockwise(crop);
        var counterClockwiseResult = RecognizeTextVariant(context, counterClockwise, dictionary, profile.MaxRecognitionWidth);
        var preferCounterClockwise = IsBetterVerticalRecognition(counterClockwiseResult, original);
        var best = preferCounterClockwise
            ? counterClockwiseResult
            : original;
        var bestOrientation = preferCounterClockwise
            ? VerticalRecognitionOrientation.CounterClockwise
            : VerticalRecognitionOrientation.Original;

        if (IsConfidentRecognition(best, profile))
        {
            return best;
        }

        using var clockwise = RotateBitmapClockwise(crop);
        var clockwiseResult = RecognizeTextVariant(context, clockwise, dictionary, profile.MaxRecognitionWidth);
        if (IsBetterVerticalRecognition(counterClockwiseResult, best))
        {
            best = counterClockwiseResult;
            bestOrientation = VerticalRecognitionOrientation.CounterClockwise;
        }

        if (IsBetterVerticalRecognition(clockwiseResult, best))
        {
            best = clockwiseResult;
            bestOrientation = VerticalRecognitionOrientation.Clockwise;
        }

        if (!profile.EnableRecognitionRetries || IsConfidentRecognition(best, profile))
        {
            return best;
        }

        var retry = bestOrientation switch
        {
            VerticalRecognitionOrientation.CounterClockwise => RecognizeHorizontalText(context, counterClockwise, dictionary, profile),
            VerticalRecognitionOrientation.Clockwise => RecognizeHorizontalText(context, clockwise, dictionary, profile),
            _ => RecognizeHorizontalText(context, crop, dictionary, profile)
        };

        return IsBetterVerticalRecognition(retry, best) ? retry : best;
    }

    private static IReadOnlyList<TextBounds> SplitIntoTextBounds(
        SKBitmap bitmap,
        SKRectI bounds,
        OcrInferenceProfile profile)
    {
        if (bounds.Height < Math.Max(18, profile.MinimumBoxSize * 4) ||
            bounds.Width <= profile.MinimumBoxSize)
        {
            return new[] { new TextBounds(bounds, ResolveTextOrientation(bounds)) };
        }

        using var crop = Crop(bitmap, bounds, padding: 0);
        if (crop == null)
        {
            return new[] { new TextBounds(bounds, ResolveTextOrientation(bounds)) };
        }

        using var normalized = CreateContrastNormalizedBitmap(crop);
        if (LooksLikeVerticalTextBounds(bounds))
        {
            var columnInk = MeasureColumnInk(normalized);
            var columnInkThreshold = ResolveColumnInkThreshold(columnInk, crop.Height);
            var rawColumnRuns = FindInkRuns(columnInk, columnInkThreshold, crop.Width);
            if (rawColumnRuns.Count > 1)
            {
                var columnRuns = MergeCloseColumnRuns(rawColumnRuns, crop.Width);
                if (columnRuns.Count > 1)
                {
                    return columnRuns
                        .Select(run => ResolveTightColumnBounds(bitmap, bounds, normalized, run, profile))
                        .Where(column => column.Height >= profile.MinimumBoxSize && column.Width >= profile.MinimumBoxSize)
                        .OrderByDescending(column => column.Left)
                        .Select(column => new TextBounds(column, TextOrientation.Vertical))
                        .ToList();
                }
            }

            return new[] { new TextBounds(bounds, TextOrientation.Vertical) };
        }

        var rowInk = MeasureRowInk(normalized);
        var inkThreshold = ResolveRowInkThreshold(rowInk, crop.Width);
        var rawRuns = FindInkRuns(rowInk, inkThreshold, crop.Height);
        if (rawRuns.Count <= 1)
        {
            return new[] { new TextBounds(bounds, TextOrientation.Horizontal) };
        }

        var lineRuns = MergeCloseLineRuns(rawRuns, crop.Height);
        if (lineRuns.Count <= 1)
        {
            return new[] { new TextBounds(bounds, TextOrientation.Horizontal) };
        }

        return lineRuns
            .Select(run => ResolveTightLineBounds(bitmap, bounds, normalized, run, profile))
            .Where(line => line.Height >= profile.MinimumBoxSize && line.Width >= profile.MinimumBoxSize)
            .Select(line => new TextBounds(line, TextOrientation.Horizontal))
            .ToList();
    }

    private static SKRectI ResolveTightLineBounds(
        SKBitmap bitmap,
        SKRectI originalBounds,
        SKBitmap normalizedCrop,
        (int Top, int Bottom) run,
        OcrInferenceProfile profile)
    {
        var left = normalizedCrop.Width;
        var right = 0;
        var threshold = Math.Max(1, Math.Min(3, run.Bottom - run.Top > 6 ? 2 : 1));

        for (var x = 0; x < normalizedCrop.Width; x++)
        {
            var ink = 0;
            for (var y = run.Top; y < run.Bottom; y++)
            {
                if (normalizedCrop.GetPixel(x, y).Red < 150)
                {
                    ink++;
                }
            }

            if (ink >= threshold)
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
            }
        }

        if (right <= left)
        {
            left = 0;
            right = normalizedCrop.Width;
        }

        var horizontalPadding = Math.Max(2, profile.CropPadding);
        return Expand(
            new SKRectI(
                originalBounds.Left + left,
                originalBounds.Top + run.Top,
                originalBounds.Left + right,
                originalBounds.Top + run.Bottom),
            padding: horizontalPadding,
            bitmap.Width,
            bitmap.Height);
    }

    private static SKRectI ResolveTightColumnBounds(
        SKBitmap bitmap,
        SKRectI originalBounds,
        SKBitmap normalizedCrop,
        (int Top, int Bottom) run,
        OcrInferenceProfile profile)
    {
        var top = normalizedCrop.Height;
        var bottom = 0;
        var threshold = Math.Max(1, run.Bottom - run.Top > 6 ? 2 : 1);

        for (var y = 0; y < normalizedCrop.Height; y++)
        {
            var ink = 0;
            for (var x = run.Top; x < run.Bottom; x++)
            {
                if (normalizedCrop.GetPixel(x, y).Red < 150)
                {
                    ink++;
                }
            }

            if (ink >= threshold)
            {
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        if (bottom <= top)
        {
            top = 0;
            bottom = normalizedCrop.Height;
        }

        return Expand(
            new SKRectI(
                originalBounds.Left + run.Top,
                originalBounds.Top + top,
                originalBounds.Left + run.Bottom,
                originalBounds.Top + bottom),
            padding: Math.Max(profile.VerticalCropPadding, (run.Bottom - run.Top) / 4),
            bitmap.Width,
            bitmap.Height);
    }

    private static int ResolveCropPadding(OcrInferenceProfile profile, TextOrientation orientation)
    {
        return orientation == TextOrientation.Vertical
            ? profile.VerticalCropPadding
            : profile.CropPadding;
    }

    private static TextOrientation ResolveTextOrientation(SKRectI bounds)
    {
        return LooksLikeVerticalTextBounds(bounds)
            ? TextOrientation.Vertical
            : TextOrientation.Horizontal;
    }

    private static bool LooksLikeVerticalTextBounds(SKRectI bounds)
    {
        return bounds.Height >= Math.Max(32, bounds.Width * 1.8) &&
               bounds.Width <= Math.Max(96, bounds.Height * 0.65);
    }

    private static int[] MeasureRowInk(SKBitmap bitmap)
    {
        var rowInk = new int[bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            var ink = 0;
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (IsInkPixel(bitmap.GetPixel(x, y)))
                {
                    ink++;
                }
            }

            rowInk[y] = ink;
        }

        return rowInk;
    }

    private static int[] MeasureColumnInk(SKBitmap bitmap)
    {
        var columnInk = new int[bitmap.Width];
        for (var x = 0; x < bitmap.Width; x++)
        {
            var ink = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).Red < 150)
                {
                    ink++;
                }
            }

            columnInk[x] = ink;
        }

        return columnInk;
    }

    private static int ResolveRowInkThreshold(IReadOnlyList<int> rowInk, int width)
    {
        var nonEmptyRows = rowInk
            .Where(value => value > 0)
            .Order()
            .ToList();
        if (nonEmptyRows.Count == 0)
        {
            return int.MaxValue;
        }

        var median = nonEmptyRows[nonEmptyRows.Count / 2];
        return Math.Max(2, Math.Min(Math.Max(2, width / 160), median / 3));
    }

    private static int ResolveColumnInkThreshold(IReadOnlyList<int> columnInk, int height)
    {
        var nonEmptyColumns = columnInk
            .Where(value => value > 0)
            .Order()
            .ToList();
        if (nonEmptyColumns.Count == 0)
        {
            return int.MaxValue;
        }

        var median = nonEmptyColumns[nonEmptyColumns.Count / 2];
        return Math.Max(1, Math.Min(Math.Max(2, height / 220), median / 4));
    }

    private static IReadOnlyList<(int Top, int Bottom)> FindInkRuns(
        IReadOnlyList<int> rowInk,
        int threshold,
        int height)
    {
        var runs = new List<(int Top, int Bottom)>();
        var start = -1;

        for (var y = 0; y < height; y++)
        {
            var hasInk = rowInk[y] >= threshold;
            if (hasInk && start < 0)
            {
                start = y;
            }
            else if (!hasInk && start >= 0)
            {
                AddRun(start, y);
                start = -1;
            }
        }

        if (start >= 0)
        {
            AddRun(start, height);
        }

        return runs;

        void AddRun(int top, int bottom)
        {
            if (bottom - top >= 2)
            {
                runs.Add((top, bottom));
            }
        }
    }

    private static IReadOnlyList<(int Top, int Bottom)> MergeCloseLineRuns(
        IReadOnlyList<(int Top, int Bottom)> runs,
        int cropHeight)
    {
        if (runs.Count <= 1)
        {
            return runs;
        }

        var merged = new List<(int Top, int Bottom)>();
        var current = runs[0];
        var typicalHeight = runs
            .Select(run => run.Bottom - run.Top)
            .Order()
            .ElementAt(runs.Count / 2);
        var maxGlyphGap = Math.Max(1, typicalHeight / 3);

        for (var i = 1; i < runs.Count; i++)
        {
            var next = runs[i];
            var gap = next.Top - current.Bottom;
            if (gap <= maxGlyphGap)
            {
                current = (current.Top, next.Bottom);
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);

        var minLineHeight = Math.Max(3, cropHeight / 80);
        return merged
            .Where(run => run.Bottom - run.Top >= minLineHeight)
            .ToList();
    }

    private static IReadOnlyList<(int Top, int Bottom)> MergeCloseColumnRuns(
        IReadOnlyList<(int Top, int Bottom)> runs,
        int cropWidth)
    {
        if (runs.Count <= 1)
        {
            return runs;
        }

        var merged = new List<(int Top, int Bottom)>();
        var current = runs[0];
        var typicalWidth = runs
            .Select(run => run.Bottom - run.Top)
            .Order()
            .ElementAt(runs.Count / 2);
        var maxGlyphGap = Math.Max(1, Math.Min(4, typicalWidth / 3));

        for (var i = 1; i < runs.Count; i++)
        {
            var next = runs[i];
            var gap = next.Top - current.Bottom;
            if (gap <= maxGlyphGap)
            {
                current = (current.Top, next.Bottom);
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);

        var minColumnWidth = Math.Max(3, cropWidth / 80);
        return merged
            .Where(run => run.Bottom - run.Top >= minColumnWidth)
            .ToList();
    }

    private static bool IsInkPixel(SKColor color)
    {
        return color.Red < 170;
    }

    private static (string Text, float Confidence) RecognizeTextVariant(
        RecognitionContext context,
        SKBitmap crop,
        IReadOnlyList<string> dictionary,
        int maxRecognitionWidth)
    {
        var inputHeight = context.InputHeight;
        var width = Math.Clamp(
            (int)Math.Ceiling(crop.Width * ((double)inputHeight / Math.Max(1, crop.Height))),
            inputHeight,
            maxRecognitionWidth);
        width = RoundUp(width, 8);

        using var resized = crop.Resize(
            new SKImageInfo(width, inputHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
            SKFilterQuality.Medium);
        var inputBitmap = resized ?? crop;
        var tensor = CreateImageTensor(inputBitmap, inputHeight, width, RecognitionMean, RecognitionStd);
        var input = NamedOnnxValue.CreateFromTensor(context.InputName, tensor);

        using var outputs = context.Session.Run(new[] { input });
        var output = outputs.First().AsTensor<float>();
        return DecodeCtc(output, dictionary);
    }

    private static bool IsBetterRecognition(
        (string Text, float Confidence) candidate,
        (string Text, float Confidence) current)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(current.Text))
        {
            return TextQuality(candidate.Text) >= 0.55;
        }

        var candidateQuality = TextQuality(candidate.Text);
        var currentQuality = TextQuality(current.Text);
        if (candidateQuality < 0.55 || candidateQuality + 0.10 < currentQuality)
        {
            return false;
        }

        var currentStructuralMarks = CountStructuralMarks(current.Text);
        if (currentStructuralMarks > 0 &&
            CountStructuralMarks(candidate.Text) < currentStructuralMarks)
        {
            return false;
        }

        if (candidate.Text.Length >= current.Text.Length + 2 &&
            candidate.Confidence >= current.Confidence * 0.85f &&
            candidateQuality >= currentQuality)
        {
            return true;
        }

        return candidate.Confidence >= current.Confidence + 0.08f &&
               candidateQuality >= currentQuality - 0.03;
    }

    private static bool IsBetterColorSeparatedRecognition(
        (string Text, float Confidence) candidate,
        (string Text, float Confidence) current)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text))
        {
            return false;
        }

        if (IsBetterRecognition(candidate, current))
        {
            return true;
        }

        var candidateQuality = TextQuality(candidate.Text);
        var currentQuality = TextQuality(current.Text);
        if (candidateQuality < 0.70 || candidateQuality + 0.08 < currentQuality)
        {
            return false;
        }

        var candidateStructuredScore = StructuredTextScore(candidate.Text);
        var currentStructuredScore = StructuredTextScore(current.Text);
        if (candidateStructuredScore > currentStructuredScore &&
            candidate.Text.Count(char.IsLetterOrDigit) >= Math.Max(4, current.Text.Count(char.IsLetterOrDigit)))
        {
            return true;
        }

        return current.Text.Length <= 6 &&
               candidate.Text.Length >= current.Text.Length * 3 &&
               candidateQuality >= 0.85;
    }

    private static bool IsBetterFontRetryRecognition(
        (string Text, float Confidence) candidate,
        (string Text, float Confidence) current)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text))
        {
            return false;
        }

        if (IsBetterRecognition(candidate, current))
        {
            return true;
        }

        var candidateQuality = TextQuality(candidate.Text);
        var currentQuality = TextQuality(current.Text);
        return candidateQuality >= 0.78 &&
               candidateQuality >= currentQuality - 0.03 &&
               candidate.Text.Count(char.IsLetterOrDigit) >= current.Text.Count(char.IsLetterOrDigit) &&
               candidate.Confidence >= current.Confidence * 0.92f;
    }

    private static bool ShouldRunRecognitionRetries(
        SKBitmap crop,
        (string Text, float Confidence) original,
        OcrInferenceProfile profile)
    {
        if (!profile.EnableRecognitionRetries)
        {
            return false;
        }

        if (!IsConfidentRecognition(original, profile))
        {
            return true;
        }

        return HasLikelyColoredText(crop);
    }

    private static bool IsConfidentRecognition((string Text, float Confidence) result, OcrInferenceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return false;
        }

        return result.Confidence >= profile.LowConfidenceRetryThreshold &&
               TextQuality(result.Text) >= 0.78;
    }

    private static bool HasLikelyColoredText(SKBitmap crop)
    {
        var stepX = Math.Max(1, crop.Width / 48);
        var stepY = Math.Max(1, crop.Height / 16);
        var chromaPixels = 0;
        var sampledPixels = 0;

        for (var y = 0; y < crop.Height; y += stepY)
        {
            for (var x = 0; x < crop.Width; x += stepX)
            {
                var color = crop.GetPixel(x, y);
                var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
                var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
                if (max - min >= 24)
                {
                    chromaPixels++;
                }

                sampledPixels++;
            }
        }

        return sampledPixels > 0 && chromaPixels / (double)sampledPixels >= 0.08;
    }

    private static bool IsBetterVerticalRecognition(
        (string Text, float Confidence) candidate,
        (string Text, float Confidence) current)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text))
        {
            return false;
        }

        if (IsBetterRecognition(candidate, current))
        {
            return true;
        }

        var candidateQuality = TextQuality(candidate.Text);
        var currentQuality = TextQuality(current.Text);
        if (candidateQuality < 0.62)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(current.Text) || currentQuality < 0.50)
        {
            return candidate.Text.Count(char.IsLetterOrDigit) >= 1;
        }

        return candidateQuality >= currentQuality - 0.04 &&
               candidate.Text.Count(char.IsLetterOrDigit) >= current.Text.Count(char.IsLetterOrDigit) &&
               candidate.Confidence >= current.Confidence * 0.80f;
    }

    private static double TextQuality(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var meaningful = 0;
        var suspicious = 0;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            meaningful++;
            if (!IsExpectedOcrCharacter(character))
            {
                suspicious++;
            }
        }

        return meaningful == 0 ? 0 : 1.0 - suspicious / (double)meaningful;
    }

    private static bool IsExpectedOcrCharacter(char character)
    {
        return char.IsLetterOrDigit(character) ||
               char.IsWhiteSpace(character) ||
               character is '/' or '\\' or '-' or '_' or '.' or ':' or ';' or ',' or '!' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or '@' or '#' or '$' or '%' or '&' or '+' or '=' or '\'' or '"' or '|';
    }

    private static int CountStructuralMarks(string text)
    {
        return text.Count(character => character is '/' or '\\' or ':' or '.' or '-' or '_');
    }

    private static bool LooksLikeStructuredText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return CountStructuralMarks(text) >= 2 ||
               text.Contains(".dll", StringComparison.OrdinalIgnoreCase) ||
               text.Contains(".exe", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Microsoft.", StringComparison.OrdinalIgnoreCase);
    }

    private static int StructuredTextScore(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var score = CountStructuralMarks(text) * 2;
        if (text.Contains('\\'))
        {
            score += 3;
        }

        if (text.Contains(".dll", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (text.Contains("Loaded", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Skipped", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
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

        return DecodeCtcSequence(output, dictionary, batchIndex: 0);
    }

    private static (string Text, float Confidence) DecodeCtcSequence(
        Tensor<float> output,
        IReadOnlyList<string> dictionary,
        int batchIndex)
    {
        var dimensions = output.Dimensions.ToArray();
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
                var value = output[batchIndex, step, cls];
                if (value > bestLogit)
                {
                    bestLogit = value;
                    bestClass = cls;
                }

                logitSum += Math.Exp(Math.Clamp(value, -30, 30));
            }

            if (bestClass != 0 && bestClass != previousClass)
            {
                var token = ResolveCtcToken(bestClass, dictionary);
                if (token != null)
                {
                    AddToken(text, token);
                    confidences.Add((float)(Math.Exp(Math.Clamp(bestLogit, -30, 30)) / Math.Max(logitSum, double.Epsilon)));
                }
            }

            previousClass = bestClass;
        }

        return (string.Concat(text).Trim(), confidences.Count == 0 ? 0 : confidences.Average());
    }

    private static string? ResolveCtcToken(int cls, IReadOnlyList<string> dictionary)
    {
        var dictionaryIndex = cls - 1;
        if (dictionaryIndex >= 0 && dictionaryIndex < dictionary.Count)
        {
            return NormalizeToken(dictionary[dictionaryIndex]);
        }

        return dictionaryIndex == dictionary.Count ? " " : null;
    }

    private static string NormalizeToken(string token)
    {
        return token switch
        {
            "\uFF0F" => "/",
            "\uFF3C" => "\\",
            "\uFF0D" => "-",
            "\uFF0E" => ".",
            "\uFF1A" => ":",
            _ => token
        };
    }

    private static void AddToken(List<string> text, string token)
    {
        if (token == " ")
        {
            if (text.Count > 0 && text[^1] != " ")
            {
                text.Add(" ");
            }

            return;
        }

        if (IsLeadingPunctuation(token) && text.Count > 0 && text[^1] == " ")
        {
            text.RemoveAt(text.Count - 1);
        }

        text.Add(token);
    }

    private static bool IsLeadingPunctuation(string text)
    {
        return text is "." or "," or ":" or ";" or "!" or "?" or ")" or "]" or "}" or "%";
    }

    private static DenseTensor<float> CreateImageTensor(
        SKBitmap bitmap,
        int height,
        int width,
        IReadOnlyList<float> mean,
        IReadOnlyList<float> std)
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
                    tensor[0, channel, y, x] = ((value / 255f) - mean[channel]) / std[channel];
                }
            }
        }

        return tensor;
    }

    private static SKBitmap CreateContrastNormalizedBitmap(SKBitmap source)
    {
        var luminance = new byte[source.Width * source.Height];
        var histogram = new int[256];

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                var value = (byte)Math.Clamp(
                    (int)Math.Round(0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue),
                    0,
                    255);
                luminance[y * source.Width + x] = value;
                histogram[value]++;
            }
        }

        var low = Percentile(histogram, luminance.Length, 0.02);
        var high = Percentile(histogram, luminance.Length, 0.98);
        if (high <= low)
        {
            high = Math.Min(255, low + 1);
        }

        var output = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var range = high - low;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var value = luminance[y * source.Width + x];
                var normalized = (byte)Math.Clamp((int)Math.Round((value - low) * 255.0 / range), 0, 255);
                output.SetPixel(x, y, new SKColor(normalized, normalized, normalized, source.GetPixel(x, y).Alpha));
            }
        }

        return output;
    }

    private static SKBitmap? TryCreateColorSeparatedBitmap(SKBitmap source)
    {
        if (source.Width < 3 || source.Height < 3)
        {
            return null;
        }

        var background = EstimateBackgroundColor(source);
        var distances = new int[source.Width * source.Height];
        var histogram = new int[256];
        var maxDistance = 0;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                var distance = ColorDistance(color, background);
                distances[y * source.Width + x] = distance;
                histogram[distance]++;
                maxDistance = Math.Max(maxDistance, distance);
            }
        }

        var foregroundThreshold = Math.Max(28, Percentile(histogram, distances.Length, 0.82));
        if (maxDistance < foregroundThreshold + 12)
        {
            return null;
        }

        var foregroundPixels = distances.Count(distance => distance >= foregroundThreshold);
        var foregroundRatio = foregroundPixels / (double)distances.Length;
        if (foregroundRatio < 0.01 || foregroundRatio > 0.55)
        {
            return null;
        }

        var output = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var distance = distances[y * source.Width + x];
                var ink = distance >= foregroundThreshold
                    ? 0
                    : distance <= foregroundThreshold * 0.45
                        ? 255
                        : 255 - (int)Math.Round((distance - foregroundThreshold * 0.45) * 255.0 / Math.Max(1, foregroundThreshold * 0.55));

                var value = (byte)Math.Clamp(ink, 0, 255);
                output.SetPixel(x, y, new SKColor(value, value, value, source.GetPixel(x, y).Alpha));
            }
        }

        return output;
    }

    private static SKBitmap? TryCreateSharpenedBitmap(SKBitmap source)
    {
        if (source.Width < 3 || source.Height < 3)
        {
            return null;
        }

        var scale = source.Height < 36 ? 2 : 1;
        var working = source;
        SKBitmap? scaled = null;
        if (scale > 1)
        {
            scaled = source.Resize(
                new SKImageInfo(source.Width * scale, source.Height * scale, SKColorType.Rgba8888, SKAlphaType.Premul),
                SKFilterQuality.High);
            working = scaled ?? source;
        }

        var output = new SKBitmap(working.Width, working.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < working.Height; y++)
        {
            for (var x = 0; x < working.Width; x++)
            {
                var center = working.GetPixel(x, y);
                var blurred = AverageNeighborhood(working, x, y);
                var red = SharpenChannel(center.Red, blurred.Red);
                var green = SharpenChannel(center.Green, blurred.Green);
                var blue = SharpenChannel(center.Blue, blurred.Blue);
                output.SetPixel(x, y, new SKColor(red, green, blue, center.Alpha));
            }
        }

        scaled?.Dispose();
        return output;
    }

    private static SKBitmap RotateBitmapCounterClockwise(SKBitmap source)
    {
        var output = new SKBitmap(source.Height, source.Width, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        canvas.Translate(0, output.Height);
        canvas.RotateDegrees(-90);
        canvas.DrawBitmap(source, 0, 0);
        return output;
    }

    private static SKBitmap RotateBitmapClockwise(SKBitmap source)
    {
        var output = new SKBitmap(source.Height, source.Width, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        canvas.Translate(output.Width, 0);
        canvas.RotateDegrees(90);
        canvas.DrawBitmap(source, 0, 0);
        return output;
    }

    private static SKColor AverageNeighborhood(SKBitmap bitmap, int centerX, int centerY)
    {
        var red = 0;
        var green = 0;
        var blue = 0;
        var count = 0;

        for (var y = Math.Max(0, centerY - 1); y <= Math.Min(bitmap.Height - 1, centerY + 1); y++)
        {
            for (var x = Math.Max(0, centerX - 1); x <= Math.Min(bitmap.Width - 1, centerX + 1); x++)
            {
                var color = bitmap.GetPixel(x, y);
                red += color.Red;
                green += color.Green;
                blue += color.Blue;
                count++;
            }
        }

        return new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count));
    }

    private static byte SharpenChannel(byte center, byte blurred)
    {
        return (byte)Math.Clamp((int)Math.Round(center + (center - blurred) * 1.2), 0, 255);
    }

    private static SKColor EstimateBackgroundColor(SKBitmap source)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        var count = 0;

        for (var x = 0; x < source.Width; x++)
        {
            Add(source.GetPixel(x, 0));
            Add(source.GetPixel(x, source.Height - 1));
        }

        for (var y = 1; y < source.Height - 1; y++)
        {
            Add(source.GetPixel(0, y));
            Add(source.GetPixel(source.Width - 1, y));
        }

        return count == 0
            ? new SKColor(255, 255, 255)
            : new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count));

        void Add(SKColor color)
        {
            red += color.Red;
            green += color.Green;
            blue += color.Blue;
            count++;
        }
    }

    private static int ColorDistance(SKColor left, SKColor right)
    {
        var red = left.Red - right.Red;
        var green = left.Green - right.Green;
        var blue = left.Blue - right.Blue;
        return Math.Clamp((int)Math.Round(Math.Sqrt(red * red + green * green + blue * blue)), 0, 255);
    }

    private static int Percentile(IReadOnlyList<int> histogram, int total, double percentile)
    {
        var target = Math.Clamp((int)Math.Round(total * percentile), 0, total - 1);
        var cumulative = 0;
        for (var i = 0; i < histogram.Count; i++)
        {
            cumulative += histogram[i];
            if (cumulative > target)
            {
                return i;
            }
        }

        return histogram.Count - 1;
    }

    private static SKBitmap? Crop(SKBitmap bitmap, SKRectI box, int padding)
    {
        var expanded = Expand(box, padding, bitmap.Width, bitmap.Height);
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
        return new SKRectI(
            Math.Clamp((int)MathF.Floor(box.Left * scaleX), 0, maxWidth),
            Math.Clamp((int)MathF.Floor(box.Top * scaleY), 0, maxHeight),
            Math.Clamp((int)MathF.Ceiling(box.Right * scaleX), 0, maxWidth),
            Math.Clamp((int)MathF.Ceiling(box.Bottom * scaleY), 0, maxHeight));
    }

    private static SKRectI UnclipBox(SKRectI box, float ratio, int maxWidth, int maxHeight)
    {
        var perimeter = 2 * Math.Max(1, box.Width + box.Height);
        var area = Math.Max(1, box.Width * box.Height);
        var padding = Math.Max(1, (int)Math.Round(area * (ratio - 1f) / perimeter));
        return Expand(box, padding, maxWidth, maxHeight);
    }

    private static SKRectI Expand(SKRectI box, int padding, int maxWidth, int maxHeight)
    {
        return new SKRectI(
            Math.Max(0, box.Left - padding),
            Math.Max(0, box.Top - padding),
            Math.Min(maxWidth, box.Right + padding),
            Math.Min(maxHeight, box.Bottom + padding));
    }

    private static SKSizeI ResolveDetectionInputSize(int width, int height, int limitSideLength)
    {
        var maxSide = Math.Max(width, height);
        var ratio = maxSide > limitSideLength
            ? limitSideLength / (double)maxSide
            : 1.0;

        return new SKSizeI(
            RoundUp(Math.Max(32, (int)Math.Round(width * ratio)), 32),
            RoundUp(Math.Max(32, (int)Math.Round(height * ratio)), 32));
    }

    private static IReadOnlyList<string> LoadDictionary(string path)
    {
        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        var cached = DictionaryCache.GetOrAdd(path, Create);
        if (cached.LastWriteTimeUtc == lastWriteTimeUtc)
        {
            return cached.Tokens;
        }

        var refreshed = Create(path);
        DictionaryCache[path] = refreshed;
        return refreshed.Tokens;

        CachedDictionary Create(string dictionaryPath)
        {
            return new CachedDictionary(
                File.GetLastWriteTimeUtc(dictionaryPath),
                File.ReadAllLines(dictionaryPath)
                    .Select(line => line.TrimEnd('\r', '\n'))
                    .ToList());
        }
    }

    private static int RoundUp(int value, int multiple)
    {
        return ((value + multiple - 1) / multiple) * multiple;
    }

    private sealed record OcrInferenceProfile(
        int DetectionLimitSideLength,
        float DetectionBitmapThreshold,
        float DetectionBoxThreshold,
        float DetectionUnclipRatio,
        int MinimumBoxSize,
        int CropPadding,
        int VerticalCropPadding,
        int MaxRecognitionWidth,
        float LowConfidenceRetryThreshold,
        bool AlwaysRunContrastDetectionPass,
        bool EnableRecognitionRetries,
        int MaximumGroupedLineWidth,
        int MaximumGroupedVerticalHeight);

    private sealed record DetectedTextBox(SKRectI Bounds, float Score);

    private sealed record TextBounds(SKRectI Bounds, TextOrientation Orientation);

    private sealed record TextRecognitionItem(SKRectI Bounds, TextOrientation Orientation, float DetectionScore);

    private sealed record RecognitionContext(InferenceSession Session, string InputName, int InputHeight);

    private sealed record CachedDictionary(DateTime LastWriteTimeUtc, IReadOnlyList<string> Tokens);

    private sealed class OcrInferenceTiming
    {
        public TimeSpan Decode { get; set; }

        public TimeSpan DetectionSessionLoad { get; set; }

        public TimeSpan DetectionRunAndDispose { get; set; }

        public TimeSpan FirstTrim { get; set; }

        public TimeSpan DictionaryLoad { get; set; }

        public TimeSpan RecognitionSessionLoad { get; set; }

        public TimeSpan Split { get; set; }

        public TimeSpan Crop { get; set; }

        public TimeSpan Recognition { get; set; }

        public TimeSpan RecognitionLoopAndDispose { get; set; }

        public TimeSpan SecondTrim { get; set; }

        public TimeSpan ResultOrdering { get; set; }

        public int DetectedBoxes { get; set; }

        public int TextCandidates { get; set; }

        public int GroupedTextCandidates { get; set; }

        public int RecognitionCalls { get; set; }

        public int OutputRegions { get; set; }

        public string? RecognitionAccelerator { get; set; }
    }

    private enum TextOrientation
    {
        Horizontal,
        Vertical
    }

    private enum VerticalRecognitionOrientation
    {
        Original,
        CounterClockwise,
        Clockwise
    }
}
