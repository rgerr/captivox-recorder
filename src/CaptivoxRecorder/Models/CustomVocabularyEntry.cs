namespace Captivox.Recorder.Models;

public sealed class CustomVocabularyEntry
{
    public string Canonical { get; set; } = string.Empty;
    public List<string> Variants { get; set; } = new();
}
