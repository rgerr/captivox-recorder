using Microsoft.Win32;
using System.Diagnostics;

namespace Captivox.Recorder.Services;

internal static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Eigen naam: een andere Captivox-installatie op dezelfde pc houdt zijn eigen opstartregel.
    private const string ValueName = "CaptivoxRecorderFree";

    public static void Apply(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            throw new InvalidOperationException("Windows Run-register kon niet worden geopend.");
        }

        if (!enabled)
        {
            if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return;
        }

        var exePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            throw new InvalidOperationException("Pad naar Captivox Recorder kon niet worden bepaald voor Windows-opstartregistratie.");
        }

        var command = '"' + exePath + '"' + (startMinimized ? " --minimized" : string.Empty);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static string GetExecutablePath()
    {
        try
        {
            var location = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(location))
            {
                return location;
            }
        }
        catch
        {
        }

        try
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var candidate = Path.Combine(AppContext.BaseDirectory, processName + ".exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}
