using System.Text.Json.Serialization;

namespace Captivox.Recorder.Models;

public sealed class SessionManifest
{
    [JsonPropertyName("meeting_id")]
    public string MeetingId { get; set; } = string.Empty;

    // Een naam die de gebruiker zelf gaf; leeg betekent "Opname <datum>".
    [JsonPropertyName("title")]
    public string? Title { get; set; }

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

}
