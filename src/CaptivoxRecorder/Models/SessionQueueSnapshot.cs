namespace LocalCaptureAgent.Models;

public sealed class SessionQueueSnapshot
{
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int FailedCount { get; set; }
    public int CompletedCount { get; set; }
    public int ReadyMarkerCount { get; set; }
    public int ClaimedCount { get; set; }
    public int ScannedSessionCount { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public List<SessionManifest> SessionManifests { get; set; } = new();

    public string ToSummaryText()
    {
        if (PendingCount == 0 && ProcessingCount == 0 && FailedCount == 0)
        {
            return "Geen wachtrij";
        }

        return $"Wacht: {PendingCount} · Bezig: {ProcessingCount} · Fout: {FailedCount}";
    }
}
