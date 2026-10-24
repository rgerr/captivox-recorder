namespace LocalCaptureAgent.Models;

public sealed class CaptureLevelsEventArgs : EventArgs
{
    public float MicPeak { get; init; }
    public float SystemPeak { get; init; }
    public TimeSpan SessionElapsed { get; init; }
}
