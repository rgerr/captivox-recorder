namespace Captivox.Recorder.Models;

// Het instellingenbestand dat de recorder en de transcriptieworker allebei lezen
// (%LOCALAPPDATA%\CaptivoxRecorderFree\settings\application_settings.json).
public sealed class ApplicationSettings
{
    public UiSettings Ui { get; set; } = new();
    public WorkerSettings Worker { get; set; } = new();
}
