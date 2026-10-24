using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Captivox.Recorder.Models;

namespace Captivox.Recorder.Services;

// Start en bewaakt de transcriptieworker (workers\captivox_transcription_worker.ps1) zolang de recorder draait.
// De worker kijkt in de opslagmap naar opnames met een READY-bestand en schrijft ze uit.
internal sealed class WorkerProcessManager : IDisposable
{
    public const string WorkerScriptName = "captivox_transcription_worker.ps1";
    private static readonly TimeSpan RestartCooldown = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private Process? _process;
    private DateTimeOffset _lastStartAttempt = DateTimeOffset.MinValue;

    public string? LastProblem { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return IsAlive(_process);
            }
        }
    }

    public static string? FindWorkerScript(out string? problem)
    {
        problem = null;
        var expectedVersion = AppRuntime.GetWorkerCompatibilityVersionTag();
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "workers", WorkerScriptName),
            AppRuntime.FindProjectFile(Path.Combine("workers", WorkerScriptName)),
        };

        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var version = ReadWorkerVersion(candidate!);
            if (string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            problem = $"De worker naast het programma heeft versie {version ?? "onbekend"}, de recorder verwacht {expectedVersion}. Pak de hele release opnieuw uit.";
        }

        problem ??= $"De map workers met {WorkerScriptName} staat niet naast het programma. Pak de hele release uit, niet alleen het .exe-bestand.";
        return null;
    }

    private static string? ReadWorkerVersion(string workerScriptPath)
    {
        try
        {
            var versionPath = Path.Combine(Path.GetDirectoryName(workerScriptPath) ?? string.Empty, "worker.version.txt");
            return File.Exists(versionPath) ? File.ReadLines(versionPath, Encoding.UTF8).FirstOrDefault()?.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public void EnsureRunning(ApplicationSettings settings)
    {
        try
        {
            if (!settings.Worker.WhisperXEnabled)
            {
                Stop();
                LastProblem = "Transcriptie staat uit in de instellingen.";
                return;
            }

            lock (_sync)
            {
                if (IsAlive(_process))
                {
                    return;
                }

                _process?.Dispose();
                _process = null;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastStartAttempt < RestartCooldown)
            {
                return;
            }

            _lastStartAttempt = now;
            var workerScript = FindWorkerScript(out var problem);
            if (workerScript is null)
            {
                LastProblem = problem;
                AppRuntime.LogMessage("Worker niet gestart: " + problem);
                return;
            }

            StopOrphanedMonitors(workerScript);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{workerScript}\" -SettingsPath \"{SettingsStore.GetSettingsPath()}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(workerScript) ?? AppContext.BaseDirectory,
            };

            // De worker draait in Windows PowerShell 5.1. Een modulepad van PowerShell 7 (recorder gestart vanuit pwsh)
            // verbergt Get-FileHash; laat 5.1 zijn eigen pad opbouwen.
            startInfo.Environment.Remove("PSModulePath");
            startInfo.Environment["CAPTIVOX_APPDATA_ROOT"] = AppRuntime.GetAppDataDirectory();
            startInfo.Environment["CAPTIVOX_EXPECTED_VERSION"] = AppRuntime.GetWorkerCompatibilityVersionTag();
            startInfo.Environment["CAPTIVOX_WORKER_SCRIPT"] = workerScript;
            startInfo.Environment["CAPTIVOX_EXPORT_SRT"] = settings.Worker.ExportSrt ? "true" : "false";
            startInfo.Environment["CAPTIVOX_EXPORT_VTT"] = settings.Worker.ExportVtt ? "true" : "false";
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                // De worker doet de audio-import en het herstel via de recorder zelf.
                startInfo.Environment["CAPTIVOX_TOKEN_HELPER_EXE"] = exePath;
                startInfo.Environment["CAPTIVOX_IMPORT_HELPER_EXE"] = exePath;
                startInfo.Environment["CAPTIVOX_RECORDER_EXE"] = exePath;
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                try
                {
                    AppRuntime.LogMessage($"Worker gestopt met code {process.ExitCode}.");
                }
                catch (InvalidOperationException)
                {
                    AppRuntime.LogMessage("Worker gestopt.");
                }
            };

            if (!process.Start())
            {
                LastProblem = "De worker kon niet worden gestart.";
                return;
            }

            lock (_sync)
            {
                _process = process;
            }

            LastProblem = null;
            AppRuntime.LogMessage($"Worker gestart (PID {process.Id}): {workerScript}");
        }
        catch (Exception ex)
        {
            LastProblem = "De worker kon niet worden gestart: " + ex.Message;
            AppRuntime.LogException("Worker starten mislukt", ex);
        }
    }

    // Stopt alleen de bewaker. Een transcriptie die op dat moment loopt, is een eigen proces en maakt zijn werk af.
    public void Stop()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (IsAlive(process))
            {
                process.Kill(entireProcessTree: false);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Worker stoppen mislukt", ex);
        }
        finally
        {
            process.Dispose();
        }
    }

    // Een bewaker van dit zelfde script die niet van ons is, bleef achter na een harde stop van de recorder. Er draait
    // maar een recorder per gebruiker, dus die mag weg. Processen van andere Captivox-installaties blijven ongemoeid.
    private void StopOrphanedMonitors(string workerScript)
    {
        try
        {
            var ownScript = Path.GetFullPath(workerScript);
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe'");
            using var results = searcher.Get();
            foreach (ManagementObject info in results)
            {
                using (info)
                {
                    var pid = Convert.ToInt32(info["ProcessId"] ?? 0);
                    var commandLine = Convert.ToString(info["CommandLine"] ?? string.Empty) ?? string.Empty;
                    if (pid <= 0 || commandLine.IndexOf(ownScript, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (commandLine.IndexOf("-ProcessSessionDir", StringComparison.OrdinalIgnoreCase) >= 0
                        || commandLine.IndexOf("-SelfTest", StringComparison.OrdinalIgnoreCase) >= 0
                        || commandLine.IndexOf("-RunOnce", StringComparison.OrdinalIgnoreCase) >= 0
                        || !Regex.IsMatch(commandLine, @"(?i)-File\s+""?[^""]*" + Regex.Escape(WorkerScriptName)))
                    {
                        continue;
                    }

                    try
                    {
                        using var orphan = Process.GetProcessById(pid);
                        orphan.Kill(entireProcessTree: false);
                        orphan.WaitForExit(5000);
                        AppRuntime.LogMessage($"Achtergebleven worker gestopt (PID {pid}).");
                    }
                    catch (Exception ex)
                    {
                        AppRuntime.LogException($"Achtergebleven worker kon niet worden gestopt (PID {pid})", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Zoeken naar achtergebleven workers mislukt", ex);
        }
    }

    public static WorkerRuntimeSnapshot? ReadRuntimeState()
    {
        try
        {
            var path = AppRuntime.GetWorkerRuntimeStatePath();
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<WorkerRuntimeSnapshot>(File.ReadAllText(path, Encoding.UTF8), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAlive(Process? process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose() => Stop();
}
