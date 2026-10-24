
namespace LocalCaptureAgent.Models;

internal sealed record LanguageProfileOption(string Id, string DisplayName, string TranscriptionCode, string OutputLanguageName, string TranscriptLabel);

internal static class LanguageProfileCatalog
{
    private static readonly IReadOnlyList<LanguageProfileOption> Options = new List<LanguageProfileOption>
    {
        new("nl", "Nederlands", "nl", "Nederlands", "Transcript"),
        new("en", "English", "en", "English", "Transcript"),
        new("de", "Deutsch", "de", "Deutsch", "Transkript"),
        new("fr", "Français", "fr", "Français", "Transcription"),
        new("it", "Italiano", "it", "Italiano", "Trascrizione"),
        new("es", "Español", "es", "Español", "Transcripción"),
        new("zh", "中文", "zh", "中文", "转录内容"),
        new("ja", "日本語", "ja", "日本語", "文字起こし")
    };

    public static IReadOnlyList<LanguageProfileOption> GetOptions() => Options;

    public static string Normalize(string? profileId)
    {
        var candidate = (profileId ?? string.Empty).Trim().ToLowerInvariant();
        return Options.Any(x => x.Id == candidate) ? candidate : "nl";
    }

    public static string NormalizeFromWorkerLanguage(string? workerLanguage)
    {
        var candidate = (workerLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return Options.FirstOrDefault(x => x.TranscriptionCode == candidate)?.Id ?? Normalize(candidate);
    }

    public static string GetDisplayName(string? profileId)
        => GetOption(profileId).DisplayName;

    public static string GetTranscriptionLanguageCode(string? profileId)
        => GetOption(profileId).TranscriptionCode;

    public static string GetOutputLanguageName(string? profileId)
        => GetOption(profileId).OutputLanguageName;

    public static string GetTranscriptLabel(string? profileId)
        => GetOption(profileId).TranscriptLabel;

    public static LanguageProfileOption GetOption(string? profileId)
        => Options.FirstOrDefault(x => x.Id == Normalize(profileId)) ?? Options[0];
}
