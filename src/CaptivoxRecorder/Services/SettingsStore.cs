using System.Text;
using System.Text.Json;
using Captivox.Recorder.Models;

namespace Captivox.Recorder.Services;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static readonly IReadOnlyList<(string Code, string Name)> Languages = new[]
    {
        ("nl", "Nederlands"),
        ("en", "Engels"),
        ("de", "Duits"),
        ("fr", "Frans"),
        ("es", "Spaans"),
        ("it", "Italiaans"),
    };

    public static readonly IReadOnlyList<(string Code, string Name)> Models = new[]
    {
        ("large-v2", "large-v2 (standaard, nauwkeurig)"),
        ("large-v3", "large-v3 (nauwkeurig)"),
        ("medium", "medium (sneller)"),
        ("small", "small (snelst, minder nauwkeurig)"),
    };

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
                Normalize(fresh);
                return fresh;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions) ?? new ApplicationSettings();
            Normalize(settings);
            return settings;
        }
        catch (Exception ex)
        {
            TryQuarantineBrokenSettings(ex);
            var settings = new ApplicationSettings();
            Normalize(settings);
            return settings;
        }
    }

    public static void Save(ApplicationSettings settings)
    {
        Normalize(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        AtomicFileWriter.WriteAllText(GetSettingsPath(), json, Encoding.UTF8);
    }

    private static void TryQuarantineBrokenSettings(Exception ex)
    {
        try
        {
            AppRuntime.LogException("Instellingen konden niet worden geladen; standaardwaarden gebruikt en het bestand is veiliggesteld", ex);
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return;
            }

            var brokenPath = Path.Combine(Path.GetDirectoryName(path)!, $"application_settings.broken-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            File.Move(path, brokenPath);
        }
        catch (Exception quarantineEx)
        {
            AppRuntime.LogException("Veiligstellen van kapotte instellingen mislukt", quarantineEx);
        }
    }

    private static void Normalize(ApplicationSettings settings)
    {
        settings.Ui ??= new UiSettings();
        settings.Worker ??= new WorkerSettings();
        settings.Worker.Retention ??= new RetentionSettings();
        settings.Worker.Import ??= new ImportSettings();
        settings.Worker.Enhancement ??= new AudioEnhancementSettings();
        settings.Worker.CustomVocabulary ??= new List<CustomVocabularyEntry>();

        WorkerProductionProfile.Normalize(settings.Worker);
        settings.Worker.RuntimeMode = "python";
        settings.Worker.Language = Languages.Any(x => x.Code == settings.Worker.Language) ? settings.Worker.Language : "nl";
        settings.Worker.WhisperXModel = Models.Any(x => x.Code == settings.Worker.WhisperXModel) ? settings.Worker.WhisperXModel : "large-v2";
        // Sprekerherkenning zit niet in de gratis versie.
        settings.Worker.DiarizationEnabled = false;
        settings.Worker.MinSpeakers = null;
        settings.Worker.MaxSpeakers = null;

        // De gratis versie bewaart alles; opruimen doet de gebruiker zelf.
        settings.Worker.Retention.Enabled = false;

        settings.Ui.OutputRoot = NormalizeRootPath(settings.Ui.OutputRoot);
        if (string.IsNullOrWhiteSpace(settings.Ui.OutputRoot))
        {
            settings.Ui.OutputRoot = UiSettings.DefaultOutputRoot;
        }

        settings.Ui.MicGain = Math.Clamp(settings.Ui.MicGain <= 0 ? 1.0 : settings.Ui.MicGain, 0.1, 4.0);
        settings.Ui.SystemGain = Math.Clamp(settings.Ui.SystemGain <= 0 ? 1.0 : settings.Ui.SystemGain, 0.1, 4.0);

        // De worker kijkt in alle mappen die ooit opslagmap waren, zodat een opname nooit blijft liggen na het wisselen.
        var roots = new List<string>();
        foreach (var root in new[] { settings.Ui.OutputRoot }.Concat(settings.Worker.KnownSessionRoots ?? new List<string>()))
        {
            var normalized = NormalizeRootPath(root);
            if (!string.IsNullOrWhiteSpace(normalized) && !roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(normalized);
            }
        }

        settings.Worker.KnownSessionRoots = roots;
    }

    private static string NormalizeRootPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    // Versie 1.0.0 kon een Hugging Face-token bewaren voor sprekerherkenning. Dat is niet meer nodig, dus weg ermee.
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
}
