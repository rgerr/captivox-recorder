namespace LocalCaptureAgent.Models;

public sealed class HardwareProfileSettings
{
    public bool StartupDetectionCompleted { get; set; }
    public DateTimeOffset? LastDetectedAtUtc { get; set; }
    public string LastGpuVendor { get; set; } = "unknown";
    public string? LastGpuName { get; set; }
    public double LastVramGb { get; set; }
    public string LastVramConfidence { get; set; } = "none";
    public string LastDetectionMethod { get; set; } = "none";
    public string LastRecommendedTranscriptionProfile { get; set; } = "cpu-fallback";
    public string LastRecommendedModelTag { get; set; } = "qwen3.8:27b";
    public string LastAdviceClass { get; set; } = "onzeker";
    public bool LastGpuRuntimeAvailable { get; set; }
    public string LastGpuRuntimeStatus { get; set; } = "not_checked";
    public string LastFallbackReason { get; set; } = string.Empty;
}

