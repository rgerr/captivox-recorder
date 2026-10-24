using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal sealed class DiagnosticsExportResult
{
    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;

    public DiagnosticsExportResult(IEnumerable<string> warnings)
    {
        Warnings = warnings.ToList().AsReadOnly();
    }
}

internal static class DiagnosticsPackageExporter
{
    private static readonly string[] SensitiveIdentifierKeys = { "machine_name", "user_name", "hostname", "worker_host" };
    private static readonly string[] SensitivePathKeys =
    {
        "command", "working_directory", "source_original_path", "output_directory", "output_file", "transcription_input_path", "python_exe", "helper_script", "transcript_text_path", "transcript_json_path", "audio_enhanced_path", "transcript_srt_path", "transcript_vtt_path",
        "active_session_dir", "session_roots", "known_session_roots", "watch_folders", "output_root", "export_root"
    };

    public static DiagnosticsExportResult Export(string destinationZipPath, SessionManifest? manifest, ApplicationSettings settings)
    {
        var tempRoot = Path.Combine(AppRuntime.GetTempDirectory(), $"diagnostics_{DateTimeOffset.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(tempRoot);
        var warnings = new List<string>();

        try
        {
            if (manifest is not null && Directory.Exists(manifest.OutputDirectory))
            {
                var sessionDump = Path.Combine(tempRoot, "session");
                Directory.CreateDirectory(sessionDump);

                foreach (var fileName in new[]
                {
                    "manifest.json",
                    "transcript.txt",
                    "transcript.json",
                    "session.log",
                    "diagnostics.log",
                    "transcription.log",
                    "phase_timings.json",
                    "helper_progress.json",
                    "helper_invocation.json",
                    "hardware_profile.json",
                    ".transcription.claim",
                    "worker.stdout.log",
                    "worker.stderr.log",
                    "TRANSCRIBED",
                    "TRANSCRIPTION_FAILED",
                    "derived_session_insights.json",
                    "confirmed_session_insights.json",
                    "session_feedback.json",
                    "feedback_profile.json",
                    "library_metadata.json",
                    "library_chat.last.json"
                })
                {
                    CopyIfExistsRedacted(Path.Combine(manifest.OutputDirectory, fileName), Path.Combine(sessionDump, fileName), warnings);
                }

                foreach (var subDirectoryName in new[] { "diagnostics", "exports", "ai" })
                {
                    var sourceSubDirectory = Path.Combine(manifest.OutputDirectory, subDirectoryName);
                    if (!Directory.Exists(sourceSubDirectory))
                    {
                        continue;
                    }

                    var targetSubDirectory = Path.Combine(sessionDump, subDirectoryName);
                    Directory.CreateDirectory(targetSubDirectory);
                    foreach (var sourceFile in Directory.EnumerateFiles(sourceSubDirectory, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(sourceSubDirectory, sourceFile);
                        var targetFile = Path.Combine(targetSubDirectory, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? targetSubDirectory);
                        CopyIfExistsRedacted(sourceFile, targetFile, warnings);
                    }
                }

            }

            var selectedLogDate = GetPreferredLogDate(manifest);

            var appLogDir = AppRuntime.GetLogDirectory();
            if (Directory.Exists(appLogDir))
            {
                var logDump = Path.Combine(tempRoot, "app_logs");
                Directory.CreateDirectory(logDump);
                CopyIfExistsRedacted(
                    Path.Combine(appLogDir, $"app_{selectedLogDate:yyyyMMdd}.log"),
                    Path.Combine(logDump, $"app_{selectedLogDate:yyyyMMdd}.log"), warnings);
            }

            var selfTestDir = AppRuntime.GetSelfTestDirectory();
            if (Directory.Exists(selfTestDir))
            {
                var selfTestDump = Path.Combine(tempRoot, "self_test");
                Directory.CreateDirectory(selfTestDump);
                foreach (var fileName in new[] { "environment.txt", "stdout.txt", "stderr.txt", "combined.txt", "summary.json" })
                {
                    CopyIfExistsRedacted(Path.Combine(selfTestDir, fileName), Path.Combine(selfTestDump, fileName), warnings);
                }
            }

            var workerRuntimeStatePath = AppRuntime.GetWorkerRuntimeStatePath();
            if (File.Exists(workerRuntimeStatePath))
            {
                var runtimeDump = Path.Combine(tempRoot, "worker_runtime");
                Directory.CreateDirectory(runtimeDump);
                CopyIfExistsRedacted(workerRuntimeStatePath, Path.Combine(runtimeDump, "worker_status.json"), warnings);
            }

            var retentionRuntimeStatePath = AppRuntime.GetRetentionRuntimeStatePath();
            if (File.Exists(retentionRuntimeStatePath))
            {
                var runtimeDump = Path.Combine(tempRoot, "retention_runtime");
                Directory.CreateDirectory(runtimeDump);
                CopyIfExistsRedacted(retentionRuntimeStatePath, Path.Combine(runtimeDump, "retention_status.json"), warnings);
            }

            // De worker schrijft zijn logboek in de gegevensmap van deze smaak (logs\worker).
            var workerLogRoot = Path.Combine(AppRuntime.GetLogDirectory(), "worker");
            if (Directory.Exists(workerLogRoot))
            {
                var workerDump = Path.Combine(tempRoot, "worker_logs");
                Directory.CreateDirectory(workerDump);
                CopyIfExistsRedacted(
                    Path.Combine(workerLogRoot, $"ready_worker_{selectedLogDate:yyyyMMdd}.log"),
                    Path.Combine(workerDump, $"ready_worker_{selectedLogDate:yyyyMMdd}.log"), warnings);
            }

            var appStateDump = Path.Combine(tempRoot, "app_state");

            try
            {
                var preflight = PreflightCheckService.Run(
                    settings.Ui.OutputRoot,
                    settings.Ui.PreferredMicDeviceId,
                    settings.Ui.PreferredSystemDeviceId,
                    settings);
                File.WriteAllText(Path.Combine(tempRoot, "preflight_summary.txt"), RedactSecrets(preflight.Details), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(tempRoot, "preflight_summary.txt"), RedactSecrets($"Preflightoverzicht kon niet worden gemaakt: {ex.Message}"), Encoding.UTF8);
            }

            var hardwareSettings = settings.Hardware ?? new HardwareProfileSettings();
            var redactedSettings = new ApplicationSettings
            {
                Ui = new UiSettings
                {
                    InterfaceLanguage = settings.Ui.InterfaceLanguage,
                    OutputRoot = settings.Ui.OutputRoot,
                    PreferredMicDeviceId = settings.Ui.PreferredMicDeviceId,
                    PreferredSystemDeviceId = settings.Ui.PreferredSystemDeviceId,
                    MicGain = settings.Ui.MicGain,
                    SystemGain = settings.Ui.SystemGain,
                    WriteStems = false,
                    CreateReadyMarker = settings.Ui.CreateReadyMarker,
                    MinimizeToTray = settings.Ui.MinimizeToTray,
                    DiagnosticsMode = settings.Ui.DiagnosticsMode,
                },
                Worker = new WorkerSettings
                {
                    WhisperXEnabled = settings.Worker.WhisperXEnabled,
                    DiarizationEnabled = settings.Worker.DiarizationEnabled,
                    RuntimeMode = settings.Worker.RuntimeMode,
                    PythonExe = settings.Worker.PythonExe,
                    WhisperXModel = settings.Worker.WhisperXModel,
                    CpuWhisperXModel = settings.Worker.CpuWhisperXModel,
                    TranscriptionProfile = settings.Worker.TranscriptionProfile,
                    WhisperCppExe = settings.Worker.WhisperCppExe,
                    WhisperCppModelPath = settings.Worker.WhisperCppModelPath,
                    ComputeType = settings.Worker.ComputeType,
                    Language = settings.Worker.Language,
                    BatchSize = settings.Worker.BatchSize,
                    MinSpeakers = settings.Worker.MinSpeakers,
                    MaxSpeakers = settings.Worker.MaxSpeakers,
                    TranscriptOutputFormat = settings.Worker.TranscriptOutputFormat,
                    DiarizationMode = settings.Worker.DiarizationMode,
                    DiarizationAcceleration = settings.Worker.DiarizationAcceleration,
                    StableDiarizationModel = settings.Worker.StableDiarizationModel,
                    ExperimentalDiarizationModel = settings.Worker.ExperimentalDiarizationModel,
                    FfmpegPath = settings.Worker.FfmpegPath,
                    PollIntervalSec = settings.Worker.PollIntervalSec,
                    MinReadyAgeSec = settings.Worker.MinReadyAgeSec,
                    RetryFailedSessions = settings.Worker.RetryFailedSessions,
                    RetryStaleStartedAfterMinutes = settings.Worker.RetryStaleStartedAfterMinutes,
                    MaxRetryAttempts = settings.Worker.MaxRetryAttempts,
                    RetryBackoffMinutes = settings.Worker.RetryBackoffMinutes,
                    CreateMarkers = settings.Worker.CreateMarkers,
                    PreserveWorkerOutput = settings.Worker.PreserveWorkerOutput,
                    Retention = new RetentionSettings
                    {
                        Enabled = settings.Worker.Retention.Enabled,
                        AudioRetentionDays = settings.Worker.Retention.AudioRetentionDays,
                        TranscriptRetentionDays = settings.Worker.Retention.TranscriptRetentionDays,
                        SessionRecordRetentionDays = settings.Worker.Retention.SessionRecordRetentionDays,
                    },
                    KnownSessionRoots = settings.Worker.KnownSessionRoots?.ToList() ?? new List<string>(),
                    Import = new ImportSettings
                    {
                        WatchFolderEnabled = settings.Worker.Import.WatchFolderEnabled,
                        RequestedWatchFolderEnabled = settings.Worker.Import.RequestedWatchFolderEnabled,
                        WatchFolders = settings.Worker.Import.WatchFolders?.ToList() ?? new List<string>(),
                        ConsumeWatchFiles = settings.Worker.Import.ConsumeWatchFiles,
                        MinimumStableAgeSeconds = settings.Worker.Import.MinimumStableAgeSeconds,
                        SupportedExtensions = settings.Worker.Import.SupportedExtensions?.ToList() ?? new List<string>()
                    },
                    Enhancement = new AudioEnhancementSettings
                    {
                        Enabled = settings.Worker.Enhancement.Enabled,
                        ImportedAudioOnly = settings.Worker.Enhancement.ImportedAudioOnly,
                        Profile = settings.Worker.Enhancement.Profile
                    }
                },
                Hardware = new HardwareProfileSettings
                {
                    StartupDetectionCompleted = hardwareSettings.StartupDetectionCompleted,
                    LastDetectedAtUtc = hardwareSettings.LastDetectedAtUtc,
                    LastGpuVendor = hardwareSettings.LastGpuVendor,
                    LastGpuName = hardwareSettings.LastGpuName,
                    LastVramGb = hardwareSettings.LastVramGb,
                    LastVramConfidence = hardwareSettings.LastVramConfidence,
                    LastDetectionMethod = hardwareSettings.LastDetectionMethod,
                    LastRecommendedTranscriptionProfile = hardwareSettings.LastRecommendedTranscriptionProfile,
                    LastRecommendedModelTag = hardwareSettings.LastRecommendedModelTag,
                    LastAdviceClass = hardwareSettings.LastAdviceClass,
                    LastGpuRuntimeAvailable = hardwareSettings.LastGpuRuntimeAvailable,
                    LastGpuRuntimeStatus = hardwareSettings.LastGpuRuntimeStatus,
                    LastFallbackReason = hardwareSettings.LastFallbackReason
                }
            };
            SettingsStore.NormalizeKnownSessionRoots(redactedSettings);
            File.WriteAllText(Path.Combine(tempRoot, "application_settings.redacted.json"), RedactSecrets(SettingsStore.CreateRedactedSettingsJson(redactedSettings)), Encoding.UTF8);
            File.WriteAllText(Path.Combine(tempRoot, "worker_profile_summary.txt"), RedactSecrets(WorkerProductionProfile.BuildSummary(redactedSettings)), Encoding.UTF8);
            var editionSnapshot = new EditionService().GetEditionState(attemptRevalidation: false);
            File.WriteAllText(Path.Combine(tempRoot, "product_line_status.redacted.json"), RedactSecrets(LicenseCacheStore.CreateRedactedSnapshotJson(editionSnapshot)), Encoding.UTF8);
            File.WriteAllText(Path.Combine(tempRoot, "README_diagnostics.txt"), "Dit diagnosepakket van Captivox Recorder bevat standaard alleen geredigeerde sessie- en supportartefacten, de bijbehorende daglogs, de laatst bewaarde worker-self-testartefacten, de actuele worker- en retention-snapshots, een worker-profieloverzicht, een geredigeerde productlijnsnapshot en een preflightoverzicht. Audio wordt niet standaard meegeleverd. Gevoelige waarden zoals e-mailadressen, tokens, bearer-achtige sleutels en expliciete user-profielpaden horen geredigeerd te zijn. Deel dit pakket alleen met beheerders of ontwikkelaars die deze gegevens mogen inzien.", Encoding.UTF8); // geen vertaling

            if (warnings.Count > 0)
            {
                File.WriteAllLines(Path.Combine(tempRoot, "export_warnings.txt"), warnings, Encoding.UTF8);
            }

            if (File.Exists(destinationZipPath))
            {
                File.Delete(destinationZipPath);
            }

            ZipFile.CreateFromDirectory(tempRoot, destinationZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return new DiagnosticsExportResult(warnings);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }


    private static DateTime GetPreferredLogDate(SessionManifest? manifest)
    {
        if (TryReadPhaseDate(manifest?.PhaseTimings?.Transcription, "started_at", out var phaseStartedAt))
        {
            return phaseStartedAt.Date;
        }

        if (manifest?.TranscriptionStartedAt is not null)
        {
            return manifest.TranscriptionStartedAt.Value.Date;
        }

        if (manifest?.CreatedAt is not null)
        {
            return manifest.CreatedAt.Date;
        }

        return DateTimeOffset.Now.Date;
    }

    private static bool TryReadPhaseDate(Dictionary<string, object?>? phase, string key, out DateTimeOffset value)
    {
        value = default;
        if (phase is null || !phase.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return DateTimeOffset.TryParse(raw.ToString(), out value);
    }

    private static void CopyIfExists(string sourcePath, string destinationPath, List<string> warnings)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            using var sourceStream = OpenSharedReadStream(sourcePath);
            using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            sourceStream.CopyTo(destinationStream);
        }
        catch (Exception ex)
        {
            warnings.Add($"{Path.GetFileName(sourcePath)} kon niet worden meegenomen: {ex.Message}");
        }
    }

    private static void CopyIfExistsRedacted(string sourcePath, string destinationPath, List<string> warnings)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            var extension = Path.GetExtension(sourcePath);
            var redactAsText = string.IsNullOrWhiteSpace(extension)
                || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

            if (!redactAsText)
            {
                using var sourceStream = OpenSharedReadStream(sourcePath);
                using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                sourceStream.CopyTo(destinationStream);
                return;
            }

            var content = ReadAllTextShared(sourcePath);
            content = RedactSecrets(content);
            File.WriteAllText(destinationPath, content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            warnings.Add($"{Path.GetFileName(sourcePath)} kon niet worden meegenomen of geredigeerd: {ex.Message}");
        }
    }

    private static FileStream OpenSharedReadStream(string sourcePath)
        => new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static string ReadAllTextShared(string sourcePath)
    {
        using var stream = OpenSharedReadStream(sourcePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string RedactSecrets(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        content = Regex.Replace(content, @"hf_[A-Za-z0-9]{20,}", "[REDACTED_HF_TOKEN]", RegexOptions.CultureInvariant);
        content = Regex.Replace(content, @"\bLIC-[A-Za-z0-9-]{8,}\b", "[REDACTED_LICENSE_ID]", RegexOptions.CultureInvariant);
        content = Regex.Replace(content, @"(?<!\*)(?<![A-Za-z0-9._%+\-])([A-Za-z0-9._%+\-]+)@([A-Za-z0-9.\-]+\.[A-Za-z]{2,})(?!\*)", "[REDACTED_EMAIL]", RegexOptions.CultureInvariant);
        content = Regex.Replace(content, @"(?im)(bearer\s+)[A-Za-z0-9._\-]{12,}", "$1[REDACTED]", RegexOptions.CultureInvariant);
        content = Regex.Replace(content, @"(?im)(api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|huggingface[_-]?token|token)\s*[:=]\s*(['""])?[^'""\r\n\s]+(['""])?", "$1=[REDACTED]", RegexOptions.CultureInvariant);
        content = Regex.Replace(content, @"(?i)(?:[A-Z]:\\{1,2}Users\\{1,2}[^\\/\r\n""']+)(?:\\{1,2}[^""\r\n]*)?", "[REDACTED_LOCAL_PATH]", RegexOptions.CultureInvariant);
        content = Regex.Replace(content, @"(?i)(?:[A-Z]:\\{1,2}[^""\r\n]+)", "[REDACTED_LOCAL_PATH]", RegexOptions.CultureInvariant);
        content = RedactQuotedKeyValues(content, SensitiveIdentifierKeys, "[REDACTED_IDENTIFIER]");
        content = RedactUnquotedKeyValues(content, SensitiveIdentifierKeys, "[REDACTED_IDENTIFIER]");
        content = RedactQuotedKeyValues(content, SensitivePathKeys, "[REDACTED_PATH]");
        content = RedactUnquotedKeyValues(content, SensitivePathKeys, "[REDACTED_PATH]");
        content = Regex.Replace(content, @"(?im)(""?(?:licenseCode|email)""?\s*[:=]\s*"")([^""\r\n]+)("")", "$1[REDACTED]$3", RegexOptions.CultureInvariant);
        content = Regex.Replace(content, @"(?im)((?:Licentiecode|E-mailadres)\s*[:=]\s*)(\S+)", "$1[REDACTED]", RegexOptions.CultureInvariant);
        return content;
    }

    private static string RedactQuotedKeyValues(string content, IEnumerable<string> keys, string replacement)
    {
        foreach (var key in keys)
        {
            var pattern = "(?im)(\"" + Regex.Escape(key) + "\"\\s*:\\s*\")([^\"]*)(\")";
            content = Regex.Replace(content, pattern, $"$1{replacement}$3", RegexOptions.CultureInvariant);
        }

        return content;
    }

    private static string RedactUnquotedKeyValues(string content, IEnumerable<string> keys, string replacement)
    {
        foreach (var key in keys)
        {
            var pattern = $@"(?im)(\b{Regex.Escape(key)}\b\s*[:=]\s*)([^\r\n]+)";
            content = Regex.Replace(content, pattern, $"$1{replacement}", RegexOptions.CultureInvariant);
        }

        return content;
    }
}
