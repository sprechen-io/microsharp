using Microsoft.Extensions.Time.Testing;

namespace MicroSharp.Tests;

public sealed class CaptureLivenessMonitorTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InterBlockTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DeliveredSilentChunksKeepCaptureLive()
    {
        var clock = new FakeTimeProvider();
        var monitor = CreateMonitor(clock);
        var wait = monitor.WaitForStallAsync();

        var chunkInterval = TimeSpan.FromMilliseconds(500);
        for (var sequence = 0; sequence < 8; sequence++)
        {
            clock.Advance(chunkInterval);
            var chunk = new PcmChunk(sequence, chunkInterval * (sequence + 1), new byte[320]);
            ObserveDeliveredChunk(monitor, chunk);
        }

        monitor.Complete();
        Assert.Equal(CaptureLivenessOutcome.Completed, await wait.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task MissingFirstBlockReachesStartupDeadline()
    {
        var clock = new FakeTimeProvider();
        var monitor = CreateMonitor(clock);
        var wait = monitor.WaitForStallAsync();

        clock.Advance(StartupTimeout - TimeSpan.FromMilliseconds(1));
        Assert.False(wait.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(CaptureLivenessOutcome.StartupTimeout, await wait.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task FirstBlockCanMoveAnExistingWaitToAnEarlierDeadline()
    {
        var clock = new FakeTimeProvider();
        var monitor = CreateMonitor(clock);
        var wait = monitor.WaitForStallAsync();

        monitor.ObserveBlock();
        clock.Advance(InterBlockTimeout);

        Assert.Equal(CaptureLivenessOutcome.InterBlockTimeout, await wait.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task SubsequentBlockExtendsTheInterBlockDeadline()
    {
        var clock = new FakeTimeProvider();
        var monitor = CreateMonitor(clock);
        monitor.ObserveBlock();
        var wait = monitor.WaitForStallAsync();

        var halfInterval = InterBlockTimeout / 2;
        clock.Advance(halfInterval);
        monitor.ObserveBlock();
        clock.Advance(halfInterval);
        Assert.False(wait.IsCompleted);
        clock.Advance(halfInterval);

        Assert.Equal(CaptureLivenessOutcome.InterBlockTimeout, await wait.WaitAsync(TestTimeout));
    }

    [Theory]
    [InlineData(false, CaptureLivenessOutcome.StartupTimeout)]
    [InlineData(true, CaptureLivenessOutcome.InterBlockTimeout)]
    public async Task ABlockAtTheDeadlineCannotEraseAStall(bool receivedFirstBlock, CaptureLivenessOutcome expected)
    {
        var clock = new FakeTimeProvider();
        var monitor = CreateMonitor(clock);
        if (receivedFirstBlock)
            monitor.ObserveBlock();

        clock.Advance(receivedFirstBlock ? InterBlockTimeout : StartupTimeout);
        monitor.ObserveBlock();
        monitor.Complete();

        Assert.Equal(expected, await monitor.WaitForStallAsync().WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task CompletionWakesAnExistingWaitWithoutClockAdvancement()
    {
        var clock = new FakeTimeProvider();
        var monitor = CreateMonitor(clock);
        var wait = monitor.WaitForStallAsync();

        monitor.Complete();
        Assert.Equal(CaptureLivenessOutcome.Completed, await wait.WaitAsync(TestTimeout));

        monitor.Complete();
        monitor.ObserveBlock();
        clock.Advance(StartupTimeout);

        Assert.Equal(CaptureLivenessOutcome.Completed, await monitor.WaitForStallAsync().WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task CancellationPropagatesAndLeavesOtherWaitersActive()
    {
        var clock = new FakeTimeProvider();
        var monitor = CreateMonitor(clock);
        using var cancellation = new CancellationTokenSource();
        var cancelledWait = monitor.WaitForStallAsync(cancellation.Token);
        var activeWait = monitor.WaitForStallAsync();

        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledWait.WaitAsync(TestTimeout));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(activeWait.IsCompleted);

        monitor.Complete();
        Assert.Equal(CaptureLivenessOutcome.Completed, await activeWait.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task RestartGetsAFreshStartupDeadline()
    {
        var clock = new FakeTimeProvider();
        var previous = CreateMonitor(clock);
        previous.ObserveBlock();
        clock.Advance(InterBlockTimeout);
        Assert.Equal(CaptureLivenessOutcome.InterBlockTimeout, await previous.WaitForStallAsync().WaitAsync(TestTimeout));

        var restarted = CreateMonitor(clock);
        var wait = restarted.WaitForStallAsync();
        clock.Advance(InterBlockTimeout);
        Assert.False(wait.IsCompleted);
        clock.Advance(StartupTimeout - InterBlockTimeout);

        Assert.Equal(CaptureLivenessOutcome.StartupTimeout, await wait.WaitAsync(TestTimeout));
    }

    private static CaptureLivenessMonitor CreateMonitor(FakeTimeProvider clock) =>
        new(StartupTimeout, InterBlockTimeout, clock);

    private static void ObserveDeliveredChunk(CaptureLivenessMonitor monitor, PcmChunk chunk)
    {
        Assert.All(chunk.Payload, sampleByte => Assert.Equal((byte)0, sampleByte));
        monitor.ObserveBlock();
    }
}
