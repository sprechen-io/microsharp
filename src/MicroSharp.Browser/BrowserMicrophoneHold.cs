using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// An opened browser source. Capture can start, end and start again
/// without releasing the source. <see cref="DisposeAsync"/> releases it.
/// </summary>
public sealed class BrowserMicrophoneHold : IAsyncDisposable
{
    private readonly IJSObjectReference _module;
    private readonly BrowserMicrophoneCapture _capture;
    private readonly SemaphoreSlim _ownership = new(1, 1);
    private readonly SemaphoreSlim _releaseGate = new(1, 1);
    private BrowserMicrophoneSession? _running;
    private Task _graphStop = Task.CompletedTask;
    private int _graphGeneration;
    private int _graphStopIssuedFor;
    private int _released;
    private int _disposeRequested;
    private int _startInFlight;

    internal BrowserMicrophoneHold(IJSObjectReference module, BrowserMicrophoneCapture capture, string sessionId)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
    }

    /// <summary>Id of the browser source.</summary>
    public string SessionId { get; }

    /// <summary>
    /// Turns the source track on or off. The value applies before connecting, during capture
    /// and during a release that has not succeeded yet.
    /// </summary>
    public async ValueTask SetSourceEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _released) != 0)
        {
            throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "The browser source has been released.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _module.InvokeVoidAsync("setSourceEnabled", cancellationToken, SessionId, enabled).ConfigureAwait(false);
        }
        catch (JSException)
        {
            throw;
        }
    }

    /// <summary>True when the source audio context and track are still running.</summary>
    public async Task<bool> IsLiveAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _released) != 0)
        {
            return false;
        }

        try
        {
            return await _module.InvokeAsync<bool>("isLive", cancellationToken, SessionId).ConfigureAwait(false);
        }
        catch (JSException)
        {
            throw;
        }
    }

    /// <summary>Reads the processing that was applied. A missing source returns empty values.</summary>
    public async Task<BrowserTrackProcessing> ReadTrackProcessingAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _released) != 0)
        {
            return BrowserTrackProcessing.Unknown;
        }

        try
        {
            var reported = await _module.InvokeAsync<BrowserTrackProcessing>(
                "trackProcessing",
                cancellationToken,
                SessionId).ConfigureAwait(false);
            return reported ?? BrowserTrackProcessing.Unknown;
        }
        catch (JSException)
        {
            throw;
        }
    }

    /// <summary>
    /// Disconnects the capture graph without releasing the source.
    /// Without a running capture the call only disconnects the browser source.
    /// </summary>
    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _released) != 0 || Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        var running = _running;
        if (running is { State: MicrophoneSessionState.Running or MicrophoneSessionState.Faulted })
        {
            await running.StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("disconnect", cancellationToken, SessionId).ConfigureAwait(false);
        }
        catch (JSException)
        {
            throw;
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Connects capture. The source remains after <see cref="IMicrophoneSession.StopAsync"/>.</summary>
    public Task<IMicrophoneSession> StartAsync(
        MicrophoneCaptureRequest request,
        CancellationToken cancellationToken = default)
        => StartAsync(request, releaseDeviceOnStop: false, onCaptureFault: null, cancellationToken);

    internal async Task<IMicrophoneSession> StartAsync(
        MicrophoneCaptureRequest request,
        bool releaseDeviceOnStop,
        Func<Exception, Task>? onCaptureFault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        MicrophoneFormats.EnsureSupported(request);
        if (request.Backpressure != CaptureBackpressure.DropOldest)
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.UnsupportedBackpressure,
                "The browser adapter cannot pause the microphone. Use drop-oldest backpressure.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        // The browser call stays outside the ownership lock so release and the source switch
        // do not wait for it. Only a successful release prevents publication after that.
        BrowserMicrophoneSession? session = null;
        DotNetObjectReference<BrowserMicrophoneSession>? reference = null;
        var generation = 0;
        var previousGraphStop = Task.CompletedTask;
        try
        {
            await _ownership.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfUnavailable();
                if (_startInFlight != 0 || _running is { State: MicrophoneSessionState.Running })
                {
                    throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "A capture session is already running.");
                }

                previousGraphStop = _graphStop;
                generation = ++_graphGeneration;
                _startInFlight = 1;
                session = new BrowserMicrophoneSession(
                    request.Format,
                    request.QueueCapacityChunks,
                    token => StopGraphAsync(generation, releaseDeviceOnStop, token),
                    () => _capture.ClearActive(session));
                reference = DotNetObjectReference.Create(session);
                session.AttachReference(reference);
                session.AttachFaultObserver(onCaptureFault);
                if (!_capture.TryReserve(session))
                {
                    _startInFlight = 0;
                    throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "A capture session is already running.");
                }
            }
            finally
            {
                _ownership.Release();
            }

            // A disconnect of the previous capture that is still running must finish before the new graph is created.
            await previousGraphStop.WaitAsync(cancellationToken).ConfigureAwait(false);
            var started = await _module.InvokeAsync<BrowserStartResult>(
                "start",
                cancellationToken,
                SessionId,
                reference,
                BrowserMicrophoneCapture.FormatPayload(request.Format)).ConfigureAwait(false);
            BrowserMicrophoneCapture.EnsureStartedFormat(request.Format, started);
            await _ownership.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _released) != 0)
                {
                    throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "The browser source has been released.");
                }

                _running = session;
                _startInFlight = 0;
                return session;
            }
            finally
            {
                _ownership.Release();
            }
        }
        catch
        {
            if (session is not null)
            {
                await _ownership.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(_running, session))
                    {
                        _running = null;
                    }

                    _startInFlight = 0;
                }
                finally
                {
                    _ownership.Release();
                }

                try
                {
                    await session.AbandonWithoutBrowserCallAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Releases the browser source. A call that already succeeded or was already disconnected is complete.
    /// If release fails while the browser is still connected, a later call still takes effect.
    /// The browser is called even when a start of the same source has not returned yet.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _released) != 0)
        {
            return;
        }

        await _releaseGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _released) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _disposeRequested, 1);
            Exception? releaseError = null;
            try
            {
                await _module.InvokeVoidAsync("release", CancellationToken.None, SessionId).ConfigureAwait(false);
                Interlocked.Exchange(ref _released, 1);
            }
            catch (JSDisconnectedException)
            {
                Interlocked.Exchange(ref _released, 1);
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Exchange(ref _released, 1);
            }
            catch (JSException exception)
            {
                releaseError = exception;
            }

            await AbandonPublishedAsync().ConfigureAwait(false);
            if (releaseError is not null)
            {
                throw releaseError;
            }
        }
        finally
        {
            _releaseGate.Release();
        }
    }

    private async Task AbandonPublishedAsync()
    {
        await _ownership.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var running = _running;
            _running = null;
            if (running is null)
            {
                return;
            }

            try
            {
                await running.AbandonWithoutBrowserCallAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
            }
        }
        finally
        {
            _ownership.Release();
        }
    }

    private async Task StopGraphAsync(int generation, bool releaseDeviceOnStop, CancellationToken cancellationToken)
    {
        Task? alreadyIssued = null;
        TaskCompletionSource? completed = null;
        await _ownership.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            // Only the capture published last disconnects the shared browser graph.
            if (generation != _graphGeneration)
            {
                return;
            }

            if (_graphStopIssuedFor == generation)
            {
                alreadyIssued = _graphStop;
            }
            else
            {
                completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _graphStop = completed.Task;
                _graphStopIssuedFor = generation;
            }
        }
        finally
        {
            _ownership.Release();
        }

        if (completed is null)
        {
            await alreadyIssued!.ConfigureAwait(false);
            return;
        }

        var failed = false;
        try
        {
            var method = releaseDeviceOnStop ? "stop" : "disconnect";
            try
            {
                await _module.InvokeVoidAsync(method, cancellationToken, SessionId).ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            if (releaseDeviceOnStop)
            {
                Interlocked.Exchange(ref _released, 1);
            }
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (failed)
            {
                await _ownership.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (_graphGeneration == generation && _graphStopIssuedFor == generation)
                    {
                        _graphStopIssuedFor = 0;
                    }
                }
                finally
                {
                    _ownership.Release();
                }
            }

            completed.TrySetResult();
        }
    }

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref _released) != 0 || Volatile.Read(ref _disposeRequested) != 0)
        {
            throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "The browser source has been released.");
        }
    }
}
