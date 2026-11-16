using Microsoft.JSInterop;
using Microsoft.Playwright;

namespace MicroSharp.Browser.Tests;

internal sealed class BrowserPageRuntime : IJSRuntime, IJSObjectReference, IAsyncDisposable
{
    private readonly IPage _page;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _pumpStop = new();
    private readonly Task _pump;
    private BrowserMicrophoneSession? _sink;

    private BrowserPageRuntime(IPage page)
    {
        _page = page;
        _pump = Task.Run(PumpAsync);
    }

    public static async Task<BrowserPageRuntime> ConnectAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => window.__microsharpReady === true");
        return new BrowserPageRuntime(page);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identifier == "import")
        {
            return (TValue)(object)this;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            object? result = identifier switch
            {
                "describe" => await _page.EvaluateAsync<BrowserHostDescription>("() => window.microsharpE2E.describe()").ConfigureAwait(false),
                "permission" => await _page.EvaluateAsync<BrowserPermissionResult>("() => window.microsharpE2E.permission()").ConfigureAwait(false),
                "requestAccess" => await _page.EvaluateAsync<BrowserAcquireResult>("preferences => window.microsharpE2E.requestAccess(preferences)", args![0]).ConfigureAwait(false),
                "acquire" => await _page.EvaluateAsync<BrowserAcquireResult>("preferences => window.microsharpE2E.acquire(preferences)", args![0]).ConfigureAwait(false),
                "start" => await StartAsync(args).ConfigureAwait(false),
                "stop" => await StopAsync(args).ConfigureAwait(false),
                "release" => await StopAsync(args).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unexpected JavaScript call {identifier}."),
            };
            return result is null ? default! : (TValue)result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pumpStop.Cancel();
        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _pumpStop.Dispose();
        _gate.Dispose();
    }

    private async Task<BrowserStartResult> StartAsync(object?[]? args)
    {
        _sink = ((DotNetObjectReference<BrowserMicrophoneSession>)args![1]!).Value;
        return await _page.EvaluateAsync<BrowserStartResult>(
            "payload => window.microsharpE2E.start(payload.sessionId, payload.format)",
            new Dictionary<string, object?>
            {
                ["sessionId"] = args[0],
                ["format"] = args[2],
            }).ConfigureAwait(false);
    }

    private async Task<object?> StopAsync(object?[]? args)
    {
        await _page.EvaluateAsync("(sessionId) => window.microsharpE2E.stop(sessionId)", args![0]).ConfigureAwait(false);
        await DeliverAsync().ConfigureAwait(false);
        _sink?.OnStopped();
        return null;
    }

    private async Task PumpAsync()
    {
        while (!_pumpStop.IsCancellationRequested)
        {
            try
            {
                await _gate.WaitAsync(_pumpStop.Token).ConfigureAwait(false);
                try
                {
                    await DeliverAsync().ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }

                await Task.Delay(20, _pumpStop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DeliverAsync()
    {
        if (_sink is null)
        {
            return;
        }

        var status = await _page.EvaluateAsync<BrowserPumpStatus>("() => window.microsharpE2E.takeStatus()").ConfigureAwait(false);
        if (!string.IsNullOrEmpty(status?.Fault))
        {
            await _sink.OnCaptureFault(status.Fault).ConfigureAwait(false);
        }

        if (status?.Chunks is null)
        {
            return;
        }

        foreach (var chunk in status.Chunks)
        {
            var bytes = new byte[chunk.Length];
            for (var index = 0; index < chunk.Length; index++)
            {
                bytes[index] = (byte)chunk[index];
            }

            _sink.OnPcmChunk(bytes);
        }
    }
}
