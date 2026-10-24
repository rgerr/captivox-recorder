using System.Security.Cryptography;
using System.Text.Json;

namespace LocalCaptureAgent.Services;

internal static class TranscriptOutputIntegrity
{
    public static bool IsComplete(string txtPath, string jsonPath, bool wantsTxt, bool wantsJson)
    {
        if (!wantsTxt && !wantsJson) return false;
        try
        {
            var directory = Path.GetDirectoryName(wantsTxt ? txtPath : jsonPath)!;
            using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "transcript.outputs.json")));
            var root = receipt.RootElement;
            if (root.GetProperty("schema").GetString() != "captivox-transcript-outputs-v1") return false;
            var outputs = root.GetProperty("outputs");
            foreach (var (kind, path, required) in new[] { ("txt", txtPath, wantsTxt), ("json", jsonPath, wantsJson) })
            {
                if (!required) continue;
                var entry = outputs.GetProperty(kind);
                var data = File.ReadAllBytes(path);
                if (entry.GetProperty("bytes").GetInt64() != data.LongLength ||
                    !string.Equals(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(data)), StringComparison.OrdinalIgnoreCase)) return false;
                if (kind == "json")
                {
                    using var transcript = JsonDocument.Parse(data);
                    if (transcript.RootElement.GetProperty("segments").ValueKind != JsonValueKind.Array) return false;
                    foreach (var segment in transcript.RootElement.GetProperty("segments").EnumerateArray())
                        if (segment.ValueKind != JsonValueKind.Object || segment.GetProperty("text").ValueKind != JsonValueKind.String) return false;
                }
            }
            return true;
        }
        catch { return false; }
    }
}
