using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

// De transcriptie-omgeving (eigen Python met de worker-pakketten en eigen FFmpeg) richt de installatie (setup) in.
// Ontbreekt die of is hij stuk, dan krijgt de gebruiker overal dezelfde, begrijpelijke melding in plaats van een
// technische fout: herstellen = de installatie opnieuw starten.
internal static class TranscriptionRuntimeStatus
{
    public const string MissingMessage = "De transcriptie-omgeving ontbreekt of is niet compleet. Start de installatie (setup) opnieuw om dit te herstellen.";
    public const string ShortStatus = "Transcriptie-omgeving ontbreekt";

    // Teksten die de worker (captivox_transcription_worker.ps1) en zijn Python-helper schrijven als Python, de pakketten
    // of FFmpeg ontbreken of niet starten.
    private static readonly string[] RuntimeFailureMarkers =
    {
        "Python executable niet gevonden",
        "WhisperX helper script niet gevonden",
        "ffmpeg niet gevonden",
        "ffmpeg niet beschikbaar",
        "ffmpeg -version faalde",
        "WhisperX/pyannote import of bootstrap mislukt", // geen vertaling
        "Vereiste runtimepakketten ontbreken",
        "Import self-test mislukt",
        "No module named",
        "DLL load failed",
    };

    // Ontbreekt Python of FFmpeg op de ingestelde plek? Alleen bestandscontrole, dus goedkoop genoeg voor de statusregel.
    public static bool IsRuntimeMissing(WorkerSettings? worker)
    {
        if (worker is null || !worker.WhisperXEnabled) return false;
        return !ExecutableLooksPresent(worker.PythonExe) || !ExecutableLooksPresent(worker.FfmpegPath);
    }

    public static bool LooksLikeRuntimeFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return false;
        foreach (var marker in RuntimeFailureMarkers)
        {
            if (error.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // De melding voor de gebruiker: bij een ontbrekende of kapotte omgeving de vaste uitleg, anders de fout zelf.
    public static string ToUserMessage(string? error)
        => LooksLikeRuntimeFailure(error) ? MissingMessage : (error ?? string.Empty);

    private static bool ExecutableLooksPresent(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return false;
        var trimmed = configured.Trim().Trim('"');
        // Een kale naam ("ffmpeg") wordt via PATH gezocht; die kan hier niet goedkoop worden gecontroleerd.
        if (!trimmed.Contains(Path.DirectorySeparatorChar) && !trimmed.Contains(Path.AltDirectorySeparatorChar)) return true;
        try
        {
            return File.Exists(trimmed);
        }
        catch
        {
            return false;
        }
    }
}
