namespace LocalCaptureAgent.Models;

public sealed class MonitoringSettings
{
    public bool HealthMonitorEnabled { get; set; } = true;
    public bool SilenceDetectionEnabled { get; set; } = true;
    public double SilenceThresholdPercent { get; set; } = 1.5;
    public int MicSilenceWarningSeconds { get; set; } = 10;
    public int SystemSilenceWarningSeconds { get; set; } = 10;
    public int BothSilenceWarningSeconds { get; set; } = 15;
    public int WarningCooldownSeconds { get; set; } = 60;
}
