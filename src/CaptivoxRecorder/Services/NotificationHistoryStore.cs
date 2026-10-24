using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LocalCaptureAgent.Services;

// De meldingen die Captivox op deze pc gaf, per dag in een eigen bestand (voor de pagina Meldingen en de bel). Alleen lokaal;
// bestanden ouder dan een week worden bij het schrijven opgeruimd.
internal static class NotificationHistoryStore
{
    internal const string FilePrefix = "notification_history_";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly object Sync = new();

    internal sealed record NotificationEntry(DateTimeOffset At, string Title, string Message);

    internal static void Append(string settingsDirectory, DateTimeOffset nowUtc, string title, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(settingsDirectory);
                var line = JsonSerializer.Serialize(new NotificationEntry(nowUtc, title ?? string.Empty, message ?? string.Empty));
                File.AppendAllText(PathFor(settingsDirectory, nowUtc), line + "\n", Encoding.UTF8);
                DeleteExpired(settingsDirectory, nowUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppRuntime.LogException("Melding bewaren mislukt", ex);
        }
    }

    // Nieuwste eerst.
    internal static List<NotificationEntry> ReadRecent(string settingsDirectory, DateTimeOffset nowUtc, TimeSpan window, int max)
    {
        var result = new List<NotificationEntry>();
        var since = nowUtc - window;
        for (var day = 0; day <= (int)Math.Ceiling(window.TotalDays); day++)
        {
            var path = PathFor(settingsDirectory, nowUtc.AddDays(-day));
            if (!File.Exists(path)) continue;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<NotificationEntry>(line) is { } entry && entry.At >= since && entry.At <= nowUtc.AddMinutes(1)) result.Add(entry);
                }
                catch (JsonException)
                {
                    // Een half geschreven regel overslaan.
                }
            }
        }

        return result.OrderByDescending(e => e.At).Take(max).ToList();
    }

    private static string PathFor(string settingsDirectory, DateTimeOffset atUtc)
        => Path.Combine(settingsDirectory, FilePrefix + atUtc.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");

    private static void DeleteExpired(string settingsDirectory, DateTimeOffset nowUtc)
    {
        foreach (var path in Directory.EnumerateFiles(settingsDirectory, FilePrefix + "*.jsonl"))
        {
            var stamp = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
            if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day) && nowUtc.UtcDateTime - day > Retention)
            {
                File.Delete(path);
            }
        }
    }
}
