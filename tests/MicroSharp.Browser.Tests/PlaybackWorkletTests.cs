using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace MicroSharp.Browser.Tests;

public sealed class PlaybackWorkletTests(ITestOutputHelper output)
{
    [Theory(Timeout = 60_000)]
    [InlineData(48_000, 48_000, 4_800, 1_000, false)]
    [InlineData(24_000, 44_100, 24_000, 997, false)]
    [InlineData(24_000, 44_100, 24_000, 997, true)]
    [InlineData(24_000, 48_000, 24_000, 997, false)]
    [InlineData(24_000, 48_000, 24_000, 997, true)]
    [InlineData(48_000, 24_000, 48_000, 997, false)]
    [InlineData(48_000, 24_000, 48_000, 997, true)]
    public async Task RendersPcmAndZeroFillsTheRemainder(
        int inputRate, int outputRate, int inputFrames, int toneHz, bool split)
    {
        const int quantum = 128;
        const double initialPhase = 0.37;
        var input = Enumerable.Range(0, inputFrames)
            .Select(frame => (short)Math.Round(16_384 * Math.Sin(2 * Math.PI * toneHz * frame / inputRate + initialPhase)))
            .ToArray();
        var contentFrames = 1 + (int)((inputFrames - 1L) * outputRate / inputRate);
        var checkpoint = quantum + ((contentFrames + quantum - 1) / quantum + 1) * quantum;

        await using var server = new LoopbackStaticServer(FindWwwRoot(), "<!doctype html><title>Playback worklet test</title>");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Timeout = 15_000 });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(server.Root.AbsoluteUri, new() { Timeout = 15_000 });
        var renderJson = await page.EvaluateAsync<string>(
            """
            async ({ input, inputRate, outputRate, quantum, checkpoint, split }) => {
                const context = new OfflineAudioContext(1, checkpoint + quantum, outputRate);
                function withTimeout(promise, operation) {
                    let timer;
                    const timeout = new Promise((_, reject) => {
                        timer = setTimeout(() => reject(new Error(operation + " timed out")), 8_000);
                    });
                    return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
                }

                await withTimeout(context.audioWorklet.addModule("./pcm-playback-processor.js"), "load worklet");
                const generation = 1;
                const node = new AudioWorkletNode(context, "microsharp-pcm-playback", {
                    numberOfInputs: 0,
                    outputChannelCount: [1],
                    processorOptions: { inputRate, capacity: input.length, generation }
                });
                node.connect(context.destination);

                let pendingSnapshot;
                node.port.onmessage = ({ data }) => {
                    if (data.kind === "credit") {
                        node.port.postMessage({ kind: "creditAck", generation });
                    } else if (data.kind === "snapshot" && pendingSnapshot?.request === data.request) {
                        pendingSnapshot.resolve(data);
                    }
                };
                function snapshot(request) {
                    const reply = new Promise(resolve => { pendingSnapshot = { request, resolve }; });
                    node.port.postMessage({ kind: "snapshot", generation, request });
                    return withTimeout(reply, "snapshot " + request);
                }
                function postPcm() {
                    const splitSizes = [1, 127, 3, 257, 64, 511];
                    for (let offset = 0, block = 0; offset < input.length; block++) {
                        const size = split ? splitSizes[block % splitSizes.length] : input.length;
                        const frames = Math.min(size, input.length - offset);
                        const bytes = new Uint8Array(frames * 2);
                        const view = new DataView(bytes.buffer);
                        for (let index = 0; index < frames; index++) {
                            view.setInt16(index * 2, input[offset + index], true);
                        }
                        node.port.postMessage({ kind: "pcm", generation, bytes }, [bytes.buffer]);
                        offset += frames;
                    }
                }

                const beforeContent = context.suspend(quantum / outputRate);
                const afterContent = context.suspend(checkpoint / outputRate);
                const rendering = context.startRendering();
                await withTimeout(beforeContent, "suspend before content");
                postPcm();
                // The port delivers messages in order. This reply acknowledges every prefill block.
                const before = await snapshot(1);
                const startFrame = Math.round(context.currentTime * context.sampleRate);
                await withTimeout(context.resume(), "resume with prefill");
                await withTimeout(afterContent, "suspend after content");
                const endFrame = Math.round(context.currentTime * context.sampleRate);
                const after = await snapshot(2);
                await withTimeout(context.resume(), "resume to complete render");
                const audio = await withTimeout(rendering, "complete render");
                return JSON.stringify({ Rate: audio.sampleRate, StartFrame: startFrame, EndFrame: endFrame,
                    Before: [before.contentOutputFrames, before.underrunOutputFrames],
                    After: [after.contentOutputFrames, after.underrunOutputFrames],
                    Samples: Array.from(audio.getChannelData(0)) });
            }
            """,
            new { input, inputRate, outputRate, quantum, checkpoint, split });
        var render = JsonSerializer.Deserialize<RenderResult>(renderJson)!;

        try
        {
            Assert.Equal(outputRate, render.Rate);
            Assert.Equal(quantum, render.StartFrame);
            Assert.Equal(checkpoint, render.EndFrame);
            Assert.Equal(new[] { 0, quantum }, render.Before);
            Assert.Equal(new[] { contentFrames, checkpoint - contentFrames }, render.After);
            Assert.Equal(checkpoint + quantum, render.Samples.Length);
            Assert.All(render.Samples[..quantum], sample => Assert.Equal(0, sample));
            Assert.All(render.Samples[(quantum + contentFrames)..], sample => Assert.Equal(0, sample));

            double maxError = 0;
            var tolerance = inputRate == outputRate ? 0 : 2e-6;
            for (var frame = 0; frame < contentFrames; frame++)
            {
                var source = (double)frame * inputRate / outputRate;
                var left = (int)source;
                var right = Math.Min(left + 1, input.Length - 1);
                var expected = (input[left] + (input[right] - input[left]) * (source - left)) / 32768;
                var actual = render.Samples[quantum + frame];
                var error = Math.Abs(actual - expected);
                Assert.True(error <= tolerance, $"Frame {frame}: expected {expected:G17}, got {actual:G17}.");
                maxError = Math.Max(maxError, error);
            }

            output.WriteLine($"Chromium {browser.Version}, {inputRate} → {render.Rate} Hz, {toneHz} Hz tone, phase {initialPhase}, split={split}, content={render.After[0]}, silence={render.After[1] - render.Before[1]} measured before final quantum, max error={maxError:G9}.");
        }
        catch (Xunit.Sdk.XunitException)
        {
            var evidence = Path.Combine(Path.GetTempPath(), $"microsharp-playback-render-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new { browser.Version, inputRate, outputRate, toneHz, initialPhase, split, input, render }));
            output.WriteLine($"Rendered sample evidence: {evidence}");
            throw;
        }
    }

    private static string FindWwwRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MicroSharp.Browser.Playback", "wwwroot");
            if (File.Exists(Path.Combine(candidate, "pcm-playback-processor.js")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Playback worklet wwwroot.");
    }

    private sealed record RenderResult(int Rate, int StartFrame, int EndFrame, int[] Before, int[] After, double[] Samples);
}
