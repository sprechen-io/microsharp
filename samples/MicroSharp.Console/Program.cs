using MicroSharp;
using MicroSharp.Native;

if (args is ["--list"])
{
    var listed = new OpenAlMicrophoneCapture(new OpenAlMicrophoneOptions("microsharp-list-only"));
    foreach (var name in listed.ListCaptureDevices())
    {
        Console.WriteLine(name);
    }

    return 0;
}

if (args is ["--device", var deviceName])
{
    var capture = new OpenAlMicrophoneCapture(new OpenAlMicrophoneOptions(deviceName));
    try
    {
        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest());
        Console.WriteLine(session.Format.MediaType);
        await session.StopAsync();
        return 0;
    }
    catch (MicrophoneException exception)
    {
        Console.Error.WriteLine(exception.Code);
        return exception.Code == MicrophoneErrorCode.DeviceNotFound ? 3 : 1;
    }
}

if (args is ["--virtual"])
{
    var capture = new VirtualMicrophoneCapture(new VirtualMicrophoneOptions
    {
        Source = new PcmToneSource(440, PcmFormat.Default),
    });
    await using var session = await capture.StartAsync(new MicrophoneCaptureRequest
    {
        Backpressure = CaptureBackpressure.Wait,
        QueueCapacityChunks = 4,
    });
    await foreach (var chunk in session.ReadChunksAsync())
    {
        Console.WriteLine(chunk.Payload.Length);
        break;
    }

    await session.StopAsync();
    return 0;
}

Console.Error.WriteLine("Usage: --list | --device <openal-name> | --virtual");
return 2;
