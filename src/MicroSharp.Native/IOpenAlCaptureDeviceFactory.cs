namespace MicroSharp.Native;

internal interface IOpenAlCaptureDeviceFactory
{
    IReadOnlyList<string> ListCaptureDevices();

    IOpenAlCaptureHandle Open(string deviceName, int sampleRateHz, int bufferSamples);
}
