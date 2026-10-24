namespace LocalCaptureAgent.Models;

public sealed class WorkerSettings
{
    public bool WhisperXEnabled { get; set; } = true;
    // De gratis versie herkent geen sprekers.
    public bool DiarizationEnabled { get; set; } = false;
    public bool UseProductionProfile { get; set; } = true;
    public string TranscriptionProfile { get; set; } = "auto";
    public string WhisperCppExe { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Branding.AppDataFolder, "runtime", "whispercpp", "whisper-cli.exe");
    public string WhisperCppModelPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Branding.AppDataFolder, "runtime", "whispercpp", "models", "ggml-large-v3-turbo-q5_0.bin");
    public string RuntimeMode { get; set; } = "python";
    public string PythonExe { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Branding.AppDataFolder, "runtime", ".venv", "Scripts", "python.exe");
    public string WhisperXModel { get; set; } = "large-v2";
    public string ComputeType { get; set; } = "float16";
    public string Language { get; set; } = "nl";
    public int BatchSize { get; set; } = 4;
    public int? MinSpeakers { get; set; } = 2;
    public int? MaxSpeakers { get; set; } = 4;
    public string TranscriptOutputFormat { get; set; } = "txt+json";
    public string DiarizationMode { get; set; } = "stable";
    public string DiarizationAcceleration { get; set; } = "auto";
    public string StableDiarizationModel { get; set; } = "pyannote/speaker-diarization-3.1";
    public string ExperimentalDiarizationModel { get; set; } = "pyannote/speaker-diarization-community-1";
    public string FfmpegPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Branding.AppDataFolder, "runtime", "ffmpeg", "ffmpeg.exe");
    public int PollIntervalSec { get; set; } = 5;
    public int MinReadyAgeSec { get; set; } = 2;
    public bool RetryFailedSessions { get; set; } = true;
    public int RetryStaleStartedAfterMinutes { get; set; } = 180;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryBackoffMinutes { get; set; } = 15;
    public bool CreateMarkers { get; set; } = true;
    public bool PreserveWorkerOutput { get; set; } = true;
    public RetentionSettings Retention { get; set; } = new();
    public ImportSettings Import { get; set; } = new();
    public AudioEnhancementSettings Enhancement { get; set; } = new();
    public List<CustomVocabularyEntry> CustomVocabulary { get; set; } = new();
    public bool ExportSrt { get; set; } = false;
    public bool ExportVtt { get; set; } = false;
    public List<string> KnownSessionRoots { get; set; } = new();
}
