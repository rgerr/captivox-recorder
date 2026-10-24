using NAudio.Wave;

namespace Captivox.Recorder.Services;

internal sealed class GapAwareStemWriter : IDisposable
{
    private readonly object _sync = new();
    private readonly WaveFileWriter _writer;
    private readonly byte[] _zeroBuffer;
    private readonly long _timelineToleranceBytes;
    private readonly int _bytesPerSecond;
    private readonly int _blockAlign;

    // RIFF stores the data chunk size in a UInt32, so a WAV cannot grow past 4 GiB. NAudio throws
    // exactly at that boundary, which used to surface as "Stop recording failed" and cost the whole
    // take. Cap just below it instead: everything recorded up to that point stays valid and
    // transcribable, and Complete() can no longer fail on a long session.
    private const long MaxDataBytesCeiling = (long)uint.MaxValue - (1L * 1024 * 1024);
    private readonly long _maxDataBytes;

    private TimeSpan? _firstCallbackElapsed;
    private long _timelineBytes;
    private bool _disposed;

    // maxDataBytesOverride exists so the regression suite can exercise the capacity guard
    // without writing four gigabytes to disk. Production always uses the RIFF ceiling.
    public GapAwareStemWriter(string path, WaveFormat waveFormat, int jitterToleranceMs, long? maxDataBytesOverride = null)
    {
        _writer = new WaveFileWriter(path, waveFormat);
        _bytesPerSecond = waveFormat.AverageBytesPerSecond;
        _blockAlign = waveFormat.BlockAlign;
        _timelineToleranceBytes = AlignToBlock(Math.Max(0, (long)Math.Round(_bytesPerSecond * (jitterToleranceMs / 1000.0))));
        _zeroBuffer = new byte[Math.Max(_bytesPerSecond / 2, _blockAlign * 8)];
        _maxDataBytes = AlignToBlock(Math.Min(maxDataBytesOverride ?? MaxDataBytesCeiling, MaxDataBytesCeiling));
    }

    public WaveFormat WaveFormat => _writer.WaveFormat;
    public long InsertedSilenceBytes { get; private set; }
    public long TruncatedTrailingSilenceBytes { get; private set; }
    public long MaxGapBytes { get; private set; }
    public TimeSpan FirstCallbackLag { get; private set; }
    public bool CapacityReached { get; private set; }
    public long DroppedBytesAfterCapacity { get; private set; }

    // Recording time that still fits before the cap. Past the cap audio is dropped without any
    // error, so the health monitor needs this to warn before that happens rather than after.
    // Read without the lock: the UI thread polls this and must never wait on a disk write.
    public TimeSpan RemainingCapacity
        => TimeSpan.FromSeconds(Math.Max(0, _maxDataBytes - Interlocked.Read(ref _timelineBytes)) / (double)_bytesPerSecond);

    public void Write(byte[] buffer, int bytesRecorded, TimeSpan sessionElapsed)
    {
        if (bytesRecorded <= 0)
        {
            return;
        }

        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_firstCallbackElapsed.HasValue)
            {
                _firstCallbackElapsed = sessionElapsed;
                FirstCallbackLag = sessionElapsed;
            }

            // Callback time describes the end of this buffer, on the shared session clock.
            var bufferStart = sessionElapsed - TimeSpan.FromSeconds(bytesRecorded / (double)_bytesPerSecond);
            EnsureTimeline(bufferStart, trailingCapBytes: null);

            var writable = (int)AlignToBlock(Math.Min(bytesRecorded, Math.Max(0, _maxDataBytes - _timelineBytes)));
            if (writable <= 0)
            {
                CapacityReached = true;
                DroppedBytesAfterCapacity += bytesRecorded;
                return;
            }

            _writer.Write(buffer, 0, writable);
            _timelineBytes += writable;
            if (writable < bytesRecorded)
            {
                CapacityReached = true;
                DroppedBytesAfterCapacity += bytesRecorded - writable;
            }
        }
    }


    public void Flush()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _writer.Flush();
        }
    }

    public void Complete(TimeSpan finalElapsed, int maxTrailingSilenceMs)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            EnsureTimeline(finalElapsed, AlignToBlock((long)Math.Round(_bytesPerSecond * (maxTrailingSilenceMs / 1000.0))));
            _writer.Flush();
        }
    }

    public double BytesToMilliseconds(long bytes)
        => bytes <= 0 ? 0 : (bytes / (double)_bytesPerSecond) * 1000.0;


    private void EnsureTimeline(TimeSpan elapsed, long? trailingCapBytes)
    {
        var relative = elapsed;
        if (relative < TimeSpan.Zero)
        {
            relative = TimeSpan.Zero;
        }

        var expectedBytes = AlignToBlock((long)Math.Floor(relative.TotalSeconds * _bytesPerSecond));
        var gap = expectedBytes - _timelineBytes;
        if (gap <= _timelineToleranceBytes)
        {
            return;
        }

        if (trailingCapBytes.HasValue && gap > trailingCapBytes.Value)
        {
            TruncatedTrailingSilenceBytes += gap - trailingCapBytes.Value;
            gap = trailingCapBytes.Value;
        }

        var remainingCapacity = _maxDataBytes - _timelineBytes;
        if (remainingCapacity <= 0)
        {
            CapacityReached = true;
            DroppedBytesAfterCapacity += Math.Max(0, gap);
            return;
        }
        if (gap > remainingCapacity)
        {
            CapacityReached = true;
            DroppedBytesAfterCapacity += gap - remainingCapacity;
            gap = AlignToBlock(remainingCapacity);
        }

        if (gap <= 0)
        {
            return;
        }

        MaxGapBytes = Math.Max(MaxGapBytes, gap);
        InsertedSilenceBytes += gap;

        while (gap > 0)
        {
            var chunk = (int)Math.Min(gap, _zeroBuffer.Length - (_zeroBuffer.Length % _blockAlign));
            if (chunk <= 0)
            {
                break;
            }

            _writer.Write(_zeroBuffer, 0, chunk);
            _timelineBytes += chunk;
            gap -= chunk;
        }
    }

    private long AlignToBlock(long bytes)
        => bytes <= 0 ? 0 : bytes - (bytes % _blockAlign);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GapAwareStemWriter));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}
