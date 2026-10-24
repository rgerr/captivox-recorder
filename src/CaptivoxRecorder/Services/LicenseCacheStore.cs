using System.Text;
using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class LicenseCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string GetLicenseCachePath()
        => Path.Combine(AppRuntime.GetSettingsDirectory(), "license_cache.json");

    public static string GetDeviceIdPath()
        => Path.Combine(AppRuntime.GetSettingsDirectory(), "device_id.txt");

    public static LicenseCacheRecord? Load()
    {
        try
        {
            var path = GetLicenseCachePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<LicenseCacheRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(LicenseCacheRecord record)
    {
        Directory.CreateDirectory(AppRuntime.GetSettingsDirectory());
        record.CachedAtUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(record, JsonOptions);
        File.WriteAllText(GetLicenseCachePath(), json, Encoding.UTF8);
    }

    public static void Clear()
    {
        var path = GetLicenseCachePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string GetOrCreateDeviceId()
    {
        Directory.CreateDirectory(AppRuntime.GetSettingsDirectory());
        var path = GetDeviceIdPath();
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        var deviceId = $"CVX-{Guid.NewGuid():N}".ToUpperInvariant();
        File.WriteAllText(path, deviceId, Encoding.UTF8);
        return deviceId;
    }

    public static string CreateRedactedSnapshotJson(EditionStateSnapshot snapshot)
    {
        var payload = new
        {
            state = snapshot.State,
            display_name = snapshot.DisplayName,
            summary = snapshot.Summary,
            detail = snapshot.Detail,
            email_masked = MaskEmail(snapshot.Email),
            device_id_suffix = snapshot.DeviceId.Length > 8 ? snapshot.DeviceId[^8..] : snapshot.DeviceId,
            license_id_masked = MaskIdentifier(snapshot.LicenseId),
            license_code_hint = string.IsNullOrWhiteSpace(snapshot.LicenseCodeHint) ? null : snapshot.LicenseCodeHint,
            valid_until_utc = snapshot.ValidUntilUtc,
            last_validated_at_utc = snapshot.LastValidatedAtUtc,
            revalidate_after_utc = snapshot.RevalidateAfterUtc,
            premium_active = snapshot.PremiumActive,
            enabled_features = snapshot.EnabledFeatures,
            last_validation_error = snapshot.LastValidationError,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string? MaskIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 8)
        {
            return $"***{trimmed[^Math.Min(4, trimmed.Length)..]}";
        }

        return $"{trimmed[..4]}***{trimmed[^4..]}";
    }

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return null;
        }

        var parts = email.Split('@', 2);
        var local = parts[0];
        var domain = parts[1];
        if (local.Length <= 2)
        {
            return $"{local[0]}***@{domain}";
        }

        return $"{local[..1]}***{local[^1]}@{domain}";
    }
}
