namespace LocalCaptureAgent.Models;

public sealed class EditionStateSnapshot
{
    public string State { get; init; } = "free";
    public string DisplayName { get; init; } = "Free";
    public string Summary { get; init; } = "Free-editie actief.";
    public string Detail { get; init; } = "Basisopname en basistranscriptie blijven beschikbaar.";
    public string Email { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string LicenseId { get; init; } = string.Empty;
    public string LicenseCodeHint { get; init; } = string.Empty;
    public DateTimeOffset? ValidUntilUtc { get; init; }
    public DateTimeOffset? LastValidatedAtUtc { get; init; }
    public DateTimeOffset? RevalidateAfterUtc { get; init; }
    public string? LastValidationError { get; init; }
    public bool PremiumActive { get; init; }
    public IReadOnlyList<string> EnabledFeatures { get; init; } = Array.Empty<string>();

    public bool IsFeatureEnabled(string feature)
        => EnabledFeatures.Any(x => string.Equals(x, feature, StringComparison.OrdinalIgnoreCase));
}
