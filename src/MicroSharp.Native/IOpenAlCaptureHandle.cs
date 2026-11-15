namespace MicroSharp.Native;

internal interface IOpenAlCaptureHandle : IDisposable
{
    void Start();

    int AvailableSamples { get; }

    void Read(short[] samples);

    void Stop();
}
