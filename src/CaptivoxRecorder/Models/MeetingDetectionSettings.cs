namespace LocalCaptureAgent.Models;

public sealed class MeetingDetectionSettings
{
    public bool Enabled { get; set; } = false;
    public bool DetectTeams { get; set; } = true;
    public bool DetectZoom { get; set; } = true;
    public bool DetectBrowserMeetings { get; set; } = true;
    public int StabilitySeconds { get; set; } = 15;
    public int PromptCooldownMinutes { get; set; } = 10;
}
