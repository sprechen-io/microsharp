const processorName = "microsharp-pcm-playback";
const processorUrl = new URL("./pcm-playback-processor.js", import.meta.url);
const operationTimeoutMs = 5000;

async function withDeadline(operation, message) {
    let timer;
    try {
        return await Promise.race([
            operation,
            new Promise((_, reject) => {
                timer = setTimeout(() => reject(new Error(message)), operationTimeoutMs);
            })
        ]);
    } finally {
        clearTimeout(timer);
    }
}

// One player owns its context. The bridge tracks credit before PCM crosses interop.
export function createPlayer(inputRate, capacity) {
    let current;
    let generation = 0;

    function activeRun() {
        if (!current?.node || current.context.state === "closed") {
            throw new Error("Playback is stopped.");
        }
        if (current.error) {
            throw current.error;
        }
        return current;
    }

    async function close(run) {
        if (current === run) {
            current = undefined;
        }
        const stopped = new Error("Playback stopped.");
        run.snapshot?.reject(stopped);
        run.drain?.reject(stopped);
        if (run.node) {
            run.node.onprocessorerror = null;
            run.node.disconnect();
            run.node.port.onmessage = null;
            run.node.port.close();
        }
        if (run.context.state !== "closed") {
            await run.context.close();
        }
    }

    function receive(run, message) {
        if (current !== run || message.generation !== run.generation) {
            return;
        }
        if (message.kind === "credit") {
            run.retired = message.retired;
            run.node.port.postMessage({ kind: "creditAck", generation: run.generation });
        } else if (message.kind === "snapshot" && run.snapshot?.request === message.request) {
            run.snapshot.resolve({
                contentOutputFrames: message.contentOutputFrames,
                underrunOutputFrames: message.underrunOutputFrames,
                rejectedInputFrames: run.rejected
            });
        } else if (message.kind === "drained") {
            run.drain?.resolve();
        }
    }

    async function start() {
        if (current) {
            activeRun();
            return current.context.sampleRate;
        }
        if (!globalThis.isSecureContext || !globalThis.AudioContext || !globalThis.AudioWorkletNode) {
            throw new Error("Playback requires AudioWorklet in a secure browser context.");
        }

        const context = new AudioContext();
        const run = {
            context, generation: ++generation, sent: 0, retired: 0, rejected: 0,
            reserved: 0, nextRequest: 0, inputEnded: false
        };
        current = run;
        try {
            if (!context.audioWorklet) {
                throw new Error("AudioWorklet is unavailable.");
            }
            // Request activation immediately from the host gesture.
            await withDeadline(
                Promise.all([context.resume(), context.audioWorklet.addModule(processorUrl.href)]),
                "Playback activation or worklet loading timed out.");
            if (context.state !== "running") {
                throw new Error("The audio context did not enter the running state.");
            }
            const node = new AudioWorkletNode(context, processorName, {
                numberOfInputs: 0,
                numberOfOutputs: 1,
                outputChannelCount: [1],
                processorOptions: { inputRate, capacity, generation: run.generation }
            });
            run.node = node;
            node.port.onmessage = ({ data }) => receive(run, data);
            node.onprocessorerror = () => {
                run.error = new Error("The playback audio processor failed.");
                run.snapshot?.reject(run.error);
                run.drain?.reject(run.error);
            };
            // A reply confirms that the processor constructor and port are working.
            await snapshot(run);
            node.connect(context.destination);
            return context.sampleRate;
        } catch (error) {
            await close(run);
            throw error;
        }
    }

    async function snapshot(run) {
        if (run.snapshot) {
            throw new Error("A playback snapshot is already in progress.");
        }
        const request = ++run.nextRequest;
        try {
            return await withDeadline(new Promise((resolve, reject) => {
                run.snapshot = { request, resolve, reject };
                run.node.port.postMessage({ kind: "snapshot", request, generation: run.generation });
            }), "Playback statistics timed out.");
        } finally {
            run.snapshot = undefined;
        }
    }

    async function drain() {
        const run = activeRun();
        if (run.inputEnded) {
            throw new Error("Playback input has already ended.");
        }
        if (run.reserved !== 0) {
            throw new Error("A playback write is already in progress.");
        }

        run.inputEnded = true;
        try {
            await withDeadline(new Promise((resolve, reject) => {
                run.drain = { resolve, reject };
                run.node.port.postMessage({ kind: "eof", generation: run.generation });
            }), "Playback drain timed out.");
        } finally {
            run.drain = undefined;
        }
    }

    return {
        start,
        drain,
        reserve(frames) {
            const run = activeRun();
            if (run.inputEnded) {
                throw new Error("Playback input has already ended.");
            }
            if (run.reserved !== 0) {
                throw new Error("A playback write is already in progress.");
            }
            const accepted = Math.min(frames, capacity - (run.sent - run.retired));
            run.rejected += frames - accepted;
            run.reserved = accepted;
            return accepted;
        },
        enqueue(bytes) {
            const run = activeRun();
            if (run.reserved === 0 || bytes.byteLength !== run.reserved * 2) {
                throw new Error("PCM does not match the playback reservation.");
            }
            // Blazor may own the interop array. Transfer only a private copy of the admitted bytes.
            const owned = new Uint8Array(bytes);
            run.node.port.postMessage({ kind: "pcm", generation: run.generation, bytes: owned }, [owned.buffer]);
            run.sent += run.reserved;
            run.reserved = 0;
        },
        cancelReservation() {
            if (current) {
                current.reserved = 0;
            }
        },
        snapshot: () => snapshot(activeRun()),
        async stop() {
            if (current) {
                await close(current);
            }
        }
    };
}
