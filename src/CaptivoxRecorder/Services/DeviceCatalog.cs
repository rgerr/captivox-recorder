using Captivox.Recorder.Models;
using NAudio.CoreAudioApi;

namespace Captivox.Recorder.Services;

public static class DeviceCatalog
{
    public static IReadOnlyList<DeviceInfo> GetCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID;
        }
        catch { }

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new DeviceInfo
            {
                Id = d.ID,
                FriendlyName = d.FriendlyName,
                IsDefault = string.Equals(d.ID, defaultId, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public static IReadOnlyList<DeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console).ID;
        }
        catch { }

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new DeviceInfo
            {
                Id = d.ID,
                FriendlyName = d.FriendlyName,
                IsDefault = string.Equals(d.ID, defaultId, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public static void PrintToConsole()
    {
        Console.WriteLine("Capture devices:");
        foreach (var device in GetCaptureDevices())
        {
            Console.WriteLine($"  [MIC] {device.Id}");
            Console.WriteLine($"        {device.FriendlyName}");
        }

        Console.WriteLine();
        Console.WriteLine("Render devices:");
        foreach (var device in GetRenderDevices())
        {
            Console.WriteLine($"  [SYS] {device.Id}");
            Console.WriteLine($"        {device.FriendlyName}");
        }
    }

    public static MMDevice GetCaptureDevice(string? id)
    {
        var enumerator = new MMDeviceEnumerator();
        return string.IsNullOrWhiteSpace(id)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
            : enumerator.GetDevice(id);
    }

    public static MMDevice GetRenderDevice(string? id)
    {
        var enumerator = new MMDeviceEnumerator();
        return string.IsNullOrWhiteSpace(id)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
            : enumerator.GetDevice(id);
    }
}
