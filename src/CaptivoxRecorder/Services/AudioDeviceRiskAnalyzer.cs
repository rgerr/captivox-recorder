using System.Text.RegularExpressions;

namespace LocalCaptureAgent.Services;

internal static class AudioDeviceRiskAnalyzer
{
    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.Compiled);

    public static bool IsSamePhysicalDeviceRisk(string? micName, string? systemName)
    {
        var normalizedMic = NormalizeFriendlyName(micName);
        var normalizedSystem = NormalizeFriendlyName(systemName);
        if (string.IsNullOrWhiteSpace(normalizedMic) || string.IsNullOrWhiteSpace(normalizedSystem))
        {
            return false;
        }

        if (string.Equals(normalizedMic, normalizedSystem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Microfoon en luidspreker van een speakerphone heten in Windows vaak verschillend, maar zijn hetzelfde apparaat.
        if (LooksLikeEchoCancelledSpeakerphone(normalizedMic) && LooksLikeEchoCancelledSpeakerphone(normalizedSystem))
        {
            return true;
        }

        return false;
    }

    public static string DetermineDeviceProfile(string? micName, string? systemName)
    {
        var combined = $"{micName} {systemName}";
        if (LooksLikeEchoCancelledSpeakerphone(combined))
        {
            return "speakerphone-echo-cancelled";
        }

        return "generic-headset";
    }

    public static string? BuildRiskWarning(string? micName, string? systemName)
    {
        if (!IsSamePhysicalDeviceRisk(micName, systemName))
        {
            return null;
        }

        if (LooksLikeEchoCancelledSpeakerphone($"{micName} {systemName}"))
        {
            return "Microfoon en systeemaudio wijzen naar dezelfde echo-onderdrukkende speakerphone. Dit vergroot het risico op Windows ducking, device-DSP, echo-cancellation en volumeregressies na stoppen van de opname.";
        }

        return "Microfoon en systeemaudio lijken naar hetzelfde fysieke device te wijzen. Dit kan leiden tot onverwachte device-DSP, ducking of scheve mixverhoudingen.";
    }

    public static string NormalizeFriendlyName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = normalized.Replace("(", " ").Replace(")", " ").Replace("-", " ");
        normalized = CollapseWhitespace.Replace(normalized, " ").Trim();
        return normalized;
    }

    private static bool LooksLikeEchoCancelledSpeakerphone(string? value)
    {
        var normalized = NormalizeFriendlyName(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("speakerphone", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("telefoon met luidspreker", StringComparison.OrdinalIgnoreCase) // geen vertaling
               || normalized.Contains("echo onderdrukkende", StringComparison.OrdinalIgnoreCase);
    }
}
