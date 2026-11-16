using Microsoft.JSInterop;

namespace MicroSharp.Browser.Tests;

internal sealed class ScriptedJsModule : IJSRuntime, IJSObjectReference
{
    public List<string> Calls { get; } = [];

    public bool HostAvailable { get; set; } = true;

    public string PermissionState { get; set; } = "granted";

    public string? AcquireError { get; set; }

    public IReadOnlyDictionary<string, object?>? LastAcquirePreferences { get; private set; }

    public List<bool> SourceEnabled { get; } = [];

    public bool Live { get; set; } = true;

    public BrowserTrackProcessing Track { get; } = new()
    {
        EchoCancellation = true,
        NoiseSuppression = false,
    };

    public bool DelayStartUntilCancelled { get; set; }

    public bool PauseStart { get; set; }

    public TaskCompletionSource<bool> StartGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool PauseAcquire { get; set; }

    public TaskCompletionSource<bool> AcquireGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int AcquireCount { get; private set; }

    public int ReleaseFailuresRemaining { get; set; }

    public bool ReleaseDisconnects { get; set; }

    public bool PauseRelease { get; set; }

    public TaskCompletionSource<bool> ReleaseGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int? StartSampleRateHz { get; set; }

    public int? RequestedSampleRateHz { get; private set; }

    public bool MalformedAcquire { get; set; }

    public bool PauseSetSource { get; set; }

    public bool FailNextSetSource { get; set; }

    public TaskCompletionSource<bool> SetSourceGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource SetSourceEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DotNetObjectReference<BrowserMicrophoneSession>? SessionReference { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add(identifier);
        cancellationToken.ThrowIfCancellationRequested();
        if (identifier == "release" && ReleaseDisconnects)
        {
            throw new JSDisconnectedException("The browser circuit disconnected.");
        }

        if (identifier == "release" && ReleaseFailuresRemaining > 0)
        {
            ReleaseFailuresRemaining--;
            throw new JSException("The browser rejected release.");
        }

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
            "acquire" => await AcquireAsync(args).ConfigureAwait(false),
            "setSourceEnabled" => await SetSourceAsync(args).ConfigureAwait(false),
            "isLive" => Live,
            "trackProcessing" => Track,
            "start" => await StartAsync(args, cancellationToken).ConfigureAwait(false),
            "disconnect" or "stop" => null,
            "release" => await CompleteReleaseAsync().ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unexpected JavaScript call {identifier}."),
        };

        return result is null ? default! : (TValue)result;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<object?> CompleteReleaseAsync()
    {
        if (!PauseRelease)
        {
            return null;
        }

        ReleaseEntered.TrySetResult();
        await ReleaseGate.Task.ConfigureAwait(false);
        return null;
    }

    private async Task<BrowserAcquireResult> AcquireAsync(object?[]? args)
    {
        if (args is { Length: > 0 } && args[0] is IReadOnlyDictionary<string, object?> preferences)
        {
            LastAcquirePreferences = preferences;
        }

        AcquireCount++;
        if (PauseAcquire)
        {
            await AcquireGate.Task.ConfigureAwait(false);
        }

        if (MalformedAcquire)
        {
            return new BrowserAcquireResult();
        }

        return string.IsNullOrEmpty(AcquireError)
            ? new BrowserAcquireResult { SessionId = "session-1" }
            : new BrowserAcquireResult { ErrorName = AcquireError };
    }

    private async Task<object?> SetSourceAsync(object?[]? args)
    {
        SetSourceEntered.TrySetResult();
        if (PauseSetSource)
        {
            await SetSourceGate.Task.ConfigureAwait(false);
        }

        if (FailNextSetSource)
        {
            FailNextSetSource = false;
            throw new JSException("The browser rejected the source switch.");
        }

        if (args is { Length: > 1 } && args[1] is bool enabled)
        {
            SourceEnabled.Add(enabled);
        }

        return null;
    }

    private async Task<BrowserStartResult> StartAsync(object?[]? args, CancellationToken cancellationToken)
    {
        if (args is { Length: > 1 })
        {
            SessionReference = args[1] as DotNetObjectReference<BrowserMicrophoneSession>;
        }

        StartEntered.TrySetResult();
        if (DelayStartUntilCancelled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        else if (PauseStart)
        {
            await StartGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        RequestedSampleRateHz = ReadInt(args, "targetSampleRate", 16_000);
        var sampleRate = StartSampleRateHz ?? RequestedSampleRateHz.Value;
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
