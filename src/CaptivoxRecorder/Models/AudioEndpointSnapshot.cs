using System.Text.Json.Serialization;

namespace Captivox.Recorder.Models;

public sealed class AudioEndpointSnapshot
{
    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; }

    [JsonPropertyName("selected_mic_device_id")]
    public string? SelectedMicDeviceId { get; set; }

    [JsonPropertyName("selected_mic_device_name")]
    public string? SelectedMicDeviceName { get; set; }

    [JsonPropertyName("selected_mic_device_state")]
    public string? SelectedMicDeviceState { get; set; }

    [JsonPropertyName("selected_mic_is_default_console")]
    public bool SelectedMicIsDefaultConsole { get; set; }

    [JsonPropertyName("selected_mic_is_default_multimedia")]
    public bool SelectedMicIsDefaultMultimedia { get; set; }

    [JsonPropertyName("selected_mic_is_default_communications")]
    public bool SelectedMicIsDefaultCommunications { get; set; }

    [JsonPropertyName("selected_mic_master_volume_scalar")]
    public float? SelectedMicMasterVolumeScalar { get; set; }

    [JsonPropertyName("selected_mic_muted")]
    public bool? SelectedMicMuted { get; set; }

    [JsonPropertyName("selected_system_device_id")]
    public string? SelectedSystemDeviceId { get; set; }

    [JsonPropertyName("selected_system_device_name")]
    public string? SelectedSystemDeviceName { get; set; }

    [JsonPropertyName("selected_system_device_state")]
    public string? SelectedSystemDeviceState { get; set; }

    [JsonPropertyName("selected_system_is_default_console")]
    public bool SelectedSystemIsDefaultConsole { get; set; }

    [JsonPropertyName("selected_system_is_default_multimedia")]
    public bool SelectedSystemIsDefaultMultimedia { get; set; }

    [JsonPropertyName("selected_system_is_default_communications")]
    public bool SelectedSystemIsDefaultCommunications { get; set; }

    [JsonPropertyName("selected_system_master_volume_scalar")]
    public float? SelectedSystemMasterVolumeScalar { get; set; }

    [JsonPropertyName("selected_system_muted")]
    public bool? SelectedSystemMuted { get; set; }

    [JsonPropertyName("default_capture_console_name")]
    public string? DefaultCaptureConsoleName { get; set; }

    [JsonPropertyName("default_capture_multimedia_name")]
    public string? DefaultCaptureMultimediaName { get; set; }

    [JsonPropertyName("default_capture_communications_name")]
    public string? DefaultCaptureCommunicationsName { get; set; }

    [JsonPropertyName("default_render_console_name")]
    public string? DefaultRenderConsoleName { get; set; }

    [JsonPropertyName("default_render_multimedia_name")]
    public string? DefaultRenderMultimediaName { get; set; }

    [JsonPropertyName("default_render_communications_name")]
    public string? DefaultRenderCommunicationsName { get; set; }
}
