namespace LocalCaptureAgent.Models;

public sealed class ImportSettings
{
    public bool WatchFolderEnabled { get; set; } = false;
    public bool RequestedWatchFolderEnabled { get; set; } = false;
    public List<string> WatchFolders { get; set; } = new();
    public bool ConsumeWatchFiles { get; set; } = true;
    public int MinimumStableAgeSeconds { get; set; } = 15;
    public List<string> SupportedExtensions { get; set; } = new() { ".m4a", ".mp3", ".wav", ".mp4", ".m4v", ".aac" };
}
