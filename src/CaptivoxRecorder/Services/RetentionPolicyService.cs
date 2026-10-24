using System.Text.Json;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static class RetentionPolicyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RetentionRuntimeSnapshot? TryLoadRuntime()
    {
        var path = AppRuntime.GetRetentionRuntimeStatePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RetentionRuntimeSnapshot>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
