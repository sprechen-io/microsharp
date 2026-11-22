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

export function createPlayer(inputRate, capacity) {
    return createTransport(inputRate, capacity);
}

export function createBorrowedPlayer(
    context, destination, inputRate, capacity, workletNodeConstructor = globalThis.AudioWorkletNode) {
    if (!context || !destination || destination.context !== context) {
        throw new TypeError("Playback destination must belong to the borrowed audio context.");
    }
    if (!Number.isInteger(inputRate) || inputRate < 8000 || inputRate > 192000) {
        throw new RangeError("Playback input rate must be an integer from 8000 through 192000 Hz.");
    }
    if (!Number.isInteger(capacity) || capacity < 2 || capacity > 192000) {
        throw new RangeError("Playback capacity must be an integer from 2 through 192000 frames.");
    }
    return createTransport(inputRate, capacity, { context, destination, workletNodeConstructor });
}

function createTransport(inputRate, capacity, borrowed) {
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
        if (current !== run) {
            return;
        }
        current = undefined;
        const stopped = new Error("Playback stopped.");
        run.snapshot?.reject(stopped);
        run.drain?.reject(stopped);
        run.creditWait?.reject(stopped);
        if (run.node) {
            run.node.onprocessorerror = null;
            run.node.disconnect();
            run.node.port.onmessage = null;
            run.node.port.close();
        }
        if (!borrowed && run.context.state !== "closed") {
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
            if (freeFrames(run) > 0) {
                run.creditWait?.resolve();
            }
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
        const NodeConstructor = borrowed ? borrowed.workletNodeConstructor : globalThis.AudioWorkletNode;
        if (!globalThis.isSecureContext || typeof NodeConstructor !== "function" || (!borrowed && !globalThis.AudioContext)) {
            throw new Error("Playback requires AudioWorklet in a secure browser context.");
        }

        const context = borrowed ? borrowed.context : new AudioContext();
        const run = {
            context, generation: ++generation, sent: 0, retired: 0, rejected: 0,
            reserved: 0, nextRequest: 0, inputEnded: false
        };
        current = run;
        try {
            if (!context.audioWorklet) {
                throw new Error("AudioWorklet is unavailable.");
            }
            if (borrowed) {
                if (context.state !== "running") {
                    throw new Error("Borrowed playback requires a running audio context.");
                }
                await withDeadline(context.audioWorklet.addModule(processorUrl.href), "Playback worklet loading timed out.");
            } else {
                // Request activation immediately from the host gesture.
                await withDeadline(
                    Promise.all([context.resume(), context.audioWorklet.addModule(processorUrl.href)]),
                    "Playback activation or worklet loading timed out.");
            }
            checkCurrent(run);
            if (context.state !== "running") {
                throw new Error("The audio context did not enter the running state.");
            }
            const node = new NodeConstructor(context, processorName, {
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
                run.creditWait?.reject(run.error);
            };
            // A reply confirms that the processor constructor and port are working.
            await snapshot(run);
            checkCurrent(run);
            node.connect(borrowed ? borrowed.destination : context.destination);
            return context.sampleRate;
        } catch (error) {
            await close(run);
            throw error;
        }
    }

    function freeFrames(run) {
        return capacity - (run.sent - run.retired);
    }

    async function waitForCredit(signal) {
        signal?.throwIfAborted();
        const run = activeRun();
        if (run.inputEnded) {
            throw new Error("Playback input has already ended.");
        }
        if (run.reserved !== 0 || run.creditWait) {
            throw new Error("A playback write or credit wait is already in progress.");
        }
        if (freeFrames(run) > 0) {
            return;
        }

        let onAbort;
        try {
            await withDeadline(new Promise((resolve, reject) => {
                run.creditWait = { resolve, reject };
                onAbort = () => reject(signal.reason);
                signal?.addEventListener("abort", onAbort, { once: true });
            }), "Playback credit timed out.");
        } finally {
            signal?.removeEventListener("abort", onAbort);
            run.creditWait = undefined;
        }
    }

    function checkCurrent(run) {
        if (current !== run) {
            throw new Error("Playback stopped.");
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
        run.creditWait?.reject(new Error("Playback input has already ended."));
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
        ...(borrowed ? { waitForCredit } : {}),
        reserve(frames) {
            const run = activeRun();
            if (run.inputEnded) {
                throw new Error("Playback input has already ended.");
            }
            if (borrowed && (!Number.isSafeInteger(frames) || frames < 0)) {
                throw new RangeError("Playback reservation must be a nonnegative integer frame count.");
            }
            if (run.reserved !== 0) {
                throw new Error("A playback write is already in progress.");
            }
            const accepted = Math.min(frames, freeFrames(run));
            run.rejected += frames - accepted;
            run.reserved = accepted;
            return accepted;
        },
        enqueue(samples) {
            const run = activeRun();
            if (borrowed) {
                if (!(samples instanceof Float32Array) || run.reserved === 0 || samples.length !== run.reserved) {
                    throw new Error("Float32 samples do not match the playback reservation.");
                }
                const owned = new Float32Array(samples);
                run.node.port.postMessage({ kind: "float32", generation: run.generation, samples: owned }, [owned.buffer]);
            } else {
                if (run.reserved === 0 || samples.byteLength !== run.reserved * 2) {
                    throw new Error("PCM does not match the playback reservation.");
                }
                // Blazor may own the interop array. Transfer only a private copy of the admitted bytes.
                const owned = new Uint8Array(samples);
                run.node.port.postMessage({ kind: "pcm", generation: run.generation, bytes: owned }, [owned.buffer]);
            }
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
