using LocalCaptureAgent.Models;
using NAudio.CoreAudioApi;

namespace LocalCaptureAgent.Services;

internal static class AudioEndpointDiagnosticsService
{
    public static AudioEndpointSnapshot Capture(MMDevice? selectedMic, MMDevice? selectedSystem)
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultCaptureConsole = TryGetDefault(enumerator, DataFlow.Capture, Role.Console);
        var defaultCaptureMultimedia = TryGetDefault(enumerator, DataFlow.Capture, Role.Multimedia);
        var defaultCaptureCommunications = TryGetDefault(enumerator, DataFlow.Capture, Role.Communications);
        var defaultRenderConsole = TryGetDefault(enumerator, DataFlow.Render, Role.Console);
        var defaultRenderMultimedia = TryGetDefault(enumerator, DataFlow.Render, Role.Multimedia);
        var defaultRenderCommunications = TryGetDefault(enumerator, DataFlow.Render, Role.Communications);

        return new AudioEndpointSnapshot
        {
            CapturedAt = DateTimeOffset.Now,
            SelectedMicDeviceId = TryGetId(selectedMic),
            SelectedMicDeviceName = TryGetFriendlyName(selectedMic),
            SelectedMicDeviceState = TryGetState(selectedMic),
            SelectedMicIsDefaultConsole = Matches(selectedMic, defaultCaptureConsole),
            SelectedMicIsDefaultMultimedia = Matches(selectedMic, defaultCaptureMultimedia),
            SelectedMicIsDefaultCommunications = Matches(selectedMic, defaultCaptureCommunications),
            SelectedMicMasterVolumeScalar = TryGetMasterVolume(selectedMic),
            SelectedMicMuted = TryGetMuted(selectedMic),
            SelectedSystemDeviceId = TryGetId(selectedSystem),
            SelectedSystemDeviceName = TryGetFriendlyName(selectedSystem),
            SelectedSystemDeviceState = TryGetState(selectedSystem),
            SelectedSystemIsDefaultConsole = Matches(selectedSystem, defaultRenderConsole),
            SelectedSystemIsDefaultMultimedia = Matches(selectedSystem, defaultRenderMultimedia),
            SelectedSystemIsDefaultCommunications = Matches(selectedSystem, defaultRenderCommunications),
            SelectedSystemMasterVolumeScalar = TryGetMasterVolume(selectedSystem),
            SelectedSystemMuted = TryGetMuted(selectedSystem),
            DefaultCaptureConsoleName = TryGetFriendlyName(defaultCaptureConsole),
            DefaultCaptureMultimediaName = TryGetFriendlyName(defaultCaptureMultimedia),
            DefaultCaptureCommunicationsName = TryGetFriendlyName(defaultCaptureCommunications),
            DefaultRenderConsoleName = TryGetFriendlyName(defaultRenderConsole),
            DefaultRenderMultimediaName = TryGetFriendlyName(defaultRenderMultimedia),
            DefaultRenderCommunicationsName = TryGetFriendlyName(defaultRenderCommunications)
        };
    }

    private static MMDevice? TryGetDefault(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try { return enumerator.GetDefaultAudioEndpoint(flow, role); }
        catch { return null; }
    }

    private static string? TryGetId(MMDevice? device)
    {
        if (device is null) return null;
        try { return device.ID; }
        catch { return null; }
    }

    private static string? TryGetFriendlyName(MMDevice? device)
    {
        if (device is null) return null;
        try { return device.FriendlyName; }
        catch { return null; }
    }

    private static string? TryGetState(MMDevice? device)
    {
        if (device is null) return null;
        try { return device.State.ToString(); }
        catch { return null; }
    }

    private static bool Matches(MMDevice? selected, MMDevice? defaultDevice)
    {
        var selectedId = TryGetId(selected);
        var defaultId = TryGetId(defaultDevice);
        return !string.IsNullOrWhiteSpace(selectedId)
            && !string.IsNullOrWhiteSpace(defaultId)
            && string.Equals(selectedId, defaultId, StringComparison.OrdinalIgnoreCase);
    }

    private static float? TryGetMasterVolume(MMDevice? device)
    {
        if (device is null) return null;
        try { return device.AudioEndpointVolume.MasterVolumeLevelScalar; }
        catch { return null; }
    }

    private static bool? TryGetMuted(MMDevice? device)
    {
        if (device is null) return null;
        try { return device.AudioEndpointVolume.Mute; }
        catch { return null; }
    }
}
