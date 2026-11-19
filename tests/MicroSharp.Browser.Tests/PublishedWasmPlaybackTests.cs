using System.Collections.Concurrent;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace MicroSharp.Browser.Tests;

public sealed class PublishedWasmPlaybackTests(ITestOutputHelper output)
{
    [Fact(Timeout = 120_000)]
    public async Task PublishedPlaybackLoadsItsWorkletAndRendersToneAcrossRestarts()
    {
        var site = Environment.GetEnvironmentVariable("MICROSHARP_PLAYBACK_PUBLISH_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(site), "Publish the Playback sample below playback/ and set MICROSHARP_PLAYBACK_PUBLISH_ROOT.");
        var served = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        await using var server = new PublishedSiteServer(site!, (path, statusCode) => served[path] = statusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Timeout = 15_000 });
        var page = await browser.NewPageAsync();
        await page.AddInitScriptAsync("""
            (() => {
                const nativeConnect = AudioNode.prototype.connect;
                AudioNode.prototype.connect = function (...args) {
                    const result = nativeConnect.apply(this, args);
                    if (this instanceof AudioWorkletNode && args[0] === this.context.destination) {
                        const analyser = this.context.createAnalyser();
                        const silent = this.context.createGain();
                        silent.gain.value = 0;
                        nativeConnect.call(this, analyser);
                        nativeConnect.call(analyser, silent);
                        nativeConnect.call(silent, this.context.destination);
                        window.playbackTestAnalyser = analyser;
                    }
                    return result;
                };
            })();
            """);
        await page.GotoAsync(new Uri(server.Root, "playback/index.html").AbsoluteUri, new() { Timeout = 30_000 });
        var status = page.Locator("#playback-status");
        await Assertions.Expect(status).ToHaveTextAsync("Ready", new() { Timeout = 20_000 });

        async Task StartAndRenderAsync()
        {
            await page.EvaluateAsync("() => { window.playbackTestAnalyser = null; }");
            await page.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
            await Assertions.Expect(status).ToHaveTextAsync("Playing", new() { Timeout = 10_000 });
            await page.GetByRole(AriaRole.Button, new() { Name = "Enqueue tone", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("""
                () => {
                    const a = window.playbackTestAnalyser;
                    if (!a) return false;
                    const samples = new Float32Array(a.fftSize);
                    a.getFloatTimeDomainData(samples);
                    return samples.some(x => Math.abs(x) > 0.10);
                }
                """, options: new() { Timeout = 10_000 });
            await Assertions.Expect(page.Locator("#accepted-input-frames")).ToHaveTextAsync("12000");
            await Assertions.Expect(page.Locator("#rejected-write-frames")).ToHaveTextAsync("0");
        }

        await StartAndRenderAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Stop", Exact = true }).ClickAsync();
        await Assertions.Expect(status).ToHaveTextAsync("Stopped");
        await Assertions.Expect(page.Locator("#output-rate")).ToHaveTextAsync("0");
        await StartAndRenderAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Dispose", Exact = true }).ClickAsync();
        await Assertions.Expect(status).ToHaveTextAsync("Disposed");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true })).ToBeDisabledAsync();

        // Chromium does not expose the AudioWorklet fetch through Playwright's page response event.
        foreach (var asset in new[] { "microsharp-playback.js", "pcm-playback-processor.js" })
        {
            var path = "/playback/_content/MicroSharp.Browser.Playback/" + asset;
            Assert.True(served.TryGetValue(path, out var statusCode), $"No request reached the server for {path}.");
            Assert.Equal(200, statusCode);
        }
        output.WriteLine($"Chromium {browser.Version}: module and worklet under /playback/, two rendered tones, stop and dispose verified.");
    }
}
