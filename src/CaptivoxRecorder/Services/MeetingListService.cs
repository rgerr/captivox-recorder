using System.Globalization;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

// De lijst op de pagina Overleggen. Een regel per opgenomen of geimporteerd overleg met de stand in gewone taal
// (opname loopt, wordt uitgeschreven, wacht op jou, klaar, ...). Bron: dezelfde scan als de oude bibliotheek. Alleen lezen.
// De teksten van een regel zijn meteen in de taal van de interface (de lijst tekent ze zelf).
internal static class MeetingListService
{
    // Kind: rec (rood, loopt), bad (rood), busy (blauw), wait (oranje, wacht op jou), ok (groen).
    internal sealed record MeetingRow(TranscriptLibraryEntry Entry, string Title, DateTime When, string WhenText, string Organization, string StatusText, string StatusKind, bool Waiting, int OpenItems)
    {
        // Wat een schermlezer bij de regel voorleest.
        public override string ToString() => $"{Title}, {WhenText}{(Organization.Length > 0 ? ", " + Organization : string.Empty)}, {StatusText}";
    }

    internal static List<MeetingRow> Load(ApplicationSettings settings, string? query, string? activeDirectory, DateTime now, CancellationToken cancellation)
    {
        var rows = new List<MeetingRow>();
        foreach (var entry in TranscriptLibraryService.Scan(settings, string.IsNullOrWhiteSpace(query) ? null : query.Trim(), cancellationToken: cancellation))
        {
            cancellation.ThrowIfCancellationRequested();
            var active = !string.IsNullOrWhiteSpace(activeDirectory) && SameDirectory(activeDirectory, entry.DirectoryPath);
            var readyMarker = File.Exists(Path.Combine(entry.DirectoryPath, "READY"));
            var claimMarker = File.Exists(Path.Combine(entry.DirectoryPath, ".transcription.claim"));
            var when = (entry.RecordedAt ?? entry.MeetingDate ?? entry.CreatedAt).LocalDateTime;
            // Wacht op jou: een opname waar iets mee mis ging (mislukt of onderbroken) en waarvan de audio er is; die kun je opnieuw
            // laten uitschrijven. Zonder audio valt er niets opnieuw uit te schrijven.
            var (text, kind) = Describe(entry.Status, entry.TranscriptionStatus, active, readyMarker, claimMarker);
            var waiting = kind == "bad" && !string.Equals((entry.Status ?? string.Empty).Trim(), "recording", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(entry.DirectoryPath, "audio_mix.wav"));
            rows.Add(new MeetingRow(entry, Title(entry), when, WhenText(entry, when, now), string.Empty, UiText.T(text), kind, waiting, 0));
        }

        return rows.OrderByDescending(r => r.When).ToList();
    }

    // Puur: de stand in gewone taal. Volgorde: wat het meest zegt eerst.
    internal static (string Text, string Kind) Describe(string? status, string? transcriptionStatus, bool active, bool readyMarker, bool claimMarker)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        var t = (transcriptionStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (active) return ("Opname loopt", "rec");
        if (s is "failed" or "error") return ("Opname mislukt", "bad");
        if (t is "failed" or "error") return ("Uitschrijven mislukt", "bad");
        if (claimMarker || t is "started" or "processing") return ("Wordt uitgeschreven", "busy");
        if (s == "interrupted") return ("Opname onderbroken", "bad");
        if (t != "completed")
        {
            if (s == "recording") return ("Opname onderbroken", "bad");
            return readyMarker || s == "completed" ? ("Wacht op uitschrijven", "busy") : ("Nog niet verwerkt", "busy");
        }

        return ("Klaar", "ok");
    }

    // Waaraan een wachtend overleg te herkennen is (mapnaam en overleg-id), voor de koppeling met Vandaag.
    internal static HashSet<string> WaitingKeys(IEnumerable<MeetingRow> rows)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(r => r.Waiting))
        {
            var folder = Path.GetFileName((row.Entry.DirectoryPath ?? string.Empty).TrimEnd('\\', '/'));
            if (folder.Length > 0) keys.Add(folder);
            if (!string.IsNullOrWhiteSpace(row.Entry.MeetingId)) keys.Add(row.Entry.MeetingId);
        }

        return keys;
    }

    internal static string Title(TranscriptLibraryEntry entry)
    {
        var title = !string.IsNullOrWhiteSpace(entry.DisplayTitle) ? entry.DisplayTitle.Trim()
            : !string.IsNullOrWhiteSpace(entry.CalendarSubject) ? entry.CalendarSubject.Trim()
            // Een geimporteerd bestand heet zoals het bestand.
            : !string.IsNullOrWhiteSpace(entry.SourceFileName) ? Path.GetFileNameWithoutExtension(entry.SourceFileName.Trim())
            : entry.MeetingId;
        // De mapnaam als titel ("meeting-<datum>-<tijd>") zegt de gebruiker niets.
        return title.Length == 0 || title.StartsWith("meeting-20", StringComparison.OrdinalIgnoreCase) || string.Equals(title, entry.MeetingId, StringComparison.OrdinalIgnoreCase)
            ? UiText.T("Opname zonder titel")
            : title;
    }

    // "vandaag 10:00 - 58 min", "vr 2 okt - 45 min".
    internal static string WhenText(TranscriptLibraryEntry entry, DateTime when, DateTime now)
    {
        var culture = UiText.Culture;
        var day = when.Date == now.Date ? UiText.F("vandaag {0}", when.ToString("HH:mm", culture))
            : when.Date == now.Date.AddDays(-1) ? UiText.F("gisteren {0}", when.ToString("HH:mm", culture))
            : when.Year == now.Year ? when.ToString(UiText.ShortDayMonthFormat, culture) : when.ToString(UiText.DayMonthYearFormat, culture);
        var duration = Duration(entry);
        return duration.Length > 0 ? day + "  -  " + duration : day;
    }

    internal static string Duration(TranscriptLibraryEntry entry)
    {
        var seconds = entry.DurationSeconds ?? (entry.FinishedAt is { } end && end > entry.CreatedAt ? (end - entry.CreatedAt).TotalSeconds : 0);
        if (seconds < 60) return string.Empty;
        var minutes = (int)Math.Round(seconds / 60);
        return minutes < 60 ? UiText.F("{0} min", minutes)
            : minutes % 60 > 0 ? UiText.F("{0} uur {1} min", minutes / 60, minutes % 60)
            : UiText.F("{0} uur", minutes / 60);
    }

    private static bool SameDirectory(string a, string b)
        => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
