namespace LocalCaptureAgent.Services;

internal static class SessionArtifactPathService
{
    public const string ExportsDirectoryName = "exports";
    public const string DiagnosticsDirectoryName = "diagnostics";
    public const string AiDirectoryName = "ai";

    public static string EnsureExportsDirectory(string sessionDirectory) => EnsureSubDirectory(sessionDirectory, ExportsDirectoryName);
    public static string EnsureDiagnosticsDirectory(string sessionDirectory) => EnsureSubDirectory(sessionDirectory, DiagnosticsDirectoryName);
    public static string EnsureAiDirectory(string sessionDirectory) => EnsureSubDirectory(sessionDirectory, AiDirectoryName);

    public static string ExportsPath(string sessionDirectory, string fileName) => Path.Combine(sessionDirectory, ExportsDirectoryName, fileName);
    public static string DiagnosticsPath(string sessionDirectory, string fileName) => Path.Combine(sessionDirectory, DiagnosticsDirectoryName, fileName);
    public static string AiPath(string sessionDirectory, string fileName) => Path.Combine(sessionDirectory, AiDirectoryName, fileName);

    public static string ResolveTranscriptTextPath(string sessionDirectory)
        => ResolveFirstExisting(sessionDirectory, "transcript.txt", Path.Combine("transcripts", "transcript.txt"));

    public static string ResolveTranscriptJsonPath(string sessionDirectory)
        => ResolveFirstExisting(sessionDirectory, "transcript.json", Path.Combine("transcripts", "transcript.json"));

    public static string ResolveExportPath(string sessionDirectory, string fileName)
        => ResolveFirstExisting(sessionDirectory, Path.Combine(ExportsDirectoryName, fileName), fileName);

    public static string ResolveDiagnosticsPath(string sessionDirectory, string fileName)
        => ResolveFirstExisting(sessionDirectory, Path.Combine(DiagnosticsDirectoryName, fileName), fileName);

    public static string ResolveAiPath(string sessionDirectory, string fileName)
        => ResolveFirstExisting(sessionDirectory, Path.Combine(AiDirectoryName, fileName), fileName);

    public static string GetPreferredAiWritePath(string sessionDirectory, string fileName)
    {
        Directory.CreateDirectory(Path.Combine(sessionDirectory, AiDirectoryName));
        return Path.Combine(sessionDirectory, AiDirectoryName, fileName);
    }

    private static string EnsureSubDirectory(string sessionDirectory, string subDirectoryName)
    {
        var path = Path.Combine(sessionDirectory, subDirectoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveFirstExisting(string sessionDirectory, params string[] relativeCandidates)
    {
        foreach (var relative in relativeCandidates)
        {
            var candidate = Path.Combine(sessionDirectory, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(sessionDirectory, relativeCandidates.First());
    }
}
