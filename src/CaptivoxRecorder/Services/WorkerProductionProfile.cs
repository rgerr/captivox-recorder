using Captivox.Recorder.Models;

namespace Captivox.Recorder.Services;

// De vaste, geteste route van de transcriptieworker. Met UseProductionProfile (standaard) worden afwijkende
// geavanceerde waarden bij het opslaan teruggezet; de worker kiest zelf NVIDIA (WhisperX/CUDA), AMD (FFmpeg
// Whisper via Vulkan) of de processor.
internal static class WorkerProductionProfile
{
    public const string DefaultComputeType = "float16";
    public const string DefaultDiarizationMode = "stable";
    public const string DefaultDiarizationAcceleration = "auto";
    public const string DefaultStableDiarizationModel = "pyannote/speaker-diarization-3.1";
    public const string DefaultExperimentalDiarizationModel = "pyannote/speaker-diarization-community-1";
    public const int DefaultPollIntervalSec = 5;
    public const int DefaultMinReadyAgeSec = 2;

    public static void Normalize(WorkerSettings settings)
    {
        settings.TranscriptionProfile = NormalizeTranscriptionProfile(settings.TranscriptionProfile);
        settings.ComputeType = string.IsNullOrWhiteSpace(settings.ComputeType) ? DefaultComputeType : settings.ComputeType.Trim().ToLowerInvariant();
        settings.WhisperCppExe = (settings.WhisperCppExe ?? string.Empty).Trim();
        settings.WhisperCppModelPath = (settings.WhisperCppModelPath ?? string.Empty).Trim();
        settings.DiarizationMode = string.IsNullOrWhiteSpace(settings.DiarizationMode) ? DefaultDiarizationMode : settings.DiarizationMode.Trim().ToLowerInvariant();
        settings.DiarizationAcceleration = NormalizeDiarizationAcceleration(settings.DiarizationAcceleration);
        settings.StableDiarizationModel = string.IsNullOrWhiteSpace(settings.StableDiarizationModel) ? DefaultStableDiarizationModel : settings.StableDiarizationModel.Trim();
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

    public static string NormalizeTranscriptionProfile(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "nvidia" or "nvidia-cuda" or "cuda" => "nvidia-cuda",
            "amd" or "amd-gpu" or "rocm" or "hip" => "amd-gpu",
            "cpu" or "cpu-fallback" => "cpu-fallback",
            _ => "auto"
        };
    }

    public static string GetTranscriptionProfileDisplayName(string? value)
        => NormalizeTranscriptionProfile(value) switch
        {
            "nvidia-cuda" => "NVIDIA-videokaart (CUDA)",
            "amd-gpu" => "AMD-videokaart (Vulkan)",
            "cpu-fallback" => "Alleen de processor",
            _ => "Automatisch"
        };

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

    public static string BuildSummary(ApplicationSettings settings)
    {
        var worker = settings.Worker;
        Normalize(worker);
        var speakers = worker.DiarizationEnabled ? "sprekers herkennen met pyannote 3.1" : "zonder sprekerherkenning";
        return $"Transcriptie: {GetTranscriptionProfileDisplayName(worker.TranscriptionProfile)}, model {worker.WhisperXModel}, taal {worker.Language}, {speakers}.";
    }
}
