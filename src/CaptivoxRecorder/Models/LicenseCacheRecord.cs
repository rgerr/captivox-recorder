namespace LocalCaptureAgent.Models;

public sealed class LicenseCacheRecord
{
    public string Email { get; set; } = string.Empty;
    public string LicenseCodeHint { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string LicenseEdition { get; set; } = string.Empty;
    public string BoundDeviceId { get; set; } = string.Empty;
    public DateTimeOffset CachedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastValidatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public List<string> Entitlements { get; set; } = new();
    public string ValidationSource { get; set; } = "mock";
    public string? LastValidationError { get; set; }
}
