using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Controls microphone capture in the browser through the included JavaScript module.
/// On an ordinary process without a browser document this adapter does not capture.
/// </summary>
public sealed class BrowserMicrophoneCapture : IMicrophoneCapture
{
    /// <summary>Stable adapter name.</summary>
    public const string AdapterId = "browser";

    private readonly IJSRuntime _jsRuntime;
    private readonly BrowserMicrophoneOptions _options;
    private readonly object _gate = new();
    private IJSObjectReference? _module;
    private BrowserMicrophoneSession? _active;

    /// <summary>Creates the adapter for a JS runtime.</summary>
    public BrowserMicrophoneCapture(IJSRuntime jsRuntime, BrowserMicrophoneOptions? options = null)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
        _options = options ?? new BrowserMicrophoneOptions();
        if (string.IsNullOrWhiteSpace(_options.ModulePath))
        {
            throw new ArgumentException("The browser module path is required.", nameof(options));
        }
    }

    /// <inheritdoc />
    public string AdapterName => AdapterId;

    /// <inheritdoc />
    public bool CapturesOperatingSystemDevice => false;

    /// <inheritdoc />
    public bool RequiresBrowserHost => true;

    /// <inheritdoc />
    public async Task<MicrophonePermission> GetPermissionAsync(CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await EnsureHostAsync(module, cancellationToken).ConfigureAwait(false);
        var result = await InvokeRequiredAsync<BrowserPermissionResult>(
            module,
            "permission",
            cancellationToken,
            []).ConfigureAwait(false);
        return new MicrophonePermission(MapPermission(result?.State), result?.State);
    }

    /// <inheritdoc />
    public Task<MicrophoneCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<int> rates = MicrophoneFormats.SupportedSampleRatesHz.ToArray();
        return Task.FromResult(new MicrophoneCapabilities(AdapterId, false, true, rates));
    }

    /// <inheritdoc />
    public async Task RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await EnsureHostAsync(module, cancellationToken).ConfigureAwait(false);
        var result = await InvokeRequiredAsync<BrowserAcquireResult>(
            module,
            "requestAccess",
            cancellationToken,
            [Preferences(_options.KeepAudioContextAlive)]).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(result?.ErrorName))
        {
            throw BrowserFailureMapper.ToException(result.ErrorName);
        }
    }

    /// <summary>
    /// Opens the browser source without connecting it.
    /// An error named by the browser stays in the result and is not thrown here.
    /// </summary>
    public async Task<BrowserPrepareOutcome> PrepareAsync(
        bool keepAudioContextAlive,
        CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await EnsureHostAsync(module, cancellationToken).ConfigureAwait(false);
        BrowserAcquireResult? acquired;
        try
        {
            acquired = await module.InvokeAsync<BrowserAcquireResult>(
                "acquire",
                cancellationToken,
                Preferences(keepAudioContextAlive)).ConfigureAwait(false);
        }
        catch (JSException)
        {
            throw;
        }
        if (acquired is null || string.IsNullOrEmpty(acquired.SessionId) || !string.IsNullOrEmpty(acquired.ErrorName))
        {
            return new BrowserPrepareOutcome(acquired ?? new BrowserAcquireResult(), null);
        }

        return new BrowserPrepareOutcome(
            acquired,
            new BrowserMicrophoneHold(module, this, acquired.SessionId));
    }

    /// <summary>
    /// Releases a browser id even when it never became a held source.
    /// An error in a browser that is still connected stays visible.
    /// </summary>
    public async ValueTask ReleaseBrowserSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        var module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await module.InvokeVoidAsync("release", cancellationToken, sessionId).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<IMicrophoneSession> StartAsync(
        MicrophoneCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        MicrophoneFormats.EnsureSupported(request);
        if (request.Backpressure != CaptureBackpressure.DropOldest)
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.UnsupportedBackpressure,
                "The browser adapter cannot pause the microphone. Use drop-oldest backpressure.");
        }

        lock (_gate)
        {
            if (_active is { State: MicrophoneSessionState.Running })
            {
                throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "A capture session is already running.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var prepared = await PrepareAsync(_options.KeepAudioContextAlive, cancellationToken).ConfigureAwait(false);
        if (prepared.Hold is null)
        {
            throw BrowserFailureMapper.ToException(prepared.Result.ErrorName);
        }

        try
        {
            return await prepared.Hold.StartAsync(request, releaseDeviceOnStop: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await prepared.Hold.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal bool TryReserve(BrowserMicrophoneSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            if (_active is { State: MicrophoneSessionState.Running } && !ReferenceEquals(_active, session))
            {
                return false;
            }

            _active = session;
            return true;
        }
    }

    internal void ClearActive(BrowserMicrophoneSession? session)
    {
        if (session is null)
        {
            return;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_active, session))
            {
                _active = null;
            }
        }
    }

    private async Task<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken)
    {
        if (_module is not null)
        {
            return _module;
        }

        try
        {
            _module = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                [_options.ModulePath]).ConfigureAwait(false);
        }
        catch (JSException exception)
        {
            throw new MicrophoneException(MicrophoneErrorCode.Unavailable, exception.Message, exception);
        }

        return _module;
    }

    private static async Task EnsureHostAsync(IJSObjectReference module, CancellationToken cancellationToken)
    {
        var host = await InvokeRequiredAsync<BrowserHostDescription>(
            module,
            "describe",
            cancellationToken,
            []).ConfigureAwait(false);
        if (host is null || !host.Media || !host.Audio || !host.Worklet)
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.Unavailable,
                "Browser microphone APIs are not available in this host.");
        }
    }

    internal static void EnsureStartedFormat(PcmFormat format, BrowserStartResult? started)
    {
        var milliseconds = (int)format.ChunkDuration.TotalMilliseconds;
        if (started is null
            || started.SampleRateHz != format.SampleRateHz
            || started.Channels != 1
            || started.ChunkMilliseconds != milliseconds)
        {
            var reported = started is null || started.SampleRateHz <= 0
                ? "audio/pcm"
                : $"audio/pcm;rate={started.SampleRateHz}";
            throw new MicrophoneException(
                MicrophoneErrorCode.Unavailable,
                $"The browser confirmed a different PCM format. reported={reported}");
        }
    }

    private Dictionary<string, object?> Preferences(bool keepAudioContextAlive)
        => new()
        {
            ["echoCancellation"] = _options.EchoCancellation,
            ["noiseSuppression"] = _options.NoiseSuppression,
            ["autoGainControl"] = _options.AutoGainControl,
            ["keepAudioContextAlive"] = keepAudioContextAlive,
        };

    internal static Dictionary<string, object?> FormatPayload(PcmFormat format)
        => new()
        {
            ["targetSampleRate"] = format.SampleRateHz,
            ["chunkMilliseconds"] = (int)format.ChunkDuration.TotalMilliseconds,
        };

    private static async Task InvokeReleaseAsync(
        IJSObjectReference module,
        string method,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await module.InvokeVoidAsync(
                method,
                cancellationToken,
                [sessionId]).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task<T> InvokeRequiredAsync<T>(
        IJSObjectReference module,
        string identifier,
        CancellationToken cancellationToken,
        object?[] args)
    {
        try
        {
            return await module.InvokeAsync<T>(identifier, cancellationToken, args).ConfigureAwait(false);
        }
        catch (JSException exception)
        {
            throw new MicrophoneException(MicrophoneErrorCode.Unavailable, exception.Message, exception);
        }
    }

    private static MicrophonePermissionState MapPermission(string? state)
        => state switch
        {
            "granted" => MicrophonePermissionState.Granted,
            "denied" => MicrophonePermissionState.Denied,
            "prompt" => MicrophonePermissionState.Prompt,
            _ => MicrophonePermissionState.Unknown,
        };
}
