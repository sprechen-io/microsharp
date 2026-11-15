using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Steuert die Mikrofonaufnahme im Browser ueber das mitgelieferte JavaScript-Modul.
/// Auf einem gewoehnlichen Prozess ohne Browserdokument nimmt dieser Adapter nicht auf.
/// </summary>
public sealed class BrowserMicrophoneCapture : IMicrophoneCapture
{
    /// <summary>Stabiler Adaptername.</summary>
    public const string AdapterId = "browser";

    private readonly IJSRuntime _jsRuntime;
    private readonly BrowserMicrophoneOptions _options;
    private readonly object _gate = new();
    private IJSObjectReference? _module;
    private BrowserMicrophoneSession? _active;

    /// <summary>Erstellt den Adapter fuer eine JS-Laufzeit.</summary>
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
            [Preferences()]).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(result?.ErrorName))
        {
            throw BrowserFailureMapper.ToException(result.ErrorName);
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
        var module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await EnsureHostAsync(module, cancellationToken).ConfigureAwait(false);
        var acquired = await InvokeRequiredAsync<BrowserAcquireResult>(
            module,
            "acquire",
            cancellationToken,
            [Preferences()]).ConfigureAwait(false);
        if (acquired is null || string.IsNullOrEmpty(acquired.SessionId) || !string.IsNullOrEmpty(acquired.ErrorName))
        {
            throw BrowserFailureMapper.ToException(acquired?.ErrorName);
        }

        var sessionId = acquired.SessionId;
        BrowserMicrophoneSession? session = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session = new BrowserMicrophoneSession(
                request.Format,
                request.QueueCapacityChunks,
                token => InvokeReleaseAsync(module, "stop", sessionId, token),
                () => ClearActive(session));
            var reference = DotNetObjectReference.Create(session);
            session.AttachReference(reference);
            var started = await module.InvokeAsync<BrowserStartResult>(
                "start",
                cancellationToken,
                [sessionId, reference, FormatPayload(request.Format)]).ConfigureAwait(false);
            EnsureStartedFormat(request.Format, started);
            lock (_gate)
            {
                _active = session;
            }

            return session;
        }
        catch (Exception exception)
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await InvokeReleaseAsync(module, "release", sessionId, CancellationToken.None).ConfigureAwait(false);
            }

            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw;
        }
    }

    private void ClearActive(BrowserMicrophoneSession? session)
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

    private static void EnsureStartedFormat(PcmFormat format, BrowserStartResult? started)
    {
        var milliseconds = (int)format.ChunkDuration.TotalMilliseconds;
        if (started is null
            || started.SampleRateHz != format.SampleRateHz
            || started.Channels != 1
            || started.ChunkMilliseconds != milliseconds)
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.Unavailable,
                "The browser confirmed a different PCM format.");
        }
    }

    private Dictionary<string, object?> Preferences()
        => new()
        {
            ["echoCancellation"] = _options.EchoCancellation,
            ["noiseSuppression"] = _options.NoiseSuppression,
            ["autoGainControl"] = _options.AutoGainControl,
        };

    private static Dictionary<string, object?> FormatPayload(PcmFormat format)
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
