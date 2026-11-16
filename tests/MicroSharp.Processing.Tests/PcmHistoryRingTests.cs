using MicroSharp.Processing;

namespace MicroSharp.Processing.Tests;

public sealed class PcmHistoryRingTests
{
    private static readonly PcmFormat Format = PcmFormat.Default;

    [Fact]
    public void Capacity_uses_the_format_rate()
    {
        var slow = new PcmFormat(8_000, TimeSpan.FromMilliseconds(20));
        var ring = new PcmHistoryRing(slow, TimeSpan.FromMilliseconds(100));

        Assert.Equal(100 * 8_000 * PcmFormat.BytesPerSample / 1000, ring.CapacityBytes);
        Assert.Equal(0, ring.CapacityBytes % PcmFormat.BytesPerSample);
    }

    [Fact]
    public void Capacity_rejects_non_positive_fractional_and_unaligned_history()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmHistoryRing.ByteCapacity(Format, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmHistoryRing.ByteCapacity(Format, TimeSpan.FromMilliseconds(-5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmHistoryRing.ByteCapacity(Format, TimeSpan.FromMilliseconds(1.5)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcmHistoryRing.ByteCapacity(Format, TimeSpan.FromMilliseconds(int.MaxValue)));

        var narrow = new PcmFormat(500, TimeSpan.FromMilliseconds(20));
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcmHistoryRing.ByteCapacity(narrow, TimeSpan.FromMilliseconds(1)));
        Assert.Contains("whole number of PCM frames", error.Message, StringComparison.Ordinal);

        var wide = new PcmFormat(44_100, TimeSpan.FromMilliseconds(20));
        var fractionalFrame = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcmHistoryRing.ByteCapacity(wide, TimeSpan.FromMilliseconds(1)));
        Assert.Contains("whole number of PCM frames", fractionalFrame.Message, StringComparison.Ordinal);
        Assert.Equal(10 * 44_100 * PcmFormat.BytesPerSample / 1000, PcmHistoryRing.ByteCapacity(wide, TimeSpan.FromMilliseconds(10)));
        Assert.Equal(20 * 44_100 * PcmFormat.BytesPerSample / 1000, PcmHistoryRing.ByteCapacity(wide, TimeSpan.FromMilliseconds(20)));

        var inexactTicks = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcmHistoryRing.ByteCapacity(Format, TimeSpan.FromTicks(10_001)));
        Assert.Contains("whole number of milliseconds", inexactTicks.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capacity_rejects_a_null_format()
    {
        Assert.Throws<ArgumentNullException>(() => new PcmHistoryRing(null!, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void Read_returns_bytes_in_order_and_does_not_advance_the_cursor()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(1_000));
        var written = Pattern(800);
        ring.Write(written);
        var cursor = new PcmHistoryCursor(0);
        var buffer = new byte[written.Length];

        var first = ring.Read(cursor, buffer);
        var second = ring.Read(cursor, buffer);

        Assert.Equal(written.Length, first);
        Assert.Equal(written.Length, second);
        Assert.Equal(written, buffer);
        Assert.Equal(0, cursor.BytePosition);
    }

    [Fact]
    public void Independent_cursors_do_not_affect_each_other()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(1_000));
        var first = ring.OpenCursor(PcmHistoryCursorOrigin.Earliest);
        var second = ring.OpenCursor(PcmHistoryCursorOrigin.Earliest);
        var written = Pattern(128);
        ring.Write(written);
        var bufferA = new byte[written.Length];
        var bufferB = new byte[32];

        Assert.Equal(written.Length, ring.Read(first, bufferA));
        Assert.Equal(32, ring.Read(second, bufferB));
        Assert.Equal(written, bufferA);
        Assert.Equal(written.AsSpan(0, 32).ToArray(), bufferB);
    }

    [Fact]
    public void Caught_up_cursor_returns_zero_and_a_future_cursor_is_invalid()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(100));
        ring.Write(Pattern(64));
        var latest = ring.OpenCursor(PcmHistoryCursorOrigin.Latest);
        var buffer = new byte[8];

        Assert.Equal(0, ring.Read(latest, buffer));
        var error = Assert.Throws<PcmHistoryCursorInvalidException>(() =>
            ring.Read(new PcmHistoryCursor(latest.BytePosition + 1), buffer));
        Assert.Equal(latest.BytePosition + 1, error.RequestedBytePosition);
        Assert.Equal(latest.BytePosition, error.WriteBytePosition);
    }

    [Fact]
    public void Wrap_and_oversize_write_keep_the_newest_bytes()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(50));
        var capacity = ring.CapacityBytes;
        var first = Pattern(capacity / 2, seed: 1);
        var second = Pattern(capacity / 2, seed: 2);
        var third = Pattern(capacity / 2, seed: 3);
        ring.Write(first);
        ring.Write(second);
        ring.Write(third);

        var earliest = ring.OpenCursor(PcmHistoryCursorOrigin.Earliest);
        Assert.Equal(first.Length + second.Length + third.Length - capacity, earliest.BytePosition);
        var buffer = new byte[capacity];
        Assert.Equal(capacity, ring.Read(earliest, buffer));
        var expected = new byte[capacity];
        second.CopyTo(expected, 0);
        third.CopyTo(expected, second.Length);
        Assert.Equal(expected, buffer);

        var huge = Pattern(capacity * 3, seed: 9);
        ring.Write(huge);
        var tail = ring.OpenCursor(PcmHistoryCursorOrigin.Earliest);
        Assert.Equal(ring.WriteBytePosition - capacity, tail.BytePosition);
        var kept = new byte[capacity];
        Assert.Equal(capacity, ring.Read(tail, kept));
        Assert.Equal(huge.AsSpan(huge.Length - capacity).ToArray(), kept);
    }

    [Fact]
    public void Stale_cursor_fails_and_odd_byte_positions_remain_readable()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(50));
        var stale = ring.OpenCursor(PcmHistoryCursorOrigin.Earliest);
        ring.Write(new byte[ring.CapacityBytes * 2]);
        var error = Assert.Throws<PcmHistoryCursorStaleException>(() => ring.Read(stale, new byte[4]));
        Assert.Equal(stale.BytePosition, error.RequestedBytePosition);
        Assert.True(error.EarliestRetainedBytePosition > stale.BytePosition);

        var odd = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(1));
        odd.Write([1, 2, 3]);
        var copy = new byte[3];
        Assert.Equal(3, odd.Read(new PcmHistoryCursor(0), copy));
        Assert.Equal(new byte[] { 1, 2, 3 }, copy);
        Assert.Equal(0, odd.WriteBytePosition % 2 == 1 ? 0 : 1);
        Assert.Equal(3, odd.WriteBytePosition);
    }

    [Fact]
    public void Empty_write_is_ignored_and_an_empty_destination_is_rejected()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(20));
        ring.Write([]);
        Assert.Equal(0, ring.WriteBytePosition);
        ring.Write(Pattern(4));
        Assert.Throws<ArgumentException>(() => ring.Read(new PcmHistoryCursor(0), Span<byte>.Empty));
        Assert.Throws<PcmHistoryCursorStaleException>(() => ring.Read(new PcmHistoryCursor(-1), new byte[1]));
    }

    [Fact]
    public async Task One_writer_and_two_readers_observe_the_same_bytes()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromSeconds(2));
        var payload = Pattern(4_000);
        using var started = new ManualResetEventSlim(false);
        var writer = Task.Run(() =>
        {
            started.Set();
            for (var offset = 0; offset < payload.Length; offset += 200)
            {
                ring.Write(payload.AsSpan(offset, 200));
            }
        });
        started.Wait();
        var readers = new Task[2];
        for (var reader = 0; reader < readers.Length; reader++)
        {
            readers[reader] = Task.Run(() =>
            {
                var cursor = ring.OpenCursor(PcmHistoryCursorOrigin.Earliest);
                var buffer = new byte[200];
                var total = 0;
                while (total < payload.Length)
                {
                    var count = ring.Read(cursor, buffer);
                    for (var index = 0; index < count; index++)
                    {
                        Assert.Equal(payload[total + index], buffer[index]);
                    }

                    total += count;
                    cursor = new PcmHistoryCursor(cursor.BytePosition + count);
                }
            });
        }

        var completed = Task.WhenAll(writer, readers[0], readers[1]);
        var finished = await Task.WhenAny(completed, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(completed, finished);
        await completed;
    }

    [Fact]
    public void Write_and_read_allocate_nothing_after_warmup()
    {
        var ring = new PcmHistoryRing(Format, TimeSpan.FromMilliseconds(1_000));
        var source = new byte[Format.BytesPerChunk];
        var destination = new byte[source.Length];
        ring.Write(source);
        _ = ring.Read(ring.OpenCursor(PcmHistoryCursorOrigin.Earliest), destination);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ring.Write(source);
        var cursor = ring.OpenCursor(PcmHistoryCursorOrigin.Earliest);
        var read = ring.Read(cursor, destination);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(source.Length, read);
        Assert.Equal(0, allocated);
    }

    private static byte[] Pattern(int byteCount, int seed = 0)
    {
        var data = new byte[byteCount];
        for (var index = 0; index < data.Length; index++)
        {
            data[index] = (byte)((index + seed) & 0xFF);
        }

        return data;
    }
}
