const sessions = new Map();

const errorNameOf = (error) => {
    if (error && typeof error === "object" && "name" in error && error.name) {
        return String(error.name);
    }

    return "Error";
};

const audioContextCtor = () => window.AudioContext || window.webkitAudioContext;

export function describe() {
    const media = typeof navigator !== "undefined"
        && !!navigator.mediaDevices
        && typeof navigator.mediaDevices.getUserMedia === "function";
    const audio = typeof audioContextCtor() === "function";
    const worklet = typeof AudioWorkletNode === "function";
    return { media, audio, worklet };
}

const ensureSupported = () => {
    const support = describe();
    if (!support.media || !support.audio || !support.worklet) {
        const error = new Error("Microphone capture is not available in this browser.");
        error.name = "NotSupportedError";
        throw error;
    }
};

const flag = (preferences, name) => {
    if (!preferences || preferences[name] === undefined || preferences[name] === null) {
        return undefined;
    }

    return preferences[name];
};

const constraintsFor = (preferences) => {
    const audio = { channelCount: 1 };
    const echoCancellation = flag(preferences, "echoCancellation");
    const noiseSuppression = flag(preferences, "noiseSuppression");
    const autoGainControl = flag(preferences, "autoGainControl");
    if (echoCancellation !== undefined) {
        audio.echoCancellation = echoCancellation;
    }

    if (noiseSuppression !== undefined) {
        audio.noiseSuppression = noiseSuppression;
    }

    if (autoGainControl !== undefined) {
        audio.autoGainControl = autoGainControl;
    }

    return { audio };
};

const stopTracks = (stream) => {
    if (!stream) {
        return;
    }

    for (const track of stream.getTracks()) {
        track.onended = null;
        if (track.readyState !== "ended") {
            track.stop();
        }
    }
};

const releaseSession = async (session) => {
    if (!session) {
        return;
    }

    const failures = [];
    try {
        session.sourceNode?.disconnect();
    } catch (error) {
        failures.push(error);
    }

    session.sourceNode = null;
    try {
        if (session.workletNode) {
            session.workletNode.port.onmessage = null;
            session.workletNode.disconnect();
        }
    } catch (error) {
        failures.push(error);
    }

    session.workletNode = null;
    try {
        stopTracks(session.stream);
    } catch (error) {
        failures.push(error);
    }

    const context = session.audioContext;
    session.audioContext = null;
    if (context && context.state !== "closed") {
        context.onstatechange = null;
        try {
            await context.close();
        } catch (error) {
            failures.push(error);
        }
    }

    if (failures.length > 0) {
        const error = new Error("Microphone release did not complete.");
        error.name = "InvalidStateError";
        throw error;
    }
};

export async function permission() {
    try {
        ensureSupported();
        if (!navigator.permissions || typeof navigator.permissions.query !== "function") {
            return { state: "unknown" };
        }

        const status = await navigator.permissions.query({ name: "microphone" });
        const state = status && status.state ? String(status.state) : "unknown";
        if (state === "granted" || state === "denied" || state === "prompt") {
            return { state };
        }

        return { state: "unknown" };
    } catch {
        return { state: "unknown" };
    }
}

export async function requestAccess(preferences) {
    let stream = null;
    try {
        ensureSupported();
        stream = await navigator.mediaDevices.getUserMedia(constraintsFor(preferences));
        stopTracks(stream);
        return { errorName: null };
    } catch (error) {
        if (stream) {
            stopTracks(stream);
        }

        return { errorName: errorNameOf(error) };
    }
}

const createSessionId = () => {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    return Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join("");
};

export async function acquire(preferences) {
    let session = null;
    try {
        ensureSupported();
        const stream = await navigator.mediaDevices.getUserMedia(constraintsFor(preferences));
        const Context = audioContextCtor();
        const audioContext = new Context({ latencyHint: "interactive" });
        session = {
            stream,
            audioContext,
            sourceNode: null,
            workletNode: null,
            dotNetRef: null,
            targetSampleRate: 0,
        };
        if (audioContext.state === "suspended") {
            await audioContext.resume();
        }

        const workletUrl = new URL("./pcm-recorder-processor.js", import.meta.url);
        await audioContext.audioWorklet.addModule(workletUrl);
        const sessionId = createSessionId();
        sessions.set(sessionId, session);
        return { sessionId, errorName: null };
    } catch (error) {
        if (session) {
            try {
                await releaseSession(session);
            } catch {
                // The failed acquisition reports the original browser error.
            }
        }

        return { sessionId: null, errorName: errorNameOf(error) };
    }
}

const requireSession = (sessionId) => {
    const session = sessions.get(sessionId);
    if (!session) {
        const error = new Error(`MIC_SESSION_NOT_FOUND: ${sessionId}`);
        error.name = "InvalidStateError";
        throw error;
    }

    return session;
};

const invokeCallback = async (callback, method, arg) => {
    if (!callback || typeof callback.invokeMethodAsync !== "function") {
        return;
    }

    try {
        await callback.invokeMethodAsync(method, arg);
    } catch {
        // A disconnected .NET reference must not keep the audio thread alive.
    }
};

export async function start(sessionId, dotNetRef, format) {
    const session = requireSession(sessionId);
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== "function") {
        const error = new Error("dotNetRef is required.");
        error.name = "InvalidStateError";
        throw error;
    }

    const targetSampleRate = Number(format && format.targetSampleRate);
    const chunkMilliseconds = Number(format && format.chunkMilliseconds);
    if (!Number.isFinite(targetSampleRate) || targetSampleRate <= 0 || !Number.isFinite(chunkMilliseconds) || chunkMilliseconds <= 0) {
        const error = new Error("The requested capture format is incomplete.");
        error.name = "NotSupportedError";
        throw error;
    }

    session.dotNetRef = dotNetRef;
    session.targetSampleRate = targetSampleRate;
    const context = session.audioContext;
    if (context.state === "suspended") {
        await context.resume();
    }

    session.sourceNode = context.createMediaStreamSource(session.stream);
    session.workletNode = new AudioWorkletNode(context, "microsharp-pcm-recorder", {
        numberOfInputs: 1,
        numberOfOutputs: 0,
        channelCount: 1,
        processorOptions: {
            targetSampleRate,
            chunkMs: chunkMilliseconds,
        },
    });

    session.workletNode.port.onmessage = (event) => {
        const callback = session.dotNetRef;
        if (!callback) {
            return;
        }

        const data = event.data;
        if (data && typeof data === "object" && !(data instanceof ArrayBuffer)) {
            if (data.kind === "outputSampleRate" && Number(data.value) !== session.targetSampleRate) {
                void invokeCallback(
                    callback,
                    "OnCaptureFault",
                    `WORKLET_OUTPUT_SAMPLE_RATE_INVALID: expected=${session.targetSampleRate} actual=${data.value}`);
            }

            return;
        }

        if (!(data instanceof ArrayBuffer) || data.byteLength === 0) {
            return;
        }

        void invokeCallback(callback, "OnPcmChunk", new Uint8Array(data));
    };

    for (const track of session.stream.getAudioTracks()) {
        track.onended = () => {
            void invokeCallback(session.dotNetRef, "OnCaptureFault", "MICROPHONE_TRACK_ENDED");
        };
    }

    session.sourceNode.connect(session.workletNode);
    if (context.state !== "running" || !session.stream.getAudioTracks().some((track) => track.readyState === "live")) {
        const error = new Error("MIC_SESSION_NOT_LIVE");
        error.name = "InvalidStateError";
        throw error;
    }

    return {
        sampleRateHz: targetSampleRate,
        channels: 1,
        chunkMilliseconds,
    };
}

export async function stop(sessionId) {
    const session = sessions.get(sessionId) ?? null;
    if (!session) {
        return;
    }

    const callback = session.dotNetRef;
    session.dotNetRef = null;
    sessions.delete(sessionId);
    await releaseSession(session);
    await invokeCallback(callback, "OnStopped");
}

export async function release(sessionId) {
    await stop(sessionId);
}
