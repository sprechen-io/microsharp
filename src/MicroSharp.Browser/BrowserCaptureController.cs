using MicroSharp;
using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Owns acquisition, reuse, stop and release of one browser microphone source.
/// The caller selects the PCM format. Speech and dialog policy stay outside this type.
/// </summary>
public sealed class BrowserCaptureController
{
    private const string SampleRateParameter = "rate=";
    private static readonly TimeSpan DefaultReleaseProgressTimeout = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private readonly BrowserCaptureControllerOptions _options;
    private readonly BrowserSourceRegistry _browser;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _releaseProgressTimeout;
    private readonly SemaphoreSlim _sourceWriter = new(1, 1);

    private Task<string>? _acquisition;
    private Task<string>? _quarantinedAcquisition;
    private Task? _quarantinedAcquisitionCleanup;
    private string? _sessionId;
    private string? _quarantinedSessionId;
    private BrowserCaptureLease? _recording;
    private bool _transitionInProgress;
    private int _lifecycleVersion;
    private long? _lastReleaseTimestamp;
    private TaskCompletionSource? _releaseCompletion;
    private TaskCompletionSource? _stopCompletion;
    private bool _recordingNeedsStopRetry;
    private bool _desiredSourceEnabled = true;
    private long _desiredSourceVersion;
    private int _appliedSourceLifecycleVersion = -1;
    private string? _appliedSourceSessionId;
    private long _appliedSourceDesiredVersion = -1;
    private CancellationTokenRegistration _sourceCancellationRegistration;

    /// <summary>
    /// Increases by one each time a release supersedes the current source.
    /// A caller compares the value to detect a stale source.
    /// </summary>
    public int SourceOwnershipGeneration => Volatile.Read(ref _lifecycleVersion);

    /// <summary>Creates a controller for one browser host.</summary>
    public BrowserCaptureController(IJSRuntime jsRuntime, BrowserCaptureControllerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        _options = options ?? new BrowserCaptureControllerOptions();
        ArgumentNullException.ThrowIfNull(_options.Format);
        if (_options.QueueCapacityChunks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.QueueCapacityChunks,
                "Queue capacity must be at least one chunk.");
        }

        _releaseProgressTimeout = _options.ReleaseProgressTimeout > TimeSpan.Zero
            ? _options.ReleaseProgressTimeout
            : DefaultReleaseProgressTimeout;
        _timeProvider = _options.TimeProvider ?? TimeProvider.System;
        _browser = new BrowserSourceRegistry(jsRuntime, _options);
    }

    /// <summary>Starts capture with <see cref="BrowserCaptureControllerOptions.Format"/>.</summary>
    public Task<BrowserCaptureLease> StartAsync(CancellationToken cancellationToken = default)
        => StartAsync(_options.Format, cancellationToken);

    private bool StillOwnsLifecycleLocked(int lifecycleVersion) => lifecycleVersion == _lifecycleVersion;

    /// <summary>
    /// </summary>
    private bool StillOwnsSourceWriteLocked(
        int lifecycleVersion,
        long desiredVersion,
        string? unpublishedSessionId,
        string sessionId)
        => desiredVersion == _desiredSourceVersion
            && StillOwnsLifecycleLocked(lifecycleVersion)
            && (unpublishedSessionId is not null || _sessionId == sessionId);

    /// <summary>Starts capture in <paramref name="format"/> and returns the lease.</summary>
    public async Task<BrowserCaptureLease> StartAsync(
        PcmFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);

        var (recording, acquisition, lifecycleVersion) =
            await ClaimLifecycleAsync().ConfigureAwait(false);

        string sessionId;
        var browserConnectPending = false;
        try
        {
            sessionId = await acquisition.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (StillOwnsLifecycleLocked(lifecycleVersion))
                {
                    _recording = null;
                    _transitionInProgress = false;
                }

                if (ReferenceEquals(_acquisition, acquisition)
                    && (acquisition.IsFaulted || acquisition.IsCanceled))
                {
                    _acquisition = null;
                }
            }

            recording.StartFailed(exception);
            throw;
        }

        bool stillOwned;
        lock (_sync)
        {
            stillOwned = StillOwnsLifecycleLocked(lifecycleVersion);

            if (stillOwned)
            {
                _sessionId = sessionId;
                if (ReferenceEquals(_acquisition, acquisition))
                {
                    _acquisition = null;
                }
            }
        }

        if (!stillOwned)
        {
            var stopped = new InvalidOperationException("Microphone recorder was stopped during start.");
            recording.StartFailed(stopped);
            throw stopped;
        }

        try
        {
            await ReconcileSourceAsync().ConfigureAwait(false);
            lock (_sync)
            {
                stillOwned = StillOwnsLifecycleLocked(lifecycleVersion);
            }

            if (!stillOwned)
            {
                throw new InvalidOperationException("Microphone recorder was stopped during start.");
            }

            browserConnectPending = true;
            var mimeType = await _browser.ConnectAsync(
                    sessionId,
                    recording,
                    format,
                    _options.QueueCapacityChunks,
                    error => OnCaptureFaultedAsync(recording, error),
                    cancellationToken)
                .ConfigureAwait(false);

            browserConnectPending = false;
            AssertReportedSampleRate(mimeType, format.SampleRateHz);

            lock (_sync)
            {
                stillOwned = StillOwnsLifecycleLocked(lifecycleVersion);
                if (stillOwned)
                {
                    _transitionInProgress = false;
                }
            }

            if (!stillOwned)
            {
                var stopped = new InvalidOperationException("Microphone recorder was stopped during start.");
                throw stopped;
            }

            recording.SetMediaType(mimeType);
            return recording;
        }
        catch (Exception exception)
        {
            var failure = await RecoverFailedStartAsync(
                    lifecycleVersion,
                    recording,
                    sessionId,
                    exception,
                    browserConnectPending,
                    cancellationToken)
                .ConfigureAwait(false);

            recording.StartFailed(failure);

            throw failure;
        }
    }

    /// <summary>
    /// </summary>
    private async Task OnCaptureFaultedAsync(BrowserCaptureLease recording, Exception exception)
    {
        _ = exception;
        await StopRecordingAsync(recording, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<(BrowserCaptureLease Recording, Task<string> Acquisition, int LifecycleVersion)>
        ClaimLifecycleAsync()
    {
        while (true)
        {
            BrowserCaptureLease? retainedFailedStop;
            lock (_sync)
            {
                if (_transitionInProgress)
                {
                    throw new InvalidOperationException("Microphone recorder is already running.");
                }

                retainedFailedStop = _recordingNeedsStopRetry ? _recording : null;
                if (_recording is not null && retainedFailedStop is null)
                {
                    throw new InvalidOperationException("Microphone recorder is already running.");
                }

                if (retainedFailedStop is null)
                {
                    var recording = new BrowserCaptureLease(StopRecordingAsync);
                    _recording = recording;
                    _recordingNeedsStopRetry = false;
                    _transitionInProgress = true;
                    var lifecycleVersion = _lifecycleVersion;
                    return (recording, GetOrStartAcquisitionLocked(), lifecycleVersion);
                }
            }

            await StopRecordingAsync(retainedFailedStop, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// </summary>
    private async Task<Exception> RecoverFailedStartAsync(
        int lifecycleVersion,
        BrowserCaptureLease recording,
        string sessionId,
        Exception exception,
        bool browserConnectPending,
        CancellationToken cancellationToken)
    {
        Task? supersedingRelease = null;
        Task? supersedingStop = null;
        var ownsFailedStartCleanup = false;
        var retainedForStopRetry = false;
        lock (_sync)
        {
            if (StillOwnsLifecycleLocked(lifecycleVersion))
            {
                ownsFailedStartCleanup = true;
                _recording = null;
                _transitionInProgress = false;
            }
            else
            {
                supersedingRelease = _releaseCompletion?.Task;
                supersedingStop = _stopCompletion?.Task;
                retainedForStopRetry = supersedingRelease is null
                    && supersedingStop is null
                    && ReferenceEquals(_recording, recording)
                    && !_transitionInProgress;
            }
        }

        Exception failure;
        var recoverFailedSupersedingStop = retainedForStopRetry;
        if (ownsFailedStartCleanup)
        {
            failure = await ReleaseAfterFailedConnectionAsync(sessionId, exception).ConfigureAwait(false);
        }
        else
        {
            try
            {
                if (supersedingRelease is not null)
                {
                    await supersedingRelease.ConfigureAwait(false);
                }
                else if (supersedingStop is not null)
                {
                    await supersedingStop.ConfigureAwait(false);
                }

                failure = exception;
            }
            catch (Exception lifecycleFailure)
            {
                recoverFailedSupersedingStop = supersedingStop is not null;
                failure = new AggregateException(exception, lifecycleFailure);
            }
        }

        if (recoverFailedSupersedingStop)
        {
            failure = await RecoverFailedSupersedingStopAsync(recording, failure).ConfigureAwait(false);
        }

        if (browserConnectPending
            && (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested))
        {
            failure = new BrowserSourceOwnershipException(
                BrowserSourceFailureKind.Unavailable,
                failure);
        }

        return failure;
    }

    private async Task<Exception> RecoverFailedSupersedingStopAsync(
        BrowserCaptureLease recording,
        Exception startFailure)
    {
        try
        {
            await StopRecordingAsync(recording, CancellationToken.None).ConfigureAwait(false);
            return startFailure;
        }
        catch (Exception stopRetryFailure)
        {
            try
            {
                await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                return new AggregateException(startFailure, stopRetryFailure);
            }
            catch (Exception releaseFailure)
            {
                return new AggregateException(startFailure, stopRetryFailure, releaseFailure);
            }
        }
    }

    /// <summary>Acquires or reuses a live source and publishes the selected source state.</summary>
    public async Task RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        Task<string> acquisition;
        int lifecycleVersion;

        lock (_sync)
        {
            if (_transitionInProgress)
            {
                throw new InvalidOperationException("Microphone recorder is changing lifecycle state.");
            }

            lifecycleVersion = _lifecycleVersion;
            acquisition = GetOrStartAcquisitionLocked();
        }

        string sessionId;
        try
        {
            sessionId = await acquisition.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_acquisition, acquisition)
                    && (acquisition.IsFaulted || acquisition.IsCanceled))
                {
                    _acquisition = null;
                }
            }

            throw;
        }

        bool keepSession;
        lock (_sync)
        {
            keepSession = StillOwnsLifecycleLocked(lifecycleVersion);
            if (keepSession)
            {
                _sessionId = sessionId;
                if (ReferenceEquals(_acquisition, acquisition))
                {
                    _acquisition = null;
                }
            }
        }

        if (keepSession)
        {
            await ReconcileSourceAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Releases the source. Concurrent callers share one release.</summary>
    public Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_releaseCompletion is not null)
            {
                return WaitForLifecycleCompletionAsync(_releaseCompletion.Task, cancellationToken);
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseCompletion = completion;
        }

        _ = CompleteReleaseAsync(completion);
        return WaitForLifecycleCompletionAsync(completion.Task, cancellationToken);
    }

    private static Task WaitForLifecycleCompletionAsync(Task completion, CancellationToken cancellationToken)
        => cancellationToken.CanBeCanceled ? completion.WaitAsync(cancellationToken) : completion;

    private async Task ReleaseCoreAsync()
    {
        Task<string>? acquisition;
        Task? quarantinedAcquisitionCleanup;
        string? sessionId;
        BrowserCaptureLease? recording;
        int lifecycleVersion;

        lock (_sync)
        {
            lifecycleVersion = ++_lifecycleVersion;
            _transitionInProgress = true;
            quarantinedAcquisitionCleanup = _quarantinedAcquisitionCleanup;
            acquisition = _acquisition
                ?? (quarantinedAcquisitionCleanup is null ? _quarantinedAcquisition : null);
            sessionId = _quarantinedSessionId ?? _sessionId;
            recording = _recording;
        }

        var releaseCompleted = false;
        try
        {
            if (acquisition is not null)
            {
                try
                {
                    sessionId = await acquisition
                        .WaitAsync(_releaseProgressTimeout, _timeProvider)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    QuarantineStalledAcquisition(lifecycleVersion, acquisition);

                    throw;
                }
                catch (Exception)
                {
                    var settlement = ReconcileFailedAcquisition(lifecycleVersion, acquisition);
                    sessionId = settlement.SessionId;

                    if (settlement.RecoveryStillPending)
                    {
                        throw;
                    }
                }
            }
            else if (quarantinedAcquisitionCleanup is not null)
            {
                await quarantinedAcquisitionCleanup
                    .WaitAsync(_releaseProgressTimeout, _timeProvider)
                    .ConfigureAwait(false);

                lock (_sync)
                {
                    if (ReferenceEquals(_quarantinedAcquisitionCleanup, quarantinedAcquisitionCleanup))
                    {
                        _quarantinedAcquisitionCleanup = null;
                    }

                    sessionId = _quarantinedSessionId ?? _sessionId;
                }
            }

            if (sessionId is not null)
            {
                lock (_sync)
                {
                    if (StillOwnsLifecycleLocked(lifecycleVersion))
                    {
                        if (_quarantinedSessionId != sessionId)
                        {
                            _sessionId = sessionId;
                        }

                        if (ReferenceEquals(_acquisition, acquisition))
                        {
                            _acquisition = null;
                        }
                    }
                }

                await ReleaseSessionAsync(sessionId).ConfigureAwait(false);
            }

            releaseCompleted = true;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_recording, recording))
                {
                    _recording = null;
                    _recordingNeedsStopRetry = false;
                }

                if (StillOwnsLifecycleLocked(lifecycleVersion))
                {
                    if (releaseCompleted)
                    {
                        _acquisition = null;
                        _quarantinedAcquisition = null;
                        _quarantinedAcquisitionCleanup = null;
                        _sessionId = null;
                        _quarantinedSessionId = null;
                        if (sessionId is not null)
                        {
                            _lastReleaseTimestamp = _timeProvider.GetTimestamp();
                        }
                    }
                    else if (sessionId is not null)
                    {
                        _sessionId = sessionId;
                        _quarantinedSessionId = sessionId;
                    }
                }
            }

            recording?.ReleaseCaptureEnded();
        }
    }

    /// <summary>
    /// </summary>
    private void QuarantineStalledAcquisition(int lifecycleVersion, Task<string> acquisition)
    {
        lock (_sync)
        {
            if (StillOwnsLifecycleLocked(lifecycleVersion))
            {
                QuarantineAcquisitionLocked(acquisition);
                if (ReferenceEquals(_acquisition, acquisition))
                {
                    _acquisition = null;
                }
            }
        }
    }

    /// <summary>
    /// </summary>
    private (string? SessionId, bool RecoveryStillPending) ReconcileFailedAcquisition(
        int lifecycleVersion,
        Task<string> acquisition)
    {
        lock (_sync)
        {
            var recoveryStillPending = _quarantinedAcquisition is not null
                && !ReferenceEquals(_quarantinedAcquisition, acquisition);
            if (recoveryStillPending)
            {
                _acquisition = null;
            }

            if (!recoveryStillPending && StillOwnsLifecycleLocked(lifecycleVersion))
            {
                _acquisition = null;
            }

            if (!recoveryStillPending
                && ReferenceEquals(_quarantinedAcquisition, acquisition))
            {
                _quarantinedAcquisition = null;
            }

            return (_quarantinedSessionId ?? _sessionId, recoveryStillPending);
        }
    }

    private async Task CompleteReleaseAsync(TaskCompletionSource completion)
    {
        try
        {
            await ReleaseCoreAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_releaseCompletion, completion))
                {
                    completion.TrySetException(exception);
                    _releaseCompletion = null;
                    _transitionInProgress = false;
                }
            }

            return;
        }

        lock (_sync)
        {
            if (ReferenceEquals(_releaseCompletion, completion))
            {
                completion.TrySetResult();
                _releaseCompletion = null;
                _transitionInProgress = false;
            }
        }
    }

    /// <summary>
    /// Publishes whether the source track is enabled.
    /// A stale failure does not release the source. Cancellation publishes
    /// <see cref="BrowserCaptureControllerOptions.SourceEnabledAfterCancellation"/>.
    /// </summary>
    public async Task SetSourceEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        long desiredVersion;
        CancellationTokenRegistration previousRegistration;
        lock (_sync)
        {
            previousRegistration = _sourceCancellationRegistration;
            _sourceCancellationRegistration = default;
            _desiredSourceEnabled = enabled;
            desiredVersion = ++_desiredSourceVersion;
        }

        previousRegistration.Dispose();
        var cancellationRegistration = cancellationToken.Register(
            () => CancelSourcePublication(desiredVersion));
        var registrationIsCurrent = false;
        lock (_sync)
        {
            if (desiredVersion == _desiredSourceVersion)
            {
                _sourceCancellationRegistration = cancellationRegistration;
                registrationIsCurrent = true;
            }
        }

        if (!registrationIsCurrent)
        {
            cancellationRegistration.Dispose();
        }

        await ReconcileSourceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private void CancelSourcePublication(long desiredVersion)
    {
        var restoreOpenState = false;
        lock (_sync)
        {
            if (desiredVersion == _desiredSourceVersion)
            {
                _sourceCancellationRegistration = default;
                _desiredSourceEnabled = _options.SourceEnabledAfterCancellation;
                _desiredSourceVersion++;
                restoreOpenState = true;
            }
        }

        if (restoreOpenState)
        {
            _ = RestoreSourceAfterCancellationAsync();
        }
    }

    private async Task RestoreSourceAfterCancellationAsync()
    {
        try
        {
            await ReconcileSourceAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _options.Diagnostic?.Invoke(exception, "Failed to restore the open microphone gate after cancellation.");
        }
    }

    private async Task ReconcileSourceAsync(
        string? unpublishedSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var terminalFailure = await WriteSourceEnabledAsync(unpublishedSessionId, cancellationToken).ConfigureAwait(false);
        if (!terminalFailure.HasValue)
        {
            return;
        }

        try
        {
            await terminalFailure.Value.ReleaseTask.ConfigureAwait(false);
        }
        catch (Exception releaseException)
        {
            throw new AggregateException(terminalFailure.Value.GateException, releaseException);
        }

        System.Runtime.ExceptionServices.ExceptionDispatchInfo
            .Capture(terminalFailure.Value.GateException)
            .Throw();
        throw new InvalidOperationException("Unreachable after rethrowing the microphone gate failure.");
    }

    private async Task<(Exception GateException, Task ReleaseTask)?> WriteSourceEnabledAsync(
        string? unpublishedSessionId,
        CancellationToken cancellationToken)
    {
        int? unpublishedLifecycleVersion = null;
        if (unpublishedSessionId is not null)
        {
            lock (_sync)
            {
                unpublishedLifecycleVersion = _lifecycleVersion;
            }
        }

        await _sourceWriter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                string? sessionId;
                int lifecycleVersion;
                bool desiredEnabled;
                long desiredVersion;
                lock (_sync)
                {
                    if (unpublishedLifecycleVersion.HasValue
                        && unpublishedLifecycleVersion.Value != _lifecycleVersion)
                    {
                        return null;
                    }

                    sessionId = unpublishedSessionId ?? _sessionId;
                    lifecycleVersion = _lifecycleVersion;
                    desiredEnabled = _desiredSourceEnabled;
                    desiredVersion = _desiredSourceVersion;

                    if (_appliedSourceLifecycleVersion == lifecycleVersion
                        && _appliedSourceSessionId == sessionId
                        && _appliedSourceDesiredVersion == desiredVersion)
                    {
                        return null;
                    }
                }

                if (desiredVersion == 0)
                {
                    return null;
                }

                if (sessionId is null)
                {
                    return null;
                }

                try
                {
                    await _browser
                        .SetSourceEnabledAsync(sessionId, desiredEnabled, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception gateException)
                {
                    var stillCurrent = false;
                    lock (_sync)
                    {
                        stillCurrent = StillOwnsSourceWriteLocked(
                            lifecycleVersion,
                            desiredVersion,
                            unpublishedSessionId,
                            sessionId);
                        if (stillCurrent && unpublishedSessionId is null)
                        {
                            _quarantinedSessionId = sessionId;
                        }
                    }

                    if (!stillCurrent)
                    {
                        continue;
                    }

                    if (unpublishedSessionId is not null)
                    {
                        throw;
                    }

                    var releaseTask = ReleaseAsync(CancellationToken.None);
                    return (gateException, releaseTask);
                }

                lock (_sync)
                {
                    if (StillOwnsSourceWriteLocked(
                            lifecycleVersion,
                            desiredVersion,
                            unpublishedSessionId,
                            sessionId))
                    {
                        _appliedSourceLifecycleVersion = lifecycleVersion;
                        _appliedSourceSessionId = sessionId;
                        _appliedSourceDesiredVersion = desiredVersion;
                        return null;
                    }
                }
            }
        }
        finally
        {
            _sourceWriter.Release();
        }
    }

    /// <summary>Reads track processing for the open source, or an empty report when none is open.</summary>
    public async Task<BrowserTrackProcessing> ReadTrackProcessingAsync(CancellationToken cancellationToken = default)
    {
        string? sessionId;
        lock (_sync)
        {
            sessionId = _sessionId;
        }

        if (sessionId is null)
        {
            return BrowserTrackProcessing.Unknown;
        }

        try
        {
            return await _browser
                .ReadTrackProcessingAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JSException)
        {
            return BrowserTrackProcessing.Unknown;
        }
        catch (JSDisconnectedException)
        {
            return BrowserTrackProcessing.Unknown;
        }
        catch (ObjectDisposedException)
        {
            return BrowserTrackProcessing.Unknown;
        }
    }

    /// <summary>Stops the active capture and keeps the source.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        BrowserCaptureLease? recording;
        lock (_sync)
        {
            recording = _recording;
        }

        return recording is null
            ? Task.CompletedTask
            : StopRecordingAsync(recording, cancellationToken);
    }

    private async Task StopRecordingAsync(BrowserCaptureLease recording, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        Task? releaseTask;
        Task? activeStopTask;
        TaskCompletionSource? stopCompletion;
        Task<string>? acquisition;
        string? sessionId;
        int lifecycleVersion;

        lock (_sync)
        {
            releaseTask = _releaseCompletion?.Task;
            activeStopTask = null;
            stopCompletion = null;
            if (releaseTask is not null)
            {
                acquisition = null;
                sessionId = null;
                lifecycleVersion = 0;
            }
            else if (_stopCompletion is not null)
            {
                activeStopTask = _stopCompletion.Task;
                acquisition = null;
                sessionId = null;
                lifecycleVersion = 0;
            }
            else
            {
                if (!ReferenceEquals(_recording, recording))
                {
                    recording.MarkStopped();
                    return;
                }

                lifecycleVersion = ++_lifecycleVersion;
                _transitionInProgress = true;
                stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopCompletion = stopCompletion;
                acquisition = _acquisition;
                sessionId = _sessionId;
            }
        }

        if (activeStopTask is not null)
        {
            await activeStopTask.ConfigureAwait(false);
            return;
        }

        if (releaseTask is not null)
        {
            try
            {
                await releaseTask.ConfigureAwait(false);
                return;
            }
            catch (Exception)
            {
                await StopRecordingAsync(recording, CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }

        if (acquisition is not null)
        {
            try
            {
                sessionId = await acquisition.ConfigureAwait(false);
                lock (_sync)
                {
                    if (StillOwnsLifecycleLocked(lifecycleVersion))
                    {
                        _sessionId = sessionId;
                        if (ReferenceEquals(_acquisition, acquisition))
                        {
                            _acquisition = null;
                        }
                    }
                }
            }
            catch (Exception)
            {
                sessionId = null;
            }
        }

        var stopCompleted = false;
        Exception? stopFailure = null;
        try
        {
            if (sessionId is not null && lifecycleVersion == Volatile.Read(ref _lifecycleVersion))
            {
                await DisconnectSessionAsync(sessionId).ConfigureAwait(false);
            }

            stopCompleted = true;
        }
        catch (Exception exception)
        {
            stopFailure = exception;
            throw;
        }
        finally
        {
            lock (_sync)
            {
                if (StillOwnsLifecycleLocked(lifecycleVersion))
                {
                    if (stopCompleted)
                    {
                        _recording = null;
                        _recordingNeedsStopRetry = false;
                    }
                    else
                    {
                        _recordingNeedsStopRetry = true;
                    }

                    _transitionInProgress = false;
                }

                if (ReferenceEquals(_stopCompletion, stopCompletion))
                {
                    if (stopFailure is null)
                    {
                        stopCompletion?.TrySetResult();
                    }
                    else
                    {
                        stopCompletion?.TrySetException(stopFailure);
                    }

                    _stopCompletion = null;
                }
            }
        }

        if (stopCompleted)
        {
            recording.MarkStopped();
        }
    }

    private Task<string> GetOrStartAcquisitionLocked()
    {
        if (_quarantinedAcquisitionCleanup is not null)
        {
            return _acquisition ??= FinishQuarantinedAcquisitionCleanupAndAcquireAsync(
                _quarantinedAcquisitionCleanup);
        }

        if (_quarantinedAcquisition is not null)
        {
            return _acquisition ??= ReleaseQuarantinedAcquisitionAndAcquireAsync(
                _quarantinedAcquisition);
        }

        if (_quarantinedSessionId is not null)
        {
            return _acquisition ??= ReleaseQuarantinedSessionAndAcquireAsync(_quarantinedSessionId);
        }

        return _acquisition ??= _sessionId is not null
            ? EnsureLiveSessionAsync(_sessionId)
            : AcquireSessionAsync();
    }

    private void QuarantineAcquisitionLocked(Task<string> acquisition)
    {
        _quarantinedAcquisition ??= acquisition;
        if (!ReferenceEquals(_quarantinedAcquisition, acquisition)
            || _quarantinedAcquisitionCleanup is not null)
        {
            return;
        }

        _quarantinedAcquisitionCleanup = CompleteQuarantinedAcquisitionCleanupAsync(acquisition);
    }

    private async Task CompleteQuarantinedAcquisitionCleanupAsync(Task<string> acquisition)
    {
        string sessionId;
        try
        {
            sessionId = await acquisition.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_quarantinedAcquisition, acquisition))
                {
                    _quarantinedAcquisition = null;
                }
            }

            _options.Diagnostic?.Invoke(exception, "Late microphone acquisition ended without a browser session.");
            return;
        }

        var ownsSession = false;
        lock (_sync)
        {
            if (ReferenceEquals(_quarantinedAcquisition, acquisition))
            {
                _quarantinedAcquisition = null;
                _sessionId = sessionId;
                _quarantinedSessionId = sessionId;
                ownsSession = true;
            }
        }

        if (!ownsSession)
        {
            return;
        }

        try
        {
            await ReleaseSessionAsync(sessionId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _options.Diagnostic?.Invoke(exception, "Late microphone session cleanup remains retryable.");
            return;
        }

        lock (_sync)
        {
            if (_sessionId == sessionId)
            {
                _sessionId = null;
            }

            if (_quarantinedSessionId == sessionId)
            {
                _quarantinedSessionId = null;
            }

            _lastReleaseTimestamp = _timeProvider.GetTimestamp();
        }
    }

    private async Task<string> FinishQuarantinedAcquisitionCleanupAndAcquireAsync(Task cleanup)
    {
        try
        {
            await cleanup
                .WaitAsync(_releaseProgressTimeout, _timeProvider)
                .ConfigureAwait(false);
        }
        catch (TimeoutException timeout)
        {
            throw new BrowserSourceOwnershipException(
                BrowserSourceFailureKind.Unavailable,
                timeout);
        }

        string? quarantinedSessionId;
        lock (_sync)
        {
            if (ReferenceEquals(_quarantinedAcquisitionCleanup, cleanup))
            {
                _quarantinedAcquisitionCleanup = null;
            }

            quarantinedSessionId = _quarantinedSessionId;
        }

        return quarantinedSessionId is not null
            ? await ReleaseQuarantinedSessionAndAcquireAsync(quarantinedSessionId).ConfigureAwait(false)
            : await AcquireSessionAsync().ConfigureAwait(false);
    }

    private async Task<string> ReleaseQuarantinedAcquisitionAndAcquireAsync(
        Task<string> acquisition)
    {
        string sessionId;
        try
        {
            sessionId = await acquisition
                .WaitAsync(_releaseProgressTimeout, _timeProvider)
                .ConfigureAwait(false);
        }
        catch (TimeoutException timeout)
        {
            throw new BrowserSourceOwnershipException(
                BrowserSourceFailureKind.Unavailable,
                timeout);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_quarantinedAcquisition, acquisition))
                {
                    _quarantinedAcquisition = null;
                }
            }

            throw;
        }

        lock (_sync)
        {
            if (ReferenceEquals(_quarantinedAcquisition, acquisition))
            {
                _quarantinedAcquisition = null;
            }

            _sessionId = sessionId;
            _quarantinedSessionId = sessionId;
        }

        return await ReleaseQuarantinedSessionAndAcquireAsync(sessionId).ConfigureAwait(false);
    }

    private async Task<string> ReleaseQuarantinedSessionAndAcquireAsync(string sessionId)
    {
        try
        {
            await ReleaseSessionAsync(sessionId).ConfigureAwait(false);
        }
        catch (Exception releaseFailure)
        {
            throw new BrowserSourceOwnershipException(
                BrowserSourceFailureKind.Unavailable,
                releaseFailure);
        }

        lock (_sync)
        {
            if (_quarantinedSessionId == sessionId)
            {
                _quarantinedSessionId = null;
            }

            if (_sessionId == sessionId)
            {
                _sessionId = null;
            }

            _lastReleaseTimestamp = _timeProvider.GetTimestamp();
        }

        return await AcquireSessionAsync().ConfigureAwait(false);
    }

    private async Task<string> EnsureLiveSessionAsync(string sessionId)
    {
        bool isLive;
        try
        {
            isLive = await _browser.IsLiveAsync(sessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            throw new BrowserSourceOwnershipException(
                BrowserSourceFailureKind.Unavailable,
                failure);
        }

        if (isLive)
        {
            return sessionId;
        }

        lock (_sync)
        {
            _quarantinedSessionId = sessionId;
        }

        return await ReleaseQuarantinedSessionAndAcquireAsync(sessionId).ConfigureAwait(false);
    }

    private async Task<string> AcquireSessionAsync()
    {
        BrowserAcquireResult acquisition;
        try
        {
            acquisition = await _browser
                .AcquireAsync(_options.KeepAudioContextAlive, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            throw new BrowserSourceOwnershipException(
                BrowserSourceFailureKind.Unavailable,
                failure);
        }

        if (acquisition is null)
        {
            throw CreateMalformedAcquisitionFailure();
        }

        var hasSession = acquisition.SessionId is not null;
        var sessionId = acquisition.SessionId;
        var hasErrorValue = acquisition.ErrorName is not null;
        var hasValidFailure = !hasSession && !string.IsNullOrWhiteSpace(acquisition.ErrorName);
        var hasValidSession = hasSession && !string.IsNullOrEmpty(sessionId) && !hasErrorValue;
        if (!hasValidSession && !hasValidFailure)
        {
            var malformedFailure = CreateMalformedAcquisitionFailure();
            if (!string.IsNullOrEmpty(sessionId))
            {
                try
                {
                    await ReleaseMalformedSessionAsync(sessionId).ConfigureAwait(false);
                }
                catch (Exception releaseFailure)
                {
                    throw new BrowserSourceOwnershipException(
                        BrowserSourceFailureKind.Unavailable,
                        new AggregateException(malformedFailure, releaseFailure));
                }
            }

            throw malformedFailure;
        }

        if (hasValidFailure)
        {
            throw CreateAcquisitionFailure(acquisition);
        }

        if (sessionId is null)
        {
            throw CreateMalformedAcquisitionFailure();
        }

        try
        {
            await ReconcileSourceAsync(sessionId).ConfigureAwait(false);
        }
        catch (Exception gateFailure)
        {
            lock (_sync)
            {
                _quarantinedSessionId = sessionId;
            }

            try
            {
                await ReleaseSessionAsync(sessionId).ConfigureAwait(false);
            }
            catch (Exception releaseFailure)
            {
                throw new BrowserSourceOwnershipException(
                    BrowserSourceFailureKind.Unavailable,
                    new AggregateException(gateFailure, releaseFailure));
            }

            lock (_sync)
            {
                if (_quarantinedSessionId == sessionId)
                {
                    _quarantinedSessionId = null;
                    _lastReleaseTimestamp = _timeProvider.GetTimestamp();
                }
            }

            throw new BrowserSourceOwnershipException(
                BrowserSourceFailureKind.Unavailable,
                gateFailure);
        }

        long? lastReleaseTimestamp;
        lock (_sync)
        {
            lastReleaseTimestamp = _lastReleaseTimestamp;
        }

        if (lastReleaseTimestamp.HasValue)
        {
            var elapsed = _timeProvider.GetElapsedTime(lastReleaseTimestamp.Value, _timeProvider.GetTimestamp());
            _options.SourceReclaimed?.Invoke(Math.Round(elapsed.TotalMilliseconds));
        }

        return sessionId;
    }

    private async Task ReleaseMalformedSessionAsync(string sessionId)
    {
        lock (_sync)
        {
            _quarantinedSessionId = sessionId;
        }

        await ReleaseSessionAsync(sessionId).ConfigureAwait(false);

        lock (_sync)
        {
            if (_quarantinedSessionId == sessionId)
            {
                _quarantinedSessionId = null;
                _lastReleaseTimestamp = _timeProvider.GetTimestamp();
            }
        }
    }

    private static BrowserSourceOwnershipException CreateAcquisitionFailure(BrowserAcquireResult acquisition)
    {
        var code = acquisition.ErrorName switch
        {
            "NotFoundError" or "DevicesNotFoundError" or "OverconstrainedError"
                => BrowserSourceFailureKind.DeviceNotFound,
            "NotAllowedError" or "PermissionDeniedError" or "SecurityError"
                => BrowserSourceFailureKind.PermissionDenied,
            _ => BrowserSourceFailureKind.Unavailable
        };

        return new BrowserSourceOwnershipException(code);
    }

    private static BrowserSourceOwnershipException CreateMalformedAcquisitionFailure() =>
        new(
            BrowserSourceFailureKind.Unavailable,
            new InvalidOperationException("The microphone acquisition result is malformed."));

    private async Task<Exception> ReleaseAfterFailedConnectionAsync(string sessionId, Exception connectionFailure)
    {
        lock (_sync)
        {
            _quarantinedSessionId = sessionId;
        }

        try
        {
            await ReleaseSessionAsync(sessionId).ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            return new AggregateException(
                "Microphone connection failed and the browser session could not be released.",
                connectionFailure,
                cleanupFailure);
        }

        lock (_sync)
        {
            if (_sessionId == sessionId)
            {
                _sessionId = null;
            }

            if (_quarantinedSessionId == sessionId)
            {
                _quarantinedSessionId = null;
            }

            _lastReleaseTimestamp = _timeProvider.GetTimestamp();
        }

        return connectionFailure;
    }

    private async Task DisconnectSessionAsync(string sessionId)
    {
        try
        {
            await _browser.DisconnectAsync(sessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReleaseSessionAsync(string sessionId)
    {
        try
        {
            await _browser.ReleaseAsync(sessionId, CancellationToken.None)
                .AsTask()
                .WaitAsync(_releaseProgressTimeout, _timeProvider)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _browser.DetachInFlightRelease(sessionId);
            throw;
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// </summary>
    private static void AssertReportedSampleRate(string? mimeType, int sampleRateHz)
    {
        if (ReadSampleRateHz(mimeType) == sampleRateHz)
        {
            return;
        }

        throw new BrowserSourceOwnershipException(
            BrowserSourceFailureKind.Unavailable,
            new InvalidOperationException(
                $"Microphone reported '{mimeType}'; capture requires {SampleRateParameter}{sampleRateHz}."));
    }

    /// <summary>
    /// </summary>
    private static int? ReadSampleRateHz(string? mimeType)
    {
        foreach (var parameter in (mimeType ?? string.Empty).Split(';', StringSplitOptions.TrimEntries))
        {
            if (parameter.StartsWith(SampleRateParameter, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parameter.AsSpan(SampleRateParameter.Length), out var hertz))
            {
                return hertz;
            }
        }

        return null;
    }

}
