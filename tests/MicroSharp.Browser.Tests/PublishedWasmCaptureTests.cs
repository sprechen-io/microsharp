using Microsoft.Playwright;

namespace MicroSharp.Browser.Tests;

public sealed class PublishedWasmCaptureTests
{
    [Fact(Timeout = 120_000)]
    public async Task PublishedSampleShowsSimulatedCapture()
    {
        var root = Environment.GetEnvironmentVariable("MICROSHARP_WASM_PUBLISH");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.WriteLine("MICROSHARP_WASM_PUBLISH is unset, so the published sample was not exercised.");
            return;
        }

        Assert.True(File.Exists(Path.Combine(root, "index.html")), "Published index.html is missing.");
        var wavPath = Path.Combine(Path.GetTempPath(), "microsharp-sim-440.wav");
        PcmWavFile.WriteSine(wavPath, 48_000, 440, seconds: 2, amplitude: 0.8);
        await using var server = new PublishedSiteServer(root);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args =
            [
                "--use-fake-device-for-media-stream",
                "--use-fake-ui-for-media-stream",
                $"--use-file-for-fake-audio-capture={wavPath}",
                "--autoplay-policy=no-user-gesture-required",
            ],
        });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(server.Root.AbsoluteUri, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60_000,
        });
        await page.Locator("#mic-start").ClickAsync(new LocatorClickOptions { Timeout = 90_000 });
        // The fake device can render one silent block before the tone. Wait for energy.
        await page.WaitForFunctionAsync(
            "() => { const chunks = document.querySelector('#mic-chunks'); const peak = document.querySelector('#mic-peak'); return chunks !== null && peak !== null && chunks.textContent !== '0' && peak.textContent !== '0'; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        var chunks = await page.Locator("#mic-chunks").InnerTextAsync();
        var peak = await page.Locator("#mic-peak").InnerTextAsync();
        await page.Locator("#mic-stop").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#mic-status')?.textContent === 'stopped'",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.Locator("#mic-release").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#mic-status')?.textContent === 'released'",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        Assert.True(int.Parse(chunks) >= 1);
        Assert.True(int.Parse(peak) > 0);
    }
}
