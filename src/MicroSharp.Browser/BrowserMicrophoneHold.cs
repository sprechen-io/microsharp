using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Eine geoeffnete Browserquelle. Die Aufnahme kann starten, enden und erneut starten,
/// ohne die Quelle freizugeben. <see cref="DisposeAsync"/> gibt sie frei.
/// </summary>
public sealed class BrowserMicrophoneHold : IAsyncDisposable
{
    private readonly IJSObjectReference _module;
    private readonly BrowserMicrophoneCapture _capture;
    private readonly SemaphoreSlim _ownership = new(1, 1);
    private BrowserMicrophoneSession? _running;
    private int _released;
    private int _disposeRequested;

    internal BrowserMicrophoneHold(IJSObjectReference module, BrowserMicrophoneCapture capture, string sessionId)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
    }

    /// <summary>Kennung der Browserquelle.</summary>
    public string SessionId { get; }

    /// <summary>Schaltet die Quellspur ein oder aus. Der Wert gilt vor dem Verbinden und waehrend der Aufnahme.</summary>
    public async ValueTask SetSourceEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        try
        {
            await _module.InvokeVoidAsync("setSourceEnabled", cancellationToken, SessionId, enabled).ConfigureAwait(false);
        }
        catch (JSException)
        {
            throw;
        }
    }

    /// <summary>Wahr, wenn Audiokontext und Spur der Quelle noch laufen.</summary>
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

    /// <summary>Liest die angewendete Verarbeitung. Eine fehlende Quelle liefert leere Werte.</summary>
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

    /// <summary>Verbindet die Aufnahme. Die Quelle bleibt nach <see cref="IMicrophoneSession.StopAsync"/> erhalten.</summary>
    public Task<IMicrophoneSession> StartAsync(
        MicrophoneCaptureRequest request,
        CancellationToken cancellationToken = default)
        => StartAsync(request, releaseDeviceOnStop: false, cancellationToken);

    internal async Task<IMicrophoneSession> StartAsync(
        MicrophoneCaptureRequest request,
        bool releaseDeviceOnStop,
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
        await _ownership.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            if (_running is { State: MicrophoneSessionState.Running })
            {
                throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "A capture session is already running.");
            }

            BrowserMicrophoneSession? session = null;
            session = new BrowserMicrophoneSession(
                request.Format,
                request.QueueCapacityChunks,
                token => StopGraphAsync(releaseDeviceOnStop, token),
                () => _capture.ClearActive(session));
            var reference = DotNetObjectReference.Create(session);
            session.AttachReference(reference);
            try
            {
                if (!_capture.TryReserve(session))
                {
                    throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "A capture session is already running.");
                }

                var started = await _module.InvokeAsync<BrowserStartResult>(
                    "start",
                    cancellationToken,
                    SessionId,
                    reference,
                    BrowserMicrophoneCapture.FormatPayload(request.Format)).ConfigureAwait(false);
                BrowserMicrophoneCapture.EnsureStartedFormat(request.Format, started);
                ThrowIfUnavailable();
                _running = session;
                return session;
            }
            catch
            {
                await session.AbandonWithoutBrowserCallAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _ownership.Release();
        }
    }

    /// <summary>
    /// Gibt die Browserquelle frei. Ein erfolgreicher oder bereits getrennter Aufruf ist abgeschlossen.
    /// Schlaegt die Freigabe im noch verbundenen Browser fehl, bleibt ein erneuter Aufruf wirksam.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _released) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _disposeRequested, 1);
        await _ownership.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _released) != 0)
            {
                return;
            }

            var running = _running;
            _running = null;
            if (running is not null)
            {
                try
                {
                    await running.AbandonWithoutBrowserCallAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                }
            }

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
        }
        finally
        {
            _ownership.Release();
        }
    }

    private async Task StopGraphAsync(bool releaseDeviceOnStop, CancellationToken cancellationToken)
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

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref _released) != 0 || Volatile.Read(ref _disposeRequested) != 0)
        {
            throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "The browser source has been released.");
        }
    }
}
