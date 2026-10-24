using NAudio.CoreAudioApi;

namespace LocalCaptureAgent.Services;

internal static class AudioActivityProbeService
{
    internal readonly record struct AudioProbeSnapshot(double RenderPeak, double CapturePeak)
    {
        public bool HasMeaningfulAudio => RenderPeak >= 0.02 || CapturePeak >= 0.02 || (RenderPeak >= 0.008 && CapturePeak >= 0.003);
    }

    public static bool HasRecentMeetingAudio(double minimumPeak = 0.01)
        => CaptureSnapshot(minimumPeak).HasMeaningfulAudio;

    public static AudioProbeSnapshot CaptureSnapshot(double minimumPeak = 0.01)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var render = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var capture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            var renderPeak = SafePeak(render);
            var capturePeak = SafePeak(capture);
            return new AudioProbeSnapshot(renderPeak, capturePeak);
        }
        catch
        {
            return new AudioProbeSnapshot(0.0, 0.0);
        }
    }

    private static double SafePeak(MMDevice device)
    {
        try
        {
            return device.AudioMeterInformation.MasterPeakValue;
        }
        catch
        {
            return 0.0;
        }
    }
}
