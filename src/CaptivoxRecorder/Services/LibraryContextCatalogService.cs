using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class LibraryContextCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string GetPath()
        => Path.Combine(AppRuntime.GetSettingsDirectory(), "library_context_catalog.json");

    public static LibraryContextCatalog Load()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
            {
                return new LibraryContextCatalog();
            }

            var catalog = JsonSerializer.Deserialize<LibraryContextCatalog>(File.ReadAllText(path), JsonOptions) ?? new LibraryContextCatalog();
            return Normalize(catalog);
        }
        catch
        {
            return new LibraryContextCatalog();
        }
    }

    public static void Save(LibraryContextCatalog catalog)
    {
        Directory.CreateDirectory(AppRuntime.GetSettingsDirectory());
        var normalized = Normalize(catalog ?? new LibraryContextCatalog());
        File.WriteAllText(GetPath(), JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public static LibraryContextCatalog MergeWithEntries(IEnumerable<TranscriptLibraryEntry> entries)
    {
        var catalog = Load();
        foreach (var entry in entries ?? Enumerable.Empty<TranscriptLibraryEntry>())
        {
            AddIfValue(catalog.Contexts, entry.ContextName);
            AddIfValue(catalog.Subcontexts, entry.SubcontextName);
            AddIfValue(catalog.MeetingTypes, entry.MeetingType);
            foreach (var tag in entry.Tags ?? new List<string>())
            {
                AddIfValue(catalog.Tags, tag);
            }
        }

        return Normalize(catalog);
    }

    private static LibraryContextCatalog Normalize(LibraryContextCatalog catalog)
    {
        catalog.Contexts = NormalizeList(catalog.Contexts);
        catalog.Subcontexts = NormalizeList(catalog.Subcontexts);
        catalog.MeetingTypes = NormalizeList(catalog.MeetingTypes);
        catalog.Tags = NormalizeList(catalog.Tags);
        return catalog;
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
        => (values ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void AddIfValue(List<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!target.Any(x => string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            target.Add(value.Trim());
        }
    }
}
