using System.Management;

namespace GlujLens.Services;

public sealed class GpuHardwareDetector
{
    public IReadOnlyList<GpuHardwareInfo> GetVideoControllers()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");

            return searcher
                .Get()
                .Cast<ManagementObject>()
                .Select(ReadGpu)
                .Where(gpu => !string.IsNullOrWhiteSpace(gpu.Name))
                .OrderByDescending(gpu => gpu.AdapterRamBytes)
                .ToList();
        }
        catch
        {
            return Array.Empty<GpuHardwareInfo>();
        }
    }

    public string CreateHardwareSignature()
    {
        var parts = GetVideoControllers()
            .Select(gpu => $"{gpu.Name}|{gpu.DriverVersion}|{gpu.AdapterRamBytes}")
            .DefaultIfEmpty("unknown-gpu");

        return string.Join(";", parts);
    }

    public ulong GetLargestAdapterRamBytes()
    {
        return GetVideoControllers()
            .Select(gpu => gpu.AdapterRamBytes)
            .DefaultIfEmpty(0UL)
            .Max();
    }

    private static GpuHardwareInfo ReadGpu(ManagementBaseObject obj)
    {
        return new GpuHardwareInfo
        {
            Name = Convert.ToString(obj["Name"]) ?? string.Empty,
            DriverVersion = Convert.ToString(obj["DriverVersion"]) ?? string.Empty,
            AdapterRamBytes = ReadAdapterRam(obj["AdapterRAM"])
        };
    }

    private static ulong ReadAdapterRam(object? value)
    {
        try
        {
            return value switch
            {
                null => 0,
                uint uintValue => uintValue,
                ulong ulongValue => ulongValue,
                int intValue when intValue > 0 => (ulong)intValue,
                long longValue when longValue > 0 => (ulong)longValue,
                _ => Convert.ToUInt64(value)
            };
        }
        catch
        {
            return 0;
        }
    }
}
