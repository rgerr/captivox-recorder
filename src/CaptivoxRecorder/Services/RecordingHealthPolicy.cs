namespace LocalCaptureAgent.Services;

// Beslissingen van de opname-health-monitor, los van MainForm zodat de regressiesuite ze kan
// toetsen. De monitor voert alleen uit wat hier besloten wordt.
internal static class RecordingHealthPolicy
{
    // Waarschuwingsmomenten voor de WAV-grens. Bij 48 kHz stereo float valt die na ongeveer
    // 3 uur en 6 minuten; een half uur vooraf geeft tijd om rustig te stoppen en opnieuw te starten.
    internal static readonly TimeSpan[] CapacityWarningLeads = { TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10) };

    // Het aantal gepasseerde waarschuwingsmomenten: 0 is nog ruim, 2 is minder dan tien minuten.
    internal static int CapacityWarningStage(TimeSpan? remaining)
    {
        if (remaining is null)
        {
            return 0;
        }

        var stage = 0;
        foreach (var lead in CapacityWarningLeads)
        {
            if (remaining.Value <= lead)
            {
                stage++;
            }
        }

        return stage;
    }

    // "Het is stil, klopt" geldt hoogstens tien minuten (daarna weer waarschuwen: misschien speelt Teams intussen op
    // een ander apparaat af) en tot er weer geluid van de anderen is.
    internal static readonly TimeSpan SilenceAcknowledgementValidity = TimeSpan.FromMinutes(10);

    internal static bool SilenceAcknowledgementActive(DateTimeOffset? acknowledgedAt, DateTimeOffset now)
        => acknowledgedAt is { } at && now >= at && now - at < SilenceAcknowledgementValidity;

    // Een melding per stilte, niet twee: niet zowel een stiltewaarschuwing als een
    // healthwaarschuwing voor dezelfde stilte.
    internal static NotificationEventType? SilenceNotificationType(bool bothSilent, bool anyChannelSilent)
        => bothSilent
            ? NotificationEventType.SilenceWarning
            : anyChannelSilent ? NotificationEventType.RecordingHealthWarning : null;
}
