namespace MicroSharp.Native.Tests;

internal sealed class RecordingOpenAlFactory : IOpenAlCaptureDeviceFactory
{
    public RecordingOpenAlFactory(RecordingOpenAlHandle handle)
    {
        Handle = handle;
    }

    public RecordingOpenAlHandle Handle { get; }

    public List<string> Devices { get; } = [];

    public int OpenCount { get; private set; }

    public bool FailOpen { get; set; }

    public Action? OnOpen { get; set; }

    public string? OpenedName { get; private set; }

    public int OpenedRate { get; private set; }

    public int OpenedBuffer { get; private set; }

    public IReadOnlyList<string> ListCaptureDevices() => Devices;

    public IOpenAlCaptureHandle Open(string deviceName, int sampleRateHz, int bufferSamples)
    {
        OpenCount++;
        OpenedName = deviceName;
        OpenedRate = sampleRateHz;
        OpenedBuffer = bufferSamples;
        OnOpen?.Invoke();
        if (FailOpen)
        {
            throw new MicrophoneException(MicrophoneErrorCode.DeviceNotFound, deviceName);
        }

        return Handle;
    }
}
