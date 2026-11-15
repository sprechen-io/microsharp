using Microsoft.Playwright;

namespace MicroSharp.Browser.Tests;

public sealed class BrowserCaptureEndToEndTests
{
    [Fact(Timeout = 120_000)]
    public async Task SimulatedMicrophoneProducesPcmThroughTheBrowserModule()
    {
        var wwwroot = FindWwwRoot();
        var wavPath = Path.Combine(Path.GetTempPath(), "microsharp-sim-440.wav");
        PcmWavFile.WriteSine(wavPath, 48_000, 440, seconds: 2, amplitude: 0.8);
        var launchArgs = new[]
        {
            "--use-fake-device-for-media-stream",
            "--use-fake-ui-for-media-stream",
            $"--use-file-for-fake-audio-capture={wavPath}",
            "--autoplay-policy=no-user-gesture-required",
        };
        Assert.Contains("--use-fake-device-for-media-stream", launchArgs);
        Assert.Contains(launchArgs, arg => arg.StartsWith("--use-file-for-fake-audio-capture=", StringComparison.Ordinal) && arg.EndsWith(Path.GetFileName(wavPath), StringComparison.Ordinal));

        await using var server = new LoopbackStaticServer(wwwroot);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = launchArgs,
        });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(server.Root.AbsoluteUri);
        await using var runtime = await BrowserPageRuntime.ConnectAsync(page);
        var capture = new BrowserMicrophoneCapture(runtime, new BrowserMicrophoneOptions
        {
            ModulePath = "./microsharp-capture.js",
        });

        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None);
        var chunks = new List<PcmChunk>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await foreach (var chunk in session.ReadChunksAsync(timeout.Token))
        {
            chunks.Add(chunk);
            if (chunks.Count == 8)
            {
                break;
            }
        }

        await session.StopAsync();

        Assert.Equal(8, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal(PcmFormat.Default.BytesPerChunk, chunk.Payload.Length));
        var samples = PcmEncoding.ToSamples(chunks.SelectMany(chunk => chunk.Payload).ToArray());
        var rms = Math.Sqrt(samples.Average(sample => sample * (double)sample));
        var atTone = TonePower.At(samples, 16_000, 440);
        var atOther = TonePower.At(samples, 16_000, 1000);
        Assert.True(rms > 1000, $"RMS {rms} is too low for the simulated tone.");
        Assert.True(atTone > atOther * 4, $"440 Hz power {atTone} did not dominate 1000 Hz power {atOther}.");
    }

    private static string FindWwwRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MicroSharp.Browser", "wwwroot");
            if (File.Exists(Path.Combine(candidate, "microsharp-capture.js")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The browser capture module was not found.");
    }
}
