namespace MicroSharp;

/// <summary>
/// Observes block delivery for a single capture. Create an instance when capture starts.
/// </summary>
public sealed class CaptureLivenessMonitor
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _interBlockTimeout;
    private long _lastActivity;
    private bool _receivedBlock;
    private CaptureLivenessOutcome? _outcome;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Starts the observation window with caller-selected timeouts and an optional clock.
    /// </summary>
    public CaptureLivenessMonitor(
        TimeSpan startupTimeout,
        TimeSpan interBlockTimeout,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(startupTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interBlockTimeout, TimeSpan.Zero);

        _startupTimeout = startupTimeout;
        _interBlockTimeout = interBlockTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastActivity = _timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Records delivery of one block, including a block of digital silence.
    /// A block delivered at or after the current deadline leaves the stall latched.
    /// </summary>
    public void ObserveBlock()
    {
        lock (_gate)
        {
            if (_outcome.HasValue)
                return;

            var now = _timeProvider.GetTimestamp();
            if (GetRemainingTime(now) <= TimeSpan.Zero)
            {
                SetOutcome(GetStallOutcome());
                return;
            }

            _lastActivity = now;
            if (!_receivedBlock)
            {
                _receivedBlock = true;
                // The first delivery can move the deadline earlier. Later blocks only extend it.
                _changed.SetResult();
                _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    /// <summary>
    /// Ends observation and wakes waiters. Completion before the deadline reports
    /// <see cref="CaptureLivenessOutcome.Completed"/>.
    /// </summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (_outcome.HasValue)
                return;

            var outcome = GetRemainingTime(_timeProvider.GetTimestamp()) <= TimeSpan.Zero
                ? GetStallOutcome()
                : CaptureLivenessOutcome.Completed;
            SetOutcome(outcome);
        }
    }

    /// <summary>
    /// Waits for a stall or capture completion. Cancellation applies to this wait.
    /// </summary>
    public async Task<CaptureLivenessOutcome> WaitForStallAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task changed;
            TimeSpan remaining;
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_outcome is { } outcome)
                    return outcome;

                remaining = GetRemainingTime(_timeProvider.GetTimestamp());
                if (remaining <= TimeSpan.Zero)
                {
                    var stall = GetStallOutcome();
                    SetOutcome(stall);
                    return stall;
                }

                changed = _changed.Task;
            }

            // TimeProvider timers accept whole milliseconds up to the system timer limit.
            var wait = TimeSpan.FromMilliseconds(Math.Min(Math.Ceiling(remaining.TotalMilliseconds), uint.MaxValue - 1));
            try
            {
                await changed.WaitAsync(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A block may have extended the deadline while the timer was running.
            }
        }
    }

    private TimeSpan GetRemainingTime(long now) =>
        (_receivedBlock ? _interBlockTimeout : _startupTimeout) - _timeProvider.GetElapsedTime(_lastActivity, now);

    private CaptureLivenessOutcome GetStallOutcome() =>
        _receivedBlock ? CaptureLivenessOutcome.InterBlockTimeout : CaptureLivenessOutcome.StartupTimeout;

    private void SetOutcome(CaptureLivenessOutcome outcome)
    {
        _outcome = outcome;
        _changed.SetResult();
    }
}
