using System.Text;
using System.Text.Json;
using LocalCaptureAgent.Models;


namespace LocalCaptureAgent.Services;

internal static partial class SettingsStore
{
    private static readonly string CurrentDefaultOutputRoot = Branding.DefaultRecordingsRoot;
    private static readonly string[] LegacyDefaultOutputRoots = new[] { @"C:\Meetily\recordings", @"C:\Meetily\Recordings" };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string GetSettingsPath()
        => Path.Combine(AppRuntime.GetSettingsDirectory(), "application_settings.json");

    public static ApplicationSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                var fresh = new ApplicationSettings();
                fresh.Ui.InterfaceLanguage = ReadInstallLanguage() ?? UiText.DefaultLanguage;
                // Een nieuwe installatie neemt standaard op in de taal van de interface (elke interfacetaal is ook een
                // transcriptietaal); wie in een andere taal vergadert, kiest dat onder Instellingen, Algemeen.
                fresh.Ui.LanguageProfile = LanguageProfileCatalog.Normalize(fresh.Ui.InterfaceLanguage);
                fresh.Worker.Language = LanguageProfileCatalog.GetTranscriptionLanguageCode(fresh.Ui.LanguageProfile);
                return fresh;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions) ?? new ApplicationSettings();
            if (string.IsNullOrWhiteSpace(settings.Ui?.InterfaceLanguage))
            {
                settings.Ui ??= new UiSettings();
                if (HasUiSection(json))
                {
                    // Geschreven door een eerdere versie: de interface was Nederlands en blijft dat.
                    settings.Ui.InterfaceLanguage = "nl";
                }
                else
                {
                    // Alleen de installatie schreef dit bestand (paden voor de transcriptie): dit is de eerste start, net als
                    // zonder bestand.
                    settings.Ui.InterfaceLanguage = ReadInstallLanguage() ?? UiText.DefaultLanguage;
                    settings.Ui.LanguageProfile = LanguageProfileCatalog.Normalize(settings.Ui.InterfaceLanguage);
                }
            }

            NormalizeSettings(settings);
            var normalizedJson = JsonSerializer.Serialize(settings, JsonOptions);
            if (!string.Equals(json.Trim(), normalizedJson.Trim(), StringComparison.Ordinal))
            {
                Directory.CreateDirectory(AppRuntime.GetSettingsDirectory());
                AtomicFileWriter.WriteAllText(path, normalizedJson, Encoding.UTF8);
            }
            return settings;
        }
        catch (Exception ex)
        {
            TryQuarantineBrokenSettings(ex);
            var settings = new ApplicationSettings();
            settings.Ui.InterfaceLanguage = ReadInstallLanguage() ?? UiText.DefaultLanguage;
            NormalizeSettings(settings);
            return settings;
        }
    }

    // Het installatieprogramma legt de taal van de installatie vast in dit bestand (alleen bij een nieuwe installatie).
    public const string InstallLanguageFileName = "install-language.txt";

    private static string? ReadInstallLanguage()
    {
        try
        {
            var path = Path.Combine(AppRuntime.GetSettingsDirectory(), InstallLanguageFileName);
            if (!File.Exists(path)) return null;
            var value = File.ReadAllText(path, Encoding.UTF8).Trim();
            return UiText.IsSupported(value) ? value.ToLowerInvariant() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Heeft het instellingenbestand een deel "ui"? Dat schrijft alleen Captivox zelf; de installatie schrijft alleen "worker".
    private static bool HasUiSection(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "ui", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    // Versie 1.0.0 van de gratis versie bewaarde een Hugging Face-token voor sprekerherkenning. De gratis versie gebruikt
    // het niet meer; het bestand wordt bij het starten opgeruimd.
    public static void RemoveLegacyToken()
    {
        try
        {
            var path = Path.Combine(AppRuntime.GetSecretsDirectory(), "hf_token.bin");
            if (File.Exists(path))
            {
                File.Delete(path);
                AppRuntime.LogMessage("Bewaard Hugging Face-token uit versie 1.0.0 verwijderd; de gratis versie gebruikt het niet meer.");
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Oud token verwijderen mislukt", ex);
        }
    }

    public static void Save(ApplicationSettings settings)
    {
        Directory.CreateDirectory(AppRuntime.GetSettingsDirectory());
        NormalizeSettings(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        AtomicFileWriter.WriteAllText(GetSettingsPath(), json, Encoding.UTF8);
    }

    private static void TryQuarantineBrokenSettings(Exception ex)
    {
        try
        {
            AppRuntime.LogException("Application settings konden niet worden geladen; defaults gebruikt en bestaande settings worden veiliggesteld", ex);
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return;
            }

            var directory = Path.GetDirectoryName(path) ?? AppRuntime.GetSettingsDirectory();
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var brokenPath = Path.Combine(directory, $"application_settings.broken-{timestamp}.json");
            if (File.Exists(brokenPath))
            {
                brokenPath = Path.Combine(directory, $"application_settings.broken-{timestamp}-{Guid.NewGuid():N}.json");
            }

            File.Move(path, brokenPath);
            var notePath = Path.ChangeExtension(brokenPath, ".txt");
            AtomicFileWriter.WriteAllText(notePath, $"Captivox kon application_settings.json niet laden en heeft dit bestand veiliggesteld.\nFout: {ex.GetType().Name}: {ex.Message}\n", Encoding.UTF8); // geen vertaling
        }
        catch (Exception quarantineEx)
        {
            AppRuntime.LogException("Application settings quarantine failed", quarantineEx);
        }
    }

    private static void NormalizeSettings(ApplicationSettings settings)
    {
        settings.Ui ??= new UiSettings();
        settings.Ui.LanguageProfile = string.IsNullOrWhiteSpace(settings.Ui.LanguageProfile)
            ? LanguageProfileCatalog.NormalizeFromWorkerLanguage(settings.Worker?.Language)
            : LanguageProfileCatalog.Normalize(settings.Ui.LanguageProfile);
        settings.Ui.InterfaceLanguage = UiText.Normalize(settings.Ui.InterfaceLanguage);
        settings.Worker ??= new WorkerSettings();
        settings.License ??= new LicenseSettings();
        settings.Hardware ??= new HardwareProfileSettings();
        settings.Worker.Retention ??= new RetentionSettings();
        settings.Ui.Monitoring ??= new MonitoringSettings();
        settings.Ui.Notifications ??= new NotificationSettings();
        settings.Ui.Notifications.RecordingFailure ??= NotificationEventSettings.WindowsEnabledByDefault();
        settings.Ui.MeetingDetection ??= new MeetingDetectionSettings();

        WorkerProductionProfile.Normalize(settings.Worker);
        settings.Worker.Retention.AudioRetentionDays = Math.Clamp(settings.Worker.Retention.AudioRetentionDays <= 0 ? 30 : settings.Worker.Retention.AudioRetentionDays, 1, 3650);
        settings.Worker.Retention.TranscriptRetentionDays = Math.Clamp(settings.Worker.Retention.TranscriptRetentionDays <= 0 ? 90 : settings.Worker.Retention.TranscriptRetentionDays, 1, 3650);
        settings.Worker.Retention.SessionRecordRetentionDays = Math.Clamp(settings.Worker.Retention.SessionRecordRetentionDays <= 0 ? 90 : settings.Worker.Retention.SessionRecordRetentionDays, settings.Worker.Retention.TranscriptRetentionDays, 3650);
        NormalizeOutputRoot(settings);
        settings.Worker.Import ??= new ImportSettings();
        if (!settings.Worker.Import.RequestedWatchFolderEnabled && settings.Worker.Import.WatchFolderEnabled)
        {
            settings.Worker.Import.RequestedWatchFolderEnabled = true;
        }
        settings.Worker.Import.MinimumStableAgeSeconds = Math.Clamp(settings.Worker.Import.MinimumStableAgeSeconds <= 0 ? 15 : settings.Worker.Import.MinimumStableAgeSeconds, 5, 3600);
        settings.Worker.Import.SupportedExtensions = (settings.Worker.Import.SupportedExtensions ?? new List<string> { ".m4a", ".mp3", ".wav", ".mp4", ".m4v", ".aac" })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().StartsWith('.') ? x.Trim().ToLowerInvariant() : "." + x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.Worker.Enhancement ??= new AudioEnhancementSettings();
        settings.Worker.CustomVocabulary ??= new List<CustomVocabularyEntry>();
        settings.Ui.CreateReadyMarker = true;
        settings.Ui.MeetingDetection.StabilitySeconds = Math.Clamp(settings.Ui.MeetingDetection.StabilitySeconds <= 0 ? 15 : settings.Ui.MeetingDetection.StabilitySeconds, 5, 120);
        settings.Ui.MeetingDetection.PromptCooldownMinutes = Math.Clamp(settings.Ui.MeetingDetection.PromptCooldownMinutes <= 0 ? 10 : settings.Ui.MeetingDetection.PromptCooldownMinutes, 1, 240);
        settings.Worker.CustomVocabulary = settings.Worker.CustomVocabulary
            .Where(x => !string.IsNullOrWhiteSpace(x.Canonical))
            .Select(x => new CustomVocabularyEntry
            {
                Canonical = x.Canonical.Trim(),
                Variants = (x.Variants ?? new List<string>())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        var mode = (settings.Worker.DiarizationMode ?? string.Empty).Trim().ToLowerInvariant();
        settings.Worker.DiarizationMode = mode is "stable" or "experimental" ? mode : "stable";
        settings.Worker.DiarizationAcceleration = WorkerProductionProfile.NormalizeDiarizationAcceleration(settings.Worker.DiarizationAcceleration);

        settings.Worker.Language = LanguageProfileCatalog.GetTranscriptionLanguageCode(settings.Ui.LanguageProfile);
        // De gratis versie herkent geen sprekers: de worker krijgt nooit de opdracht daartoe.
        settings.Worker.DiarizationEnabled = false;

        NormalizeKnownSessionRoots(settings);
    }

    private static List<string> CleanList(IEnumerable<string>? values, bool lowerCase)
        => (values ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => lowerCase ? x.Trim().ToLowerInvariant() : x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void NormalizeOutputRoot(ApplicationSettings settings)
    {
        var configured = string.IsNullOrWhiteSpace(settings.Ui.OutputRoot) ? CurrentDefaultOutputRoot : settings.Ui.OutputRoot.Trim();
        var normalizedConfigured = NormalizeRootPath(configured);

        if (IsLegacyDefaultOutputRoot(normalizedConfigured))
        {
            settings.Worker.KnownSessionRoots ??= new List<string>();
            if (!settings.Worker.KnownSessionRoots.Any(x => string.Equals(NormalizeRootPath(x), normalizedConfigured, StringComparison.OrdinalIgnoreCase)))
            {
                settings.Worker.KnownSessionRoots.Add(configured);
            }

            settings.Ui.OutputRoot = CurrentDefaultOutputRoot;
            return;
        }

        settings.Ui.OutputRoot = string.IsNullOrWhiteSpace(normalizedConfigured) ? CurrentDefaultOutputRoot : normalizedConfigured;
    }

    private static bool IsLegacyDefaultOutputRoot(string? path)
        => !string.IsNullOrWhiteSpace(path) && LegacyDefaultOutputRoots.Any(x => string.Equals(NormalizeRootPath(x), NormalizeRootPath(path), StringComparison.OrdinalIgnoreCase));

    private static string NormalizeRootPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static void NormalizeKnownSessionRoots(ApplicationSettings settings)
    {
        settings.Worker.RuntimeMode = "python";

        settings.Worker.KnownSessionRoots ??= new List<string>();
        var canonicalRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? candidate)
        {
            var normalized = NormalizeRootPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!canonicalRoots.ContainsKey(normalized))
            {
                canonicalRoots[normalized] = normalized;
            }
        }

        AddRoot(settings.Ui.OutputRoot);
        foreach (var root in settings.Worker.KnownSessionRoots)
        {
            AddRoot(root);
        }

        settings.Ui.OutputRoot = canonicalRoots.Keys.FirstOrDefault(x => string.Equals(x, NormalizeRootPath(settings.Ui.OutputRoot), StringComparison.OrdinalIgnoreCase))
            ?? (string.IsNullOrWhiteSpace(settings.Ui.OutputRoot) ? CurrentDefaultOutputRoot : NormalizeRootPath(settings.Ui.OutputRoot));

        settings.Worker.KnownSessionRoots = canonicalRoots.Values
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string CreateRedactedSettingsJson(ApplicationSettings settings)
    {
        var hardwareSettings = settings.Hardware ?? new HardwareProfileSettings();
        var clone = new ApplicationSettings
        {
            Ui = new UiSettings
            {
                LanguageProfile = settings.Ui.LanguageProfile,
                InterfaceLanguage = settings.Ui.InterfaceLanguage,
                OutputRoot = settings.Ui.OutputRoot,
                PreferredMicDeviceId = settings.Ui.PreferredMicDeviceId,
                PreferredSystemDeviceId = settings.Ui.PreferredSystemDeviceId,
                MicGain = settings.Ui.MicGain,
                SystemGain = settings.Ui.SystemGain,
                WriteStems = false,
                CreateReadyMarker = true,
                MinimizeToTray = settings.Ui.MinimizeToTray,
                DiagnosticsMode = settings.Ui.DiagnosticsMode,
                Monitoring = new MonitoringSettings
                {
                    HealthMonitorEnabled = settings.Ui.Monitoring.HealthMonitorEnabled,
                    SilenceDetectionEnabled = settings.Ui.Monitoring.SilenceDetectionEnabled,
                    SilenceThresholdPercent = settings.Ui.Monitoring.SilenceThresholdPercent,
                    MicSilenceWarningSeconds = settings.Ui.Monitoring.MicSilenceWarningSeconds,
                    SystemSilenceWarningSeconds = settings.Ui.Monitoring.SystemSilenceWarningSeconds,
                    BothSilenceWarningSeconds = settings.Ui.Monitoring.BothSilenceWarningSeconds,
                    WarningCooldownSeconds = settings.Ui.Monitoring.WarningCooldownSeconds,
                },
                Notifications = new NotificationSettings
                {
                    RecordingStoppedWaitingForTranscription = new NotificationEventSettings
                    {
                        WindowsEnabled = settings.Ui.Notifications.RecordingStoppedWaitingForTranscription.WindowsEnabled,
                    },
                    TranscriptReady = new NotificationEventSettings
                    {
                        WindowsEnabled = settings.Ui.Notifications.TranscriptReady.WindowsEnabled,
                    },
                    TranscriptionFailed = new NotificationEventSettings
                    {
                        WindowsEnabled = settings.Ui.Notifications.TranscriptionFailed.WindowsEnabled,
                    },
                    SilenceWarning = new NotificationEventSettings
                    {
                        WindowsEnabled = settings.Ui.Notifications.SilenceWarning.WindowsEnabled,
                    },
                    RecordingHealthWarning = new NotificationEventSettings
                    {
                        WindowsEnabled = settings.Ui.Notifications.RecordingHealthWarning.WindowsEnabled,
                    },
                    RecordingFailure = new NotificationEventSettings
                    {
                        WindowsEnabled = settings.Ui.Notifications.RecordingFailure.WindowsEnabled,
                    },
                },
                LibrarySelectedContextFilter = settings.Ui.LibrarySelectedContextFilter,
                LibrarySelectedSubcontextFilter = settings.Ui.LibrarySelectedSubcontextFilter,
                LibrarySelectedTagFilter = settings.Ui.LibrarySelectedTagFilter,
            },
            Worker = new WorkerSettings
            {
                WhisperXEnabled = settings.Worker.WhisperXEnabled,
                DiarizationEnabled = settings.Worker.DiarizationEnabled,
                UseProductionProfile = settings.Worker.UseProductionProfile,
                RuntimeMode = "python",
                PythonExe = settings.Worker.PythonExe,
                WhisperXModel = settings.Worker.WhisperXModel,
                CpuWhisperXModel = settings.Worker.CpuWhisperXModel,
                ComputeType = settings.Worker.ComputeType,
                Language = settings.Worker.Language,
                BatchSize = settings.Worker.BatchSize,
                MinSpeakers = settings.Worker.MinSpeakers,
                MaxSpeakers = settings.Worker.MaxSpeakers,
                TranscriptOutputFormat = settings.Worker.TranscriptOutputFormat,
                DiarizationMode = settings.Worker.DiarizationMode,
                StableDiarizationModel = settings.Worker.StableDiarizationModel,
                ExperimentalDiarizationModel = settings.Worker.ExperimentalDiarizationModel,
                FfmpegPath = settings.Worker.FfmpegPath,
                PollIntervalSec = settings.Worker.PollIntervalSec,
                MinReadyAgeSec = settings.Worker.MinReadyAgeSec,
                RetryFailedSessions = settings.Worker.RetryFailedSessions,
                RetryStaleStartedAfterMinutes = settings.Worker.RetryStaleStartedAfterMinutes,
                MaxRetryAttempts = settings.Worker.MaxRetryAttempts,
                RetryBackoffMinutes = settings.Worker.RetryBackoffMinutes,
                CreateMarkers = settings.Worker.CreateMarkers,
                PreserveWorkerOutput = settings.Worker.PreserveWorkerOutput,
                Retention = new RetentionSettings
                {
                    Enabled = settings.Worker.Retention.Enabled,
                    AudioRetentionDays = settings.Worker.Retention.AudioRetentionDays,
                    TranscriptRetentionDays = settings.Worker.Retention.TranscriptRetentionDays,
                    SessionRecordRetentionDays = settings.Worker.Retention.SessionRecordRetentionDays,
                },
                Import = new ImportSettings
                {
                    WatchFolderEnabled = settings.Worker.Import.WatchFolderEnabled,
                    RequestedWatchFolderEnabled = settings.Worker.Import.RequestedWatchFolderEnabled,
                    WatchFolders = settings.Worker.Import.WatchFolders?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                    ConsumeWatchFiles = settings.Worker.Import.ConsumeWatchFiles,
                    MinimumStableAgeSeconds = settings.Worker.Import.MinimumStableAgeSeconds,
                    SupportedExtensions = settings.Worker.Import.SupportedExtensions?.ToList() ?? new List<string>(),
                },
                Enhancement = new AudioEnhancementSettings
                {
                    Enabled = settings.Worker.Enhancement.Enabled,
                    ImportedAudioOnly = settings.Worker.Enhancement.ImportedAudioOnly,
                    Profile = settings.Worker.Enhancement.Profile,
                },
                CustomVocabulary = settings.Worker.CustomVocabulary?
                    .Select(x => new CustomVocabularyEntry { Canonical = x.Canonical, Variants = x.Variants?.ToList() ?? new List<string>() })
                    .ToList() ?? new List<CustomVocabularyEntry>(),
                ExportSrt = settings.Worker.ExportSrt,
                ExportVtt = settings.Worker.ExportVtt,
                KnownSessionRoots = settings.Worker.KnownSessionRoots?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            },
            Hardware = new HardwareProfileSettings
            {
                StartupDetectionCompleted = hardwareSettings.StartupDetectionCompleted,
                LastDetectedAtUtc = hardwareSettings.LastDetectedAtUtc,
                LastGpuVendor = hardwareSettings.LastGpuVendor,
                LastGpuName = hardwareSettings.LastGpuName,
                LastVramGb = hardwareSettings.LastVramGb,
                LastVramConfidence = hardwareSettings.LastVramConfidence,
                LastDetectionMethod = hardwareSettings.LastDetectionMethod,
                LastRecommendedTranscriptionProfile = hardwareSettings.LastRecommendedTranscriptionProfile,
                LastRecommendedModelTag = hardwareSettings.LastRecommendedModelTag,
                LastAdviceClass = hardwareSettings.LastAdviceClass,
                LastGpuRuntimeAvailable = hardwareSettings.LastGpuRuntimeAvailable,
                LastGpuRuntimeStatus = hardwareSettings.LastGpuRuntimeStatus,
                LastFallbackReason = hardwareSettings.LastFallbackReason
            }
        };

        return JsonSerializer.Serialize(clone, JsonOptions);
    }
}
