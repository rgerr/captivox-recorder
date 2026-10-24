using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class WorkerProductionProfile
{
    public const string DefaultComputeType = "float16";
    public const string DefaultDiarizationMode = "stable";
    public const string DefaultDiarizationAcceleration = "auto";
    public const string DefaultStableDiarizationModel = "pyannote/speaker-diarization-3.1";
    public const string DefaultExperimentalDiarizationModel = "pyannote/speaker-diarization-community-1";
    public const string AmdCpuDiarizationModel = "pyannote/speaker-diarization-3.1";
    public const int DefaultPollIntervalSec = 5;
    public const int DefaultMinReadyAgeSec = 2;

    public static void Normalize(WorkerSettings settings)
    {
        settings.TranscriptionProfile = HardwareAssessmentService.NormalizeTranscriptionProfile(settings.TranscriptionProfile);
        settings.ComputeType = string.IsNullOrWhiteSpace(settings.ComputeType) ? DefaultComputeType : settings.ComputeType.Trim().ToLowerInvariant();
        settings.WhisperCppExe = string.IsNullOrWhiteSpace(settings.WhisperCppExe) ? settings.WhisperCppExe : settings.WhisperCppExe.Trim();
        settings.WhisperCppModelPath = string.IsNullOrWhiteSpace(settings.WhisperCppModelPath) ? settings.WhisperCppModelPath : settings.WhisperCppModelPath.Trim();
        settings.DiarizationMode = string.IsNullOrWhiteSpace(settings.DiarizationMode) ? DefaultDiarizationMode : settings.DiarizationMode.Trim().ToLowerInvariant();
        settings.DiarizationAcceleration = NormalizeDiarizationAcceleration(settings.DiarizationAcceleration);
        settings.StableDiarizationModel = string.IsNullOrWhiteSpace(settings.StableDiarizationModel) ? DefaultStableDiarizationModel : settings.StableDiarizationModel.Trim();
        if (string.Equals(settings.StableDiarizationModel, DefaultExperimentalDiarizationModel, StringComparison.OrdinalIgnoreCase))
        {
            settings.StableDiarizationModel = DefaultStableDiarizationModel;
        }
        settings.ExperimentalDiarizationModel = string.IsNullOrWhiteSpace(settings.ExperimentalDiarizationModel) ? DefaultExperimentalDiarizationModel : settings.ExperimentalDiarizationModel.Trim();
        settings.PollIntervalSec = settings.PollIntervalSec <= 0 ? DefaultPollIntervalSec : settings.PollIntervalSec;
        settings.MinReadyAgeSec = settings.MinReadyAgeSec < 0 ? DefaultMinReadyAgeSec : settings.MinReadyAgeSec;

        if (settings.UseProductionProfile)
        {
            settings.ComputeType = DefaultComputeType;
            settings.DiarizationMode = DefaultDiarizationMode;
            settings.DiarizationAcceleration = DefaultDiarizationAcceleration;
            settings.StableDiarizationModel = DefaultStableDiarizationModel;
            settings.PollIntervalSec = DefaultPollIntervalSec;
            settings.MinReadyAgeSec = DefaultMinReadyAgeSec;
        }
    }

    public static string GetEffectiveComputeType(WorkerSettings settings)
    {
        var profile = HardwareAssessmentService.NormalizeTranscriptionProfile(settings.TranscriptionProfile);
        if (profile == "amd-gpu") { return "ffmpeg-whisper"; }
        if (profile == "cpu-fallback") { return "int8"; }
        return settings.UseProductionProfile ? DefaultComputeType : settings.ComputeType;
    }

    public static string NormalizeDiarizationAcceleration(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? DefaultDiarizationAcceleration : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "cpu" => "cpu",
            "auto" => "auto",
            "amd-rocm" or "rocm" or "amd-rocm-experimental" => "amd-rocm-experimental",
            "nvidia-cuda" or "cuda" or "nvidia-cuda-experimental" => "nvidia-cuda-experimental",
            _ => DefaultDiarizationAcceleration
        };
    }

    public static string GetEffectiveDiarizationMode(WorkerSettings settings)
        => settings.UseProductionProfile ? DefaultDiarizationMode : settings.DiarizationMode;

    public static string GetEffectiveDiarizationAcceleration(WorkerSettings settings)
        => settings.UseProductionProfile ? DefaultDiarizationAcceleration : NormalizeDiarizationAcceleration(settings.DiarizationAcceleration);

    public static string GetEffectiveDiarizationModel(WorkerSettings settings)
        => GetEffectiveDiarizationModel(settings, HardwareAssessmentService.NormalizeTranscriptionProfile(settings.TranscriptionProfile));

    public static string GetEffectiveDiarizationModel(WorkerSettings settings, string effectiveTranscriptionProfile)
    {
        if (string.Equals(effectiveTranscriptionProfile, "amd-gpu", StringComparison.OrdinalIgnoreCase))
        {
            return AmdCpuDiarizationModel;
        }

        return GetEffectiveDiarizationMode(settings) == "experimental" ? settings.ExperimentalDiarizationModel : settings.StableDiarizationModel;
    }

    public static IReadOnlyList<string> GetDeviationMessages(WorkerSettings settings)
    {
        Normalize(settings);
        var messages = new List<string>();
        if (settings.UseProductionProfile)
        {
            return messages;
        }

        if (!string.Equals(settings.ComputeType, DefaultComputeType, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add($"compute={settings.ComputeType}");
        }

        if (!string.Equals(settings.DiarizationMode, DefaultDiarizationMode, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add($"diarization={settings.DiarizationMode}");
        }

        if (!string.Equals(settings.DiarizationAcceleration, DefaultDiarizationAcceleration, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add($"diarization-acceleration={settings.DiarizationAcceleration}");
        }

        if (string.Equals(settings.DiarizationMode, "stable", StringComparison.OrdinalIgnoreCase) && !string.Equals(settings.StableDiarizationModel, DefaultStableDiarizationModel, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add($"stable-model={settings.StableDiarizationModel}");
        }

        return messages;
    }

    public static string BuildSummary(ApplicationSettings settings)
        => BuildSummary(settings.Worker);

    public static string BuildSummary(WorkerSettings settings)
    {
        Normalize(settings);
        var normalizedProfile = HardwareAssessmentService.NormalizeTranscriptionProfile(settings.TranscriptionProfile);
        if (normalizedProfile == "amd-gpu")
        {
            return "Productieprofiel actief: transcriptieprofiel AMD GPU · engine FFmpeg Whisper GPU met Vulkan-validatie · compute ffmpeg-whisper · diarization via pyannote 3.1 met GPU-sidecar auto en CPU-fallback · poll " + DefaultPollIntervalSec + "s · READY-age " + DefaultMinReadyAgeSec + "s. Afwijkende geavanceerde waarden worden bij opslaan teruggezet naar het productieprofiel; hardwareprofiel blijft apart selecteerbaar.";
        }
        if (settings.UseProductionProfile)
        {
            return $"Productieprofiel actief: transcriptieprofiel {HardwareAssessmentService.GetTranscriptionProfileDisplayName(settings.TranscriptionProfile)} · NVIDIA gebruikt WhisperX/CUDA met {DefaultComputeType}; AMD gebruikt FFmpeg Whisper GPU via Vulkan-validatie met compute ffmpeg-whisper · standaarddiarization via pyannote 3.1 met GPU-sidecar auto en CPU-fallback · poll {DefaultPollIntervalSec}s · READY-age {DefaultMinReadyAgeSec}s. Afwijkende geavanceerde waarden worden bij opslaan teruggezet naar het productieprofiel; hardwareprofiel blijft apart selecteerbaar.";
        }

        var deviations = GetDeviationMessages(settings);
        if (deviations.Count == 0)
        {
            return $"Productieprofiel uitgeschakeld, maar de huidige waarden komen nog overeen met de standaardroute ({HardwareAssessmentService.GetTranscriptionProfileDisplayName(settings.TranscriptionProfile)} · {DefaultComputeType} + pyannote/speaker-diarization-3.1, diarization-acceleration auto).";
        }

        return $"Bewuste override van productieprofiel actief: profiel={HardwareAssessmentService.GetTranscriptionProfileDisplayName(settings.TranscriptionProfile)}, {string.Join(", ", deviations)}.";
    }
}
