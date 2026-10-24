using System.Text.Json.Serialization;

namespace Captivox.Recorder.Models;

public sealed class WorkerRuntimeSnapshot
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("worker_root")]
    public string? WorkerRoot { get; set; }

    [JsonPropertyName("worker_script")]
    public string? WorkerScript { get; set; }

    [JsonPropertyName("worker_pid")]
    public int WorkerPid { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("active_session_dir")]
    public string? ActiveSessionDir { get; set; }

    [JsonPropertyName("last_seen_utc")]
    public DateTimeOffset? LastSeenUtc { get; set; }

    [JsonPropertyName("last_activity_utc")]
    public DateTimeOffset? LastActivityUtc { get; set; }

    [JsonPropertyName("session_roots")]
    public List<string> SessionRoots { get; set; } = new();
}
