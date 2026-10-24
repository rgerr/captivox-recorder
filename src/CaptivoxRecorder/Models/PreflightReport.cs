namespace LocalCaptureAgent.Models;

public sealed class PreflightReport
{
    public bool Success { get; init; }
    public bool HasWarnings { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    public string Details => string.Join(Environment.NewLine, Lines);
}
