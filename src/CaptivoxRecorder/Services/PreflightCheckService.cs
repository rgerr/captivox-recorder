using System.Diagnostics;
using Captivox.Recorder.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Captivox.Recorder.Services;

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
            errors.Add("Geen apparaat gekozen voor het geluid van de pc.");
        }

        if (!string.IsNullOrWhiteSpace(outputRoot))
        {
            try
            {
                Directory.CreateDirectory(outputRoot);
                using var fs = new FileStream(Path.Combine(outputRoot, $".captivox-preflight-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
                lines.Add($"Opslagmap schrijfbaar: {outputRoot}");
            }
            catch (Exception ex)
            {
                errors.Add($"De opslagmap is niet bruikbaar: {ex.Message}");
            }
        }
        else
        {
            errors.Add("Er is geen opslagmap ingesteld.");
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
            lines.Add("Controle geslaagd: apparaten en opslagmap zijn bereikbaar.");
        }

        return new PreflightReport
        {
            Success = errors.Count == 0,
            HasWarnings = warnings.Count > 0,
            Lines = lines,
        };
    }

    // Een ontbrekende transcriptie-omgeving blokkeert het opnemen niet: de opname wacht dan tot hij er is.
    private static void InspectWorkerConfiguration(ApplicationSettings settings, List<string> lines, List<string> warnings, List<string> errors)
    {
        var (ok, text) = CheckTranscriptionRuntime(settings);
        if (ok)
        {
            lines.Add(text);
        }
        else
        {
            warnings.Add(text);
        }

        lines.Add(WorkerProductionProfile.BuildSummary(settings));
    }

    public static (bool Ok, string Text) CheckTranscriptionRuntime(ApplicationSettings settings)
    {
        if (!settings.Worker.WhisperXEnabled)
        {
            return (false, "Uitschrijven staat uit in de instellingen. Opnemen werkt wel.");
        }

        var missing = new List<string>();
        if (!TryResolveExecutable(settings.Worker.PythonExe, out _))
        {
            missing.Add("Python-omgeving");
        }

        if (!TryResolveExecutable(settings.Worker.FfmpegPath, out _) && !TryResolveExecutable("ffmpeg", out _))
        {
            missing.Add("FFmpeg");
        }

        if (missing.Count > 0)
        {
            return (false, $"Uitschrijven is nog niet geinstalleerd ({string.Join(" en ", missing)} ontbreekt). Voer install-prereqs.ps1 uit in de programmamap; tot die tijd blijven opnames wachten.");
        }

        if (settings.Worker.DiarizationEnabled && !SettingsStore.HasToken())
        {
            return (true, "Uitschrijven is klaar voor gebruik. Sprekerherkenning wacht nog op een Hugging Face-token (Instellingen).");
        }

        return (true, "Uitschrijven is klaar voor gebruik.");
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
                warnings.Add("Het gekozen pc-geluid is niet het standaard afspeelapparaat van Windows. Teams, Zoom en de browser spelen meestal af via het standaardapparaat; controleer of je de anderen hoort in de opname.");
            }
            return renderDevice.FriendlyName;
        }
        catch (Exception ex)
        {
            errors.Add($"Het apparaat voor het pc-geluid kan niet worden geopend: {ex.Message}");
            return null;
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

}
