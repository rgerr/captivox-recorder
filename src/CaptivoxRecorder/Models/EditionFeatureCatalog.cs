namespace LocalCaptureAgent.Models;

public static class EditionFeatureCatalog
{
    public const string WatchFolders = "watch_folders";
    public const string Templates = "templates";
    public const string StructuredExportHooks = "structured_export_hooks";
    public const string MeetingChat = "meeting_chat";

    public static readonly IReadOnlyList<string> PremiumFeatures = new[]
    {
        WatchFolders,
        Templates,
        StructuredExportHooks,
        MeetingChat,
    };
}
