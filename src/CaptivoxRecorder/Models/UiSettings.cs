using System.Text.Json.Serialization;

namespace LocalCaptureAgent.Models;

public sealed class UiSettings
{
    // De taal van de opnames (transcriptie), zie LanguageProfileCatalog. Los van de taal van de interface hieronder.
    public string LanguageProfile { get; set; } = "nl";
    // De taal van de interface: en, nl, de of fr (zie UiText). Leeg in een instellingenbestand van voor deze instelling;
    // SettingsStore.Load maakt daar nl van (de interface was toen Nederlands) en voor een nieuwe installatie en (of de taal
    // die bij de installatie is gekozen).
    public string InterfaceLanguage { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = Branding.DefaultRecordingsRoot;
    public string? PreferredMicDeviceId { get; set; }
    public string? PreferredSystemDeviceId { get; set; }
    public double MicGain { get; set; } = 1.0;
    public double SystemGain { get; set; } = 1.0;
    public bool WriteStems { get; set; } = false;
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool CreateReadyMarker { get; set; } = true;
    public bool MinimizeToTray { get; set; } = false;
    public bool DiagnosticsMode { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public MonitoringSettings Monitoring { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public MeetingDetectionSettings MeetingDetection { get; set; } = new();
    public string LibrarySelectedContextFilter { get; set; } = string.Empty;
    public string LibrarySelectedSubcontextFilter { get; set; } = string.Empty;
    public string LibrarySelectedMeetingTypeFilter { get; set; } = string.Empty;
    public string LibrarySelectedTagFilter { get; set; } = string.Empty;
}
