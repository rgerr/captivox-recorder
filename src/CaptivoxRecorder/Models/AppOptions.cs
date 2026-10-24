namespace LocalCaptureAgent.Models;

public sealed class AppOptions
{
    public string Command { get; init; } = "record";
    public string OutputRoot { get; init; } = Branding.DefaultRecordingsRoot;
    public string? MicDeviceId { get; init; }
    public string? SystemDeviceId { get; init; }
    public int OutputSampleRate { get; init; } = 16000;
    public int OutputChannels { get; init; } = 1;
    public bool WriteStems { get; init; } = false;
    public bool CreateReadyMarker { get; init; } = true;
    public bool DiagnosticsMode { get; init; } = false;
    public double MicGain { get; init; } = 1.0;
    public double SystemGain { get; init; } = 1.0;
    public int? DurationSeconds { get; init; }
    public string DeviceProfile { get; init; } = "generic-headset";
    public bool SameDeviceRisk { get; init; }

    // Quality profile intended to stay close to Meetily's recording philosophy:
    // preserve intelligibility, avoid clipping, prefer mic dominance during overlap,
    // and smooth asynchronous device behavior through windowed processing.
    public int AnalysisWindowMs { get; init; } = 50;
    public bool EnableAdaptiveDucking { get; init; } = true;
    public bool EnableMicNoiseGate { get; init; } = true;
    public bool EnableMicHighPassFilter { get; init; } = true;
    public bool EnableSystemHighPassFilter { get; init; } = false;
    public double DuckingFloorGain { get; init; } = 0.22;
    public double MicPresenceThreshold { get; init; } = 0.018;
    public double SystemPresenceThreshold { get; init; } = 0.012;
    public double NoiseGateThreshold { get; init; } = 0.010;
    public double NoiseGateCloseThreshold { get; init; } = 0.007;
    public double NoiseGateFloorGain { get; init; } = 0.10;
    public double LimiterThreshold { get; init; } = 0.92;
    public double FinalTargetPeak { get; init; } = 0.85;
    public double AttackMs { get; init; } = 12;
    public double ReleaseMs { get; init; } = 180;
    public double MicGateHoldMs { get; init; } = 140;
    public int HighPassCutoffHz { get; init; } = 80;

    // Synchronisation and shutdown hardening.
    public int JitterToleranceMs { get; init; } = 20;
    public int MaxTrailingSilenceMs { get; init; } = 250;
    public int StopTimeoutSeconds { get; init; } = 10;

    public static AppOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new AppOptions();
        }

        var command = args[0];
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            var value = (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                ? args[++i]
                : "true";
            dict[key] = value;
        }

        var legacyHighPass = GetOptionalBool(dict, "enable-high-pass-filter");

        return new AppOptions
        {
            Command = command,
            OutputRoot = Get(dict, "output-root", Branding.DefaultRecordingsRoot),
            MicDeviceId = GetOptional(dict, "mic-device-id"),
            SystemDeviceId = GetOptional(dict, "system-device-id"),
            OutputSampleRate = GetInt(dict, "output-sample-rate", 16000),
            OutputChannels = GetInt(dict, "output-channels", 1),
            WriteStems = GetBool(dict, "write-stems", false),
            CreateReadyMarker = true,
            DiagnosticsMode = GetBool(dict, "diagnostics-mode", false),
            MicGain = GetDouble(dict, "mic-gain", 1.0),
            SystemGain = GetDouble(dict, "system-gain", 1.0),
            DurationSeconds = GetOptionalInt(dict, "duration-seconds"),
            AnalysisWindowMs = GetInt(dict, "analysis-window-ms", 50),
            EnableAdaptiveDucking = GetBool(dict, "enable-adaptive-ducking", true),
            EnableMicNoiseGate = GetBool(dict, "enable-mic-noise-gate", true),
            EnableMicHighPassFilter = GetBool(dict, "enable-mic-high-pass-filter", legacyHighPass ?? true),
            EnableSystemHighPassFilter = GetBool(dict, "enable-system-high-pass-filter", legacyHighPass ?? false),
            DuckingFloorGain = GetDouble(dict, "ducking-floor-gain", 0.22),
            MicPresenceThreshold = GetDouble(dict, "mic-presence-threshold", 0.018),
            SystemPresenceThreshold = GetDouble(dict, "system-presence-threshold", 0.012),
            NoiseGateThreshold = GetDouble(dict, "noise-gate-threshold", 0.010),
            NoiseGateCloseThreshold = GetDouble(dict, "noise-gate-close-threshold", 0.007),
            NoiseGateFloorGain = GetDouble(dict, "noise-gate-floor-gain", 0.10),
            LimiterThreshold = GetDouble(dict, "limiter-threshold", 0.92),
            FinalTargetPeak = GetDouble(dict, "final-target-peak", 0.85),
            AttackMs = GetDouble(dict, "attack-ms", 12.0),
            ReleaseMs = GetDouble(dict, "release-ms", 180.0),
            MicGateHoldMs = GetDouble(dict, "mic-gate-hold-ms", 140.0),
            HighPassCutoffHz = GetInt(dict, "high-pass-cutoff-hz", 80),
            JitterToleranceMs = GetInt(dict, "jitter-tolerance-ms", 20),
            MaxTrailingSilenceMs = GetInt(dict, "max-trailing-silence-ms", 250),
            StopTimeoutSeconds = GetInt(dict, "stop-timeout-seconds", 10),
        };
    }

    private static string Get(Dictionary<string, string> dict, string key, string fallback)
        => dict.TryGetValue(key, out var value) ? value : fallback;

    private static string? GetOptional(Dictionary<string, string> dict, string key)
        => dict.TryGetValue(key, out var value) ? value : null;

    private static int GetInt(Dictionary<string, string> dict, string key, int fallback)
        => dict.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static int? GetOptionalInt(Dictionary<string, string> dict, string key)
        => dict.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : null;

    private static bool GetBool(Dictionary<string, string> dict, string key, bool fallback)
        => dict.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static bool? GetOptionalBool(Dictionary<string, string> dict, string key)
        => dict.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : null;

    private static double GetDouble(Dictionary<string, string> dict, string key, double fallback)
        => dict.TryGetValue(key, out var value) && double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
