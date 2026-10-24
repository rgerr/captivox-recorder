using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class MeetingDetectionService
{
    private static readonly string[] NonMeetingTitleHints =
    {
        "chat", "agenda", "calendar", "activity", "activiteiten", "bestanden", "files", "settings", "instellingen", "home", "startpagina"
    };

    internal sealed class DetectionResult
    {
        public string AppKind { get; init; } = string.Empty;
        public string ProcessName { get; init; } = string.Empty;
        public string WindowTitle { get; init; } = string.Empty;
        public string IdentityKey { get; init; } = string.Empty;
        public string PromptText { get; init; } = string.Empty;
        public double RenderPeak { get; init; }
        public double CapturePeak { get; init; }
    }

    public static DetectionResult? TryDetect(MeetingDetectionSettings settings)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return null;
        }

        Process? process;
        try
        {
            process = Process.GetProcessById((int)processId);
        }
        catch
        {
            return null;
        }

        var processName = (process.ProcessName ?? string.Empty).Trim();
        var normalizedProcess = processName.ToLowerInvariant();
        var normalizedTitle = title.Trim().ToLowerInvariant();
        if (ContainsNonMeetingHints(normalizedTitle))
        {
            return null;
        }

        var audio = AudioActivityProbeService.CaptureSnapshot();
        if (!audio.HasMeaningfulAudio)
        {
            return null;
        }

        if (settings.DetectTeams && IsLikelyTeamsMeeting(normalizedProcess, normalizedTitle))
        {
            return BuildResult("teams", processName, title, audio);
        }

        if (settings.DetectZoom && IsLikelyZoomMeeting(normalizedProcess, normalizedTitle))
        {
            return BuildResult("zoom", processName, title, audio);
        }

        var browserLike = normalizedProcess is "chrome" or "msedge" or "firefox" or "brave" or "opera" || normalizedProcess.Contains("chrome") || normalizedProcess.Contains("edge") || normalizedProcess.Contains("firefox") || normalizedProcess.Contains("brave");
        if (settings.DetectBrowserMeetings && browserLike && IsLikelyBrowserMeeting(normalizedTitle))
        {
            return BuildResult("browser", processName, title, audio);
        }

        return null;
    }

    private static bool ContainsNonMeetingHints(string normalizedTitle)
        => NonMeetingTitleHints.Any(hint => normalizedTitle.Contains(hint, StringComparison.OrdinalIgnoreCase));

    private static bool IsLikelyTeamsMeeting(string normalizedProcess, string normalizedTitle)
    {
        if (!(normalizedProcess.Contains("teams") || normalizedTitle.Contains("microsoft teams")))
        {
            return false;
        }

        return normalizedTitle.Contains("meeting")
               || normalizedTitle.Contains("vergadering")
               || normalizedTitle.Contains("gesprek")
               || normalizedTitle.Contains("call")
               || normalizedTitle.Contains(" | microsoft teams")
               || normalizedTitle.Contains(" - microsoft teams");
    }

    private static bool IsLikelyZoomMeeting(string normalizedProcess, string normalizedTitle)
    {
        if (!(normalizedProcess.Contains("zoom") || normalizedTitle.Contains("zoom")))
        {
            return false;
        }

        return normalizedTitle.Contains("zoom meeting")
               || normalizedTitle.Contains("zoom workplace")
               || normalizedTitle.Contains("meeting")
               || normalizedTitle.Contains("vergadering");
    }

    private static bool IsLikelyBrowserMeeting(string normalizedTitle)
    {
        return normalizedTitle.Contains("google meet")
               || Regex.IsMatch(normalizedTitle, @"\bmeet\b") && normalizedTitle.Contains("google")
               || normalizedTitle.Contains("microsoft teams") && (normalizedTitle.Contains("meeting") || normalizedTitle.Contains("vergadering") || normalizedTitle.Contains("call"))
               || normalizedTitle.Contains("zoom") && normalizedTitle.Contains("meeting");
    }

    private static DetectionResult BuildResult(string appKind, string processName, string title, AudioActivityProbeService.AudioProbeSnapshot audio)
    {
        var label = appKind switch
        {
            "teams" => "Microsoft Teams",
            "zoom" => "Zoom",
            _ => "browsermeeting"
        };

        return new DetectionResult
        {
            AppKind = appKind,
            ProcessName = processName,
            WindowTitle = title,
            IdentityKey = $"{appKind}|{processName}|{NormalizeIdentityTitle(title)}".Trim(),
            PromptText = $"Meeting gedetecteerd via {label}. Gebruik de hoofdknop Opname starten om handmatig te beginnen.",
            RenderPeak = audio.RenderPeak,
            CapturePeak = audio.CapturePeak
        };
    }

    private static string NormalizeIdentityTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var normalized = title.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\b\d{1,2}:\d{2}\b", string.Empty);
        normalized = normalized.Replace("microsoft teams", string.Empty)
            .Replace("google meet", string.Empty)
            .Replace("zoom meeting", string.Empty)
            .Replace("zoom workplace", string.Empty);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim(' ', '-', '|');
        return normalized;
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }
}
