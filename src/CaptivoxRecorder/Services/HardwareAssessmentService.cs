using LocalCaptureAgent.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text;

namespace LocalCaptureAgent.Services;

internal sealed record HardwareAssessmentResult(
    string Summary,
    string Detail,
    string RecommendedModelTag,
    string AdviceClass,
    bool GpuDetected,
    bool IsReliable,
    string? GpuName,
    double VramGb,
    string DetectionMethod,
    DateTimeOffset DetectedAtUtc,
    bool UsedFallback,
    string GpuVendor,
    string VramConfidence,
    string RecommendedTranscriptionProfile,
    string TranscriptionProfileDisplayName,
    string TranscriptionProfileReason);

internal static partial class HardwareAssessmentService
{
    private const double WmiAdapterRamBoundaryGb = 3.75d;

    private static string DefaultRecommendedModel => string.Empty;

    public static HardwareAssessmentResult Detect(string? languageProfile = null)
    {
        // Het advies is interfacetekst: Nederlands als brontekst, het scherm toont het in de taal van de interface (UiText).
        // Het wordt ook in de instellingen bewaard, dus altijd in dezelfde taal. languageProfile blijft voor bestaande aanroepen.
        _ = languageProfile;
        var detectedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            var totalRamGb = TryReadTotalPhysicalMemoryGb();
            var gpu = TryReadBestGpu();

            var hasDiscreteGpu = gpu is not null && !string.IsNullOrWhiteSpace(gpu.Name);
            var vramGb = gpu?.AdapterRamGb ?? 0;
            var vendor = ResolveGpuVendor(gpu?.Name);
            var isReliable = hasDiscreteGpu && gpu!.IsReliable && vramGb > 0;
            var vramConfidence = !hasDiscreteGpu
                ? "none"
                : isReliable
                    ? "high"
                    : "low";
            // Zonder lokale AI is er geen model om aan te raden.
            var recommendedModel = string.Empty;
            string adviceClass;
            if (!hasDiscreteGpu || (isReliable && vramGb < 6))
            {
                adviceClass = "licht";
            }
            else if (!isReliable)
            {
                adviceClass = "onzeker";
            }
            else if (vramGb >= 16 && totalRamGb >= 24)
            {
                adviceClass = "zwaar";
            }
            else if (vramGb >= 10 && totalRamGb >= 16)
            {
                adviceClass = "gebalanceerd";
            }
            else
            {
                adviceClass = "licht";
            }

            var transcriptProfile = ResolveRecommendedTranscriptionProfile(vendor, hasDiscreteGpu);
            var transcriptProfileName = GetTranscriptionProfileDisplayName(transcriptProfile);
            var transcriptProfileReason = BuildTranscriptionProfileReason(vendor, hasDiscreteGpu, transcriptProfile);

            const string advicePrefix = "Hardware-inschatting";

            var summary = BuildSummary(advicePrefix, adviceClass, recommendedModel, isReliable, hasDiscreteGpu, transcriptProfileName);
            var detail = BuildDetail(gpu, totalRamGb, detectedAtUtc, vendor, vramConfidence, transcriptProfileName, transcriptProfileReason);

            return new HardwareAssessmentResult(
                summary,
                detail,
                recommendedModel,
                adviceClass,
                hasDiscreteGpu,
                isReliable,
                gpu?.Name,
                vramGb,
                gpu?.Source ?? ("niet gedetecteerd"),
                detectedAtUtc,
                gpu?.UsedFallback == true || !isReliable,
                vendor,
                vramConfidence,
                transcriptProfile,
                transcriptProfileName,
                transcriptProfileReason);
        }
        catch
        {
            return new HardwareAssessmentResult("Hardware-inschatting niet beschikbaar.", "Het systeem kon geen betrouwbare GPU- of geheugenschatting bepalen. Transcriptieprofiel valt terug op CPU.", DefaultRecommendedModel, "onbekend", false, false, null, 0, "fout", detectedAtUtc, true, "unknown", "none", "cpu-fallback", "CPU fallback", "Hardwaredetectie faalde; CPU fallback is het veilige transcriptieprofiel.");
        }
    }

    public static string NormalizeTranscriptionProfile(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "nvidia" or "nvidia-cuda" or "cuda" => "nvidia-cuda",
            "amd" or "amd-gpu" or "rocm" or "hip" => "amd-gpu",
            "cpu" or "cpu-fallback" => "cpu-fallback",
            _ => "auto"
        };
    }

    public static string GetTranscriptionProfileDisplayName(string? value)
    {
        return NormalizeTranscriptionProfile(value) switch
        {
            "nvidia-cuda" => "NVIDIA CUDA",
            "amd-gpu" => "AMD GPU",
            "cpu-fallback" => "CPU fallback",
            _ => "Automatisch"
        };
    }

    private static string BuildSummary(string advicePrefix, string adviceClass, string recommendedModel, bool isReliable, bool gpuDetected, string transcriptProfileName)
    {
        var profilePart = $" Transcriptieprofiel: {transcriptProfileName}.";

        if (string.IsNullOrWhiteSpace(recommendedModel))
        {
            return $"{advicePrefix}: {adviceClass}." + profilePart;
        }

        if (!gpuDetected)
        {
            return ($"{advicePrefix}: {adviceClass}. Aanbevolen download: {recommendedModel}.") + profilePart;
        }

        if (!isReliable)
        {
            return ($"{advicePrefix}: onzeker. Geen hard LLM-modeladvies; controleer het GPU-geheugen handmatig voordat je het actieve model wijzigt.") + profilePart;
        }

        return ($"{advicePrefix}: {adviceClass}. Aanbevolen download: {recommendedModel}.") + profilePart;
    }

    private static string BuildDetail(GpuInfo? gpu, double totalRamGb, DateTimeOffset detectedAtUtc, string vendor, string vramConfidence, string transcriptProfileName, string transcriptProfileReason)
    {
        var gpuLabel = gpu is not null
            ? $"GPU gevonden: {gpu.Name}"
            : ("Geen bruikbare GPU gedetecteerd");
        var vendorLabel = $"Vendor: {vendor}";
        var vramLabel = gpu is not null && gpu.AdapterRamGb > 0
            ? $"Dedicated VRAM: {gpu.AdapterRamGb:0.#} GB"
            : ("Dedicated VRAM niet beschikbaar");
        var ramLabel = totalRamGb > 0
            ? $"Systeemgeheugen: {totalRamGb:0.#} GB"
            : ("Systeemgeheugen niet beschikbaar");
        var methodLabel = gpu is not null
            ? $"Detectiemethode: {gpu.Source}"
            : ("Detectiemethode: geen");
        var confidenceLabel = $"VRAM-betrouwbaarheid: {vramConfidence}";
        var profileLabel = $"Automatisch transcriptieprofiel: {transcriptProfileName}";
        var timestampLabel = $"Laatste scan: {detectedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}";

        return string.Join(" · ", gpuLabel, vendorLabel, vramLabel, ramLabel, methodLabel, confidenceLabel, profileLabel, transcriptProfileReason, timestampLabel);
    }

    private static string BuildTranscriptionProfileReason(string vendor, bool gpuDetected, string transcriptProfile)
    {
        if (gpuDetected && vendor == "amd" && transcriptProfile == "cpu-fallback")
        {
            return "AMD-GPU gedetecteerd; Captivox schrijft standaard uit op de processor, dat is nauwkeuriger. De snelle route via de videokaart zet je aan onder Instellingen, Opnemen.";
        }

        if (!gpuDetected || transcriptProfile == "cpu-fallback")
        {
            return "Geen NVIDIA- of AMD-GPU gedetecteerd; CPU fallback is geselecteerd.";
        }

        if (transcriptProfile == "nvidia-cuda")
        {
            return "NVIDIA-GPU gedetecteerd; CUDA-profiel is geselecteerd en de helper valideert CUDA tijdens runtime.";
        }

        if (transcriptProfile == "amd-gpu")
        {
            return "AMD-GPU gedetecteerd; AMD GPU-profiel is geselecteerd. Captivox gebruikt FFmpeg Whisper GPU met Vulkan-validatie wanneer die runtime aanwezig is, anders volgt CPU fallback.";
        }

        return vendor;
    }

    private static string ResolveRecommendedTranscriptionProfile(string vendor, bool gpuDetected)
    {
        if (!gpuDetected)
        {
            return "cpu-fallback";
        }

        return vendor switch
        {
            "nvidia" => "nvidia-cuda",
            // AMD: standaard de nauwkeurige processorroute; de snelle videokaartroute is een keuze van de gebruiker.
            "amd" => "cpu-fallback",
            _ => "cpu-fallback"
        };
    }

    private static string ResolveGpuVendor(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
        {
            return "unknown";
        }

        var name = gpuName.Trim();
        if (name.Contains("nvidia", StringComparison.OrdinalIgnoreCase) || name.Contains("geforce", StringComparison.OrdinalIgnoreCase) || name.Contains("rtx", StringComparison.OrdinalIgnoreCase) || name.Contains("gtx", StringComparison.OrdinalIgnoreCase))
        {
            return "nvidia";
        }

        if (name.Contains("amd", StringComparison.OrdinalIgnoreCase) || name.Contains("radeon", StringComparison.OrdinalIgnoreCase) || name.Contains("rx ", StringComparison.OrdinalIgnoreCase))
        {
            return "amd";
        }

        if (name.Contains("intel", StringComparison.OrdinalIgnoreCase) || name.Contains("arc", StringComparison.OrdinalIgnoreCase) || name.Contains("iris", StringComparison.OrdinalIgnoreCase))
        {
            return "intel";
        }

        return "unknown";
    }

    private static double TryReadTotalPhysicalMemoryGb()
    {
        using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
        foreach (ManagementObject obj in searcher.Get())
        {
            if (obj["TotalPhysicalMemory"] is ulong bytes && bytes > 0)
            {
                return BytesToGb(bytes);
            }
        }

        return 0;
    }

    private static GpuInfo? TryReadBestGpu()
    {
        var candidates = new List<GpuInfo>();
        candidates.AddRange(TryReadNvidiaSmiGpus());
        candidates.AddRange(TryReadRegistryGpus());
        candidates.AddRange(TryReadWmiGpus());

        return candidates
            .Where(gpu => !IsIgnoredGpuName(gpu.Name))
            .OrderBy(gpu => GetDetectionPriority(gpu.Source))
            .ThenByDescending(gpu => gpu.AdapterRamGb)
            .ThenByDescending(gpu => gpu.IsReliable)
            .ThenBy(gpu => gpu.UsedFallback)
            .FirstOrDefault();
    }

    private static IEnumerable<GpuInfo> TryReadNvidiaSmiGpus()
    {
        var executable = ResolveNvidiaSmiPath();
        if (string.IsNullOrWhiteSpace(executable))
        {
            yield break;
        }

        var output = RunProcess(executable, "--query-gpu=name,memory.total --format=csv,noheader,nounits", TimeSpan.FromSeconds(3));
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var parts = line.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            if (double.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var memoryMiB) && memoryMiB > 0)
            {
                yield return new GpuInfo(parts[0], memoryMiB / 1024d, "nvidia-smi memory.total", true, false);
            }
        }
    }

    private static IEnumerable<GpuInfo> TryReadRegistryGpus()
    {
        using var videoRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
        if (videoRoot is null)
        {
            yield break;
        }

        foreach (var adapterKeyName in videoRoot.GetSubKeyNames())
        {
            using var adapterKey = videoRoot.OpenSubKey(adapterKeyName);
            if (adapterKey is null)
            {
                continue;
            }

            foreach (var subKeyName in adapterKey.GetSubKeyNames())
            {
                using var deviceKey = adapterKey.OpenSubKey(subKeyName);
                if (deviceKey is null)
                {
                    continue;
                }

                var name = ReadRegistryString(deviceKey, "HardwareInformation.AdapterString");
                var qwordMemoryBytes = ReadRegistryUInt64(deviceKey, "HardwareInformation.qwMemorySize");
                if (qwordMemoryBytes > 0)
                {
                    yield return new GpuInfo(ResolveGpuName(name, adapterKeyName), BytesToGb(qwordMemoryBytes), "registry HardwareInformation.qwMemorySize", true, false);
                    continue;
                }

                var dwordMemoryBytes = ReadRegistryUInt64(deviceKey, "HardwareInformation.MemorySize");
                if (dwordMemoryBytes > 0)
                {
                    var memoryGb = BytesToGb(dwordMemoryBytes);
                    var reliable = memoryGb < WmiAdapterRamBoundaryGb;
                    yield return new GpuInfo(ResolveGpuName(name, adapterKeyName), memoryGb, "registry HardwareInformation.MemorySize (legacy)", reliable, !reliable);
                }
            }
        }
    }

    private static IEnumerable<GpuInfo> TryReadWmiGpus()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
        foreach (ManagementObject obj in searcher.Get())
        {
            var name = Convert.ToString(obj["Name"])?.Trim();
            if (string.IsNullOrWhiteSpace(name) || IsIgnoredGpuName(name))
            {
                continue;
            }

            var adapterRamBytes = ReadManagementUInt64(obj["AdapterRAM"]);
            if (adapterRamBytes <= 0)
            {
                yield return new GpuInfo(name, 0, "WMI Win32_VideoController.AdapterRAM (geen VRAM-waarde)", false, true);
                continue;
            }

            var memoryGb = BytesToGb(adapterRamBytes);
            var reliable = memoryGb < WmiAdapterRamBoundaryGb;
            yield return new GpuInfo(name, memoryGb, "WMI Win32_VideoController.AdapterRAM (fallback)", reliable, !reliable);
        }
    }

    private static string? ResolveNvidiaSmiPath()
    {
        var pathOutput = RunProcess("where", "nvidia-smi", TimeSpan.FromSeconds(2));
        var pathFromWhere = pathOutput?
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(pathFromWhere))
        {
            return pathFromWhere;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var defaultPath = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private static string? RunProcess(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup only.
                }

                return null;
            }

            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveGpuName(string? registryName, string fallback)
        => string.IsNullOrWhiteSpace(registryName) ? fallback : registryName.Trim();

    private static string? ReadRegistryString(RegistryKey key, string valueName)
    {
        var value = key.GetValue(valueName);
        if (value is string text)
        {
            return text.Trim('\0', ' ', '\t', '\r', '\n');
        }

        if (value is byte[] bytes && bytes.Length > 0)
        {
            var unicode = Encoding.Unicode.GetString(bytes).Trim('\0', ' ', '\t', '\r', '\n');
            if (!string.IsNullOrWhiteSpace(unicode))
            {
                return unicode;
            }

            var ascii = Encoding.ASCII.GetString(bytes).Trim('\0', ' ', '\t', '\r', '\n');
            return string.IsNullOrWhiteSpace(ascii) ? null : ascii;
        }

        return null;
    }

    private static ulong ReadRegistryUInt64(RegistryKey key, string valueName)
        => ReadObjectUInt64(key.GetValue(valueName));

    private static ulong ReadManagementUInt64(object? value)
        => ReadObjectUInt64(value);

    private static ulong ReadObjectUInt64(object? value)
    {
        return value switch
        {
            null => 0,
            ulong ulongValue => ulongValue,
            long longValue when longValue > 0 => (ulong)longValue,
            uint uintValue => uintValue,
            int intValue when intValue > 0 => (uint)intValue,
            byte[] bytes when bytes.Length >= 8 => BitConverter.ToUInt64(bytes, 0),
            byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0),
            _ => 0,
        };
    }

    private static int GetDetectionPriority(string? source)
    {
        var value = (source ?? string.Empty).ToLowerInvariant();
        if (value.Contains("nvidia-smi", StringComparison.Ordinal)) return 0;
        if (value.Contains("qwmemorysize", StringComparison.Ordinal)) return 1;
        if (value.Contains("memorysize", StringComparison.Ordinal)) return 2;
        if (value.Contains("adapterram", StringComparison.Ordinal)) return 3;
        return 9;
    }

    private static bool IsIgnoredGpuName(string name)
        => name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Parsec", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("RDP", StringComparison.OrdinalIgnoreCase);

    private static double BytesToGb(ulong bytes)
        => bytes / 1024d / 1024d / 1024d;

    private sealed record GpuInfo(string Name, double AdapterRamGb, string Source, bool IsReliable, bool UsedFallback);
}
