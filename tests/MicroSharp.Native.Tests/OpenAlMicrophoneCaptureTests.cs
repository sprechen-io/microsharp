namespace MicroSharp.Native.Tests;

public sealed class OpenAlMicrophoneCaptureTests
{
    private const string AbsentDeviceName = "microsharp-absent-capture-device";

    [Fact]
    public void TheAdapterDeclaresOperatingSystemCapture()
    {
        var capture = new OpenAlMicrophoneCapture(new OpenAlMicrophoneOptions(AbsentDeviceName));

        Assert.Equal(OpenAlMicrophoneCapture.AdapterId, capture.AdapterName);
        Assert.True(capture.CapturesOperatingSystemDevice);
        Assert.False(capture.RequiresBrowserHost);
    }

    [Fact]
    public async Task AnExplicitAbsentDeviceNameFailsWithoutLeavingASession()
    {
        var capture = new OpenAlMicrophoneCapture(new OpenAlMicrophoneOptions(AbsentDeviceName));
        var permission = await capture.GetPermissionAsync();
        Assert.NotEqual(MicrophonePermissionState.Granted, permission.State);

        await AssertDeviceNotFoundAsync(capture);
        await AssertDeviceNotFoundAsync(capture);
    }

    private static async Task AssertDeviceNotFoundAsync(OpenAlMicrophoneCapture capture)
    {
        IMicrophoneSession? session = null;
        try
        {
            session = await capture.StartAsync(new MicrophoneCaptureRequest());
            Assert.Fail("The absent device name opened a capture device.");
        }
        catch (MicrophoneException exception)
        {
            Assert.Equal(MicrophoneErrorCode.DeviceNotFound, exception.Code);
            Assert.Equal(AbsentDeviceName, exception.Detail);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }
    }
}
