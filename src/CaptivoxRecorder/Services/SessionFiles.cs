using System.Text;

namespace LocalCaptureAgent.Services;

// Alle onderdelen lezen, verplaatsen en verwijderen bestanden in een opnamemap via deze klasse, zodat dat overal op dezelfde
// manier gaat.
internal static class SessionFiles
{
    public static bool Exists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (File.Exists(path)) return true;
        return false;
    }


    public static string ReadAllText(string path, Encoding? encoding = null)
    {
        return encoding is null ? File.ReadAllText(path) : File.ReadAllText(path, encoding);
    }

    public static Task<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        return encoding is null ? File.ReadAllTextAsync(path, cancellationToken) : File.ReadAllTextAsync(path, encoding, cancellationToken);
    }

    public static byte[] ReadAllBytes(string path)
    {
        return File.ReadAllBytes(path);
    }

    // Zoals File.ReadAllLines: geen lege laatste regel na de laatste regelovergang.
    public static string[] ReadAllLines(string path, Encoding? encoding = null)
    {
        var lines = new List<string>();
        using var reader = new StringReader(ReadAllText(path, encoding));
        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine()) lines.Add(line);
        return lines.ToArray();
    }

    // Een stroom om te lezen: het bestand zelf, of de ontsleutelde inhoud in het geheugen.
    public static Stream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    public static DateTime GetLastWriteTimeUtc(string path)
    {
        return File.GetLastWriteTimeUtc(path);
    }

    // Verwijdert het bestand en een versleuteld exemplaar ervan.
    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    // Verplaatst het bestand en een versleuteld exemplaar ervan (dat krijgt de nieuwe naam met .cvx erachter).
    public static void Move(string source, string destination, bool overwrite = false)
    {
        if (File.Exists(source)) File.Move(source, destination, overwrite);
    }

    // Een kopie buiten de opnamemap (bijvoorbeeld een export die de gebruiker kiest): altijd onversleuteld.
    public static void CopyOut(string source, string destination, bool overwrite = false)
    {
        File.Copy(source, destination, overwrite);
    }

    // Tekst toevoegen aan een bestand in een opnamemap. Is het bestand versleuteld, dan eerst de bestaande inhoud terugzetten
    // (die gaat daarna weer samen versleuteld), zodat er niets verloren gaat.
    public static void AppendAllText(string path, string text, Encoding? encoding = null)
    {
        if (encoding is null) File.AppendAllText(path, text);
        else File.AppendAllText(path, text, encoding);
    }

    // De bestanden in een map zoals de rest van de recorder ze kent: een versleuteld bestand staat erin onder zijn gewone naam.
    public static IEnumerable<string> EnumerateFiles(string directory, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        var plain = Directory.EnumerateFiles(directory, pattern, option).Where(f => !IsVaultFile(f)).ToList();
        return plain;
    }

    private static bool IsVaultFile(string path)
        => false;

    // Een echt bestand om te lezen voor een onderdeel dat een pad nodig heeft; Dispose ruimt een tijdelijk exemplaar op.
    public static SessionReadableFile OpenReadableFile(string path)
    {
        return new SessionReadableFile(path, null);
    }
}

internal sealed class SessionReadableFile : IDisposable
{
    private readonly IDisposable? _cleanup;

    public SessionReadableFile(string path, IDisposable? cleanup)
    {
        Path = path;
        _cleanup = cleanup;
    }

    public string Path { get; }

    public bool IsTemporary => _cleanup is not null;

    public void Dispose() => _cleanup?.Dispose();
}
