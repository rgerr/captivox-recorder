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
        var userWarnings = new List<string>();
        var userErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(micDeviceId))
        {
            errors.Add("Geen microfoon geselecteerd.");
            userErrors.Add("Er is geen microfoon gekozen. Kies er een onder Instellingen, Opnemen.");
        }

        if (string.IsNullOrWhiteSpace(systemDeviceId))
        {
            errors.Add("Geen systeemaudio-device geselecteerd.");
            userErrors.Add("Er is geen apparaat gekozen voor het geluid van de anderen. Kies er een onder Instellingen, Opnemen.");
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
                userErrors.Add($"Captivox kan niet schrijven in de map voor opnames ({outputRoot}). Kies een andere map onder Instellingen, Opnemen.");
            }
        }
        else
        {
            errors.Add("Uitvoermap ontbreekt.");
            userErrors.Add("Er is geen map voor de opnames ingesteld. Kies er een onder Instellingen, Opnemen.");
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
            selectedMicFriendlyName = InspectCaptureDevice(micDeviceId, defaultMic, lines, warnings, errors, userWarnings, userErrors);
        }

        if (!string.IsNullOrWhiteSpace(systemDeviceId))
        {
            selectedRenderFriendlyName = InspectRenderDevice(systemDeviceId, defaultRender, lines, warnings, errors, userWarnings, userErrors);
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
            InspectWorkerConfiguration(settings, lines, warnings, errors, userWarnings, userErrors);
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
            UserWarnings = userWarnings,
            UserErrors = userErrors,
        };
    }

    private static void InspectWorkerConfiguration(ApplicationSettings settings, List<string> lines, List<string> warnings, List<string> errors, List<string> userWarnings, List<string> userErrors)
    {
        if (!settings.Worker.WhisperXEnabled)
        {
            warnings.Add("WhisperX worker staat uit. Opnemen werkt wel, maar automatische transcriptie, import, watch folders en retention worden dan niet uitgevoerd.");
            userWarnings.Add("Uitschrijven staat uit. Opnemen werkt wel, maar de opname wordt niet vanzelf in tekst omgezet.");
            return;
        }

        // Zonder transcriptie-omgeving kan opnemen gewoon; alleen uitschrijven wacht tot de installatie is hersteld.
        var pythonOk = InspectExternalDependency("Python", settings.Worker.PythonExe, lines);
        var ffmpegOk = InspectExternalDependency("FFmpeg", settings.Worker.FfmpegPath, lines);
        if (!pythonOk || !ffmpegOk)
        {
            warnings.Add(TranscriptionRuntimeStatus.MissingMessage + " Opnemen werkt wel; uitschrijven pas daarna.");
            userWarnings.Add(TranscriptionRuntimeStatus.MissingMessage + " Opnemen werkt wel; uitschrijven pas daarna.");
        }
        lines.Add(WorkerProductionProfile.BuildSummary(settings));


        if (settings.Worker.Import.WatchFolderEnabled)
        {
            if (settings.Worker.Import.WatchFolders.Count == 0)
            {
                warnings.Add("Watch folder verwerking staat aan maar er zijn nog geen watch folders geconfigureerd.");
                userWarnings.Add("Automatisch importeren uit een map staat aan, maar er is nog geen map gekozen.");
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
                            userWarnings.Add($"De map voor automatisch importeren bestaat niet: {watchFolder}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Watch folder niet leesbaar: {watchFolder} ({ex.Message})");
                        userErrors.Add($"De map voor automatisch importeren is niet te lezen: {watchFolder}");
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
                warnings.Add($"Import helper executable kon niet worden bevestigd. Watch folder ingest vereist een beschikbare {Branding.ExecutableBaseName}.exe in de programmamap.");
                userWarnings.Add("Automatisch importeren uit een map werkt nu niet: Captivox vindt zijn eigen programma niet in de programmamap. Installeer Captivox opnieuw.");
            }
        }
    }

    private static string? InspectCaptureDevice(string deviceId, MMDevice? defaultMic, List<string> lines, List<string> warnings, List<string> errors, List<string> userWarnings, List<string> userErrors)
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
                userWarnings.Add($"Je microfoon ({micDevice.FriendlyName}) is een ander apparaat dan je standaardmicrofoon ({defaultMic.FriendlyName}). Teams en Zoom gebruiken meestal de standaardmicrofoon.");
            }
            return micDevice.FriendlyName;
        }
        catch (Exception ex)
        {
            errors.Add($"Microfoon kan niet worden geopend: {ex.Message}");
            userErrors.Add("De gekozen microfoon kan niet worden geopend. Staat hij aan en is hij aangesloten? Kies anders een andere onder Instellingen, Opnemen.");
            return null;
        }
    }

    private static string? InspectRenderDevice(string deviceId, MMDevice? defaultRender, List<string> lines, List<string> warnings, List<string> errors, List<string> userWarnings, List<string> userErrors)
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
                userWarnings.Add($"Het geluid van de anderen komt van een ander apparaat ({renderDevice.FriendlyName}) dan je standaard afspeelapparaat ({defaultRender.FriendlyName}). Teams en Zoom spelen meestal af via het standaardapparaat.");
            }
            return renderDevice.FriendlyName;
        }
        catch (Exception ex)
        {
            errors.Add($"Systeemaudio-device kan niet worden geopend: {ex.Message}");
            userErrors.Add("Het apparaat voor het geluid van de anderen kan niet worden geopend. Kies een ander onder Instellingen, Opnemen.");
            return null;
        }
    }

    // De technische details gaan in de regels; de melding voor de gebruiker zet de aanroeper erbij.
    private static bool InspectExternalDependency(string label, string configuredValue, List<string> lines)
    {
        if (TryResolveExecutable(configuredValue, out var resolvedPath))
        {
            lines.Add($"{label} bereikbaar: {resolvedPath}");
            return true;
        }

        lines.Add(string.IsNullOrWhiteSpace(configuredValue)
            ? $"{label}: pad ontbreekt."
            : $"{label} niet gevonden: {configuredValue}");
        return false;
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
