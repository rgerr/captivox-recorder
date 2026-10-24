using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class LibrarySessionMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string FileName = "library_metadata.json";

    public static LibrarySessionMetadata Load(string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory))
        {
            return new LibrarySessionMetadata();
        }

        var path = Path.Combine(sessionDirectory, FileName);
        if (!File.Exists(path))
        {
            return new LibrarySessionMetadata();
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<LibrarySessionMetadata>(File.ReadAllText(path), JsonOptions);
            return Normalize(metadata ?? new LibrarySessionMetadata());
        }
        catch
        {
            return new LibrarySessionMetadata();
        }
    }

    public static void Save(string sessionDirectory, LibrarySessionMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
        {
            throw new DirectoryNotFoundException("Sessiemap voor bibliotheekmetadata ontbreekt.");
        }

        var normalized = Normalize(metadata ?? new LibrarySessionMetadata());
        normalized.UpdatedAt = DateTimeOffset.UtcNow;
        var path = Path.Combine(sessionDirectory, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    private static LibrarySessionMetadata Normalize(LibrarySessionMetadata metadata)
    {
        metadata.DisplayTitle = (metadata.DisplayTitle ?? string.Empty).Trim();
        metadata.ContextName = (metadata.ContextName ?? string.Empty).Trim();
        metadata.SubcontextName = (metadata.SubcontextName ?? string.Empty).Trim();
        metadata.MeetingType = (metadata.MeetingType ?? string.Empty).Trim();
        metadata.MeetingSeriesName = (metadata.MeetingSeriesName ?? string.Empty).Trim();
        metadata.CalendarSubject = (metadata.CalendarSubject ?? string.Empty).Trim();
        metadata.CalendarOrganizer = (metadata.CalendarOrganizer ?? string.Empty).Trim();
        metadata.CalendarLocation = (metadata.CalendarLocation ?? string.Empty).Trim();
        metadata.CalendarItemId = (metadata.CalendarItemId ?? string.Empty).Trim();
        metadata.CalendarSource = (metadata.CalendarSource ?? string.Empty).Trim();
        metadata.CalendarStoreName = (metadata.CalendarStoreName ?? string.Empty).Trim();
        metadata.ImportInboxStatus = (metadata.ImportInboxStatus ?? string.Empty).Trim();
        metadata.RecordedAtConfidenceNote = (metadata.RecordedAtConfidenceNote ?? string.Empty).Trim();
        metadata.ContextId = string.IsNullOrWhiteSpace(metadata.ContextId) ? metadata.ContextName : metadata.ContextId.Trim();
        metadata.SubcontextId = string.IsNullOrWhiteSpace(metadata.SubcontextId) ? metadata.SubcontextName : metadata.SubcontextId.Trim();
        metadata.MeetingSeriesId = string.IsNullOrWhiteSpace(metadata.MeetingSeriesId) ? metadata.MeetingSeriesName : metadata.MeetingSeriesId.Trim();
        metadata.Tags = metadata.Tags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        metadata.Participants = metadata.Participants
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return metadata;
    }
}
