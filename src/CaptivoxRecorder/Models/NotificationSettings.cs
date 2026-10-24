namespace LocalCaptureAgent.Models;

public sealed class NotificationSettings
{
    public NotificationEventSettings RecordingStoppedWaitingForTranscription { get; set; } = NotificationEventSettings.DisabledByDefault();
    public NotificationEventSettings TranscriptReady { get; set; } = NotificationEventSettings.WindowsEnabledByDefault();
    public NotificationEventSettings TranscriptionFailed { get; set; } = NotificationEventSettings.WindowsEnabledByDefault();
    public NotificationEventSettings SilenceWarning { get; set; } = NotificationEventSettings.DisabledByDefault();
    public NotificationEventSettings RecordingHealthWarning { get; set; } = NotificationEventSettings.DisabledByDefault();
    // Standaard aan, ook bij een verse installatie: een opname die niets meer opslaat moet de
    // gebruiker bereiken zonder dat die daar eerst een instelling voor moet vinden.
    public NotificationEventSettings RecordingFailure { get; set; } = NotificationEventSettings.WindowsEnabledByDefault();
}
