namespace LocalCaptureAgent.Models;

public sealed class AudioEnhancementSettings
{
    public bool Enabled { get; set; } = false;
    public bool ImportedAudioOnly { get; set; } = true;
    public string Profile { get; set; } = "light-clean";
}
