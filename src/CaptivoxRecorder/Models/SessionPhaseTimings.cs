using System.Text.Json.Serialization;

namespace Captivox.Recorder.Models;

public sealed class SessionPhaseTimings
{
    [JsonPropertyName("recorder")]
    public Dictionary<string, object?> Recorder { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("worker")]
    public Dictionary<string, object?> Worker { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("transcription")]
    public Dictionary<string, object?> Transcription { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
