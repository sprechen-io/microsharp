namespace MicroSharp.Processing;

/// <summary>
/// Fixed PCM history with independent byte cursors.
/// One writer and any number of readers may call this instance. Each call holds one lock.
/// Two concurrent writers are not part of the contract.
/// </summary>
/// <remarks>
/// Derived from the in-memory PCM ring in sprechen-io/sprechen-io commit
/// 1d603f3a75f408d1d11010cd49190e110979803b. The live capture queue in
/// <c>MicroSharp</c> remains a separate bounded transport. This ring does not
/// open a device and does not segment speech. Write and read copy into storage
/// that was allocated once. They do not allocate on the hot path.
/// </remarks>
public sealed class PcmHistoryRing
{
    private readonly byte[] _buffer;
    private readonly object _gate = new();
    private long _writeBytePosition;

    /// <summary>Allocates a history window for <paramref name="format"/> and <paramref name="history"/>.</summary>
    public PcmHistoryRing(PcmFormat format, TimeSpan history)
    {
        ArgumentNullException.ThrowIfNull(format);
        Format = format;
        CapacityBytes = ByteCapacity(format, history);
        _buffer = new byte[CapacityBytes];
    }

    /// <summary>PCM format used for the capacity calculation. Its chunk duration is not the history window.</summary>
    public PcmFormat Format { get; }

    /// <summary>Fixed number of bytes retained. This is a whole number of PCM frames.</summary>
    public int CapacityBytes { get; }

    /// <summary>Absolute byte offset of the next byte that has not been written.</summary>
    public long WriteBytePosition
    {
        get
        {
            lock (_gate)
            {
                return _writeBytePosition;
            }
        }
    }

    /// <summary>Oldest absolute byte offset still stored.</summary>
    public long EarliestRetainedBytePosition
    {
        get
        {
            lock (_gate)
            {
                return EarliestRetainedBytePositionLocked();
            }
        }
    }

    /// <summary>
    /// Byte capacity for a positive whole-millisecond <paramref name="history"/>.
    /// The duration must be an exact number of millisecond ticks. The sample numerator
    /// must divide by 1000 before the byte count is formed, and that count must contain
    /// a whole number of frames. The arithmetic is checked.
    /// </summary>
    public static int ByteCapacity(PcmFormat format, TimeSpan history)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (history.Ticks <= 0 || history.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(history),
                history,
                "History must be a positive whole number of milliseconds.");
        }

        var wholeMilliseconds = history.Ticks / TimeSpan.TicksPerMillisecond;
        if (wholeMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(history),
                history,
                "History must be a positive whole number of milliseconds.");
        }

        long numerator;
        try
        {
            numerator = checked(wholeMilliseconds * format.SampleRateHz * PcmFormat.BytesPerSample);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(history),
                history,
                "History translates to a byte capacity that overflows.");
        }

        if (numerator % 1000 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(history),
                history,
                "History capacity must contain a whole number of PCM frames.");
        }

        var bytes = numerator / 1000;
        if (bytes <= 0 || bytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(history),
                history,
                "History translates to an unsupported byte capacity.");
        }

        if (bytes % PcmFormat.BytesPerSample != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(history),
                history,
                "History capacity must contain a whole number of PCM frames.");
        }

        return (int)bytes;
    }

    /// <summary>
    /// Appends <paramref name="pcm"/>. An empty span does nothing.
    /// A span longer than <see cref="CapacityBytes"/> keeps only the newest bytes
    /// and still advances <see cref="WriteBytePosition"/> by the full length.
    /// Byte lengths do not have to be frame-aligned.
    /// </summary>
    public void Write(ReadOnlySpan<byte> pcm)
    {
        if (pcm.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            var remaining = pcm;
            if (remaining.Length > _buffer.Length)
            {
                var overflow = remaining.Length - _buffer.Length;
                _writeBytePosition += overflow;
                remaining = remaining.Slice(overflow);
            }

            var position = (int)(_writeBytePosition % _buffer.Length);
            var first = Math.Min(remaining.Length, _buffer.Length - position);
            remaining.Slice(0, first).CopyTo(_buffer.AsSpan(position));
            if (first < remaining.Length)
            {
                remaining.Slice(first).CopyTo(_buffer.AsSpan(0));
            }

            _writeBytePosition += remaining.Length;
        }
    }

    /// <summary>Opens a caller-owned cursor. The ring does not store it.</summary>
    public PcmHistoryCursor OpenCursor(PcmHistoryCursorOrigin origin)
    {
        lock (_gate)
        {
            return origin switch
            {
                PcmHistoryCursorOrigin.Latest => new PcmHistoryCursor(_writeBytePosition),
                PcmHistoryCursorOrigin.Earliest => new PcmHistoryCursor(EarliestRetainedBytePositionLocked()),
                _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown history cursor origin."),
            };
        }
    }

    /// <summary>
    /// Copies bytes from <paramref name="cursor"/> into <paramref name="destination"/>.
    /// The cursor value is not advanced. A cursor exactly at the write position returns 0.
    /// </summary>
    public int Read(PcmHistoryCursor cursor, Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            throw new ArgumentException("History reads must request at least one byte.", nameof(destination));
        }

        lock (_gate)
        {
            var earliest = EarliestRetainedBytePositionLocked();
            if (cursor.BytePosition < earliest)
            {
                throw new PcmHistoryCursorStaleException(cursor.BytePosition, earliest);
            }

            if (cursor.BytePosition > _writeBytePosition)
            {
                throw new PcmHistoryCursorInvalidException(cursor.BytePosition, _writeBytePosition);
            }

            var available = _writeBytePosition - cursor.BytePosition;
            if (available == 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(available, destination.Length);
            var position = (int)(cursor.BytePosition % _buffer.Length);
            var first = Math.Min(toRead, _buffer.Length - position);
            _buffer.AsSpan(position, first).CopyTo(destination);
            if (first < toRead)
            {
                _buffer.AsSpan(0, toRead - first).CopyTo(destination.Slice(first));
            }

            return toRead;
        }
    }

    private long EarliestRetainedBytePositionLocked()
    {
        return _writeBytePosition <= _buffer.Length
            ? 0L
            : _writeBytePosition - _buffer.Length;
    }
}
