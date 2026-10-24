using System.Reflection;
using System.Text;
using System.Text.Json;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

internal static class AppRuntime
{
    private const string ProductFolder = Branding.AppDataFolder;
    private static readonly object AppDataPathSync = new();
    private static string? _resolvedAppDataDirectory;
    private static readonly object ActiveRecordingSync = new();
    private static string? _activeManifestPath;
    private static string? _activeSessionLogPath;

    public static string GetAppDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedAppDataDirectory))
        {
            return _resolvedAppDataDirectory!;
        }

        lock (AppDataPathSync)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedAppDataDirectory))
            {
                return _resolvedAppDataDirectory!;
            }

            // De override (eigen naam per smaak, zie Branding) is alleen voor tests in een afgeschermde map. Er wordt nooit
            // iets overgenomen uit de gegevensmap van een andere Captivox.
            var overridePath = Environment.GetEnvironmentVariable(Branding.AppDataOverrideVariable);
            var preferredPath = !string.IsNullOrWhiteSpace(overridePath)
                ? Path.GetFullPath(overridePath)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolder);
            Directory.CreateDirectory(preferredPath);
            _resolvedAppDataDirectory = preferredPath;
            return preferredPath;
        }
    }

    public static string GetLogDirectory()
    {
        var path = Path.Combine(GetAppDataDirectory(), "logs");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetSettingsDirectory()
    {
        var path = Path.Combine(GetAppDataDirectory(), "settings");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetSecretsDirectory()
    {
        var path = Path.Combine(GetAppDataDirectory(), "secrets");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetTempDirectory()
    {
        var path = Path.Combine(GetAppDataDirectory(), "temp");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetLogFilePath()
        => Path.Combine(GetLogDirectory(), $"app_{DateTimeOffset.Now:yyyyMMdd}.log");


    public static string GetSelfTestDirectory()
    {
        var path = Path.Combine(GetTempDirectory(), "worker_selftest_latest");
        Directory.CreateDirectory(path);
        return path;
    }


    public static string GetWorkerRuntimeDirectory()
    {
        var path = Path.Combine(GetTempDirectory(), "worker_runtime");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetWorkerRuntimeStatePath()
        => Path.Combine(GetWorkerRuntimeDirectory(), "worker_status.json");

    public static string GetRetentionRuntimeDirectory()
    {
        var path = Path.Combine(GetTempDirectory(), "retention_runtime");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetRetentionRuntimeStatePath()
        => Path.Combine(GetRetentionRuntimeDirectory(), "retention_status.json");

    public static string GetProductVersionTag()
    {
        var assembly = typeof(AppRuntime).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = NormalizeVersionTag(informational);
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        return NormalizeVersionTag(assembly.GetName().Version?.ToString()) ?? "v?";
    }


    public static string GetWorkerCompatibilityVersionTag()
    {
        var assembly = typeof(AppRuntime).Assembly;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var normalized = NormalizeVersionTag(TrimVersionToThreeComponents(fileVersion));
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            return $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        }

        return GetProductVersionTag();
    }

    private static string? TrimVersionToThreeComponents(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var text = version.Trim();
        var dashIndex = text.IndexOf('-');
        if (dashIndex >= 0)
        {
            text = text[..dashIndex];
        }

        var plusIndex = text.IndexOf('+');
        if (plusIndex >= 0)
        {
            text = text[..plusIndex];
        }

        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3)
        {
            return string.Join('.', parts.Take(3));
        }

        return text;
    }

    public static string? NormalizeVersionTag(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var normalized = version.Trim();
        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.StartsWith('v') ? normalized : $"v{normalized}";
    }


    public static void LogException(string title, Exception? exception)
        => SafeWrite($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");

    public static void LogMessage(string message)
        => SafeWrite($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");

    public static string? FindProjectFile(string relativePath)
    {
        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    public static void RegisterActiveRecording(string manifestPath, string sessionLogPath)
    {
        lock (ActiveRecordingSync)
        {
            _activeManifestPath = manifestPath;
            _activeSessionLogPath = sessionLogPath;
        }
    }

    public static void ClearActiveRecording(string? manifestPath = null)
    {
        lock (ActiveRecordingSync)
        {
            if (!string.IsNullOrWhiteSpace(manifestPath) && !string.Equals(_activeManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activeManifestPath = null;
            _activeSessionLogPath = null;
        }
    }

    public static void TryMarkActiveRecordingInterrupted(string reason, Exception? exception = null)
    {
        string? manifestPath;
        string? sessionLogPath;
        lock (ActiveRecordingSync)
        {
            manifestPath = _activeManifestPath;
            sessionLogPath = _activeSessionLogPath;
        }

        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<SessionManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new SessionManifest();
            if (!string.Equals(manifest.Status, "recording", StringComparison.OrdinalIgnoreCase))
            {
                ClearActiveRecording(manifestPath);
                return;
            }

            var now = DateTimeOffset.Now;
            manifest.Status = "interrupted";
            manifest.FinishedAt = now;
            manifest.ErrorCode = "APP_INTERRUPTED";
            manifest.ErrorMessage = exception is null ? reason : $"{reason}: {exception.Message}";
            manifest.RecoveryStatus = "interrupted";
            manifest.RecoveryNotes = manifest.ErrorMessage;
            manifest.LastHeartbeatAt = now;
            manifest.TranscriptionStatus = null;
            manifest.TranscriptionStartedAt = null;
            manifest.TranscriptionFinishedAt = null;
            manifest.TranscriptionError = null;
            if (manifest.PhaseTimings?.Recorder is not null)
            {
                manifest.PhaseTimings.Recorder["interrupted_at"] = now.ToString("o");
            }

            var normalizedJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            AtomicFileWriter.WriteAllText(manifestPath, normalizedJson, Encoding.UTF8);

            if (!string.IsNullOrWhiteSpace(sessionLogPath))
            {
                File.AppendAllText(sessionLogPath, $"[{now:yyyy-MM-dd HH:mm:ss}] Sessie als interrupted gemarkeerd: {manifest.ErrorMessage}{Environment.NewLine}", Encoding.UTF8); // geen vertaling
            }

            SafeWrite($"[{now:yyyy-MM-dd HH:mm:ss.fff}] Actieve opnamesessie als interrupted gemarkeerd: {manifestPath} :: {manifest.ErrorMessage}{Environment.NewLine}"); // geen vertaling
        }
        catch (Exception ex)
        {
            SafeWrite($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] Active recording interruption marking failed{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        finally
        {
            ClearActiveRecording(manifestPath);
        }
    }

    private static void SafeWrite(string content)
    {
        try
        {
            File.AppendAllText(GetLogFilePath(), content);
        }
        catch
        {
            // Logging should never crash the recorder.
        }
    }
}
