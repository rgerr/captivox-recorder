using System.Diagnostics;
using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class ImportSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SessionManifest ImportAudio(string sourcePath, ApplicationSettings settings, bool fromWatchFolder = false, bool consumeSource = false)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Geen bronbestand opgegeven voor import.");
        }

        var sourceFile = new FileInfo(sourcePath);
        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("Bronbestand voor import niet gevonden.", sourcePath);
        }

        var outputRoot = string.IsNullOrWhiteSpace(settings.Ui.OutputRoot) ? Branding.DefaultRecordingsRoot : settings.Ui.OutputRoot.Trim();
        Directory.CreateDirectory(outputRoot);

        var ffmpegPath = string.IsNullOrWhiteSpace(settings.Worker.FfmpegPath) ? "ffmpeg" : settings.Worker.FfmpegPath.Trim();
        var importedAt = DateTimeOffset.Now;
        var metadataInspection = ImportedRecordingMetadataService.Inspect(sourceFile.FullName, ffmpegPath, importedAt, fromWatchFolder);
        var hasTrustedRecordedAt = metadataInspection.RecommendedRecordedAt.HasValue
            && string.Equals(metadataInspection.MetadataConfidence, "high", StringComparison.OrdinalIgnoreCase);
        var sessionClock = hasTrustedRecordedAt && metadataInspection.RecommendedRecordedAt is DateTimeOffset trustedRecordedAt
            ? trustedRecordedAt
            : importedAt;
        var meetingId = $"import-{sessionClock:yyyy-MM-dd-HHmmss}-{Guid.NewGuid():N}"[..44];
        var sessionDir = Path.Combine(outputRoot, meetingId);
        Directory.CreateDirectory(sessionDir);

        var stagedSource = Path.Combine(sessionDir, "source" + sourceFile.Extension.ToLowerInvariant());
        File.Copy(sourceFile.FullName, stagedSource, overwrite: false);

        var manifest = new SessionManifest
        {
            MeetingId = meetingId,
            CreatedAt = sessionClock,
            FinishedAt = null,
            OutputDirectory = sessionDir,
            OutputFile = Path.Combine(sessionDir, "audio_mix.wav"),
            Status = "importing",
            AppVersion = AppRuntime.GetProductVersionTag(),
            PhaseTimings = new SessionPhaseTimings(),
            TranscriptionStatus = "preparing_import",
            TranscriptionEngine = "whisperx",
            TranscriptionInputPath = Path.Combine(sessionDir, "audio_mix.wav"),
            TranscriptOutputFormat = settings.Worker.TranscriptOutputFormat,
            SourceType = "import",
            SourceFileName = sourceFile.Name,
            SourceOriginalPath = sourceFile.FullName,
            ImportMode = fromWatchFolder ? "watch_folder" : "manual",
            ImportSourceProfile = metadataInspection.ImportSourceProfile,
            ImportSourceProfileReason = metadataInspection.ImportSourceProfileReason,
            EmbeddedRecordedAt = metadataInspection.EmbeddedRecordedAt,
            OneDriveObservedAt = metadataInspection.OneDriveObservedAt,
            RecordedAt = metadataInspection.RecommendedRecordedAt,
            RecordedAtSource = metadataInspection.RecommendedRecordedAtSource,
            RecordedAtConfidence = metadataInspection.MetadataConfidence,
            RecordedAtConfidenceReason = metadataInspection.RecommendedRecordedAtConfidenceReason,
            ManualRecordedAtConfirmed = false,
            FileCreatedAt = metadataInspection.FileCreatedAt,
            FileModifiedAt = metadataInspection.FileModifiedAt,
            ImportedAt = importedAt,
            DurationSeconds = metadataInspection.DurationSeconds,
            AgendaMatchStatus = string.Equals(metadataInspection.MetadataConfidence, "high", StringComparison.OrdinalIgnoreCase)
                ? "match_candidate"
                : "manual_review_required",
            AgendaMatchConfidence = metadataInspection.MetadataConfidence,
            AgendaMatchBasis = string.Equals(metadataInspection.MetadataConfidence, "high", StringComparison.OrdinalIgnoreCase) ? "trusted_recorded_at" : "manual_review_required",
            AgendaMatchRecordedAtUsed = metadataInspection.RecommendedAgendaMatchTime,
            AgendaMatchReason = metadataInspection.ConfidenceReason,
            RecoveryStatus = null,
            RecoveryNotes = null,
            LastHeartbeatAt = DateTimeOffset.Now,
            ErrorCode = null,
            ErrorMessage = null,
            CaptureDiagnostics = new CaptureDiagnostics
            {
                StopTimeoutSeconds = 0,
                MicInputFormat = "import",
                SystemInputFormat = "import"
            }
        };

        var sessionLogPath = Path.Combine(sessionDir, "session.log");
        WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Import gestart vanaf bronbestand: {sourceFile.FullName}");
        WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Metadata-inspectie: profile={metadataInspection.ImportSourceProfile}; recorded_at={metadataInspection.RecommendedRecordedAt:O}; source={metadataInspection.RecommendedRecordedAtSource}; confidence={metadataInspection.MetadataConfidence}; imported_at={importedAt:O}; onedrive_observed_at={metadataInspection.OneDriveObservedAt:O}; duration_seconds={metadataInspection.DurationSeconds}");
        WriteImportMetadataSnapshot(sessionDir, metadataInspection);
        RecordRecorderPhase(manifest, "import_started_at", manifest.CreatedAt);
        RecordRecorderPhase(manifest, "source_staged_at", DateTimeOffset.Now);
        WriteManifest(manifest);

        try
        {
            RunFfmpeg(ffmpegPath, new[] { "-y", "-i", stagedSource, "-vn", "-ac", "1", "-ar", "16000", manifest.OutputFile }, sessionDir, sessionLogPath, "Basismix voor import maken");
            EnsureFileCreated(manifest.OutputFile, "Basismix voor import maken");

            if (settings.Worker.Enhancement.Enabled)
            {
                var onlyImports = settings.Worker.Enhancement.ImportedAudioOnly;
                if (!onlyImports || manifest.SourceType == "import")
                {
                    var enhancedPath = Path.Combine(sessionDir, "audio_enhanced.wav");
                    var filter = ResolveEnhancementFilter(settings.Worker.Enhancement.Profile);
                    RunFfmpeg(ffmpegPath, new[] { "-y", "-i", manifest.OutputFile, "-af", filter, enhancedPath }, sessionDir, sessionLogPath, "Audio enhancement uitvoeren");
                    if (File.Exists(enhancedPath))
                    {
                        EnsureFileCreated(enhancedPath, "Audio enhancement uitvoeren");
                        manifest.AudioEnhancedPath = enhancedPath;
                        manifest.TranscriptionInputPath = enhancedPath;
                    }
                }
            }

            EnsureFileCreated(manifest.TranscriptionInputPath ?? manifest.OutputFile, "Transcription input valideren");
            RecordRecorderPhase(manifest, "transcription_input_ready_at", DateTimeOffset.Now);

            WriteReadyMarker(sessionDir);
            var readyWrittenAt = DateTimeOffset.Now;
            RecordRecorderPhase(manifest, "ready_written_at", readyWrittenAt);
            WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] READY marker geschreven voor importsessie.");

            manifest.Status = "completed";
            manifest.TranscriptionStatus = "ready_for_transcription";
            manifest.FinishedAt = DateTimeOffset.Now;
            manifest.LastHeartbeatAt = DateTimeOffset.Now;
            manifest.ErrorCode = null;
            manifest.ErrorMessage = null;
            WriteManifest(manifest);

            if (fromWatchFolder && consumeSource)
            {
                try
                {
                    File.Delete(sourceFile.FullName);
                    WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Bronbestand uit watch folder verwijderd na succesvolle import.");
                }
                catch (Exception ex)
                {
                    WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Bronbestand kon niet worden verwijderd: {ex.Message}");
                }
            }

            return manifest;
        }
        catch (Exception ex)
        {
            manifest.Status = "failed";
            manifest.TranscriptionStatus = "import_failed";
            manifest.ErrorCode = "import_failed";
            manifest.ErrorMessage = ex.Message;
            manifest.FinishedAt = DateTimeOffset.Now;
            manifest.LastHeartbeatAt = DateTimeOffset.Now;
            WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Import mislukt: {ex.Message}");
            try
            {
                WriteManifest(manifest);
            }
            catch
            {
            }

            throw;
        }
    }


    private static void RecordRecorderPhase(SessionManifest manifest, string key, DateTimeOffset value)
    {
        manifest.PhaseTimings ??= new SessionPhaseTimings();
        manifest.PhaseTimings.Recorder[key] = value.ToString("o");
    }

    private static void EnsureFileCreated(string path, string purpose)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"{purpose} leverde geen uitvoerbestand op.");
        }

        var info = new FileInfo(path);
        if (info.Length <= 0)
        {
            throw new InvalidOperationException($"{purpose} leverde een leeg uitvoerbestand op: {path}");
        }
    }

    private static void WriteReadyMarker(string sessionDir)
    {
        var readyPath = Path.Combine(sessionDir, "READY");
        var tempPath = readyPath + ".tmp";
        AtomicFileWriter.WriteAllText(tempPath, string.Empty, System.Text.Encoding.UTF8);
        File.Move(tempPath, readyPath, overwrite: true);
        if (!File.Exists(readyPath))
        {
            throw new InvalidOperationException("READY marker kon niet worden geschreven voor importsessie.");
        }
    }

    private static string ResolveEnhancementFilter(string? profile)
    {
        return (profile ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "speech-focus" => "highpass=f=90,lowpass=f=7200,afftdn=nf=-30",
            _ => "highpass=f=80,afftdn=nf=-25"
        };
    }

    private static void RunFfmpeg(string ffmpegPath, IReadOnlyList<string> arguments, string workingDirectory, string sessionLogPath, string purpose)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"FFmpeg kon niet worden gestart voor: {purpose}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timeout = TimeSpan.FromHours(4);
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            var timeoutMessage = $"FFmpeg duurde langer dan {timeout.TotalHours:0} uur tijdens {purpose} en is afgebroken.";
            WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {timeoutMessage}");
            throw new TimeoutException(timeoutMessage);
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] FFmpeg fout tijdens {purpose}: {stderr}");
            throw new InvalidOperationException($"FFmpeg fout tijdens {purpose}: {stderr}");
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] FFmpeg stdout ({purpose}): {stdout}");
        }
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            WriteSessionLog(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] FFmpeg stderr ({purpose}): {stderr}");
        }
    }

    private static void WriteSessionLog(string logPath, string line)
        => File.AppendAllText(logPath, line + Environment.NewLine);

    private static void WriteImportMetadataSnapshot(string sessionDir, ImportedRecordingMetadataInspection inspection)
    {
        var path = Path.Combine(sessionDir, "import_metadata.json");
        AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(inspection, JsonOptions), System.Text.Encoding.UTF8);
    }

    private static void WriteManifest(SessionManifest manifest)
    {
        var path = Path.Combine(manifest.OutputDirectory, "manifest.json");
        AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), System.Text.Encoding.UTF8);
    }
}
