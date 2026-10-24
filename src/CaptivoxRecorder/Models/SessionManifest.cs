using System.Text.Json.Serialization;

namespace LocalCaptureAgent.Models;

public sealed class SessionManifest
{
    [JsonPropertyName("meeting_id")]
    public string MeetingId { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public DateTimeOffset? FinishedAt { get; set; }

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = Environment.MachineName;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "windows";

    [JsonPropertyName("mic_device")]
    public string MicDevice { get; set; } = string.Empty;

    [JsonPropertyName("system_device")]
    public string SystemDevice { get; set; } = string.Empty;

    [JsonPropertyName("sample_rate_input_mic")]
    public int? SampleRateInputMic { get; set; }

    [JsonPropertyName("sample_rate_input_system")]
    public int? SampleRateInputSystem { get; set; }

    [JsonPropertyName("channels_input_mic")]
    public int? ChannelsInputMic { get; set; }

    [JsonPropertyName("channels_input_system")]
    public int? ChannelsInputSystem { get; set; }

    [JsonPropertyName("sample_rate_output")]
    public int SampleRateOutput { get; set; }

    [JsonPropertyName("channels_output")]
    public int ChannelsOutput { get; set; }

    [JsonPropertyName("output_file")]
    public string OutputFile { get; set; } = string.Empty;

    [JsonPropertyName("output_directory")]
    public string OutputDirectory { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "created";

    [JsonPropertyName("app_version")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("mic_stem")]
    public string? MicStem { get; set; }

    [JsonPropertyName("system_stem")]
    public string? SystemStem { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("quality_profile")]
    public string QualityProfile { get; set; } = "speech-first-windowed-mix-v5";

    [JsonPropertyName("device_profile")]
    public string? DeviceProfile { get; set; }

    [JsonPropertyName("same_device_risk")]
    public bool SameDeviceRisk { get; set; }

    [JsonPropertyName("same_device_warning")]
    public string? SameDeviceWarning { get; set; }

    [JsonPropertyName("phase_timings")]
    public SessionPhaseTimings? PhaseTimings { get; set; }

    [JsonPropertyName("capture_diagnostics")]
    public CaptureDiagnostics? CaptureDiagnostics { get; set; }

    [JsonPropertyName("mix_metrics")]
    public MixMetrics? MixMetrics { get; set; }

    [JsonPropertyName("transcription_status")]
    public string? TranscriptionStatus { get; set; }

    [JsonPropertyName("transcription_started_at")]
    public DateTimeOffset? TranscriptionStartedAt { get; set; }

    [JsonPropertyName("transcription_finished_at")]
    public DateTimeOffset? TranscriptionFinishedAt { get; set; }

    [JsonPropertyName("transcription_finished_at_authority")]
    public string? TranscriptionFinishedAtAuthority { get; set; }


    [JsonPropertyName("transcription_error")]
    public string? TranscriptionError { get; set; }

    [JsonPropertyName("transcript_text_path")]
    public string? TranscriptTextPath { get; set; }

    [JsonPropertyName("transcript_json_path")]
    public string? TranscriptJsonPath { get; set; }

    [JsonPropertyName("transcription_engine")]
    public string? TranscriptionEngine { get; set; }

    [JsonPropertyName("transcription_attempts")]
    public int TranscriptionAttempts { get; set; }

    [JsonPropertyName("transcription_input_path")]
    public string? TranscriptionInputPath { get; set; }

    [JsonPropertyName("transcript_output_format")]
    public string? TranscriptOutputFormat { get; set; }

    [JsonPropertyName("worker_version")]
    public string? WorkerVersion { get; set; }

    [JsonPropertyName("worker_host")]
    public string? WorkerHost { get; set; }

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "recording";

    [JsonPropertyName("source_file_name")]
    public string? SourceFileName { get; set; }

    [JsonPropertyName("source_original_path")]
    public string? SourceOriginalPath { get; set; }

    [JsonPropertyName("import_mode")]
    public string? ImportMode { get; set; }

    [JsonPropertyName("import_source_profile")]
    public string? ImportSourceProfile { get; set; }

    [JsonPropertyName("import_source_profile_reason")]
    public string? ImportSourceProfileReason { get; set; }

    [JsonPropertyName("embedded_recorded_at")]
    public DateTimeOffset? EmbeddedRecordedAt { get; set; }

    [JsonPropertyName("onedrive_observed_at")]
    public DateTimeOffset? OneDriveObservedAt { get; set; }

    [JsonPropertyName("recorded_at")]
    public DateTimeOffset? RecordedAt { get; set; }

    [JsonPropertyName("recorded_at_source")]
    public string? RecordedAtSource { get; set; }

    [JsonPropertyName("recorded_at_confidence")]
    public string? RecordedAtConfidence { get; set; }

    [JsonPropertyName("recorded_at_confidence_reason")]
    public string? RecordedAtConfidenceReason { get; set; }

    [JsonPropertyName("manual_recorded_at_confirmed")]
    public bool ManualRecordedAtConfirmed { get; set; }

    [JsonPropertyName("manual_recorded_at_confirmed_at")]
    public DateTimeOffset? ManualRecordedAtConfirmedAt { get; set; }

    [JsonPropertyName("manual_recorded_at_correction_reason")]
    public string? ManualRecordedAtCorrectionReason { get; set; }

    [JsonPropertyName("capture_time_audit_status")]
    public string? CaptureTimeAuditStatus { get; set; }

    [JsonPropertyName("capture_time_audit_last_attempt_at")]
    public DateTimeOffset? CaptureTimeAuditLastAttemptAt { get; set; }

    [JsonPropertyName("capture_time_audit_error")]
    public string? CaptureTimeAuditError { get; set; }

    [JsonPropertyName("file_created_at")]
    public DateTimeOffset? FileCreatedAt { get; set; }

    [JsonPropertyName("file_modified_at")]
    public DateTimeOffset? FileModifiedAt { get; set; }

    [JsonPropertyName("imported_at")]
    public DateTimeOffset? ImportedAt { get; set; }

    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("agenda_match_status")]
    public string? AgendaMatchStatus { get; set; }

    [JsonPropertyName("agenda_match_confidence")]
    public string? AgendaMatchConfidence { get; set; }

    [JsonPropertyName("agenda_match_basis")]
    public string? AgendaMatchBasis { get; set; }

    [JsonPropertyName("agenda_match_recorded_at_used")]
    public DateTimeOffset? AgendaMatchRecordedAtUsed { get; set; }

    [JsonPropertyName("agenda_match_time_delta_minutes")]
    public double? AgendaMatchTimeDeltaMinutes { get; set; }

    [JsonPropertyName("agenda_match_reason")]
    public string? AgendaMatchReason { get; set; }

    [JsonPropertyName("manual_agenda_match_confirmed")]
    public bool ManualAgendaMatchConfirmed { get; set; }

    [JsonPropertyName("manual_agenda_match_confirmed_at")]
    public DateTimeOffset? ManualAgendaMatchConfirmedAt { get; set; }

    [JsonPropertyName("agenda_match_candidates")]
    public List<AgendaMatchCandidateSnapshot> AgendaMatchCandidates { get; set; } = new();

    [JsonPropertyName("recovery_status")]
    public string? RecoveryStatus { get; set; }

    [JsonPropertyName("recovery_notes")]
    public string? RecoveryNotes { get; set; }

    [JsonPropertyName("last_heartbeat_at")]
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    [JsonPropertyName("audio_enhanced_path")]
    public string? AudioEnhancedPath { get; set; }

    [JsonPropertyName("transcript_srt_path")]
    public string? TranscriptSrtPath { get; set; }

    [JsonPropertyName("transcript_vtt_path")]
    public string? TranscriptVttPath { get; set; }

    [JsonPropertyName("transcript_schema_version")]
    public string? TranscriptSchemaVersion { get; set; }



    [JsonPropertyName("insights_status")]
    public string? InsightsStatus { get; set; }

    [JsonPropertyName("insights_contract_status")]
    public string? InsightsContractStatus { get; set; }

    [JsonPropertyName("insights_actions_count")]
    public int? InsightsActionsCount { get; set; }

    [JsonPropertyName("insights_decisions_count")]
    public int? InsightsDecisionsCount { get; set; }

    [JsonPropertyName("insights_open_questions_count")]
    public int? InsightsOpenQuestionsCount { get; set; }

    [JsonPropertyName("insights_topics_count")]
    public int? InsightsTopicsCount { get; set; }

    [JsonPropertyName("insights_filtered_items_count")]
    public int? InsightsFilteredItemsCount { get; set; }

    [JsonPropertyName("structured_export_status")]
    public string? StructuredExportStatus { get; set; }

    [JsonPropertyName("structured_export_finished_at")]
    public DateTimeOffset? StructuredExportFinishedAt { get; set; }

    [JsonPropertyName("structured_export_error")]
    public string? StructuredExportError { get; set; }

    [JsonPropertyName("structured_export_directory")]
    public string? StructuredExportDirectory { get; set; }

    [JsonPropertyName("post_meeting_review_pack_status")]
    public string? PostMeetingReviewPackStatus { get; set; }

    [JsonPropertyName("post_meeting_review_pack_path")]
    public string? PostMeetingReviewPackPath { get; set; }

    [JsonPropertyName("post_meeting_review_markdown_path")]
    public string? PostMeetingReviewMarkdownPath { get; set; }

    [JsonPropertyName("post_meeting_review_status_path")]
    public string? PostMeetingReviewStatusPath { get; set; }

    [JsonPropertyName("post_meeting_review_generated_at_utc")]
    public DateTimeOffset? PostMeetingReviewGeneratedAtUtc { get; set; }

    // Velden die deze editie niet kent (bijvoorbeeld uit een uitgebreidere versie) blijven bij opnieuw opslaan
    // ongewijzigd in manifest.json staan in plaats van stil te verdwijnen.
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalFields { get; set; }
}


public sealed class AgendaMatchCandidateSnapshot
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("start_at")]
    public DateTimeOffset? StartAt { get; set; }

    [JsonPropertyName("end_at")]
    public DateTimeOffset? EndAt { get; set; }

    [JsonPropertyName("calendar_account")]
    public string CalendarAccount { get; set; } = string.Empty;

    [JsonPropertyName("match_score")]
    public double MatchScore { get; set; }

    [JsonPropertyName("match_reason")]
    public string MatchReason { get; set; } = string.Empty;
}
