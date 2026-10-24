namespace LocalCaptureAgent.Models;

public sealed class RetentionSettings
{
    public bool Enabled { get; set; } = false;
    public int AudioRetentionDays { get; set; } = 30;
    public int TranscriptRetentionDays { get; set; } = 90;
    public int SessionRecordRetentionDays { get; set; } = 90;
}
