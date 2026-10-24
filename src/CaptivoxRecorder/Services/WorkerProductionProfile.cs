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
        // De AMD-route alleen als de gebruiker die zelf koos. Eerdere versies zetten bij een AMD-kaart zelf amd-gpu; dat
        // wordt de nauwkeurige processorroute.
        if (settings.AmdGpuFastRoute)
        {
            settings.TranscriptionProfile = "amd-gpu";
        }
        else if (settings.TranscriptionProfile == "amd-gpu")
        {
            settings.TranscriptionProfile = "cpu-fallback";
        }
        settings.ComputeType = string.IsNullOrWhiteSpace(settings.ComputeType) ? DefaultComputeType : settings.ComputeType.Trim().ToLowerInvariant();
        settings.CpuWhisperXModel = string.IsNullOrWhiteSpace(settings.CpuWhisperXModel) ? WorkerSettings.DefaultCpuWhisperXModel : settings.CpuWhisperXModel.Trim();
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

    // Het WhisperX-model voor een transcriptieprofiel; zelfde keuze als Get-EffectiveWhisperXModel in de worker.
    public static string GetEffectiveWhisperXModel(WorkerSettings settings, string effectiveTranscriptionProfile)
    {
        var profile = HardwareAssessmentService.NormalizeTranscriptionProfile(effectiveTranscriptionProfile);
        if (profile == "amd-gpu") { return "ffmpeg-whisper-gpu"; }
        if (profile == "cpu-fallback")
        {
            return string.IsNullOrWhiteSpace(settings.CpuWhisperXModel) ? WorkerSettings.DefaultCpuWhisperXModel : settings.CpuWhisperXModel.Trim();
        }
        return settings.WhisperXModel;
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
        // De gratis versie: alleen de route en het model, in gewone taal (geen sprekerherkenning, geen profieltermen).
        var cpuModel = GetEffectiveWhisperXModel(settings, "cpu-fallback");
        return HardwareAssessmentService.NormalizeTranscriptionProfile(settings.TranscriptionProfile) switch
        {
            "amd-gpu" => $"Snel via de AMD-videokaart (FFmpeg Whisper met Vulkan), iets minder nauwkeurig. Lukt dat niet, dan op de processor met {cpuModel}.",
            "nvidia-cuda" => $"Via de NVIDIA-videokaart (CUDA) met {settings.WhisperXModel}. Werkt CUDA niet, dan op de processor met {cpuModel}.",
            "cpu-fallback" => $"Op de processor met {cpuModel} (int8), op lage prioriteit en met een begrensd aantal rekenkernen.",
            _ => $"Automatisch: met een NVIDIA-videokaart via CUDA ({settings.WhisperXModel}), anders op de processor met {cpuModel} (int8, lage prioriteit).",
        };
    }
}
