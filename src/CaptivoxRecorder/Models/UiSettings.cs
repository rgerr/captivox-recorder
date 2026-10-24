namespace Captivox.Recorder.Models;

public sealed class UiSettings
{
    // In het gebruikersprofiel en niet onder C:\ of Documenten: alleen deze gebruiker kan erbij en OneDrive
    // synchroniseert de map niet standaard. Opnames blijven op deze pc.
    public static readonly string DefaultOutputRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Captivox", "Opnames");

    public string OutputRoot { get; set; } = DefaultOutputRoot;
    public string? PreferredMicDeviceId { get; set; }
    public string? PreferredSystemDeviceId { get; set; }
    public double MicGain { get; set; } = 1.0;
    public double SystemGain { get; set; } = 1.0;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool DiagnosticsMode { get; set; } = false;
}
