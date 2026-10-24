namespace LocalCaptureAgent.Models;

public sealed class TranscriptLibraryEntry
{
    public string MeetingId { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public string SourceType { get; set; } = "recording";
    public string Status { get; set; } = string.Empty;
    public string? TranscriptionStatus { get; set; }
    public string? AppVersion { get; set; }
    public string? WorkerVersion { get; set; }
    public DateTimeOffset? ReadyWrittenAt { get; set; }
    public DateTimeOffset? TranscriptionStartedAt { get; set; }
    public DateTimeOffset? TranscriptionFinishedAt { get; set; }
    public string? TranscriptionTimingAuthority { get; set; }
    public string? TranscriptTextPath { get; set; }
    public string? TranscriptJsonPath { get; set; }
    public string? SourceFileName { get; set; }
    public string? ImportSourceProfile { get; set; }
    public string? ImportSourceProfileReason { get; set; }
    public DateTimeOffset? EmbeddedRecordedAt { get; set; }
    public DateTimeOffset? OneDriveObservedAt { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public string? RecordedAtSource { get; set; }
    public string? RecordedAtConfidence { get; set; }
    public string? RecordedAtConfidenceReason { get; set; }
    public bool ManualRecordedAtConfirmed { get; set; }
    public DateTimeOffset? ManualRecordedAtConfirmedAt { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public string? AgendaMatchStatus { get; set; }
    public string? AgendaMatchConfidence { get; set; }
    public string? AgendaMatchBasis { get; set; }
    public DateTimeOffset? AgendaMatchRecordedAtUsed { get; set; }
    public double? AgendaMatchTimeDeltaMinutes { get; set; }
    public string? AgendaMatchReason { get; set; }
    public bool ManualAgendaMatchConfirmed { get; set; }
    public DateTimeOffset? ManualAgendaMatchConfirmedAt { get; set; }
    public string SearchSnippet { get; set; } = string.Empty;
    public string ContextName { get; set; } = string.Empty;
    public string SubcontextName { get; set; } = string.Empty;
    public string MeetingType { get; set; } = string.Empty;
    public string MeetingSeriesName { get; set; } = string.Empty;
    public string CalendarSubject { get; set; } = string.Empty;
    public string CalendarOrganizer { get; set; } = string.Empty;
    public string CalendarLocation { get; set; } = string.Empty;
    public string CalendarItemId { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<string> Participants { get; set; } = new();
    public DateTimeOffset? MeetingDate { get; set; }

    public override string ToString() => $"{CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {MeetingId}";
}
