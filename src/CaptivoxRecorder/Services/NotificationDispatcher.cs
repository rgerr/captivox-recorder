using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal enum NotificationEventType
{
    RecordingStoppedWaitingForTranscription,
    TranscriptReady,
    TranscriptionFailed,
    SilenceWarning,
    RecordingHealthWarning,
    // Opname dood of vol. Bewust los van de stilte- en healthmeldingen: die zijn luidruchtig en
    // worden dus uitgezet, en dat mag dit alarm niet meenemen.
    RecordingFailure
}

// Meldingen gaan alleen lokaal naar Windows (ballon bij het systeemvak).
internal sealed class NotificationDispatcher
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Func<ApplicationSettings> _settingsAccessor;

    public NotificationDispatcher(NotifyIcon notifyIcon, Func<ApplicationSettings> settingsAccessor)
    {
        _notifyIcon = notifyIcon;
        _settingsAccessor = settingsAccessor;
    }

    public Task NotifyAsync(NotificationEventType eventType, string title, string message, ToolTipIcon icon = ToolTipIcon.Info, bool includeWindows = true)
    {
        try
        {
            var settings = _settingsAccessor();
            var eventSettings = GetEventSettings(settings.Ui.Notifications, eventType);
            if (!eventSettings.WindowsEnabled)
            {
                return Task.CompletedTask;
            }

            // In de taal van de interface; zo ook in de lokale geschiedenis (pagina Meldingen en de bel).
            title = UiText.T(title);
            message = UiText.T(message);
            NotificationHistoryStore.Append(AppRuntime.GetSettingsDirectory(), DateTimeOffset.UtcNow, title, message);
            if (includeWindows)
            {
                _notifyIcon.ShowBalloonTip(3500, title, message, icon);
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"Notification dispatch failed for {eventType}", ex);
        }

        return Task.CompletedTask;
    }

    private static NotificationEventSettings GetEventSettings(NotificationSettings settings, NotificationEventType eventType)
        => eventType switch
        {
            NotificationEventType.RecordingStoppedWaitingForTranscription => settings.RecordingStoppedWaitingForTranscription,
            NotificationEventType.TranscriptReady => settings.TranscriptReady,
            NotificationEventType.TranscriptionFailed => settings.TranscriptionFailed,
            NotificationEventType.SilenceWarning => settings.SilenceWarning,
            NotificationEventType.RecordingHealthWarning => settings.RecordingHealthWarning,
            NotificationEventType.RecordingFailure => settings.RecordingFailure,
            _ => NotificationEventSettings.DisabledByDefault()
        };
}
