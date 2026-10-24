using System.Text.Json.Serialization;

namespace LocalCaptureAgent.Models;

public sealed class LibraryContextCatalog
{
    [JsonPropertyName("contexts")]
    public List<string> Contexts { get; set; } = new();

    [JsonPropertyName("subcontexts")]
    public List<string> Subcontexts { get; set; } = new();

    [JsonPropertyName("meeting_types")]
    public List<string> MeetingTypes { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}
