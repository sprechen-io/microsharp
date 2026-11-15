using Microsoft.JSInterop;

namespace MicroSharp.Browser.Tests;

internal sealed class ScriptedJsModule : IJSRuntime, IJSObjectReference
{
    public List<string> Calls { get; } = [];

    public bool HostAvailable { get; set; } = true;

    public string PermissionState { get; set; } = "granted";

    public string? AcquireError { get; set; }

    public bool DelayStartUntilCancelled { get; set; }

    public int? StartSampleRateHz { get; set; }

    public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DotNetObjectReference<BrowserMicrophoneSession>? SessionReference { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add(identifier);
        cancellationToken.ThrowIfCancellationRequested();
        object? result = identifier switch
        {
            "import" => this,
            "describe" => new BrowserHostDescription
            {
                Media = HostAvailable,
                Audio = HostAvailable,
                Worklet = HostAvailable,
            },
            "permission" => new BrowserPermissionResult { State = PermissionState },
            "requestAccess" => new BrowserAcquireResult { ErrorName = AcquireError },
            "acquire" => string.IsNullOrEmpty(AcquireError)
                ? new BrowserAcquireResult { SessionId = "session-1" }
                : new BrowserAcquireResult { ErrorName = AcquireError },
            "start" => await StartAsync(args, cancellationToken).ConfigureAwait(false),
            "stop" or "release" => null,
            _ => throw new InvalidOperationException($"Unexpected JavaScript call {identifier}."),
        };

        return result is null ? default! : (TValue)result;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<BrowserStartResult> StartAsync(object?[]? args, CancellationToken cancellationToken)
    {
        if (args is { Length: > 1 })
        {
            SessionReference = args[1] as DotNetObjectReference<BrowserMicrophoneSession>;
        }

        if (DelayStartUntilCancelled)
        {
            StartEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        var sampleRate = StartSampleRateHz ?? ReadInt(args, "targetSampleRate", 16_000);
        var chunkMilliseconds = ReadInt(args, "chunkMilliseconds", 20);
        return new BrowserStartResult
        {
            SampleRateHz = sampleRate,
            Channels = 1,
            ChunkMilliseconds = chunkMilliseconds,
        };
    }

    private static int ReadInt(object?[]? args, string name, int fallback)
    {
        if (args is not { Length: > 2 } || args[2] is not IReadOnlyDictionary<string, object?> values || !values.TryGetValue(name, out var value) || value is null)
        {
            return fallback;
        }

        return Convert.ToInt32(value);
    }
}
