namespace GlujLens.Services;

public sealed class GpuHardwareInfo
{
    public string Name { get; init; } = string.Empty;

    public string DriverVersion { get; init; } = string.Empty;

    public ulong AdapterRamBytes { get; init; }
}
