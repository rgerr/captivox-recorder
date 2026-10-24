using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Captivox.Recorder.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Captivox.Recorder.Services;

public sealed class AudioCaptureSession : IDisposable
{
    private readonly AppOptions _options;
    private readonly Stopwatch _sessionClock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly StringBuilder _sessionLog = new();
    private readonly object _stateSync = new();

    private WasapiCapture? _micCapture;
    private WasapiLoopbackCapture? _systemCapture;
    private GapAwareStemWriter? _micWriter;
    private GapAwareStemWriter? _systemWriter;
    private TaskCompletionSource<bool>? _micStopped;
    private TaskCompletionSource<bool>? _systemStopped;
    private MMDevice? _micDevice;
    private MMDevice? _systemDevice;
    private SessionManifest? _manifest;
    // v8.18.0: een gestorven capture-thread bleef alleen in het sessielogboek staan. Op
    // 2026-09-08 stopten beide threads om 15:37 en merkte niemand dat pas om 21:06, toen de
    // gebruiker zelf op stop drukte. De UI-monitor leest deze twee nu elke tick.
    public bool CaptureFailed { get; private set; }
    public string? CaptureFailureSummary { get; private set; }

    // v8.19.0: sinds v8.17.0 gooit de stemwriter niet meer op de 4 GiB-grens maar kapt hij stil
    // af. De capture leeft dan door, de niveaus blijven binnenkomen en CaptureFailed blijft
    // false, terwijl alle nieuwe audio verdwijnt. Precies het scenario van 8 september, dus
    // de monitor moet dit apart zien, en liefst ruim voordat het gebeurt.
    public bool CapacityReached
        => (_micWriter?.CapacityReached ?? false) || (_systemWriter?.CapacityReached ?? false);

    public TimeSpan? RemainingCapacity
    {
        get
        {
            var mic = _micWriter?.RemainingCapacity;
            var system = _systemWriter?.RemainingCapacity;
            if (mic is null || system is null)
            {
                return mic ?? system;
            }

            return mic < system ? mic : system;
        }
    }

    private Exception? _captureFailure;
    private string? _captureFailureCode;
    private bool _stopRequested;
    private bool _disposed;
    private long _lastLevelPublishTick;
    private long _lastHeartbeatTick;
    private float _lastMicPeak;
    private float _lastSystemPeak;

    public AudioCaptureSession(AppOptions options)
    {
        _options = options;
    }

    public event EventHandler<CaptureLevelsEventArgs>? LevelsUpdated;

    private string? GetManifestPath()
        => _manifest is null ? null : Path.Combine(_manifest.OutputDirectory, "manifest.json");

    private string? GetSessionLogPath()
        => _manifest is null ? null : Path.Combine(_manifest.OutputDirectory, "session.log");

    private void RegisterActiveRecordingIfPossible()
    {
        var manifestPath = GetManifestPath();
        var sessionLogPath = GetSessionLogPath();
        if (!string.IsNullOrWhiteSpace(manifestPath) && !string.IsNullOrWhiteSpace(sessionLogPath))
        {
            AppRuntime.RegisterActiveRecording(manifestPath, sessionLogPath);
        }
    }

    private void ClearActiveRecordingIfPossible()
    {
        var manifestPath = GetManifestPath();
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            AppRuntime.ClearActiveRecording(manifestPath);
        }
    }

    public async Task<SessionManifest> StartAsync()
    {
        EnsureNotDisposed();
        _micDevice = DeviceCatalog.GetCaptureDevice(_options.MicDeviceId);
        _systemDevice = DeviceCatalog.GetRenderDevice(_options.SystemDeviceId);

        var meetingId = $"meeting-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}";
        var outputDirectory = Path.Combine(_options.OutputRoot, meetingId);
        Directory.CreateDirectory(outputDirectory);

        _manifest = new SessionManifest
        {
            MeetingId = meetingId,
            CreatedAt = DateTimeOffset.Now,
            MicDevice = _micDevice.FriendlyName,
            SystemDevice = _systemDevice.FriendlyName,
            SampleRateOutput = _options.OutputSampleRate,
            ChannelsOutput = _options.OutputChannels,
            OutputDirectory = outputDirectory,
            OutputFile = Path.Combine(outputDirectory, "audio_mix.wav"),
            MicStem = _options.WriteStems ? Path.Combine(outputDirectory, "mic.wav") : Path.Combine(outputDirectory, ".mic.tmp.wav"),
            SystemStem = _options.WriteStems ? Path.Combine(outputDirectory, "system.wav") : Path.Combine(outputDirectory, ".system.tmp.wav"),
            Status = "recording",
            AppVersion = AppRuntime.GetProductVersionTag(),
            DeviceProfile = _options.DeviceProfile,
            SameDeviceRisk = _options.SameDeviceRisk,
            SameDeviceWarning = null,
            PhaseTimings = new SessionPhaseTimings(),
            CaptureDiagnostics = new CaptureDiagnostics
            {
                StopTimeoutSeconds = _options.StopTimeoutSeconds,
                SameDeviceRisk = _options.SameDeviceRisk,
                DeviceProfile = _options.DeviceProfile
            },
            TranscriptionStatus = null,
            TranscriptionStartedAt = null,
            TranscriptionFinishedAt = null,
            TranscriptionError = null,
            TranscriptionAttempts = 0,
            TranscriptTextPath = null,
            TranscriptJsonPath = null,
            TranscriptionEngine = "whisperx",
            TranscriptionInputPath = Path.Combine(outputDirectory, "audio_mix.wav"),
            SourceType = "recording",
            LastHeartbeatAt = DateTimeOffset.Now
        };

        Log($"Starting recording: {meetingId}");
        Log($"Mic device: {_micDevice.FriendlyName}");
        Log($"System device: {_systemDevice.FriendlyName}");
        RecordRecorderPhase("recording_started_at", DateTimeOffset.Now);
        RecordRecorderPhaseValue("device_profile", _options.DeviceProfile);
        RecordRecorderPhaseValue("same_device_risk", _options.SameDeviceRisk);
        _manifest.CaptureDiagnostics!.AudioEndpointAtStart = CaptureAudioEndpointSnapshotBestEffort("at_start");
        WriteManifest();
        WriteSessionLog();
        RegisterActiveRecordingIfPossible();

        _micCapture = new WasapiCapture(_micDevice);
        _systemCapture = new WasapiLoopbackCapture(_systemDevice);

        _manifest.CaptureDiagnostics!.MicInputFormat = _micCapture.WaveFormat.ToString();
        _manifest.CaptureDiagnostics.SystemInputFormat = _systemCapture.WaveFormat.ToString();
        _manifest.SampleRateInputMic = _micCapture.WaveFormat.SampleRate;
        _manifest.SampleRateInputSystem = _systemCapture.WaveFormat.SampleRate;
        _manifest.ChannelsInputMic = _micCapture.WaveFormat.Channels;
        _manifest.ChannelsInputSystem = _systemCapture.WaveFormat.Channels;
        WriteManifest();

        _micWriter = new GapAwareStemWriter(_manifest.MicStem!, _micCapture.WaveFormat, _options.JitterToleranceMs);
        _systemWriter = new GapAwareStemWriter(_manifest.SystemStem!, _systemCapture.WaveFormat, _options.JitterToleranceMs);

        _micStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _systemStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _micCapture.DataAvailable += OnMicDataAvailable;
        _systemCapture.DataAvailable += OnSystemDataAvailable;
        _micCapture.RecordingStopped += OnMicRecordingStopped;
        _systemCapture.RecordingStopped += OnSystemRecordingStopped;

        _sessionClock.Start();
        _micCapture.StartRecording();
        _systemCapture.StartRecording();

        Log($"Mic format: {_micCapture.WaveFormat}");
        Log($"System format: {_systemCapture.WaveFormat}");
        Log($"Jitter tolerance: {_options.JitterToleranceMs} ms | stop timeout: {_options.StopTimeoutSeconds}s | trailing silence cap: {_options.MaxTrailingSilenceMs} ms");
        WriteSessionLog();

        await Task.Yield();
        return _manifest;
    }

    public async Task<SessionManifest> StopAsync()
    {
        EnsureNotDisposed();
        if (_manifest is null || _micCapture is null || _systemCapture is null || _micWriter is null || _systemWriter is null || _micStopped is null || _systemStopped is null)
        {
            throw new InvalidOperationException("Session is not running.");
        }

        try
        {
            _stopRequested = true;
            var stopRequestedAt = DateTimeOffset.Now;
            RecordRecorderPhase("stop_requested_at", stopRequestedAt);
            Log("Stopping recording...");
            _micCapture.StopRecording();
            _systemCapture.StopRecording();

            var stopAllTask = Task.WhenAll(_micStopped.Task, _systemStopped.Task);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.StopTimeoutSeconds)));
            var completedTask = await Task.WhenAny(stopAllTask, timeoutTask).ConfigureAwait(false);
            if (completedTask != stopAllTask)
            {
                RegisterCaptureFailure("STOP_TIMEOUT", new TimeoutException($"Capture stop did not finish within {_options.StopTimeoutSeconds} seconds."));
                if (_manifest.CaptureDiagnostics is not null)
                {
                    _manifest.CaptureDiagnostics.StopTimeoutHit = true;
                }
                Log("Stop timeout hit. Session will be marked as failed.");
            }
            else
            {
                await stopAllTask.ConfigureAwait(false);
            }

            return await Task.Run(FinalizeStoppedSessionCore).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_manifest is not null && _manifest.Status != "failed")
            {
                _manifest.Status = "failed";
                _manifest.ErrorCode = _manifest.ErrorCode ?? "STOP_OR_MIX_FAILED";
                _manifest.ErrorMessage = ex.Message;
                _manifest.FinishedAt = DateTimeOffset.Now;
                _manifest.TranscriptionStatus = null;
                _manifest.TranscriptionError = null;
                WriteManifest();
            }

            Log($"ERROR: {ex}");
            WriteSessionLog();
            ClearActiveRecordingIfPossible();
            throw;
        }
        finally
        {
            Dispose();
        }
    }

    private AudioEndpointSnapshot? CaptureAudioEndpointSnapshotBestEffort(string stage)
    {
        try
        {
            return AudioEndpointDiagnosticsService.Capture(_micDevice, _systemDevice);
        }
        catch (Exception ex)
        {
            Log($"WARNING: audio endpoint diagnostics ({stage}) failed but recording flow will continue: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private SessionManifest FinalizeStoppedSessionCore()
    {
        if (_manifest is null || _micWriter is null || _systemWriter is null)
        {
            throw new InvalidOperationException("Session is not running.");
        }

        _sessionClock.Stop();
        _micWriter.Complete(_sessionClock.Elapsed, _options.MaxTrailingSilenceMs);
        _systemWriter.Complete(_sessionClock.Elapsed, _options.MaxTrailingSilenceMs);
        if (_micWriter.CapacityReached || _systemWriter.CapacityReached)
        {
            Log($"WARNING: WAV capaciteitsgrens bereikt; audio is afgekapt op de 4 GiB RIFF-grens. " +
                $"mic_dropped_bytes={_micWriter.DroppedBytesAfterCapacity}, system_dropped_bytes={_systemWriter.DroppedBytesAfterCapacity}. " +
                "De opname tot dat punt blijft bruikbaar en transcribeerbaar.");
        }

        var stemsCompletedAt = DateTimeOffset.Now;
        RecordRecorderPhase("stems_completed_at", stemsCompletedAt);
        if (TryGetRecorderPhaseDate("stop_requested_at", out var stopAt))
        {
            RecordRecorderPhaseValue("stop_to_stems_completed_ms", (stemsCompletedAt - stopAt).TotalMilliseconds);
        }

        CaptureDiagnosticsSnapshot();
        _manifest.CaptureDiagnostics!.AudioEndpointAfterStop = CaptureAudioEndpointSnapshotBestEffort("after_stop");
        WriteManifest();
        ReleaseStemWriters();

        if (_captureFailure is not null)
        {
            _manifest.Status = "failed";
            _manifest.ErrorCode = _captureFailureCode ?? "CAPTURE_FAILED";
            _manifest.ErrorMessage = _captureFailure.Message;
            _manifest.FinishedAt = DateTimeOffset.Now;
            _manifest.TranscriptionStatus = null;
            _manifest.TranscriptionError = null;
            WriteManifest();
            Log($"Capture failure preserved stems only: {_captureFailure}");
            WriteSessionLog();
            ClearActiveRecordingIfPossible();
            throw new InvalidOperationException("Capture is voortijdig afgebroken. READY is niet geschreven en de sessie is als failed gemarkeerd.", _captureFailure);
        }

        var mixStartedAt = DateTimeOffset.Now;
        RecordRecorderPhase("mix_started_at", mixStartedAt);
        var metrics = OfflineMixer.CreateWhisperReadyMix(_manifest.MicStem!, _manifest.SystemStem!, _manifest.OutputFile, _options);
        var mixFinishedAt = DateTimeOffset.Now;
        RecordRecorderPhase("mix_finished_at", mixFinishedAt);
        if (TryGetRecorderPhaseDate("stop_requested_at", out stopAt))
        {
            RecordRecorderPhaseValue("stop_to_mix_finished_ms", (mixFinishedAt - stopAt).TotalMilliseconds);
        }
        RecordRecorderPhaseValue("mix_duration_ms", (mixFinishedAt - mixStartedAt).TotalMilliseconds);

        _manifest.MixMetrics = metrics;
        _manifest.Status = "completed";
        _manifest.FinishedAt = DateTimeOffset.Now;
        _manifest.TranscriptionInputPath = _manifest.OutputFile;
        _manifest.TranscriptTextPath = null;
        _manifest.TranscriptJsonPath = null;
        _manifest.TranscriptionStartedAt = null;
        _manifest.TranscriptionFinishedAt = null;
        _manifest.TranscriptionError = null;
        _manifest.TranscriptionStatus = "ready_for_transcription";
        WriteManifest();
        Log($"Mix created: {_manifest.OutputFile}");
        Log($"Mix peak after limiting: {metrics.MixPeakAfterLimiting:F4}");
        Log($"Average system gain: {metrics.AverageSystemGain:F3}");
        Log($"Average mic gain: {metrics.AverageMicGain:F3}");

        var readyPath = Path.Combine(_manifest.OutputDirectory, "READY");
        File.WriteAllText(readyPath, string.Empty);
        var readyWrittenAt = DateTimeOffset.Now;
        RecordRecorderPhase("ready_written_at", readyWrittenAt);
        if (TryGetRecorderPhaseDate("stop_requested_at", out stopAt))
        {
            RecordRecorderPhaseValue("stop_to_ready_ms", (readyWrittenAt - stopAt).TotalMilliseconds);
        }
        if (TryGetRecorderPhaseDate("mix_finished_at", out var finishedAt))
        {
            RecordRecorderPhaseValue("mix_to_ready_ms", (readyWrittenAt - finishedAt).TotalMilliseconds);
        }
        Log("READY marker written.");
        WriteManifest();

        if (!_options.WriteStems)
        {
            SafeDelete(_manifest.MicStem!);
            SafeDelete(_manifest.SystemStem!);
            _manifest.MicStem = null;
            _manifest.SystemStem = null;
            WriteManifest();
            Log("Temporary stems removed.");
        }

        PublishLevels(0f, 0f, _sessionClock.Elapsed, force: true);
        WriteSessionLog();
        ClearActiveRecordingIfPossible();
        return _manifest;
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        var elapsed = _sessionClock.Elapsed;
        _micWriter?.Write(e.Buffer, e.BytesRecorded, elapsed);
        _lastMicPeak = MeasurePeak(e.Buffer, e.BytesRecorded, _micCapture?.WaveFormat);
        CheckpointHeartbeat(elapsed);
        PublishLevels(_lastMicPeak, _lastSystemPeak, elapsed);
    }

    private void OnSystemDataAvailable(object? sender, WaveInEventArgs e)
    {
        var elapsed = _sessionClock.Elapsed;
        _systemWriter?.Write(e.Buffer, e.BytesRecorded, elapsed);
        _lastSystemPeak = MeasurePeak(e.Buffer, e.BytesRecorded, _systemCapture?.WaveFormat);
        CheckpointHeartbeat(elapsed);
        PublishLevels(_lastMicPeak, _lastSystemPeak, elapsed);
    }

    private void OnMicRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            RegisterCaptureFailure("MIC_CAPTURE_FAILED", e.Exception);
            Log($"Mic capture stopped with error: {e.Exception}");
        }
        else
        {
            RecordRecorderPhase("mic_stopped_at", DateTimeOffset.Now);
            Log(_stopRequested ? "Mic capture stopped." : "Mic capture stopped unexpectedly without explicit error.");
        }

        WriteSessionLog();
        _micStopped?.TrySetResult(true);
    }

    private void OnSystemRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            RegisterCaptureFailure("SYSTEM_CAPTURE_FAILED", e.Exception);
            Log($"System capture stopped with error: {e.Exception}");
        }
        else
        {
            RecordRecorderPhase("system_stopped_at", DateTimeOffset.Now);
            Log(_stopRequested ? "System capture stopped." : "System capture stopped unexpectedly without explicit error.");
        }

        WriteSessionLog();
        _systemStopped?.TrySetResult(true);
    }

    private void RegisterCaptureFailure(string code, Exception exception)
    {
        lock (_stateSync)
        {
            _captureFailure ??= exception;
            _captureFailureCode ??= code;
            CaptureFailed = true;
            CaptureFailureSummary ??= $"{code}: {exception.Message}";
            if (_manifest is not null)
            {
                _manifest.Status = "failed";
                _manifest.ErrorCode = code;
                _manifest.ErrorMessage = exception.Message;
                WriteManifest();
            }
        }
    }

    private void CaptureDiagnosticsSnapshot()
    {
        if (_manifest?.CaptureDiagnostics is null || _micWriter is null || _systemWriter is null)
        {
            return;
        }

        _manifest.CaptureDiagnostics.MicFirstCallbackLagMs = _micWriter.FirstCallbackLag.TotalMilliseconds;
        _manifest.CaptureDiagnostics.SystemFirstCallbackLagMs = _systemWriter.FirstCallbackLag.TotalMilliseconds;
        _manifest.CaptureDiagnostics.MicInsertedSilenceMs = _micWriter.BytesToMilliseconds(_micWriter.InsertedSilenceBytes);
        _manifest.CaptureDiagnostics.SystemInsertedSilenceMs = _systemWriter.BytesToMilliseconds(_systemWriter.InsertedSilenceBytes);
        _manifest.CaptureDiagnostics.MicTruncatedTrailingSilenceMs = _micWriter.BytesToMilliseconds(_micWriter.TruncatedTrailingSilenceBytes);
        _manifest.CaptureDiagnostics.SystemTruncatedTrailingSilenceMs = _systemWriter.BytesToMilliseconds(_systemWriter.TruncatedTrailingSilenceBytes);
        _manifest.CaptureDiagnostics.MicMaxGapMs = _micWriter.BytesToMilliseconds(_micWriter.MaxGapBytes);
        _manifest.CaptureDiagnostics.SystemMaxGapMs = _systemWriter.BytesToMilliseconds(_systemWriter.MaxGapBytes);
        WriteManifest();
    }

    private void RecordRecorderPhase(string key, DateTimeOffset value)
    {
        if (_manifest?.PhaseTimings is null)
        {
            return;
        }

        _manifest.PhaseTimings.Recorder[key] = value.ToString("o");
        WriteManifest();
    }

    private void RecordRecorderPhaseValue(string key, object? value)
    {
        if (_manifest?.PhaseTimings is null)
        {
            return;
        }

        _manifest.PhaseTimings.Recorder[key] = value;
        WriteManifest();
    }

    private bool TryGetRecorderPhaseDate(string key, out DateTimeOffset value)
    {
        value = default;
        if (_manifest?.PhaseTimings is null || !_manifest.PhaseTimings.Recorder.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return DateTimeOffset.TryParse(raw.ToString(), out value);
    }

    private void CheckpointHeartbeat(TimeSpan elapsed)
    {
        if (_manifest is null)
        {
            return;
        }

        var nowTicks = Stopwatch.GetTimestamp();
        if (_lastHeartbeatTick != 0 && (nowTicks - _lastHeartbeatTick) / (double)Stopwatch.Frequency < 10.0)
        {
            return;
        }

        _lastHeartbeatTick = nowTicks;
        _manifest.LastHeartbeatAt = DateTimeOffset.Now;
        try
        {
            _micWriter?.WaveFormat.ToString();
        }
        catch
        {
        }

        try
        {
            WriteManifest();
            _micWriter?.Flush();
            _systemWriter?.Flush();
        }
        catch
        {
            // heartbeat/checkpoint best-effort only
        }
    }

    private void WriteManifest()
    {
        if (_manifest is null)
        {
            return;
        }

        var manifestPath = GetManifestPath();
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        lock (_stateSync)
        {
            AtomicFileWriter.WriteAllText(manifestPath, JsonSerializer.Serialize(_manifest, _jsonOptions), Encoding.UTF8);
        }
    }

    private void WriteSessionLog()
    {
        if (_manifest is null)
        {
            return;
        }

        var logPath = GetSessionLogPath();
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        File.WriteAllText(logPath, _sessionLog.ToString());
        WriteDiagnosticsLog();
    }

    private void WriteDiagnosticsLog()
    {
        if (_manifest is null || !_options.DiagnosticsMode)
        {
            return;
        }

        var diagnosticsPath = Path.Combine(_manifest.OutputDirectory, "diagnostics.log");
        var sb = new StringBuilder();
        sb.AppendLine("Captivox Recorder diagnostics");
        sb.AppendLine($"Session: {_manifest.MeetingId}");
        sb.AppendLine($"Status: {_manifest.Status}");
        sb.AppendLine($"Output: {_manifest.OutputFile}");
        sb.AppendLine($"Mic gain: {_options.MicGain:F2} | System gain: {_options.SystemGain:F2}");
        sb.AppendLine("READY marker: verplicht");
        sb.AppendLine($"Write stems: {_options.WriteStems}");
        sb.AppendLine($"Diagnostics mode: {_options.DiagnosticsMode}");
        sb.AppendLine();
        sb.AppendLine("session.log spiegeling:");
        sb.Append(_sessionLog.ToString());

        if (_manifest.CaptureDiagnostics is not null)
        {
            sb.AppendLine();
            sb.AppendLine("capture_diagnostics:");
            sb.AppendLine(JsonSerializer.Serialize(_manifest.CaptureDiagnostics, _jsonOptions));
        }

        if (_manifest.MixMetrics is not null)
        {
            sb.AppendLine();
            sb.AppendLine("mix_metrics:");
            sb.AppendLine(JsonSerializer.Serialize(_manifest.MixMetrics, _jsonOptions));
        }

        File.WriteAllText(diagnosticsPath, sb.ToString());
    }

    private void Log(string message)
    {
        _sessionLog.Append('[')
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append("] ")
            .AppendLine(message);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private void ReleaseStemWriters()
    {
        _micWriter?.Dispose();
        _micWriter = null;
        _systemWriter?.Dispose();
        _systemWriter = null;
    }

    private void PublishLevels(float micPeak, float systemPeak, TimeSpan elapsed, bool force = false)
    {
        var tick = Environment.TickCount64;
        if (!force && tick - _lastLevelPublishTick < 80)
        {
            return;
        }

        _lastLevelPublishTick = tick;
        LevelsUpdated?.Invoke(this, new CaptureLevelsEventArgs
        {
            MicPeak = micPeak,
            SystemPeak = systemPeak,
            SessionElapsed = elapsed
        });
    }

    private static float MeasurePeak(byte[] buffer, int bytesRecorded, WaveFormat? format)
    {
        if (format is null || bytesRecorded <= 0)
        {
            return 0f;
        }

        var encoding = format.Encoding;
        var bitsPerSample = format.BitsPerSample;
        var channels = Math.Max(1, format.Channels);
        var frameSize = Math.Max(format.BlockAlign, channels * Math.Max(1, bitsPerSample / 8));
        float peak = 0f;

        if (encoding == WaveFormatEncoding.IeeeFloat && bitsPerSample == 32)
        {
            for (var offset = 0; offset + 3 < bytesRecorded; offset += 4)
            {
                var sample = MathF.Abs(BitConverter.ToSingle(buffer, offset));
                if (sample > peak)
                {
                    peak = sample;
                }
            }

            return Math.Clamp(peak, 0f, 1.5f);
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 16)
        {
            for (var offset = 0; offset + 1 < bytesRecorded; offset += 2)
            {
                var sample = Math.Abs(BitConverter.ToInt16(buffer, offset) / (float)short.MaxValue);
                if (sample > peak)
                {
                    peak = sample;
                }
            }

            return Math.Clamp(peak, 0f, 1.5f);
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 24)
        {
            for (var offset = 0; offset + 2 < bytesRecorded; offset += 3)
            {
                var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((value & 0x800000) != 0)
                {
                    value |= unchecked((int)0xFF000000);
                }

                var sample = Math.Abs(value / 8388607f);
                if (sample > peak)
                {
                    peak = sample;
                }
            }

            return Math.Clamp(peak, 0f, 1.5f);
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 32)
        {
            for (var offset = 0; offset + 3 < bytesRecorded; offset += 4)
            {
                var sample = Math.Abs(BitConverter.ToInt32(buffer, offset) / 2147483647f);
                if (sample > peak)
                {
                    peak = sample;
                }
            }

            return Math.Clamp(peak, 0f, 1.5f);
        }

        // Fallback: inspect one byte per frame for a rough activity estimate.
        for (var offset = 0; offset < bytesRecorded; offset += frameSize)
        {
            var sample = Math.Abs((buffer[offset] - 128) / 128f);
            if (sample > peak)
            {
                peak = sample;
            }
        }

        return Math.Clamp(peak, 0f, 1.5f);
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioCaptureSession));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        ClearActiveRecordingIfPossible();

        if (_micCapture is not null)
        {
            _micCapture.DataAvailable -= OnMicDataAvailable;
            _micCapture.RecordingStopped -= OnMicRecordingStopped;
            _micCapture.Dispose();
            _micCapture = null;
        }

        if (_systemCapture is not null)
        {
            _systemCapture.DataAvailable -= OnSystemDataAvailable;
            _systemCapture.RecordingStopped -= OnSystemRecordingStopped;
            _systemCapture.Dispose();
            _systemCapture = null;
        }

        _micWriter?.Dispose();
        _micWriter = null;
        _systemWriter?.Dispose();
        _systemWriter = null;

        _micDevice?.Dispose();
        _micDevice = null;
        _systemDevice?.Dispose();
        _systemDevice = null;
    }
}
