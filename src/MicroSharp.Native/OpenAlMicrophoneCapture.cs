namespace MicroSharp.Native;

/// <summary>
/// Opens a named OpenAL 1.1 capture device and delivers PCM into the MicroSharp contract.
/// </summary>
public sealed class OpenAlMicrophoneCapture : IMicrophoneCapture
{
    /// <summary>Stable adapter name.</summary>
    public const string AdapterId = "openal";

    private readonly OpenAlMicrophoneOptions _options;
    private readonly IOpenAlCaptureDeviceFactory _devices;
    private readonly object _gate = new();
    private OpenAlMicrophoneSession? _active;

    /// <summary>Creates the adapter for one concrete capture device.</summary>
    public OpenAlMicrophoneCapture(OpenAlMicrophoneOptions options)
        : this(options, new AlcCaptureDeviceFactory())
    {
    }

    internal OpenAlMicrophoneCapture(OpenAlMicrophoneOptions options, IOpenAlCaptureDeviceFactory devices)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
    }

    /// <inheritdoc />
    public string AdapterName => AdapterId;

    /// <inheritdoc />
    public bool CapturesOperatingSystemDevice => true;

    /// <inheritdoc />
    public bool RequiresBrowserHost => false;

    /// <summary>Reads the OpenAL capture list without opening a device.</summary>
    public IReadOnlyList<string> ListCaptureDevices() => _devices.ListCaptureDevices();

    /// <inheritdoc />
    public Task<MicrophonePermission> GetPermissionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var granted = ListCaptureDevices().Contains(_options.DeviceName, StringComparer.Ordinal);
        return Task.FromResult(new MicrophonePermission(
            granted ? MicrophonePermissionState.Granted : MicrophonePermissionState.Denied,
            granted ? AdapterId : "openal-device-absent"));
    }

    /// <inheritdoc />
    public Task<MicrophoneCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<int> rates = MicrophoneFormats.SupportedSampleRatesHz.ToArray();
        return Task.FromResult(new MicrophoneCapabilities(AdapterId, true, false, rates));
    }

    /// <inheritdoc />
    public Task RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ListCaptureDevices().Contains(_options.DeviceName, StringComparer.Ordinal))
        {
            throw new MicrophoneException(MicrophoneErrorCode.DeviceNotFound, _options.DeviceName);
        }

        return Task.CompletedTask;
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
                "OpenAL capture cannot pause the device. Use drop-oldest backpressure.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_active is { State: MicrophoneSessionState.Running })
            {
                throw new MicrophoneException(MicrophoneErrorCode.Unavailable, "A capture session is already running.");
            }
        }

        var bufferSamples = Math.Max(
            checked(request.QueueCapacityChunks * request.Format.SamplesPerChunk),
            request.Format.SampleRateHz);
        IOpenAlCaptureHandle? handle = null;
        OpenAlMicrophoneSession? session = null;
        try
        {
            handle = _devices.Open(_options.DeviceName, request.Format.SampleRateHz, bufferSamples);
            cancellationToken.ThrowIfCancellationRequested();
            handle.Start();
            cancellationToken.ThrowIfCancellationRequested();
            var opened = handle;
            session = new OpenAlMicrophoneSession(
                request,
                opened,
                _options.TimeProvider ?? TimeProvider.System,
                () => ClearActive(session));
            handle = null;
            lock (_gate)
            {
                _active = session;
            }

            return session;
        }
        catch
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                handle?.Stop();
                handle?.Dispose();
            }

            throw;
        }
    }

    private void ClearActive(OpenAlMicrophoneSession? session)
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
