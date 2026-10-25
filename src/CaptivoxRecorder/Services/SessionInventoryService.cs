using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class SessionInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> GetSessionRoots(ApplicationSettings settings)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? candidate)
        {
            var normalized = NormalizeRoot(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
            {
                return;
            }

            if (!roots.ContainsKey(normalized))
            {
                roots[normalized] = normalized;
            }
        }

        AddRoot(settings.Ui.OutputRoot);
        foreach (var root in settings.Worker.KnownSessionRoots ?? new List<string>())
        {
            AddRoot(root);
        }

        return roots.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // De mappen waarvan de lijsten en het zoeken de opnames tonen. GetSessionRoots blijft alle mappen geven (de wachtrij en het
    // herstel na een onderbreking gelden voor alle opnames).
    public static IReadOnlyList<string> GetLibraryRoots(ApplicationSettings settings)
    {
        return GetSessionRoots(settings);
    }

    public static SessionQueueSnapshot ScanQueue(ApplicationSettings settings, int maxSessions = 250)
    {
        var snapshot = new SessionQueueSnapshot();
        var manifests = new List<(SessionManifest Manifest, string DirectoryPath, DateTime LastWriteUtc)>();
        var scannedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSessionRoots(settings))
        {
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root);
            }
            catch
            {
                continue;
            }

            foreach (var dir in directories)
            {
                var normalizedDir = NormalizeRoot(dir);
                if (string.IsNullOrWhiteSpace(normalizedDir) || !scannedDirectories.Add(normalizedDir))
                {
                    continue;
                }

                var manifestPath = Path.Combine(normalizedDir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<SessionManifest>(json, JsonOptions);
                    if (manifest is null)
                    {
                        continue;
                    }

                    manifests.Add((manifest, normalizedDir, Directory.GetLastWriteTimeUtc(normalizedDir)));
                }
                catch
                {
                }
            }
        }

        var ordered = manifests.OrderByDescending(x => x.LastWriteUtc).ToList();
        snapshot.ScannedSessionCount = ordered.Count;

        foreach (var item in ordered)
        {
            var transcriptionStatus = Normalize(item.Manifest.TranscriptionStatus);
            var recorderStatus = Normalize(item.Manifest.Status);
            var readyMarkerPath = Path.Combine(item.DirectoryPath, "READY");
            var claimPath = Path.Combine(item.DirectoryPath, ".transcription.claim");
            var hasReady = File.Exists(readyMarkerPath);
            var hasClaim = File.Exists(claimPath);
            var expectedOutputs = GetExpectedOutputs(item.DirectoryPath, item.Manifest.TranscriptOutputFormat, settings.Worker.TranscriptOutputFormat);
            var outputsExist = ExpectedOutputsExist(expectedOutputs);

            if (transcriptionStatus == "completed")
            {
                snapshot.CompletedCount++;
                continue;
            }

            if (transcriptionStatus is "failed" or "error")
            {
                snapshot.FailedCount++;
                continue;
            }

            if (transcriptionStatus == "started")
            {
                snapshot.ProcessingCount++;
                if (hasReady)
                {
                    snapshot.ReadyMarkerCount++;
                }
                if (hasClaim)
                {
                    snapshot.ClaimedCount++;
                }
                continue;
            }

            if (outputsExist)
            {
                snapshot.CompletedCount++;
                continue;
            }

            if (recorderStatus == "completed" && hasReady)
            {
                snapshot.ReadyMarkerCount++;
                if (hasClaim)
                {
                    snapshot.ProcessingCount++;
                    snapshot.ClaimedCount++;
                }
                else
                {
                    snapshot.PendingCount++;
                }
            }
        }

        foreach (var item in ordered.Take(Math.Max(1, maxSessions)))
        {
            snapshot.SessionManifests.Add(item.Manifest);
        }

        snapshot.GeneratedAt = DateTimeOffset.Now;
        return snapshot;
    }

    public static WorkerRuntimeSnapshot? TryLoadWorkerRuntime()
    {
        var path = AppRuntime.GetWorkerRuntimeStatePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WorkerRuntimeSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static (string? TxtPath, string? JsonPath, bool WantsTxt, bool WantsJson) GetExpectedOutputs(string sessionDir, string? manifestOutputFormat, string? defaultOutputFormat)
    {
        var format = (manifestOutputFormat ?? defaultOutputFormat ?? "txt+json").Trim().ToLowerInvariant();
        var txtPath = format is "txt" or "txt+json" ? SessionArtifactPathService.ResolveTranscriptTextPath(sessionDir) : null;
        var jsonPath = format is "json" or "txt+json" ? SessionArtifactPathService.ResolveTranscriptJsonPath(sessionDir) : null;
        return (txtPath, jsonPath, txtPath is not null, jsonPath is not null);
    }

    private static bool ExpectedOutputsExist((string? TxtPath, string? JsonPath, bool WantsTxt, bool WantsJson) expected)
    {
        var txtOk = !expected.WantsTxt || (expected.TxtPath is not null && File.Exists(expected.TxtPath));
        var jsonOk = !expected.WantsJson || (expected.JsonPath is not null && File.Exists(expected.JsonPath));
        return txtOk && jsonOk;
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizeRoot(string? path)
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
}
