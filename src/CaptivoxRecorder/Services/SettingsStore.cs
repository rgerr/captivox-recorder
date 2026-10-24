using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Captivox.Recorder.Models;

namespace Captivox.Recorder.Services;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    // Het Hugging Face-token staat versleuteld met Windows DPAPI: alleen deze gebruiker op deze pc kan het lezen.
    private static readonly byte[] HfTokenEntropy = Encoding.UTF8.GetBytes("CaptivoxRecorderFree::HF_TOKEN::v1");

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

    public static string GetTokenPath()
        => Path.Combine(AppRuntime.GetSecretsDirectory(), "hf_token.bin");

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
        settings.Worker.MinSpeakers = settings.Worker.MinSpeakers is > 0 ? Math.Min(settings.Worker.MinSpeakers.Value, 20) : null;
        settings.Worker.MaxSpeakers = settings.Worker.MaxSpeakers is > 0 ? Math.Min(settings.Worker.MaxSpeakers.Value, 20) : null;
        if (settings.Worker.MinSpeakers is int min && settings.Worker.MaxSpeakers is int max && max < min)
        {
            settings.Worker.MaxSpeakers = min;
        }

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

    public static bool HasToken() => File.Exists(GetTokenPath());

    public static void SaveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Het Hugging Face-token is leeg.");
        }

        var normalizedToken = token.Trim();
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(normalizedToken), HfTokenEntropy, DataProtectionScope.CurrentUser);
        var tokenPath = GetTokenPath();
        var tempPath = tokenPath + ".tmp";
        File.WriteAllBytes(tempPath, protectedBytes);
        try
        {
            if (!string.Equals(LoadTokenFromPath(tempPath), normalizedToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Het opgeslagen token kon niet worden teruggelezen.");
            }

            File.Move(tempPath, tokenPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public static bool TryLoadToken(out string? token, out string? error)
    {
        try
        {
            var path = GetTokenPath();
            if (!File.Exists(path))
            {
                token = null;
                error = "Geen opgeslagen Hugging Face-token gevonden.";
                return false;
            }

            token = LoadTokenFromPath(path);
            error = null;
            return !string.IsNullOrWhiteSpace(token);
        }
        catch (Exception ex)
        {
            token = null;
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static string LoadTokenFromPath(string path)
        => Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), HfTokenEntropy, DataProtectionScope.CurrentUser));

    public static void DeleteToken()
    {
        var path = GetTokenPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
