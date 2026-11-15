using OpenTK.Audio.OpenAL;

namespace MicroSharp.Native;

internal sealed class AlcCaptureHandle : IOpenAlCaptureHandle
{
    private readonly ALCaptureDevice _device;
    private bool _started;
    private bool _closed;

    public AlcCaptureHandle(ALCaptureDevice device)
    {
        if (device.Equals(ALCaptureDevice.Null))
        {
            throw new ArgumentException("An OpenAL capture device is required.", nameof(device));
        }

        _device = device;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        ALC.CaptureStart(_device);
        _started = true;
    }

    public int AvailableSamples
    {
        get
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return ALC.GetInteger(_device, AlcGetInteger.CaptureSamples);
        }
    }

    public void Read(short[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ObjectDisposedException.ThrowIf(_closed, this);
        ALC.CaptureSamples(_device, samples, samples.Length);
    }

    public void Stop()
    {
        if (_closed || !_started)
        {
            return;
        }

        ALC.CaptureStop(_device);
        _started = false;
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        Stop();
        _closed = true;
        if (!ALC.CaptureCloseDevice(_device))
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.Unavailable,
                "OpenAL did not close the capture device.");
        }
    }
}
