using System.Text.Json.Serialization;

namespace Captivox.Recorder.Models;

public sealed class CaptureDiagnostics
{
    [JsonPropertyName("mic_input_format")]
    public string? MicInputFormat { get; set; }

    [JsonPropertyName("system_input_format")]
    public string? SystemInputFormat { get; set; }

    [JsonPropertyName("mic_first_callback_lag_ms")]
    public double MicFirstCallbackLagMs { get; set; }

    [JsonPropertyName("system_first_callback_lag_ms")]
    public double SystemFirstCallbackLagMs { get; set; }

    [JsonPropertyName("mic_inserted_silence_ms")]
    public double MicInsertedSilenceMs { get; set; }

    [JsonPropertyName("system_inserted_silence_ms")]
    public double SystemInsertedSilenceMs { get; set; }

    [JsonPropertyName("mic_truncated_trailing_silence_ms")]
    public double MicTruncatedTrailingSilenceMs { get; set; }

    [JsonPropertyName("system_truncated_trailing_silence_ms")]
    public double SystemTruncatedTrailingSilenceMs { get; set; }

    [JsonPropertyName("mic_max_gap_ms")]
    public double MicMaxGapMs { get; set; }

    [JsonPropertyName("system_max_gap_ms")]
    public double SystemMaxGapMs { get; set; }

    [JsonPropertyName("stop_timeout_hit")]
    public bool StopTimeoutHit { get; set; }

    [JsonPropertyName("stop_timeout_seconds")]
    public int StopTimeoutSeconds { get; set; }

    [JsonPropertyName("same_device_risk")]
    public bool SameDeviceRisk { get; set; }

    [JsonPropertyName("device_profile")]
    public string? DeviceProfile { get; set; }

    [JsonPropertyName("audio_endpoint_at_start")]
    public AudioEndpointSnapshot? AudioEndpointAtStart { get; set; }

    [JsonPropertyName("audio_endpoint_after_stop")]
    public AudioEndpointSnapshot? AudioEndpointAfterStop { get; set; }

}
