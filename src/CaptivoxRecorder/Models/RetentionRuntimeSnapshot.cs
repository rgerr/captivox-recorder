namespace LocalCaptureAgent.Models;

public sealed class RetentionRuntimeSnapshot
{
    public bool Enabled { get; set; }
    public bool Success { get; set; } = true;
    public DateTimeOffset LastRunAt { get; set; } = DateTimeOffset.Now;
    public int ScannedSessionCount { get; set; }
    public int EligibleSessionCount { get; set; }
    public int DeletedAudioFileCount { get; set; }
    public int DeletedTranscriptFileCount { get; set; }
    public int DeletedSessionRecordFileCount { get; set; }
    public int DeletedSessionDirectoryCount { get; set; }
    public string Summary { get; set; } = "Retentie nog niet uitgevoerd";
    public string? LastError { get; set; }
}
