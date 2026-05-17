using Microsoft.ML.OnnxRuntime;

namespace GlujLens.Services;

public sealed class OnnxRuntimeSessionFactory
{
    public OnnxSessionBundle LoadSessions(
        IReadOnlyList<string> modelPaths,
        string? accelerator,
        CancellationToken cancellationToken = default,
        bool optimizeForLowMemory = false)
    {
        if (modelPaths.Count == 0)
        {
            throw new InvalidOperationException("No ONNX model files were found.");
        }

        var requestedAccelerator = NormalizeAccelerator(accelerator);
        var sessions = new List<OnnxLoadedSession>();
        var activeAccelerator = requestedAccelerator;

        try
        {
            foreach (var modelPath in modelPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sessions.Add(LoadSession(modelPath, requestedAccelerator, optimizeForLowMemory));
            }
        }
        catch when (string.Equals(requestedAccelerator, "Auto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(requestedAccelerator, "DirectML", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var loadedSession in sessions)
            {
                loadedSession.Dispose();
            }

            sessions.Clear();
            activeAccelerator = "CPU";

            foreach (var modelPath in modelPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sessions.Add(LoadSession(modelPath, "CPU", optimizeForLowMemory));
            }
        }

        return new OnnxSessionBundle(sessions, activeAccelerator);
    }

    private static OnnxLoadedSession LoadSession(string modelPath, string accelerator, bool optimizeForLowMemory)
    {
        var options = CreateSessionOptions(accelerator, optimizeForLowMemory);
        try
        {
            var session = new InferenceSession(modelPath, options);
            return new OnnxLoadedSession(modelPath, session);
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }

    private static SessionOptions CreateSessionOptions(string accelerator, bool optimizeForLowMemory)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
        };

        if (optimizeForLowMemory)
        {
            options.EnableMemoryPattern = false;
            options.EnableCpuMemArena = false;
        }

        if (string.Equals(accelerator, "DirectML", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(accelerator, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            options.EnableMemoryPattern = false;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            options.AppendExecutionProvider_DML(0);
        }
        else
        {
            ConfigureCpuParallelism(options);
        }

        return options;
    }

    private static void ConfigureCpuParallelism(SessionOptions options)
    {
        var processorCount = Environment.ProcessorCount;
        if (processorCount <= 1)
        {
            return;
        }

        options.ExecutionMode = ExecutionMode.ORT_PARALLEL;
        options.IntraOpNumThreads = Math.Clamp(processorCount - 1, 1, 8);
        options.InterOpNumThreads = Math.Clamp(processorCount / 2, 1, 4);
    }

    private static string NormalizeAccelerator(string? accelerator)
    {
        return accelerator?.Trim() switch
        {
            "CPU" => "CPU",
            "DirectML" => "DirectML",
            _ => "Auto"
        };
    }
}

public sealed class OnnxSessionBundle : IDisposable
{
    public OnnxSessionBundle(IReadOnlyList<OnnxLoadedSession> sessions, string accelerator)
    {
        Sessions = sessions;
        Accelerator = accelerator;
    }

    public IReadOnlyList<OnnxLoadedSession> Sessions { get; }

    public string Accelerator { get; }

    public void Dispose()
    {
        foreach (var session in Sessions)
        {
            session.Dispose();
        }
    }
}

public sealed class OnnxLoadedSession : IDisposable
{
    public OnnxLoadedSession(string modelPath, InferenceSession session)
    {
        ModelPath = modelPath;
        Session = session;
    }

    public string ModelPath { get; }

    public InferenceSession Session { get; }

    public string Name => Path.GetFileName(ModelPath);

    public void Dispose()
    {
        Session.Dispose();
    }
}
