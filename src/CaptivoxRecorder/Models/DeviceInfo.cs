namespace Captivox.Recorder.Models;

public sealed class DeviceInfo
{
    public string Id { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }

    public override string ToString()
        => IsDefault ? $"{FriendlyName} (default)" : FriendlyName;
}
