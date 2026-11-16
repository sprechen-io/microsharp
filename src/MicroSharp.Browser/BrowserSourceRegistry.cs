using MicroSharp;
using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Holds browser sources for one <see cref="BrowserCaptureController"/>.
/// The format and queue length are arguments. This type does not choose a speech-product rate.
/// </summary>
internal sealed class BrowserSourceRegistry
{
    private readonly object _gate = new();
    private readonly BrowserMicrophoneCapture _capture;
    private readonly Dictionary<string, BrowserMicrophoneHold> _holds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IMicrophoneSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _releases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _releaseDetached = new(StringComparer.Ordinal);

    public BrowserSourceRegistry(IJSRuntime jsRuntime, BrowserCaptureControllerOptions options)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        ArgumentNullException.ThrowIfNull(options);
        _capture = new BrowserMicrophoneCapture(
            jsRuntime,
            new BrowserMicrophoneOptions
            {
                EchoCancellation = options.EchoCancellation,
                NoiseSuppression = options.NoiseSuppression,
                AutoGainControl = options.AutoGainControl,
                KeepAudioContextAlive = options.KeepAudioContextAlive,
            });
    }

    public async ValueTask<BrowserAcquireResult> AcquireAsync(
        bool keepAliveEnabled,
        CancellationToken cancellationToken)
    {
        var prepared = await _capture.PrepareAsync(keepAliveEnabled, cancellationToken).ConfigureAwait(false);
        if (prepared.Hold is not null)
        {
            lock (_gate)
            {
                _holds[prepared.Hold.SessionId] = prepared.Hold;
            }
        }

        return prepared.Result;
    }

    public async ValueTask<string> ConnectAsync(
        string sessionId,
        BrowserCaptureLease lease,
        PcmFormat format,
        int queueCapacityChunks,
        Func<Exception, Task>? onCaptureFault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(format);
        var hold = FindHold(sessionId) ?? throw new InvalidOperationException($"Microphone session '{sessionId}' is not open.");
        IMicrophoneSession session;
        try
        {
            session = await hold.StartAsync(
                new MicrophoneCaptureRequest
                {
                    Format = format,
                    Backpressure = CaptureBackpressure.DropOldest,
                    QueueCapacityChunks = queueCapacityChunks,
                },
                releaseDeviceOnStop: false,
                onCaptureFault,
                cancellationToken).ConfigureAwait(false);
        }
        catch (MicrophoneException exception) when (TryReportedFormat(exception.Message, out var reported))
        {
            throw new InvalidOperationException(
                $"Microphone reported '{reported}'; capture requires rate={format.SampleRateHz}.");
        }

        lock (_gate)
        {
            _sessions[sessionId] = session;
        }

        lease.AttachCapture(session);
        return $"audio/pcm;rate={format.SampleRateHz}";
    }

    public async ValueTask DisconnectAsync(string sessionId, CancellationToken cancellationToken)
    {
        var hold = FindHold(sessionId);
        lock (_gate)
        {
            _sessions.Remove(sessionId);
        }

        if (hold is null)
        {
            return;
        }

        await hold.DisconnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ReleaseAsync(string sessionId, CancellationToken cancellationToken)
    {
        Task release;
        BrowserMicrophoneHold? starting = null;
        TaskCompletionSource? completion = null;
        lock (_gate)
        {
            _sessions.Remove(sessionId);
            if (!_releaseDetached.Contains(sessionId) && _releases.TryGetValue(sessionId, out var pending))
            {
                release = pending;
            }
            else if (!_releaseDetached.Contains(sessionId) && _holds.TryGetValue(sessionId, out var hold))
            {
                // The source stays findable until release succeeds, so a later attempt hits the same source.
                // The task is stored before it starts. An immediate failure must not block the next attempt.
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _releases[sessionId] = completion.Task;
                release = completion.Task;
                starting = hold;
            }
            else
            {
                release = _capture.ReleaseBrowserSessionAsync(sessionId, cancellationToken).AsTask();
            }
        }

        if (starting is not null && completion is not null)
        {
            _ = FinishReleaseAsync(sessionId, starting, completion);
        }

        return new ValueTask(release);
    }

    public ValueTask SetSourceEnabledAsync(string sessionId, bool enabled, CancellationToken cancellationToken)
    {
        var hold = FindHold(sessionId);
        if (hold is null)
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(hold.SetSourceEnabledAsync(enabled, cancellationToken).AsTask());
    }

    /// <summary>
    /// The caller stopped waiting. The browser call continues, and the next attempt reaches the browser again.
    /// </summary>
    internal void DetachInFlightRelease(string sessionId)
    {
        lock (_gate)
        {
            _releases.Remove(sessionId);
            _releaseDetached.Add(sessionId);
        }
    }

    public async ValueTask<bool> IsLiveAsync(string sessionId, CancellationToken cancellationToken)
    {
        var hold = FindHold(sessionId);
        if (hold is null)
        {
            return false;
        }

        return await hold.IsLiveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BrowserTrackProcessing> ReadTrackProcessingAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var hold = FindHold(sessionId);
        if (hold is null)
        {
            return BrowserTrackProcessing.Unknown;
        }

        return await hold.ReadTrackProcessingAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FinishReleaseAsync(
        string sessionId,
        BrowserMicrophoneHold hold,
        TaskCompletionSource completion)
    {
        try
        {
            await hold.DisposeAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (_holds.TryGetValue(sessionId, out var current) && ReferenceEquals(current, hold))
                {
                    _holds.Remove(sessionId);
                    _releaseDetached.Remove(sessionId);
                }

                if (_releases.TryGetValue(sessionId, out var pending) && ReferenceEquals(pending, completion.Task))
                {
                    _releases.Remove(sessionId);
                }
            }

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (_releases.TryGetValue(sessionId, out var pending) && ReferenceEquals(pending, completion.Task))
                {
                    _releases.Remove(sessionId);
                }
            }

            completion.TrySetException(exception);
        }
    }

    private BrowserMicrophoneHold? FindHold(string sessionId)
    {
        lock (_gate)
        {
            return _holds.TryGetValue(sessionId, out var hold) ? hold : null;
        }
    }

    private static bool TryReportedFormat(string message, out string reported)
    {
        const string marker = "reported=";
        var index = message.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            reported = string.Empty;
            return false;
        }

        reported = message[(index + marker.Length)..].Trim();
        return reported.Length > 0;
    }
}
