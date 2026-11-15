namespace MicroSharp.Native;

/// <summary>
/// Benennt das OpenAL-Aufnahmegeraet. Ein leerer Name wird abgelehnt, damit nicht das Standardgeraet oeffnet.
/// </summary>
public sealed class OpenAlMicrophoneOptions
{
    /// <summary>Erstellt die Optionen fuer genau einen Geraetenamen.</summary>
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

    /// <summary>Name, der an <c>alcCaptureOpenDevice</c> geht.</summary>
    public string DeviceName { get; }

    /// <summary>Uhr fuer die Lese-Pause. Ohne Angabe wird <see cref="TimeProvider.System"/> verwendet.</summary>
    public TimeProvider? TimeProvider { get; init; }
}
