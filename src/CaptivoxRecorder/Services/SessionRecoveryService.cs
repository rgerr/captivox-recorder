using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class SessionRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int RecoverInterruptedSessions(ApplicationSettings settings, TimeSpan staleAfter)
    {
        var recovered = 0;
        foreach (var root in SessionInventoryService.GetSessionRoots(settings))
        {
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(root); }
            catch { continue; }

            foreach (var dir in dirs)
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                SessionManifest? manifest;
                try
                {
                    manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath), JsonOptions);
                }
                catch
                {
                    continue;
                }

                if (manifest is null)
                {
                    continue;
                }

                if (!string.Equals(manifest.Status, "recording", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var heartbeat = manifest.LastHeartbeatAt ?? manifest.CreatedAt;
                if (DateTimeOffset.Now - heartbeat < staleAfter)
                {
                    continue;
                }

                manifest.Status = "interrupted";
                manifest.ErrorCode = "SESSION_INTERRUPTED";
                manifest.ErrorMessage = "Sessie is onderbroken aangetroffen en gemarkeerd voor herstelinspectie.";
                manifest.FinishedAt = DateTimeOffset.Now;
                manifest.RecoveryStatus = "interrupted";
                manifest.RecoveryNotes = "Herkend bij herstart; live capture kan niet naadloos worden hervat. Reeds opgeslagen artefacten blijven behouden.";
                manifest.LastHeartbeatAt = DateTimeOffset.Now;

                AtomicFileWriter.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), System.Text.Encoding.UTF8);
                var sessionLogPath = Path.Combine(dir, "session.log");
                File.AppendAllText(sessionLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Sessierecovery: opname als interrupted gemarkeerd na herstart.{Environment.NewLine}"); // geen vertaling
                recovered++;
            }
        }

        return recovered;
    }

    public static TranscriptionQueueRecoveryResult RecoverInterruptedTranscriptions(ApplicationSettings settings, TimeSpan staleAfter)
    {
        var result = new TranscriptionQueueRecoveryResult();
        foreach (var root in SessionInventoryService.GetSessionRoots(settings))
        {
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(root); }
            catch { continue; }

            foreach (var dir in dirs)
            {
                result.ScannedSessions++;
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                SessionManifest? manifest;
                try
                {
                    manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions);
                }
                catch
                {
                    continue;
                }

                if (manifest is null || !string.Equals(manifest.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var expected = GetExpectedOutputs(dir, manifest.TranscriptOutputFormat, settings.Worker.TranscriptOutputFormat);
                var outputsExist = ExpectedOutputsExist(expected);
                var transcriptionStatus = Normalize(manifest.TranscriptionStatus);

                if (transcriptionStatus == "completed")
                {
                    if (outputsExist)
                    {
                        result.AlreadyCompleted++;
                    }
                    continue;
                }

                var claimPath = Path.Combine(dir, ".transcription.claim");
                var readyPath = Path.Combine(dir, "READY");
                using var claim = TryReadClaim(claimPath);
                var claimActive = ClaimHasLiveProcess(claim);
                var claimAge = GetClaimAge(claim, claimPath);

                // A live helper can legitimately run for hours. Age alone never revokes ownership.
                // A fresh claim also needs time to bootstrap, even before its PID is visible.
                if (claimActive || (File.Exists(claimPath) && claimAge < staleAfter))
                {
                    result.ActiveClaimsSkipped++;
                    continue;
                }

                if (outputsExist)
                {
                    manifest.TranscriptionStatus = "completed";
                    manifest.TranscriptionFinishedAt ??= DateTimeOffset.Now;
                    manifest.TranscriptionFinishedAtAuthority ??= "startup_recovery_existing_outputs";
                    manifest.TranscriptionError = null;
                    manifest.TranscriptTextPath = expected.TxtPath;
                    manifest.TranscriptJsonPath = expected.JsonPath;
                    manifest.LastHeartbeatAt = DateTimeOffset.Now;
                    WriteManifest(manifestPath, manifest);
                    TryRemoveFile(claimPath);
                    result.CompletedFromExistingOutputs++;
                    AppendSessionLog(dir, "Transcriptierecovery: bestaande transcriptartefacten gevonden; manifest op completed gezet.");
                    continue;
                }

                if (!File.Exists(Path.Combine(dir, "audio_mix.wav")))
                {
                    result.MissingAudioSkipped++;
                    continue;
                }

                if (File.Exists(claimPath) && !claimActive && claimAge >= staleAfter)
                {
                    TryRemoveFile(claimPath);
                    result.StaleClaimsRemoved++;
                }

                if (!File.Exists(readyPath))
                {
                    File.WriteAllText(readyPath, $"ready={DateTimeOffset.Now:O}{Environment.NewLine}meeting_id={manifest.MeetingId}{Environment.NewLine}", Encoding.UTF8);
                    result.ReadyMarkersWritten++;
                }

                if (transcriptionStatus is "started" or "processing" or "running" or "queued" or "ready_for_transcription" or "failed" or "error" or "")
                {
                    manifest.TranscriptionStatus = "ready_for_transcription";
                    manifest.TranscriptionStartedAt = null;
                    manifest.TranscriptionFinishedAt = null;
                    manifest.TranscriptionFinishedAtAuthority = null;
                    manifest.TranscriptionError = null;
                    manifest.TranscriptTextPath = expected.TxtPath;
                    manifest.TranscriptJsonPath = expected.JsonPath;
                    manifest.LastHeartbeatAt = DateTimeOffset.Now;
                    manifest.RecoveryStatus = "transcription_requeued";
                    manifest.RecoveryNotes = "Bij herstart opnieuw klaargezet voor transcriptieverwerking omdat de vorige verwerking niet volledig was afgerond.";
                    WriteManifest(manifestPath, manifest);
                    result.RequeuedSessions++;
                    AppendSessionLog(dir, "Transcriptierecovery: onvolledige transcriptieverwerking opnieuw klaargezet met READY-marker.");
                }
            }
        }

        return result;
    }

    // Opnieuw uitschrijven op verzoek van de gebruiker: een afgeronde, onderbroken of mislukte opname met audio waar de worker
    // nu niet mee bezig is. Een bestaand transcript gaat opzij (niet weg: het krijgt de tijd erachter); de worker pakt de
    // opname daarna weer op via de READY-marker, zoals na een gewone opname.
    internal static bool IsRetranscribableStatus(string? status)
        => Normalize(status) is "completed" or "interrupted" or "failed" or "error";

    public static bool TryRequeueTranscription(string sessionDirectory, out string reason)
    {
        var manifestPath = Path.Combine(sessionDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            reason = "De gegevens van deze opname (manifest.json) ontbreken.";
            return false;
        }

        if (!File.Exists(Path.Combine(sessionDirectory, "audio_mix.wav")))
        {
            reason = "De opname zelf (audio_mix.wav) ontbreekt; er is niets om uit te schrijven.";
            return false;
        }

        if (File.Exists(Path.Combine(sessionDirectory, ".transcription.claim")))
        {
            reason = "Deze opname wordt op dit moment uitgeschreven.";
            return false;
        }

        var manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath), JsonOptions);
        if (manifest is null)
        {
            reason = "De gegevens van deze opname (manifest.json) zijn niet te lezen.";
            return false;
        }

        if (!IsRetranscribableStatus(manifest.Status))
        {
            reason = "Alleen een afgeronde, onderbroken of mislukte opname kan opnieuw worden uitgeschreven.";
            return false;
        }

        if (Normalize(manifest.TranscriptionStatus) is "started" or "processing" or "running")
        {
            reason = "Deze opname wordt op dit moment uitgeschreven.";
            return false;
        }

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var path in new[] { SessionArtifactPathService.ResolveTranscriptTextPath(sessionDirectory), SessionArtifactPathService.ResolveTranscriptJsonPath(sessionDirectory) })
        {
            if (SessionFiles.Exists(path))
            {
                SessionFiles.Move(path, $"{path}.vorige-{stamp}");
            }
        }

        // De merktekens van de vorige ronde horen niet bij de nieuwe; de worker zet ze opnieuw.
        foreach (var marker in new[] { "TRANSCRIBED", "TRANSCRIPTION_FAILED" })
        {
            var markerPath = Path.Combine(sessionDirectory, marker);
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }

        // Een onderbroken of mislukte opname met audio telt na opnieuw klaarzetten als afgerond, zodat de worker haar oppakt.
        if (!string.Equals(Normalize(manifest.Status), "completed", StringComparison.Ordinal))
        {
            manifest.Status = "completed";
            manifest.FinishedAt ??= manifest.LastHeartbeatAt ?? DateTimeOffset.Now;
            manifest.ErrorCode = null;
            manifest.ErrorMessage = null;
        }

        manifest.TranscriptionStatus = "ready_for_transcription";
        manifest.TranscriptionStartedAt = null;
        manifest.TranscriptionFinishedAt = null;
        manifest.TranscriptionFinishedAtAuthority = null;
        manifest.TranscriptionError = null;
        manifest.TranscriptionAttempts = 0;
        manifest.LastHeartbeatAt = DateTimeOffset.Now;
        manifest.RecoveryStatus = "transcription_requeued_manual";
        manifest.RecoveryNotes = "Op verzoek van de gebruiker opnieuw klaargezet om uit te schrijven.";
        WriteManifest(manifestPath, manifest);
        File.WriteAllText(Path.Combine(sessionDirectory, "READY"), $"ready={DateTimeOffset.Now:O}{Environment.NewLine}meeting_id={manifest.MeetingId}{Environment.NewLine}", Encoding.UTF8);
        AppendSessionLog(sessionDirectory, "Opnieuw uitschrijven op verzoek: bestaand transcript opzij gezet en READY-marker geschreven.");
        reason = string.Empty;
        return true;
    }

    private static void WriteManifest(string manifestPath, SessionManifest manifest)
        => AtomicFileWriter.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);

    private static void AppendSessionLog(string sessionDirectory, string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(sessionDirectory, "session.log"), $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void TryRemoveFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static JsonDocument? TryReadClaim(string claimPath)
    {
        if (!File.Exists(claimPath))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(claimPath, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    private static bool ClaimHasLiveProcess(JsonDocument? claim)
    {
        if (claim is null)
        {
            return false;
        }

        var root = claim.RootElement;
        return ProcessIsAlive(JsonHelperGetInt(root, "owner_pid"))
            || ProcessIsAlive(JsonHelperGetInt(root, "helper_pid"))
            || ProcessIsAlive(JsonHelperGetInt(root, "worker_pid"));
    }

    private static TimeSpan GetClaimAge(JsonDocument? claim, string claimPath)
    {
        if (claim is not null)
        {
            var claimedAt = JsonHelperGetString(claim.RootElement, "claimed_at");
            if (DateTimeOffset.TryParse(claimedAt, out var parsed))
            {
                return DateTimeOffset.Now - parsed;
            }
        }

        try
        {
            return DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(claimPath), TimeSpan.Zero);
        }
        catch
        {
            return TimeSpan.MaxValue;
        }
    }

    private static bool ProcessIsAlive(int? processId)
    {
        if (!processId.HasValue || processId.Value <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static int? JsonHelperGetInt(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string JsonHelperGetString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;

    private static (string TxtPath, string JsonPath, bool WantsTxt, bool WantsJson) GetExpectedOutputs(string sessionDir, string? manifestOutputFormat, string? defaultOutputFormat)
    {
        var format = (manifestOutputFormat ?? defaultOutputFormat ?? "txt+json").Trim().ToLowerInvariant();
        var txtPath = format is "txt" or "txt+json"
            ? SessionArtifactPathService.ResolveTranscriptTextPath(sessionDir)
            : string.Empty;
        var jsonPath = format is "json" or "txt+json"
            ? SessionArtifactPathService.ResolveTranscriptJsonPath(sessionDir)
            : string.Empty;
        return (txtPath, jsonPath, !string.IsNullOrWhiteSpace(txtPath), !string.IsNullOrWhiteSpace(jsonPath));
    }

    private static bool ExpectedOutputsExist((string TxtPath, string JsonPath, bool WantsTxt, bool WantsJson) expected)
    {
        return TranscriptOutputIntegrity.IsComplete(expected.TxtPath, expected.JsonPath, expected.WantsTxt, expected.WantsJson);
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}

internal sealed class TranscriptionQueueRecoveryResult
{
    public int ScannedSessions { get; set; }
    public int AlreadyCompleted { get; set; }
    public int CompletedFromExistingOutputs { get; set; }
    public int RequeuedSessions { get; set; }
    public int ReadyMarkersWritten { get; set; }
    public int StaleClaimsRemoved { get; set; }
    public int ActiveClaimsSkipped { get; set; }
    public int MissingAudioSkipped { get; set; }
}
