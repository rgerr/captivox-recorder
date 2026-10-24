using System.Text.Json.Serialization;

namespace Captivox.Recorder.Models;

public sealed class MixMetrics
{
    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("mic_peak")]
    public double MicPeak { get; set; }

    [JsonPropertyName("system_peak")]
    public double SystemPeak { get; set; }

    [JsonPropertyName("mix_peak_before_limiting")]
    public double MixPeakBeforeLimiting { get; set; }

    [JsonPropertyName("mix_peak_after_limiting")]
    public double MixPeakAfterLimiting { get; set; }

    [JsonPropertyName("ducked_windows")]
    public int DuckedWindows { get; set; }

    [JsonPropertyName("noise_gated_windows")]
    public int NoiseGatedWindows { get; set; }

    [JsonPropertyName("average_system_gain")]
    public double AverageSystemGain { get; set; }

    [JsonPropertyName("average_mic_gain")]
    public double AverageMicGain { get; set; }

    [JsonPropertyName("estimated_clipped_samples_before_limiting")]
    public int EstimatedClippedSamplesBeforeLimiting { get; set; }

    [JsonPropertyName("analysis_window_ms")]
    public int AnalysisWindowMs { get; set; }

    [JsonPropertyName("mic_gate_hold_ms")]
    public double MicGateHoldMs { get; set; }

    [JsonPropertyName("noise_gate_close_threshold")]
    public double NoiseGateCloseThreshold { get; set; }
}
