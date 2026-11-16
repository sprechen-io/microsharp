namespace MicroSharp.Native;

/// <summary>
/// Delivers PCM from an in-process source. This adapter does not open an operating-system microphone.
/// </summary>
public sealed class VirtualMicrophoneCapture : IMicrophoneCapture
{
    /// <summary>Stable adapter name.</summary>
    public const string AdapterId = "virtual-pcm";

    private readonly VirtualMicrophoneOptions _options;
    private readonly object _gate = new();
    private VirtualMicrophoneSession? _active;

    /// <summary>Creates the adapter for a virtual PCM source.</summary>
    public VirtualMicrophoneCapture(VirtualMicrophoneOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.Source is null)
        {
            throw new ArgumentException("A PCM source is required.", nameof(options));
        }
    }

    /// <inheritdoc />
    public string AdapterName => AdapterId;

    /// <inheritdoc />
    public bool CapturesOperatingSystemDevice => false;

    /// <inheritdoc />
    public bool RequiresBrowserHost => false;

    /// <inheritdoc />
    public Task<MicrophonePermission> GetPermissionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MicrophonePermission(_options.Permission, "virtual-pcm"));
    }

    /// <inheritdoc />
    public Task<MicrophoneCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<int> rates = MicrophoneFormats.SupportedSampleRatesHz.ToArray();
        return Task.FromResult(new MicrophoneCapabilities(AdapterId, false, false, rates));
    }

    /// <inheritdoc />
    public Task RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IMicrophoneSession> StartAsync(
        MicrophoneCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        MicrophoneFormats.EnsureSupported(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();
        if (!_options.Source.Format.Matches(request.Format))
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.UnsupportedFormat,
                "The virtual source format does not match the capture request.");
        }

        lock (_gate)
        {
            if (_active is { State: MicrophoneSessionState.Running })
            {
                throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "A capture session is already running.");
            }

            VirtualMicrophoneSession? session = null;
            session = new VirtualMicrophoneSession(
                request,
                _options.Source,
                _options.PaceInRealTime,
                _options.TimeProvider ?? TimeProvider.System,
                () => ClearActive(session));
            _active = session;
            return Task.FromResult<IMicrophoneSession>(session);
        }
    }

    private void EnsureAvailable()
    {
        if (_options.DeviceMissing)
        {
            throw new MicrophoneException(MicrophoneErrorCode.DeviceNotFound, "virtual-pcm");
        }

        if (_options.Permission != MicrophonePermissionState.Granted)
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.PermissionDenied,
                _options.Permission.ToString());
        }
    }

    private void ClearActive(VirtualMicrophoneSession? session)
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
}
