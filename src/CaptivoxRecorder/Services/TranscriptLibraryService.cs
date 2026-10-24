using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class TranscriptLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<TranscriptLibraryEntry> Scan(
        ApplicationSettings settings,
        string? query = null,
        string? tagFilter = null,
        string? contextFilter = null,
        string? subcontextFilter = null,
        string? meetingTypeFilter = null,
        int maxItems = 500,
        CancellationToken cancellationToken = default)
    {
        var q = (query ?? string.Empty).Trim();
        var activeTag = (tagFilter ?? string.Empty).Trim();
        var activeContext = (contextFilter ?? string.Empty).Trim();
        var activeSubcontext = (subcontextFilter ?? string.Empty).Trim();
        var activeMeetingType = (meetingTypeFilter ?? string.Empty).Trim();
        var results = new List<TranscriptLibraryEntry>();
        foreach (var root in SessionInventoryService.GetSessionRoots(settings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root);
            }
            catch
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath), JsonOptions);
                    if (manifest is null)
                    {
                        continue;
                    }

                    var loadedLibraryMetadata = LibrarySessionMetadataService.Load(dir);
                    var libraryMetadata = loadedLibraryMetadata;
                    var displayTitle = ResolveDisplayTitle(libraryMetadata, manifest);
                    var transcriptText = string.Empty;

                    if (!string.IsNullOrWhiteSpace(activeContext)
                        && !string.Equals(libraryMetadata.ContextName, activeContext, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(activeSubcontext)
                        && !string.Equals(libraryMetadata.SubcontextName, activeSubcontext, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(activeMeetingType)
                        && !string.Equals(libraryMetadata.MeetingType, activeMeetingType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(activeTag)
                        && !libraryMetadata.Tags.Any(x => string.Equals(x, activeTag, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        transcriptText = TranscriptExportService.ReadTranscriptText(dir) ?? string.Empty;

                        var haystackParts = new List<string?>
                        {
                            manifest.MeetingId,
                            displayTitle,
                            manifest.SourceFileName,
                            manifest.SourceType,
                            manifest.TranscriptionStatus,
                            libraryMetadata.ContextName,
                            libraryMetadata.SubcontextName,
                            libraryMetadata.MeetingType,
                            libraryMetadata.MeetingSeriesName,
                            transcriptText,
                            string.Join(" ", libraryMetadata.Tags),
                            string.Join(" ", libraryMetadata.Participants)
                        };
                        var haystack = string.Join("\n", haystackParts.Where(x => !string.IsNullOrWhiteSpace(x)));
                        if (haystack.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    var readyWrittenAt = TryReadPhaseDate(manifest.PhaseTimings?.Recorder, "ready_written_at");
                    var transcriptionStartedAt = TryReadPhaseDate(manifest.PhaseTimings?.Transcription, "started_at") ?? manifest.TranscriptionStartedAt;
                    var transcriptionFinishedAt = TryReadPhaseDate(manifest.PhaseTimings?.Transcription, "finished_at") ?? manifest.TranscriptionFinishedAt;
                    var transcriptionTimingAuthority = TryReadPhaseString(manifest.PhaseTimings?.Transcription, "authority")
                        ?? (transcriptionStartedAt.HasValue || transcriptionFinishedAt.HasValue ? "manifest.top_level" : null);

                    results.Add(new TranscriptLibraryEntry
                    {
                        MeetingId = manifest.MeetingId,
                        DisplayTitle = displayTitle,
                        CreatedAt = manifest.CreatedAt,
                        FinishedAt = manifest.FinishedAt,
                        DirectoryPath = dir,
                        SourceType = manifest.SourceType,
                        Status = manifest.Status,
                        TranscriptionStatus = manifest.TranscriptionStatus,
                        AppVersion = manifest.AppVersion,
                        WorkerVersion = manifest.WorkerVersion,
                        ReadyWrittenAt = readyWrittenAt,
                        TranscriptionStartedAt = transcriptionStartedAt,
                        TranscriptionFinishedAt = transcriptionFinishedAt,
                        TranscriptionTimingAuthority = transcriptionTimingAuthority,
                        TranscriptTextPath = manifest.TranscriptTextPath ?? Path.Combine(dir, "transcript.txt"),
                        TranscriptJsonPath = manifest.TranscriptJsonPath ?? Path.Combine(dir, "transcript.json"),
                        SourceFileName = manifest.SourceFileName,
                        ImportSourceProfile = manifest.ImportSourceProfile,
                        ImportSourceProfileReason = manifest.ImportSourceProfileReason,
                        EmbeddedRecordedAt = manifest.EmbeddedRecordedAt,
                        OneDriveObservedAt = manifest.OneDriveObservedAt,
                        RecordedAt = manifest.RecordedAt,
                        RecordedAtSource = manifest.RecordedAtSource,
                        RecordedAtConfidence = manifest.RecordedAtConfidence,
                        RecordedAtConfidenceReason = manifest.RecordedAtConfidenceReason,
                        ManualRecordedAtConfirmed = manifest.ManualRecordedAtConfirmed,
                        ManualRecordedAtConfirmedAt = manifest.ManualRecordedAtConfirmedAt,
                        ImportedAt = manifest.ImportedAt,
                        DurationSeconds = manifest.DurationSeconds,
                        AgendaMatchStatus = manifest.AgendaMatchStatus,
                        AgendaMatchConfidence = manifest.AgendaMatchConfidence,
                        AgendaMatchBasis = manifest.AgendaMatchBasis,
                        AgendaMatchRecordedAtUsed = manifest.AgendaMatchRecordedAtUsed,
                        AgendaMatchTimeDeltaMinutes = manifest.AgendaMatchTimeDeltaMinutes,
                        AgendaMatchReason = manifest.AgendaMatchReason,
                        ManualAgendaMatchConfirmed = manifest.ManualAgendaMatchConfirmed,
                        ManualAgendaMatchConfirmedAt = manifest.ManualAgendaMatchConfirmedAt,
                        SearchSnippet = string.IsNullOrWhiteSpace(q) ? string.Empty : CreateSnippet(transcriptText, q),
                        ContextName = libraryMetadata.ContextName,
                        SubcontextName = libraryMetadata.SubcontextName,
                        MeetingType = libraryMetadata.MeetingType,
                        MeetingSeriesName = libraryMetadata.MeetingSeriesName,
                        CalendarSubject = libraryMetadata.CalendarSubject,
                        CalendarOrganizer = libraryMetadata.CalendarOrganizer,
                        CalendarLocation = libraryMetadata.CalendarLocation,
                        CalendarItemId = libraryMetadata.CalendarItemId,
                        Tags = libraryMetadata.Tags,
                        Participants = libraryMetadata.Participants,
                        MeetingDate = libraryMetadata.MeetingDate,
                    });
                }
                catch
                {
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results.OrderByDescending(x => x.CreatedAt).Take(maxItems).ToList();
    }

    // De titel uit de bibliotheekgegevens. Versie 1.1.1 van de gratis versie bewaarde een zelfgekozen titel in manifest.json
    // ("title"); zonder titel in de bibliotheekgegevens geldt die titel nog.
    private static string ResolveDisplayTitle(LibrarySessionMetadata metadata, SessionManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(metadata.DisplayTitle))
        {
            return metadata.DisplayTitle;
        }
        if (manifest.AdditionalFields is not null
            && manifest.AdditionalFields.TryGetValue("title", out var title)
            && title.ValueKind == JsonValueKind.String)
        {
            return (title.GetString() ?? string.Empty).Trim();
        }
        return string.Empty;
    }



    private static DateTimeOffset? TryReadPhaseDate(Dictionary<string, object?>? phase, string key)
    {
        if (phase is null || !phase.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw.ToString(), out var value) ? value : null;
    }

    private static string? TryReadPhaseString(Dictionary<string, object?>? phase, string key)
    {
        if (phase is null || !phase.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        var value = raw.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string CreateSnippet(string transcriptText, string query)
    {
        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return transcriptText.Length <= 140 ? transcriptText : transcriptText[..140] + "…";
        }

        var index = transcriptText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return transcriptText.Length <= 140 ? transcriptText : transcriptText[..140] + "…";
        }

        var start = Math.Max(0, index - 40);
        var length = Math.Min(180, transcriptText.Length - start);
        var snippet = transcriptText.Substring(start, length).Trim();
        if (start > 0)
        {
            snippet = "…" + snippet;
        }
        if (start + length < transcriptText.Length)
        {
            snippet += "…";
        }

        return snippet;
    }
}
