using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Captivox.Recorder.Services;

internal sealed class ImportedRecordingMetadataInspection
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double? DurationSeconds { get; set; }
    public DateTimeOffset? EmbeddedRecordedAt { get; set; }
    public DateTimeOffset? FileCreatedAt { get; set; }
    public DateTimeOffset? FileModifiedAt { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
    public DateTimeOffset? OneDriveObservedAt { get; set; }
    public string ImportMode { get; set; } = "manual";
    public string ImportSourceProfile { get; set; } = "unknown";
    public string ImportSourceProfileReason { get; set; } = string.Empty;
    public string MetadataConfidence { get; set; } = "unknown";
    public string RecommendedRecordedAtSource { get; set; } = "none";
    public DateTimeOffset? RecommendedRecordedAt { get; set; }
    public string RecommendedRecordedAtConfidenceReason { get; set; } = string.Empty;
    public string ConfidenceReason { get; set; } = string.Empty;
}

internal static class ImportedRecordingMetadataService
{
    public static ImportedRecordingMetadataInspection Inspect(string sourcePath, string? ffmpegPath = null, DateTimeOffset? importedAt = null, bool fromWatchFolder = false)
    {
        var file = new FileInfo(sourcePath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Importbestand niet gevonden.", sourcePath);
        }

        var result = new ImportedRecordingMetadataInspection
        {
            FileName = file.Name,
            FilePath = file.FullName,
            FileCreatedAt = SafeFileTime(() => file.CreationTime),
            FileModifiedAt = SafeFileTime(() => file.LastWriteTime),
            ImportedAt = importedAt ?? DateTimeOffset.Now,
            ImportMode = fromWatchFolder ? "watch_folder" : "manual"
        };
        ApplyImportSourceProfile(result);

        TryReadFfprobe(file.FullName, ffmpegPath, result);
        ApplyRecommendation(result);
        return result;
    }

    private static void ApplyImportSourceProfile(ImportedRecordingMetadataInspection result)
    {
        var path = (result.FilePath ?? string.Empty).ToLowerInvariant();
        var name = (result.FileName ?? string.Empty).ToLowerInvariant();
        var extension = Path.GetExtension(result.FileName ?? string.Empty).ToLowerInvariant();
        var likelyOneDrive = path.Contains("onedrive", StringComparison.OrdinalIgnoreCase);
        var likelyVoiceMemo = extension is ".m4a" or ".mp4" or ".mov"
            || name.Contains("nieuwe opname", StringComparison.OrdinalIgnoreCase)
            || name.Contains("voice", StringComparison.OrdinalIgnoreCase)
            || name.Contains("memo", StringComparison.OrdinalIgnoreCase);

        if (likelyOneDrive && likelyVoiceMemo)
        {
            result.ImportSourceProfile = "ios_voice_memos_onedrive";
            result.ImportSourceProfileReason = "Bestand lijkt via OneDrive-watchfolder uit iOS Dictafoon/voice memo of vergelijkbare mobiele audiobron te komen.";
            result.OneDriveObservedAt = result.FileModifiedAt ?? result.FileCreatedAt ?? result.ImportedAt;
            return;
        }

        if (string.Equals(result.ImportMode, "manual", StringComparison.OrdinalIgnoreCase))
        {
            result.ImportSourceProfile = "manual_audio";
            result.ImportSourceProfileReason = "Bestand is handmatig geïmporteerd; filesystemtijden blijven ondersteunend maar niet automatisch doorslaggevend.";
            return;
        }

        result.ImportSourceProfile = "unknown";
        result.ImportSourceProfileReason = "Bronprofiel niet betrouwbaar herkend; behandel opnametijd en agenda-match als onzeker tenzij embedded metadata of handmatige bevestiging beschikbaar is.";
    }

    private static DateTimeOffset? SafeFileTime(Func<DateTime> getter)
    {
        try
        {
            var value = getter();
            return value == default ? null : new DateTimeOffset(value);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyRecommendation(ImportedRecordingMetadataInspection result)
    {
        var modifiedMinusDuration = EstimateStartFromModifiedAt(result);

        if (IsPlausibleRecordedTime(result.EmbeddedRecordedAt, result.ImportedAt))
        {
            result.MetadataConfidence = "high";
            result.RecommendedRecordedAtSource = "embedded_audio_metadata";
            result.RecommendedRecordedAt = result.EmbeddedRecordedAt;
            result.ConfidenceReason = "Interne audio metadata bevat een plausibele originele opnametijd.";
            result.RecommendedRecordedAtConfidenceReason = result.ConfidenceReason;
        }
        else if (IsPlausibleRecordedTime(result.FileCreatedAt, result.ImportedAt) && !IsLikelyImportOrSyncTime(result.FileCreatedAt, result.ImportedAt))
        {
            result.MetadataConfidence = "low";
            result.RecommendedRecordedAtSource = "filesystem_created_at_observed_untrusted";
            result.RecommendedRecordedAt = null;
            result.ConfidenceReason = "Filesystem aanmaaktijd is zichtbaar, maar wordt bij externe/iOS/OneDrive-import niet vertrouwd als originele opnamestart. Geen automatische agenda-match.";
            result.RecommendedRecordedAtConfidenceReason = result.ConfidenceReason;
        }
        else if (IsPlausibleRecordedTime(modifiedMinusDuration, result.ImportedAt) && !IsLikelyImportOrSyncTime(result.FileModifiedAt, result.ImportedAt))
        {
            result.MetadataConfidence = "low";
            result.RecommendedRecordedAtSource = "filesystem_modified_at_minus_duration_observed_untrusted";
            result.RecommendedRecordedAt = null;
            result.ConfidenceReason = "Filesystem wijzigingstijd minus duur is slechts een indicatie en wordt bij externe/iOS/OneDrive-import niet vertrouwd als originele opnamestart. Geen automatische agenda-match.";
            result.RecommendedRecordedAtConfidenceReason = result.ConfidenceReason;
        }
        else if (result.ImportedAt.HasValue)
        {
            result.MetadataConfidence = "low";
            result.RecommendedRecordedAtSource = "imported_at_fallback_only";
            result.RecommendedRecordedAt = null;
            result.ConfidenceReason = "Alleen import-/syncmoment of daarmee samenvallende filesystemtijd beschikbaar. Dit mag niet als harde agenda-matchtijd worden gebruikt.";
            result.RecommendedRecordedAtConfidenceReason = result.ConfidenceReason;
        }
        else
        {
            result.MetadataConfidence = "unknown";
            result.RecommendedRecordedAtSource = "none";
            result.RecommendedRecordedAt = null;
            result.ConfidenceReason = "Geen bruikbare tijdinformatie gevonden.";
            result.RecommendedRecordedAtConfidenceReason = result.ConfidenceReason;
        }
    }

    private static bool IsPlausibleRecordedTime(DateTimeOffset? candidate, DateTimeOffset? importedAt)
    {
        if (!candidate.HasValue)
        {
            return false;
        }

        var value = candidate.Value;
        if (value.Year < 2000 || value.Year > DateTimeOffset.Now.Year + 1)
        {
            return false;
        }

        if (importedAt.HasValue && value > importedAt.Value.AddMinutes(10))
        {
            return false;
        }

        return true;
    }

    private static bool IsLikelyImportOrSyncTime(DateTimeOffset? candidate, DateTimeOffset? importedAt)
    {
        if (!candidate.HasValue || !importedAt.HasValue)
        {
            return false;
        }

        return (importedAt.Value - candidate.Value).Duration() <= TimeSpan.FromMinutes(15);
    }

    private static DateTimeOffset? EstimateStartFromModifiedAt(ImportedRecordingMetadataInspection result)
    {
        if (!result.FileModifiedAt.HasValue || !result.DurationSeconds.HasValue || result.DurationSeconds.Value <= 0)
        {
            return null;
        }

        return result.FileModifiedAt.Value.AddSeconds(-result.DurationSeconds.Value);
    }

    private static void TryReadFfprobe(string path, string? ffmpegPath, ImportedRecordingMetadataInspection result)
    {
        var ffprobe = ResolveFfprobePath(ffmpegPath);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add("-print_format");
            psi.ArgumentList.Add("json");
            psi.ArgumentList.Add("-show_format");
            psi.ArgumentList.Add("-show_streams");
            psi.ArgumentList.Add(path);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return;
            }

            if (!process.WaitForExit(8000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var durationProp)
                    && double.TryParse(durationProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
                {
                    result.DurationSeconds = duration;
                }

                if (format.TryGetProperty("tags", out var tags))
                {
                    result.EmbeddedRecordedAt = TryReadTagDate(tags, "creation_time")
                        ?? TryReadTagDate(tags, "com.apple.quicktime.creationdate")
                        ?? TryReadTagDate(tags, "date");
                }
            }

            if (!result.EmbeddedRecordedAt.HasValue && root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (stream.TryGetProperty("tags", out var tags))
                    {
                        result.EmbeddedRecordedAt = TryReadTagDate(tags, "creation_time")
                            ?? TryReadTagDate(tags, "com.apple.quicktime.creationdate")
                            ?? TryReadTagDate(tags, "date");
                        if (result.EmbeddedRecordedAt.HasValue)
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
            // Metadata-inspectie is defensief. Import mag niet falen door ontbrekende ffprobe/metadata.
        }
    }

    private static string ResolveFfprobePath(string? ffmpegPath)
    {
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            var trimmed = ffmpegPath.Trim().Trim('"');
            var name = Path.GetFileName(trimmed);
            if (name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(Path.GetDirectoryName(trimmed) ?? string.Empty, "ffprobe.exe");
                if (File.Exists(candidate)) return candidate;
            }
            if (name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(trimmed))
            {
                return trimmed;
            }
        }

        return OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
    }

    private static DateTimeOffset? TryReadTagDate(JsonElement tags, string propertyName)
    {
        foreach (var property in tags.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var dto))
            {
                return dto;
            }
            if (DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out dto))
            {
                return dto;
            }
        }

        return null;
    }
}
