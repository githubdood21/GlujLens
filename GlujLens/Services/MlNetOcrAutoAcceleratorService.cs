using GlujLens.Models;
using System.Diagnostics;

namespace GlujLens.Services;

public sealed class MlNetOcrAutoAcceleratorService
{
    private const ulong MinimumDirectMlAdapterRamBytes = 2UL * 1024 * 1024 * 1024;
    private readonly AppSettings _settings;
    private readonly MlNetOcrModelCatalog _modelCatalog;
    private readonly OnnxRuntimeSessionFactory _sessionFactory;
    private readonly GpuHardwareDetector _gpuHardwareDetector;
    private readonly object _lock = new();

    public MlNetOcrAutoAcceleratorService(
        AppSettings settings,
        MlNetOcrModelCatalog modelCatalog,
        OnnxRuntimeSessionFactory sessionFactory,
        GpuHardwareDetector gpuHardwareDetector)
    {
        _settings = settings;
        _modelCatalog = modelCatalog;
        _sessionFactory = sessionFactory;
        _gpuHardwareDetector = gpuHardwareDetector;
    }

    public string ResolveEffectiveAccelerator(CancellationToken cancellationToken = default)
    {
        var configured = _settings.MlNetOcrAccelerator?.Trim();
        if (string.Equals(configured, "CPU", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configured, "DirectML", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(configured, "DirectML", StringComparison.OrdinalIgnoreCase)
                ? "DirectML"
                : "CPU";
        }

        EnsureAutoAccelerator(cancellationToken);
        return _settings.MlNetOcrAutoAccelerator ?? "CPU";
    }

    public void EnsureAutoAccelerator(CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_settings.MlNetOcrAccelerator, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_lock)
        {
            var model = _modelCatalog.GetSelectedPaddleOcrModel();
            var signature = CreateSignature(model);
            if (!string.IsNullOrWhiteSpace(_settings.MlNetOcrAutoAccelerator) &&
                string.Equals(_settings.MlNetOcrAutoAcceleratorSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            var largestAdapterRam = _gpuHardwareDetector.GetLargestAdapterRamBytes();
            if (largestAdapterRam > 0 && largestAdapterRam < MinimumDirectMlAdapterRamBytes)
            {
                Cache("CPU", signature, $"Largest detected GPU adapter RAM is {FormatBytes(largestAdapterRam)}, below the 2 GB DirectML threshold.");
                return;
            }

            if (model == null)
            {
                Cache("CPU", signature, "No PaddleOCR ONNX model is selected for Auto accelerator benchmarking.");
                return;
            }

            var modelPaths = new[] { model.DetectionModelPath, model.RecognitionModelPath };
            var cpu = BenchmarkModelLoad(modelPaths, "CPU", cancellationToken);
            var directMl = BenchmarkModelLoad(modelPaths, "DirectML", cancellationToken);

            if (!directMl.Success)
            {
                Cache("CPU", signature, $"DirectML warmup failed; using CPU. {directMl.ErrorMessage}");
                return;
            }

            if (!cpu.Success || directMl.Elapsed <= cpu.Elapsed)
            {
                Cache("DirectML", signature, $"DirectML warmup {FormatElapsed(directMl.Elapsed)}; CPU warmup {(cpu.Success ? FormatElapsed(cpu.Elapsed) : "failed")}.");
                return;
            }

            Cache("CPU", signature, $"CPU warmup {FormatElapsed(cpu.Elapsed)} beat DirectML warmup {FormatElapsed(directMl.Elapsed)}.");
        }
    }

    private BenchmarkResult BenchmarkModelLoad(
        IReadOnlyList<string> modelPaths,
        string accelerator,
        CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var sessions = _sessionFactory.LoadSessions(modelPaths, accelerator, cancellationToken);
            stopwatch.Stop();
            return new BenchmarkResult(true, stopwatch.Elapsed, null);
        }
        catch (Exception ex)
        {
            return new BenchmarkResult(false, TimeSpan.MaxValue, ex.Message);
        }
    }

    private string CreateSignature(PaddleOnnxOcrModel? model)
    {
        var hardware = _gpuHardwareDetector.CreateHardwareSignature();
        var modelSignature = model == null
            ? "no-model"
            : $"{model.DetectionModelPath}|{File.GetLastWriteTimeUtc(model.DetectionModelPath).Ticks}|{model.RecognitionModelPath}|{File.GetLastWriteTimeUtc(model.RecognitionModelPath).Ticks}";

        return $"{hardware}|{modelSignature}";
    }

    private void Cache(string accelerator, string signature, string reason)
    {
        _settings.MlNetOcrAutoAccelerator = accelerator;
        _settings.MlNetOcrAutoAcceleratorSignature = signature;
        _settings.MlNetOcrAutoAcceleratorReason = reason;
        _settings.Save();
    }

    private static string FormatBytes(ulong bytes)
    {
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:0.0}s"
            : $"{elapsed.TotalMilliseconds:0}ms";
    }

    private sealed record BenchmarkResult(bool Success, TimeSpan Elapsed, string? ErrorMessage);
}
