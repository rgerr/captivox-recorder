using System.Text.Json.Serialization;

namespace LocalCaptureAgent.Models;

public sealed class LibrarySessionMetadata
{
    [JsonPropertyName("display_title")]
    public string DisplayTitle { get; set; } = string.Empty;

    [JsonPropertyName("context_id")]
    public string ContextId { get; set; } = string.Empty;

    [JsonPropertyName("context_name")]
    public string ContextName { get; set; } = string.Empty;

    [JsonPropertyName("subcontext_id")]
    public string SubcontextId { get; set; } = string.Empty;

    [JsonPropertyName("subcontext_name")]
    public string SubcontextName { get; set; } = string.Empty;

    [JsonPropertyName("meeting_type")]
    public string MeetingType { get; set; } = string.Empty;

    [JsonPropertyName("meeting_series_id")]
    public string MeetingSeriesId { get; set; } = string.Empty;

    [JsonPropertyName("meeting_series_name")]
    public string MeetingSeriesName { get; set; } = string.Empty;

    [JsonPropertyName("participants")]
    public List<string> Participants { get; set; } = new();

    [JsonPropertyName("meeting_date")]
    public DateTimeOffset? MeetingDate { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("calendar_subject")]
    public string CalendarSubject { get; set; } = string.Empty;

    [JsonPropertyName("calendar_organizer")]
    public string CalendarOrganizer { get; set; } = string.Empty;

    [JsonPropertyName("calendar_location")]
    public string CalendarLocation { get; set; } = string.Empty;

    [JsonPropertyName("calendar_item_id")]
    public string CalendarItemId { get; set; } = string.Empty;

    [JsonPropertyName("calendar_source")]
    public string CalendarSource { get; set; } = string.Empty;

    [JsonPropertyName("calendar_store_name")]
    public string CalendarStoreName { get; set; } = string.Empty;


    [JsonPropertyName("import_inbox_status")]
    public string ImportInboxStatus { get; set; } = string.Empty;

    [JsonPropertyName("recorded_at_confidence_note")]
    public string RecordedAtConfidenceNote { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Velden die deze editie niet kent blijven bij opnieuw opslaan ongewijzigd staan in plaats van stil te verdwijnen.
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalFields { get; set; }

    [JsonIgnore]
    public string OrganizationId
    {
        get => ContextId;
        set => ContextId = value ?? string.Empty;
    }

    [JsonIgnore]
    public string OrganizationName
    {
        get => ContextName;
        set => ContextName = value ?? string.Empty;
    }

    [JsonIgnore]
    public string ProjectId
    {
        get => SubcontextId;
        set => SubcontextId = value ?? string.Empty;
    }

    [JsonIgnore]
    public string ProjectName
    {
        get => SubcontextName;
        set => SubcontextName = value ?? string.Empty;
    }
}
