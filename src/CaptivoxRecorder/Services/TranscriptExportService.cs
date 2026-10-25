using System.Text;
using System.Text.Json;

namespace LocalCaptureAgent.Services;

internal static class TranscriptExportService
{
    public static void EnsureAdjacentExports(string sessionDir)
    {
        var jsonPath = SessionArtifactPathService.ResolveTranscriptJsonPath(sessionDir);
        if (!SessionFiles.Exists(jsonPath))
        {
            return;
        }

        var srtPath = SessionArtifactPathService.ExportsPath(sessionDir, "transcript.srt");
        var vttPath = SessionArtifactPathService.ExportsPath(sessionDir, "transcript.vtt");
        ExportSrt(jsonPath, srtPath);
        ExportVtt(jsonPath, vttPath);
    }

    public static string? ReadTranscriptText(string sessionDir)
    {
        var txtPath = SessionArtifactPathService.ResolveTranscriptTextPath(sessionDir);
        if (SessionFiles.Exists(txtPath))
        {
            return SessionFiles.ReadAllText(txtPath);
        }

        var jsonPath = SessionArtifactPathService.ResolveTranscriptJsonPath(sessionDir);
        if (!SessionFiles.Exists(jsonPath))
        {
            return null;
        }

        var segments = ReadSegments(jsonPath);
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            var label = string.IsNullOrWhiteSpace(segment.Speaker) ? "Onbekend" : segment.Speaker;
            sb.AppendLine($"[{segment.Start:hh\\:mm\\:ss}] {label}");
            sb.AppendLine(segment.Text.Trim());
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public static void ExportSrt(string transcriptJsonPath, string destinationPath)
    {
        var segments = ReadSegments(transcriptJsonPath);
        var sb = new StringBuilder();
        var index = 1;
        foreach (var seg in segments)
        {
            sb.AppendLine(index.ToString());
            sb.AppendLine($"{FormatSrt(seg.Start)} --> {FormatSrt(seg.End)}");
            var speakerPrefix = string.IsNullOrWhiteSpace(seg.Speaker) ? string.Empty : $"[{seg.Speaker}] ";
            sb.AppendLine(speakerPrefix + seg.Text.Trim());
            sb.AppendLine();
            index++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        File.WriteAllText(destinationPath, sb.ToString(), Encoding.UTF8);
    }

    public static void ExportVtt(string transcriptJsonPath, string destinationPath)
    {
        var segments = ReadSegments(transcriptJsonPath);
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();
        foreach (var seg in segments)
        {
            sb.AppendLine($"{FormatVtt(seg.Start)} --> {FormatVtt(seg.End)}");
            var speakerPrefix = string.IsNullOrWhiteSpace(seg.Speaker) ? string.Empty : $"<v {seg.Speaker}>";
            sb.AppendLine(speakerPrefix + seg.Text.Trim());
            sb.AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        File.WriteAllText(destinationPath, sb.ToString(), Encoding.UTF8);
    }


    private static List<TranscriptSegment> ReadSegments(string transcriptJsonPath)
    {
        using var document = JsonDocument.Parse(SessionFiles.ReadAllText(transcriptJsonPath));
        if (!document.RootElement.TryGetProperty("segments", out var segmentsElement) || segmentsElement.ValueKind != JsonValueKind.Array)
        {
            return new List<TranscriptSegment>();
        }

        var segments = new List<TranscriptSegment>();
        foreach (var segment in segmentsElement.EnumerateArray())
        {
            var start = segment.TryGetProperty("start", out var startElement) && startElement.TryGetDouble(out var startValue) ? startValue : 0.0;
            var end = segment.TryGetProperty("end", out var endElement) && endElement.TryGetDouble(out var endValue) ? endValue : start;
            var text = segment.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
            var speaker = segment.TryGetProperty("speaker", out var speakerElement) ? speakerElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                segments.Add(new TranscriptSegment(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text, speaker));
            }
        }

        return segments;
    }

    private static string FormatSrt(TimeSpan ts) => $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
    private static string FormatVtt(TimeSpan ts) => $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";

    private sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text, string? Speaker);
}
