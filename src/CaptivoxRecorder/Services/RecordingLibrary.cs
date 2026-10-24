using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Captivox.Recorder.Models;

namespace Captivox.Recorder.Services;

internal enum RecordingState
{
    Recording,
    Waiting,
    Transcribing,
    Done,
    Failed,
    Interrupted,
}

internal sealed record RecordingEntry(
    string Directory,
    string Title,
    DateTimeOffset StartedAt,
    TimeSpan? Duration,
    RecordingState State,
    string StatusText,
    string? Problem,
    bool IsImport);

// Een opname is een map in de opslagmap met manifest.json; recorder en worker houden daarin de stand bij.
internal static class RecordingLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static List<RecordingEntry> Scan(IEnumerable<string> roots, string? activeRecordingDirectory)
    {
        var entries = new List<RecordingEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root).ToList();
            }
            catch (Exception ex)
            {
                AppRuntime.LogException($"Opslagmap kon niet worden gelezen: {root}", ex);
                continue;
            }

            foreach (var directory in directories)
            {
                var full = Path.GetFullPath(directory);
                if (!seen.Add(full))
                {
                    continue;
                }

                var entry = TryRead(full, activeRecordingDirectory);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries.OrderByDescending(x => x.StartedAt).ToList();
    }

    private static RecordingEntry? TryRead(string directory, string? activeRecordingDirectory)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        SessionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions);
        }
        catch
        {
            manifest = null;
        }

        if (manifest is null)
        {
            return new RecordingEntry(directory, Path.GetFileName(directory), Directory.GetCreationTime(directory), null, RecordingState.Failed, "Onleesbaar", "manifest.json is beschadigd.", false);
        }

        var isActive = !string.IsNullOrWhiteSpace(activeRecordingDirectory)
            && string.Equals(Path.GetFullPath(activeRecordingDirectory), directory, StringComparison.OrdinalIgnoreCase);
        var isImport = string.Equals(manifest.SourceType, "import", StringComparison.OrdinalIgnoreCase);
        var startedAt = manifest.RecordedAt ?? manifest.CreatedAt;
        TimeSpan? duration = manifest.MixMetrics?.DurationSeconds is double mixSeconds && mixSeconds > 0
            ? TimeSpan.FromSeconds(mixSeconds)
            : manifest.DurationSeconds is double seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
        if (duration is null && manifest.FinishedAt is DateTimeOffset finished && !isImport && finished > manifest.CreatedAt)
        {
            duration = finished - manifest.CreatedAt;
        }

        var title = !string.IsNullOrWhiteSpace(manifest.Title)
            ? manifest.Title!
            : isImport && !string.IsNullOrWhiteSpace(manifest.SourceFileName)
                ? Path.GetFileNameWithoutExtension(manifest.SourceFileName)!
                : "Opname " + startedAt.ToString("d MMMM yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));

        var (state, statusText, problem) = DetermineState(directory, manifest, isActive);
        return new RecordingEntry(directory, title, startedAt, duration, state, statusText, problem, isImport);
    }

    private static (RecordingState State, string Text, string? Problem) DetermineState(string directory, SessionManifest manifest, bool isActive)
    {
        var status = (manifest.Status ?? string.Empty).ToLowerInvariant();
        var transcription = (manifest.TranscriptionStatus ?? string.Empty).ToLowerInvariant();

        if (status == "recording")
        {
            return isActive
                ? (RecordingState.Recording, "Opname loopt", null)
                : (RecordingState.Interrupted, "Onderbroken", "De opname is niet netjes gestopt (de recorder of de pc stopte tijdens het opnemen).");
        }

        if (status == "importing")
        {
            return (RecordingState.Waiting, "Wordt geimporteerd", null);
        }

        if (status is "failed" or "interrupted")
        {
            return (status == "interrupted" ? RecordingState.Interrupted : RecordingState.Failed, status == "interrupted" ? "Onderbroken" : "Opname mislukt", manifest.ErrorMessage);
        }

        if (transcription == "completed")
        {
            return (RecordingState.Done, "Klaar", null);
        }

        if (transcription == "failed")
        {
            return (RecordingState.Failed, "Uitschrijven mislukt", manifest.TranscriptionError);
        }

        if (transcription == "started" || File.Exists(Path.Combine(directory, ".transcription.claim")))
        {
            return (RecordingState.Transcribing, "Wordt uitgeschreven", null);
        }

        return (RecordingState.Waiting, "Wacht op uitschrijven", manifest.TranscriptionError);
    }

    public static string? ReadTranscript(string directory)
    {
        try
        {
            return TranscriptExportService.ReadTranscriptText(directory);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"Transcript lezen mislukt: {directory}", ex);
            return null;
        }
    }

    // Past alleen de gevraagde velden aan; velden die de worker toevoegde blijven staan.
    private static void EditManifest(string directory, Action<JsonObject> edit)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        var node = JsonNode.Parse(File.ReadAllText(manifestPath, Encoding.UTF8)) as JsonObject
            ?? throw new InvalidOperationException("manifest.json is geen geldig object.");
        edit(node);
        AtomicFileWriter.WriteAllText(manifestPath, node.ToJsonString(JsonOptions), Encoding.UTF8);
    }

    public static void Rename(string directory, string title)
        => EditManifest(directory, node => node["title"] = string.IsNullOrWhiteSpace(title) ? null : title.Trim());

    // Het oude transcript gaat naar een submap; daarna pakt de worker de opname opnieuw op.
    public static void QueueRetranscription(string directory)
    {
        if (File.Exists(Path.Combine(directory, ".transcription.claim")))
        {
            throw new InvalidOperationException("Deze opname wordt nu uitgeschreven. Wacht tot dat klaar is.");
        }

        if (!File.Exists(Path.Combine(directory, "audio_mix.wav")))
        {
            throw new InvalidOperationException("Het geluidsbestand (audio_mix.wav) ontbreekt; opnieuw uitschrijven kan niet.");
        }

        var previous = Path.Combine(directory, "vorige_transcripties", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        foreach (var name in new[] { "transcript.txt", "transcript.json", "transcript.srt", "transcript.vtt", "TRANSCRIBED",
                     Path.Combine("transcripts", "transcript.txt"), Path.Combine("transcripts", "transcript.json"),
                     Path.Combine(SessionArtifactPathService.ExportsDirectoryName, "transcript.srt"),
                     Path.Combine(SessionArtifactPathService.ExportsDirectoryName, "transcript.vtt") })
        {
            var source = Path.Combine(directory, name);
            if (!File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(previous);
            var target = Path.Combine(previous, Path.GetFileName(name));
            if (File.Exists(target))
            {
                target = Path.Combine(previous, Path.GetFileNameWithoutExtension(name) + "_" + Guid.NewGuid().ToString("N")[..6] + Path.GetExtension(name));
            }

            File.Move(source, target);
        }

        EditManifest(directory, node =>
        {
            node["status"] = "completed";
            node["transcription_status"] = "ready_for_transcription";
            node["transcription_error"] = null;
            node["transcription_attempts"] = 0;
            node["transcription_started_at"] = null;
            node["transcription_finished_at"] = null;
            node["transcript_text_path"] = null;
            node["transcript_json_path"] = null;
        });

        File.WriteAllText(Path.Combine(directory, "READY"), string.Empty);
    }
}
