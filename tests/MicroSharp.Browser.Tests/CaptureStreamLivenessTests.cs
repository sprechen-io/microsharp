using Microsoft.Extensions.Time.Testing;
using MicroSharp.Native;

namespace MicroSharp.Browser.Tests;

public sealed class CaptureStreamLivenessTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task SilentBlocksFollowTheSameDeadlinesForBothTransports(bool browser, bool complete)
    {
        var format = PcmFormat.Default;
        using var source = new MemoryStream(new byte[2 * format.BytesPerChunk]);
        var module = new ScriptedJsModule();
        IMicrophoneCapture capture = browser
            ? new BrowserMicrophoneCapture(module)
            : new VirtualMicrophoneCapture(new VirtualMicrophoneOptions
            {
                Source = new StreamPcmSource(source, format),
            });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var session = await capture.StartAsync(
            new MicrophoneCaptureRequest { Format = format }, deadline.Token);
        await using var chunks = session.ReadChunksAsync(deadline.Token).GetAsyncEnumerator();
        var clock = new FakeTimeProvider();
        var monitor = new CaptureLivenessMonitor(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(40), clock);
        var result = monitor.WaitForStallAsync(deadline.Token);

        for (var sequence = 0; sequence < 2; sequence++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(30));
            module.SessionReference?.Value.OnPcmChunk(new byte[format.BytesPerChunk]);
            Assert.True(await chunks.MoveNextAsync());
            Assert.Equal(sequence, chunks.Current.Sequence);
            Assert.All(chunks.Current.Payload, value => Assert.Equal((byte)0, value));
            monitor.ObserveBlock();
        }

        clock.Advance(TimeSpan.FromMilliseconds(39));
        Assert.False(result.IsCompleted);
        if (complete)
        {
            await session.StopAsync(deadline.Token);
            monitor.Complete();
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(
            complete ? CaptureLivenessOutcome.Completed : CaptureLivenessOutcome.InterBlockTimeout,
            await result);
    }
}
