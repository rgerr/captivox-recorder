namespace LocalCaptureAgent.Models;

public sealed class ApplicationSettings
{
    public UiSettings Ui { get; set; } = new();
    public WorkerSettings Worker { get; set; } = new();
    public LicenseSettings License { get; set; } = new();
    public HardwareProfileSettings Hardware { get; set; } = new();
}
