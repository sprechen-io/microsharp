using OpenTK.Audio.OpenAL;

namespace MicroSharp.Native;

internal sealed class AlcCaptureDeviceFactory : IOpenAlCaptureDeviceFactory
{
    public IReadOnlyList<string> ListCaptureDevices()
    {
        return Guard(() =>
        {
            var names = ALC.GetString(AlcGetStringList.CaptureDeviceSpecifier);
            if (names is null || names.Count == 0)
            {
                return [];
            }

            return names.Where(static name => !string.IsNullOrWhiteSpace(name)).ToArray();
        });
    }

    public IOpenAlCaptureHandle Open(string deviceName, int sampleRateHz, int bufferSamples)
    {
        return Guard(() =>
        {
            var device = ALC.CaptureOpenDevice(deviceName, sampleRateHz, ALFormat.Mono16, bufferSamples);
            if (device.Equals(ALCaptureDevice.Null))
            {
                throw new MicrophoneException(MicrophoneErrorCode.DeviceNotFound, deviceName);
            }

            return new AlcCaptureHandle(device);
        });
    }

    private static T Guard<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (Exception exception) when (exception is DllNotFoundException or TypeInitializationException)
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.Unavailable,
                "The OpenAL library could not be loaded.",
                exception);
        }
    }
}
