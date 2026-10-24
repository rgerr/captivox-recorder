using System.Diagnostics;
using LocalCaptureAgent.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LocalCaptureAgent.Services;

internal static class PreflightCheckService
{
    public static PreflightReport Run(string outputRoot, string? micDeviceId, string? systemDeviceId, ApplicationSettings? settings = null)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(micDeviceId))
        {
            errors.Add("Geen microfoon geselecteerd.");
        }

        if (string.IsNullOrWhiteSpace(systemDeviceId))
        {
            errors.Add("Geen systeemaudio-device geselecteerd.");
        }

        if (!string.IsNullOrWhiteSpace(outputRoot))
        {
            try
            {
                Directory.CreateDirectory(outputRoot);
                using var fs = new FileStream(Path.Combine(outputRoot, $".captivox-preflight-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
                lines.Add($"Uitvoermap schrijfbaar: {outputRoot}");
            }
            catch (Exception ex)
            {
                errors.Add($"Uitvoermap niet bruikbaar: {ex.Message}");
            }
        }
        else
        {
            errors.Add("Uitvoermap ontbreekt.");
        }

        using var enumerator = new MMDeviceEnumerator();
        MMDevice? defaultMic = null;
        MMDevice? defaultRender = null;

        try { defaultMic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); } catch { }
        try { defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console); } catch { }

        string? selectedMicFriendlyName = null;
        string? selectedRenderFriendlyName = null;

        if (!string.IsNullOrWhiteSpace(micDeviceId))
        {
            selectedMicFriendlyName = InspectCaptureDevice(micDeviceId, defaultMic, lines, warnings, errors);
        }

        if (!string.IsNullOrWhiteSpace(systemDeviceId))
        {
            selectedRenderFriendlyName = InspectRenderDevice(systemDeviceId, defaultRender, lines, warnings, errors);
        }

        var deviceProfile = AudioDeviceRiskAnalyzer.DetermineDeviceProfile(selectedMicFriendlyName, selectedRenderFriendlyName);
        if (!string.IsNullOrWhiteSpace(deviceProfile))
        {
            lines.Add($"Deviceprofiel: {deviceProfile}");
        }

        if (defaultMic is not null)
        {
            lines.Add($"Huidige standaardmicrofoon: {defaultMic.FriendlyName}");
        }

        if (defaultRender is not null)
        {
            lines.Add($"Huidig standaard afspeelapparaat: {defaultRender.FriendlyName}");
        }

        if (settings is not null)
        {
            InspectWorkerConfiguration(settings, lines, warnings, errors);
        }

        if (warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Waarschuwingen:");
            lines.AddRange(warnings.Select(w => $"- {w}"));
        }

        if (errors.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Blokkerende punten:");
            lines.AddRange(errors.Select(e => $"- {e}"));
        }

        if (warnings.Count == 0 && errors.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add("Preflight geslaagd. Devices, uitvoermap en relevante transcriptie-afhankelijkheden zijn bereikbaar.");
        }

        return new PreflightReport
        {
            Success = errors.Count == 0,
            HasWarnings = warnings.Count > 0,
            Lines = lines,
        };
    }

    private static void InspectWorkerConfiguration(ApplicationSettings settings, List<string> lines, List<string> warnings, List<string> errors)
    {
        if (!settings.Worker.WhisperXEnabled)
        {
            warnings.Add("WhisperX worker staat uit. Opnemen werkt wel, maar automatische transcriptie, import, watch folders en retention worden dan niet uitgevoerd.");
            return;
        }

        InspectExternalDependency("Python", settings.Worker.PythonExe, required: true, lines, errors);
        InspectExternalDependency("FFmpeg", settings.Worker.FfmpegPath, required: true, lines, errors);
        lines.Add("Herstelhulp: gebruik install-prereqs.ps1 in de bundle-root voor dependency-check en worker-venv-bootstrap.");
        lines.Add(WorkerProductionProfile.BuildSummary(settings));


        if (settings.Worker.Import.WatchFolderEnabled)
        {
            if (settings.Worker.Import.WatchFolders.Count == 0)
            {
                warnings.Add("Watch folder verwerking staat aan maar er zijn nog geen watch folders geconfigureerd.");
            }
            else
            {
                foreach (var watchFolder in settings.Worker.Import.WatchFolders.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(watchFolder))
                    {
                        continue;
                    }

                    try
                    {
                        if (Directory.Exists(watchFolder))
                        {
                            _ = Directory.EnumerateFileSystemEntries(watchFolder).Take(1).ToList();
                            lines.Add($"Watch folder bereikbaar: {watchFolder}");
                        }
                        else
                        {
                            warnings.Add($"Watch folder bestaat nog niet: {watchFolder}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Watch folder niet leesbaar: {watchFolder} ({ex.Message})");
                    }
                }
            }

            var helperPath = ResolveCurrentExecutableCandidate();
            if (helperPath is not null)
            {
                lines.Add($"Import helper beschikbaar: {helperPath}");
            }
            else
            {
                warnings.Add("Import helper executable kon niet worden bevestigd. Watch folder ingest vereist een beschikbare " + Branding.ExecutableBaseName + ".exe in de programmamap.");
            }
        }
    }

    private static string? InspectCaptureDevice(string deviceId, MMDevice? defaultMic, List<string> lines, List<string> warnings, List<string> errors)
    {
        try
        {
            using var micDevice = DeviceCatalog.GetCaptureDevice(deviceId);
            using var capture = new WasapiCapture(micDevice);
            lines.Add($"Geselecteerde microfoon: {micDevice.FriendlyName}");
            lines.Add($"Microfoonformaat: {capture.WaveFormat}");

            if (defaultMic is not null && !string.Equals(defaultMic.ID, micDevice.ID, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("De gekozen microfoon is niet de huidige standaardmicrofoon. Dat hoeft niet fout te zijn, maar controleer of dit bewust is.");
            }
            return micDevice.FriendlyName;
        }
        catch (Exception ex)
        {
            errors.Add($"Microfoon kan niet worden geopend: {ex.Message}");
            return null;
        }
    }

    private static string? InspectRenderDevice(string deviceId, MMDevice? defaultRender, List<string> lines, List<string> warnings, List<string> errors)
    {
        try
        {
            using var renderDevice = DeviceCatalog.GetRenderDevice(deviceId);
            using var capture = new WasapiLoopbackCapture(renderDevice);
            lines.Add($"Geselecteerde systeemaudio: {renderDevice.FriendlyName}");
            lines.Add($"Loopback-formaat: {capture.WaveFormat}");

            if (defaultRender is not null && !string.Equals(defaultRender.ID, renderDevice.ID, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("De gekozen systeemaudio is niet het huidige standaard afspeelapparaat. Audio van Teams/Zoom/Browser komt vaak op het standaard afspeelapparaat uit; kies dus bewust het juiste renderdevice.");
            }
            return renderDevice.FriendlyName;
        }
        catch (Exception ex)
        {
            errors.Add($"Systeemaudio-device kan niet worden geopend: {ex.Message}");
            return null;
        }
    }

    private static void InspectExternalDependency(string label, string configuredValue, bool required, List<string> lines, List<string> errors)
    {
        if (TryResolveExecutable(configuredValue, out var resolvedPath))
        {
            lines.Add($"{label} bereikbaar: {resolvedPath}");
            return;
        }

        var message = string.IsNullOrWhiteSpace(configuredValue)
            ? $"{label} pad ontbreekt."
            : $"{label} niet gevonden of niet uitvoerbaar: {configuredValue}";

        if (required)
        {
            errors.Add($"{message} Herstel: voer install-prereqs.ps1 uit of stel in Instellingen een geldig pad in.");
        }
    }

    private static bool TryResolveExecutable(string? configuredValue, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return false;
        }

        var trimmed = configuredValue.Trim().Trim('"');
        if (File.Exists(trimmed))
        {
            resolvedPath = Path.GetFullPath(trimmed);
            return true;
        }

        if (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = trimmed,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (!process.Start())
            {
                return false;
            }
            var firstLine = process.StandardOutput.ReadLine();
            process.WaitForExit(3000);
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(firstLine))
            {
                resolvedPath = firstLine.Trim();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string? ResolveCurrentExecutableCandidate()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, Branding.ExecutableBaseName + ".exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
