namespace LocalCaptureAgent.Models;

public sealed class PreflightReport
{
    public bool Success { get; init; }
    public bool HasWarnings { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
    // Korte, gewone zinnen voor de gebruiker: alleen wat echt aandacht vraagt. De technische regels (Lines) gaan naar het logboek.
    public IReadOnlyList<string> UserWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UserErrors { get; init; } = Array.Empty<string>();

    public string Details => string.Join(Environment.NewLine, Lines);
}
