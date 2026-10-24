using Captivox.Recorder.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Captivox.Recorder.Services;

public static class OfflineMixer
{
    public static MixMetrics CreateWhisperReadyMix(string micStemPath, string systemStemPath, string outputPath, AppOptions options)
    {
        var effectiveOptions = GetEffectiveOptions(options);
        var mic = LoadAsMonoFloat(micStemPath, effectiveOptions.OutputSampleRate);
        var system = LoadAsMonoFloat(systemStemPath, effectiveOptions.OutputSampleRate);

        if (effectiveOptions.EnableMicHighPassFilter)
        {
            ApplyHighPassInPlace(mic, effectiveOptions.OutputSampleRate, effectiveOptions.HighPassCutoffHz);
        }

        if (effectiveOptions.EnableSystemHighPassFilter)
        {
            ApplyHighPassInPlace(system, effectiveOptions.OutputSampleRate, effectiveOptions.HighPassCutoffHz);
        }

        var targetLength = Math.Max(mic.Length, system.Length);
        if (mic.Length != targetLength) Array.Resize(ref mic, targetLength);
        if (system.Length != targetLength) Array.Resize(ref system, targetLength);

        var windowSamples = Math.Max(1, (int)Math.Round(effectiveOptions.OutputSampleRate * (effectiveOptions.AnalysisWindowMs / 1000.0)));
        var mix = new float[targetLength];
        var metrics = new MixMetrics
        {
            DurationSeconds = targetLength / (double)effectiveOptions.OutputSampleRate,
            MicPeak = Peak(mic),
            SystemPeak = Peak(system),
            AnalysisWindowMs = effectiveOptions.AnalysisWindowMs,
            MicGateHoldMs = effectiveOptions.MicGateHoldMs,
            NoiseGateCloseThreshold = effectiveOptions.NoiseGateCloseThreshold
        };

        var attackCoeff = SmoothingCoefficient(effectiveOptions.AttackMs, effectiveOptions.OutputSampleRate, windowSamples);
        var releaseCoeff = SmoothingCoefficient(effectiveOptions.ReleaseMs, effectiveOptions.OutputSampleRate, windowSamples);
        var currentMicGain = (float)effectiveOptions.MicGain;
        var currentSystemGain = (float)effectiveOptions.SystemGain;
        double micGainAccumulator = 0;
        double systemGainAccumulator = 0;
        var windowCount = 0;
        var clippedEstimate = 0;
        float maxBeforeLimiter = 0;
        var gateHoldWindows = Math.Max(1, (int)Math.Ceiling(effectiveOptions.MicGateHoldMs / Math.Max(1.0, effectiveOptions.AnalysisWindowMs)));
        var remainingGateHoldWindows = 0;

        for (var start = 0; start < targetLength; start += windowSamples)
        {
            windowCount++;
            var end = Math.Min(targetLength, start + windowSamples);
            var micRms = Rms(mic, start, end);
            var systemRms = Rms(system, start, end);

            var desiredMicGain = (float)effectiveOptions.MicGain;
            var desiredSystemGain = (float)effectiveOptions.SystemGain;

            if (effectiveOptions.EnableMicNoiseGate)
            {
                if (micRms >= effectiveOptions.NoiseGateThreshold)
                {
                    remainingGateHoldWindows = gateHoldWindows;
                }
                else if (micRms <= effectiveOptions.NoiseGateCloseThreshold)
                {
                    remainingGateHoldWindows = Math.Max(0, remainingGateHoldWindows - 1);
                }

                if (micRms < effectiveOptions.NoiseGateThreshold)
                {
                    var relative = (micRms - effectiveOptions.NoiseGateCloseThreshold) /
                                   Math.Max(0.0001, effectiveOptions.NoiseGateThreshold - effectiveOptions.NoiseGateCloseThreshold);
                    relative = Math.Clamp(relative, 0.0, 1.0);
                    var expanderGain = Lerp((float)effectiveOptions.NoiseGateFloorGain, 1.0f, (float)relative);

                    if (remainingGateHoldWindows <= 0)
                    {
                        desiredMicGain *= expanderGain;
                        if (expanderGain < 0.995f)
                        {
                            metrics.NoiseGatedWindows++;
                        }
                    }
                }
            }

            if (effectiveOptions.EnableAdaptiveDucking && micRms >= effectiveOptions.MicPresenceThreshold && systemRms >= effectiveOptions.SystemPresenceThreshold)
            {
                var dominance = Math.Clamp((micRms - effectiveOptions.MicPresenceThreshold) / Math.Max(0.0001, 0.12 - effectiveOptions.MicPresenceThreshold), 0.0, 1.0);
                var targetScale = Lerp((float)effectiveOptions.SystemGain, (float)(effectiveOptions.SystemGain * effectiveOptions.DuckingFloorGain), (float)dominance);
                desiredSystemGain = Math.Min(desiredSystemGain, targetScale);
                desiredMicGain = Math.Max(desiredMicGain, (float)(effectiveOptions.MicGain * 1.03));
                metrics.DuckedWindows++;
            }

            currentMicGain = SmoothTowards(currentMicGain, desiredMicGain, desiredMicGain < currentMicGain ? attackCoeff : releaseCoeff);
            currentSystemGain = SmoothTowards(currentSystemGain, desiredSystemGain, desiredSystemGain < currentSystemGain ? attackCoeff : releaseCoeff);
            micGainAccumulator += currentMicGain;
            systemGainAccumulator += currentSystemGain;

            for (var i = start; i < end; i++)
            {
                var sample = (mic[i] * currentMicGain) + (system[i] * currentSystemGain);
                var abs = MathF.Abs(sample);
                if (abs > 1.0f)
                {
                    clippedEstimate++;
                }

                if (abs > maxBeforeLimiter)
                {
                    maxBeforeLimiter = abs;
                }

                mix[i] = sample;
            }
        }

        metrics.AverageMicGain = windowCount > 0 ? micGainAccumulator / windowCount : effectiveOptions.MicGain;
        metrics.AverageSystemGain = windowCount > 0 ? systemGainAccumulator / windowCount : effectiveOptions.SystemGain;
        metrics.EstimatedClippedSamplesBeforeLimiting = clippedEstimate;
        metrics.MixPeakBeforeLimiting = maxBeforeLimiter;

        ApplyLookaheadStyleLimiterInPlace(mix, (float)effectiveOptions.LimiterThreshold, effectiveOptions.OutputSampleRate);
        var postLimiterPeak = Peak(mix);
        metrics.MixPeakAfterLimiting = postLimiterPeak;

        if (postLimiterPeak > 0.0001f)
        {
            var finalScale = (float)Math.Min(1.0, effectiveOptions.FinalTargetPeak / postLimiterPeak);
            if (finalScale < 0.9999f)
            {
                ScaleInPlace(mix, finalScale);
                metrics.MixPeakAfterLimiting = Peak(mix);
            }
        }

        WritePcm16Mono(outputPath, mix, effectiveOptions.OutputSampleRate);
        return metrics;
    }

    private static AppOptions GetEffectiveOptions(AppOptions options)
    {
        if (!string.Equals(options.DeviceProfile, "speakerphone-echo-cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        return new AppOptions
        {
            Command = options.Command,
            OutputRoot = options.OutputRoot,
            MicDeviceId = options.MicDeviceId,
            SystemDeviceId = options.SystemDeviceId,
            OutputSampleRate = options.OutputSampleRate,
            OutputChannels = options.OutputChannels,
            WriteStems = options.WriteStems,
            CreateReadyMarker = options.CreateReadyMarker,
            DiagnosticsMode = options.DiagnosticsMode,
            MicGain = options.MicGain,
            SystemGain = options.SystemGain,
            DurationSeconds = options.DurationSeconds,
            DeviceProfile = options.DeviceProfile,
            SameDeviceRisk = options.SameDeviceRisk,
            AnalysisWindowMs = options.AnalysisWindowMs,
            EnableAdaptiveDucking = false,
            EnableMicNoiseGate = options.EnableMicNoiseGate,
            EnableMicHighPassFilter = options.EnableMicHighPassFilter,
            EnableSystemHighPassFilter = options.EnableSystemHighPassFilter,
            DuckingFloorGain = options.DuckingFloorGain,
            MicPresenceThreshold = Math.Min(options.MicPresenceThreshold, 0.012),
            SystemPresenceThreshold = Math.Max(options.SystemPresenceThreshold, 0.016),
            NoiseGateThreshold = Math.Min(options.NoiseGateThreshold, 0.0065),
            NoiseGateCloseThreshold = Math.Min(options.NoiseGateCloseThreshold, 0.0040),
            NoiseGateFloorGain = Math.Max(options.NoiseGateFloorGain, 0.32),
            LimiterThreshold = options.LimiterThreshold,
            FinalTargetPeak = options.FinalTargetPeak,
            AttackMs = options.AttackMs,
            ReleaseMs = options.ReleaseMs,
            MicGateHoldMs = Math.Max(options.MicGateHoldMs, 180),
            HighPassCutoffHz = options.HighPassCutoffHz,
            JitterToleranceMs = options.JitterToleranceMs,
            MaxTrailingSilenceMs = options.MaxTrailingSilenceMs,
            StopTimeoutSeconds = options.StopTimeoutSeconds,
        };
    }

    private static float[] LoadAsMonoFloat(string inputPath, int outputSampleRate)
    {
        using var reader = new WaveFileReader(inputPath);
        ISampleProvider provider = reader.ToSampleProvider();

        if (provider.WaveFormat.Channels != 1)
        {
            provider = new AveragingMonoSampleProvider(provider);
        }

        if (provider.WaveFormat.SampleRate != outputSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, outputSampleRate);
        }

        var buffer = new float[outputSampleRate * 4];
        var samples = new List<float>(outputSampleRate * 60);
        while (true)
        {
            var read = provider.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        return samples.ToArray();
    }

    private static void ApplyHighPassInPlace(float[] samples, int sampleRate, int cutoffHz)
    {
        if (samples.Length == 0 || cutoffHz <= 0)
        {
            return;
        }

        var dt = 1.0f / sampleRate;
        var rc = 1.0f / (2.0f * MathF.PI * cutoffHz);
        var alpha = rc / (rc + dt);

        var prevInput = samples[0];
        var prevOutput = samples[0];
        for (var i = 1; i < samples.Length; i++)
        {
            var input = samples[i];
            var output = alpha * (prevOutput + input - prevInput);
            samples[i] = output;
            prevInput = input;
            prevOutput = output;
        }
    }

    private static void ApplyLookaheadStyleLimiterInPlace(float[] samples, float threshold, int sampleRate)
    {
        if (samples.Length == 0)
        {
            return;
        }

        var attackCoeff = MathF.Exp(-1f / MathF.Max(1f, sampleRate * 0.0025f));
        var releaseCoeff = MathF.Exp(-1f / MathF.Max(1f, sampleRate * 0.080f));
        var env = 0f;
        var gain = 1f;

        for (var i = 0; i < samples.Length; i++)
        {
            var abs = MathF.Abs(samples[i]);
            env = abs > env
                ? (attackCoeff * env) + ((1 - attackCoeff) * abs)
                : (releaseCoeff * env) + ((1 - releaseCoeff) * abs);

            var desiredGain = env > threshold && env > 0f ? threshold / env : 1f;
            gain = desiredGain < gain
                ? (attackCoeff * gain) + ((1 - attackCoeff) * desiredGain)
                : (releaseCoeff * gain) + ((1 - releaseCoeff) * desiredGain);

            var shaped = samples[i] * gain;
            samples[i] = MathF.Tanh(shaped * 1.10f) / MathF.Tanh(1.10f);
        }
    }

    private static void WritePcm16Mono(string outputPath, float[] samples, int sampleRate)
    {
        var format = new WaveFormat(sampleRate, 16, 1);
        using var writer = new WaveFileWriter(outputPath, format);
        var buffer = new byte[Math.Max(2048, format.BlockAlign * 256)];
        var sampleIndex = 0;

        while (sampleIndex < samples.Length)
        {
            var samplesThisRound = Math.Min(buffer.Length / 2, samples.Length - sampleIndex);
            var byteIndex = 0;
            for (var i = 0; i < samplesThisRound; i++)
            {
                var value = (short)Math.Clamp((int)Math.Round(samples[sampleIndex + i] * short.MaxValue), short.MinValue, short.MaxValue);
                buffer[byteIndex++] = (byte)(value & 0xFF);
                buffer[byteIndex++] = (byte)((value >> 8) & 0xFF);
            }

            writer.Write(buffer, 0, byteIndex);
            sampleIndex += samplesThisRound;
        }
    }

    private static float Peak(float[] samples)
    {
        var max = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var abs = MathF.Abs(samples[i]);
            if (abs > max)
            {
                max = abs;
            }
        }
        return max;
    }

    private static double Rms(float[] samples, int start, int end)
    {
        if (end <= start)
        {
            return 0;
        }

        double sum = 0;
        for (var i = start; i < end; i++)
        {
            sum += samples[i] * samples[i];
        }
        return Math.Sqrt(sum / (end - start));
    }

    private static float SmoothTowards(float current, float target, float coefficient)
        => (coefficient * current) + ((1 - coefficient) * target);

    private static float SmoothingCoefficient(double ms, int sampleRate, int blockSamples)
    {
        var blocksPerMs = sampleRate / 1000.0 / Math.Max(1, blockSamples);
        var effectiveBlocks = Math.Max(1e-6, ms * blocksPerMs);
        return (float)Math.Exp(-1.0 / effectiveBlocks);
    }

    private static float Lerp(float a, float b, float t)
        => a + ((b - a) * t);

    private static void ScaleInPlace(float[] samples, float scale)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= scale;
        }
    }
}

internal sealed class AveragingMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _sourceBuffer;

    public AveragingMonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _sourceBuffer = new float[4096 * _channels];
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var sourceSamplesNeeded = count * _channels;
        if (_sourceBuffer.Length < sourceSamplesNeeded)
        {
            Array.Resize(ref _sourceBuffer, sourceSamplesNeeded);
        }

        var read = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
        var framesRead = read / _channels;
        for (var frame = 0; frame < framesRead; frame++)
        {
            float sum = 0;
            var baseIndex = frame * _channels;
            for (var ch = 0; ch < _channels; ch++)
            {
                sum += _sourceBuffer[baseIndex + ch];
            }
            buffer[offset + frame] = sum / _channels;
        }

        return framesRead;
    }
}
