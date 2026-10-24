namespace LocalCaptureAgent.Models;

public sealed class NotificationEventSettings
{
    public bool WindowsEnabled { get; set; }

    public static NotificationEventSettings DisabledByDefault() => new();

    public static NotificationEventSettings WindowsEnabledByDefault() => new()
    {
        WindowsEnabled = true
    };
}
