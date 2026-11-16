namespace MicroSharp.Native;

/// <summary>
/// Names the OpenAL capture device. An empty name is rejected so the default device is not opened.
/// </summary>
public sealed class OpenAlMicrophoneOptions
{
    /// <summary>Creates the options for exactly one device name.</summary>
    public OpenAlMicrophoneOptions(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentException(
                "An explicit OpenAL capture device name is required. The default device is not opened.",
                nameof(deviceName));
        }

        DeviceName = deviceName;
    }

    /// <summary>Name passed to <c>alcCaptureOpenDevice</c>.</summary>
    public string DeviceName { get; }

    /// <summary>Clock for the read pause. When omitted, <see cref="TimeProvider.System"/> is used.</summary>
    public TimeProvider? TimeProvider { get; init; }
}
