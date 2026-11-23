import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { runInNewContext } from "node:vm";

const worklet = new URL("../../src/MicroSharp.Browser.Playback/wwwroot/pcm-playback-processor.js", import.meta.url);
const source = readFileSync(worklet, "utf8");

function createWorklet(inputRate, outputRate, capacity, startup) {
    const messages = [];
    const generation = 1;
    let Processor;
    let frame = 0;
    runInNewContext(source, {
        sampleRate: outputRate,
        get currentFrame() { return frame; },
        AudioWorkletProcessor: class {
            port = { postMessage: message => messages.push(message) };
        },
        registerProcessor(name, constructor) {
            assert.equal(name, "microsharp-pcm-playback");
            Processor = constructor;
        }
    }, { filename: worklet.pathname });

    const processor = new Processor({ processorOptions: { inputRate, capacity, generation, ...startup } });
    function send(kind, data) {
        processor.port.onmessage({ data: { kind, generation, ...data } });
    }

    let request = 0;
    return {
        send,
        enqueue(...samples) {
            const bytes = new Uint8Array(samples.length * 2);
            const view = new DataView(bytes.buffer);
            samples.forEach((sample, index) => view.setInt16(index * 2, sample, true));
            send("pcm", { bytes });
        },
        render(frames) {
            const channel = new Float32Array(frames).fill(NaN);
            assert.equal(processor.process([], [[channel]]), true);
            frame += frames;
            return Array.from(channel, sample => sample * 32768);
        },
        snapshot() {
            send("snapshot", { request: ++request });
            const reply = messages.pop();
            assert.equal(reply.kind, "snapshot");
            assert.equal(reply.request, request);
            assert.equal(reply.generation, generation);
            return [reply.contentOutputFrames, reply.underrunOutputFrames];
        },
        credits() {
            return messages.filter(message => message.kind === "credit").map(message => {
                assert.equal(message.generation, generation);
                return message.retired;
            });
        },
        drains() {
            return messages.filter(message => message.kind === "drained").map(message => {
                assert.equal(message.generation, generation);
                return message.contentOutputFrames;
            });
        }
    };
}

test("a short stream preloaded before EOF begins at the requested context frame", () => {
    const worklet = createWorklet(48_000, 48_000, 4, {
        earliestOutputFrame: 7,
        minimumBufferedInputFrames: 4
    });

    worklet.enqueue(100, 200);
    assert.deepEqual(worklet.render(4), [0, 0, 0, 0]);
    assert.deepEqual(worklet.snapshot(), [0, 0]);
    worklet.send("eof");
    assert.deepEqual(worklet.render(5), [0, 0, 0, 100, 200]);
    assert.deepEqual(worklet.drains(), [2]);
    assert.deepEqual(worklet.snapshot(), [2, 0]);
});

test("a past start frame does not delay a newly available prefill", () => {
    const worklet = createWorklet(48_000, 48_000, 4, {
        earliestOutputFrame: 2,
        minimumBufferedInputFrames: 2
    });
    worklet.enqueue(100);
    assert.deepEqual(worklet.render(4), [0, 0, 0, 0]);
    worklet.enqueue(200);
    assert.deepEqual(worklet.render(2), [100, 200]);
    assert.deepEqual(worklet.snapshot(), [2, 0]);
});

test("empty EOF ignores a future start frame", () => {
    const worklet = createWorklet(48_000, 48_000, 4, { earliestOutputFrame: 80 });
    worklet.send("eof");
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.drains(), [0]);
    assert.deepEqual(worklet.snapshot(), [0, 0]);
});

function exceedsReservation(write) {
    // The processor runs in a different realm, so match the error name.
    assert.throws(write, { name: "RangeError", message: "PCM exceeds the playback reservation." });
}

test("FIFO rejects invalid messages atomically and preserves order through wraparound", () => {
    const worklet = createWorklet(48_000, 48_000, 4);
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.snapshot(), [0, 2]);

    exceedsReservation(() => worklet.enqueue(1, 2, 3, 4, 5));
    worklet.enqueue(-32768, 513, -1025);
    exceedsReservation(() => worklet.enqueue(7, 8));
    exceedsReservation(() => worklet.send("pcm", { bytes: Uint8Array.of(1, 2, 3) }));
    worklet.enqueue(32767);
    exceedsReservation(() => worklet.enqueue(9));

    assert.deepEqual(worklet.render(2), [-32768, 513]);
    assert.deepEqual(worklet.credits(), [2]);
    worklet.enqueue(1234, -5678);
    assert.deepEqual(worklet.render(5), [-1025, 32767, 1234, -5678, 0]);
    assert.deepEqual(worklet.snapshot(), [6, 3]);
    assert.deepEqual(worklet.credits(), [2]);
    worklet.send("creditAck");
    assert.deepEqual(worklet.credits(), [2, 6]);
});

test("fractional underrun resumes at the same source position", () => {
    const worklet = createWorklet(24_000, 48_000, 3);
    worklet.enqueue(1000);
    const first = worklet.render(3);
    assert.deepEqual(first, [1000, 0, 0]);
    assert.deepEqual(worklet.snapshot(), [1, 2]);
    assert.deepEqual(worklet.render(3), [0, 0, 0]);
    assert.deepEqual(worklet.snapshot(), [1, 5]);
    assert.deepEqual(worklet.credits(), []);

    worklet.enqueue(3000, -1000);
    const resumed = worklet.render(5);
    assert.deepEqual(resumed, [2000, 3000, 1000, -1000, 0]);
    assert.deepEqual(worklet.snapshot(), [5, 6]);
    assert.deepEqual(worklet.credits(), [2]);

    const continuous = createWorklet(24_000, 48_000, 3);
    continuous.enqueue(1000, 3000, -1000);
    assert.deepEqual(continuous.render(5), [first[0], ...resumed.slice(0, 4)]);
});

test("downsampling returns credit for late skipped frames in a small FIFO", () => {
    const worklet = createWorklet(96_000, 24_000, 2);
    worklet.enqueue(100, 200);
    assert.deepEqual(worklet.render(2), [100, 0]);
    assert.deepEqual(worklet.credits(), [2]);
    worklet.send("creditAck");

    worklet.enqueue(300);
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.credits(), [2, 3]);
    worklet.send("creditAck");

    worklet.enqueue(400, 500);
    assert.deepEqual(worklet.render(2), [500, 0]);
    assert.deepEqual(worklet.credits(), [2, 3, 5]);
    assert.deepEqual(worklet.snapshot(), [2, 4]);
});

test("empty EOF acknowledges once from a render callback and leaves padding out of the counters", () => {
    const worklet = createWorklet(48_000, 48_000, 1);
    assert.deepEqual(worklet.render(2), [0, 0]);
    worklet.send("eof");
    worklet.send("eof");
    worklet.enqueue(100);
    assert.deepEqual(worklet.render(0), []);
    assert.deepEqual(worklet.drains(), []);

    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.drains(), [0]);
    assert.deepEqual(worklet.snapshot(), [0, 2]);
    assert.deepEqual(worklet.credits(), []);
});

test("stale EOF leaves writes open and matching EOF seals a full FIFO", () => {
    const worklet = createWorklet(48_000, 48_000, 4);
    worklet.enqueue(100, 0);
    worklet.send("eof", { generation: 0 });
    const bytes = Uint8Array.of(99, 0);
    worklet.send("pcm", { generation: 0, bytes });
    worklet.enqueue(200, 300);
    worklet.send("eof");
    assert.deepEqual(worklet.drains(), []);

    assert.deepEqual(worklet.render(2), [100, 0]);
    assert.deepEqual(worklet.drains(), []);
    worklet.enqueue(400, 500);
    assert.deepEqual(worklet.render(2), [200, 300]);
    assert.deepEqual(worklet.drains(), [4]);
    worklet.send("eof");
    worklet.send("creditAck");
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.drains(), [4]);
    assert.deepEqual(worklet.snapshot(), [4, 0]);
    assert.deepEqual(worklet.credits(), [2, 4]);
});

test("EOF releases an already-rendered single frame awaiting fractional lookahead", () => {
    const worklet = createWorklet(24_000, 44_100, 1);
    worklet.enqueue(1234);
    assert.deepEqual(worklet.render(3), [1234, 0, 0]);
    assert.deepEqual(worklet.credits(), []);
    worklet.send("eof");
    assert.deepEqual(worklet.drains(), []);

    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.drains(), [1]);
    assert.deepEqual(worklet.snapshot(), [1, 2]);
    assert.deepEqual(worklet.credits(), [1]);
});

test("EOF drops a downsample skip beyond the input boundary after retiring late skipped frames", () => {
    const worklet = createWorklet(96_000, 24_000, 2);
    worklet.enqueue(100, 200);
    assert.deepEqual(worklet.render(2), [100, 0]);
    worklet.send("creditAck");
    worklet.enqueue(300);
    worklet.send("eof");
    assert.deepEqual(worklet.drains(), []);

    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.drains(), [1]);
    assert.deepEqual(worklet.snapshot(), [1, 1]);
    assert.deepEqual(worklet.credits(), [2, 3]);
});

test("Float32 ingress shares the bounded ring and keeps samples between PCM16 steps", () => {
    const worklet = createWorklet(24000, 48000, 2);
    const samples = Float32Array.of(0.12345678, -0.00000021);
    exceedsReservation(() => worklet.send("float32", { samples: new Float32Array(3) }));
    worklet.send("float32", { samples });
    exceedsReservation(() => worklet.send("float32", { samples }));
    worklet.send("eof");
    const midpoint = Math.fround(samples[0] + (samples[1] - samples[0]) / 2);
    assert.deepEqual(worklet.render(4), [samples[0], midpoint, samples[1], 0].map(value => value * 32768));
    assert.deepEqual(worklet.drains(), [3]);
});

test("prefill and lead preserve all data and retire nothing before first output", () => {
    const worklet = createWorklet(48000, 48000, 3, {
        minimumBufferedInputFrames: 3,
        initialOutputDelayFrames: 3
    });
    worklet.enqueue(100, 200);
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.snapshot(), [0, 0]);
    assert.deepEqual(worklet.credits(), []);

    worklet.enqueue(300);
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.snapshot(), [0, 0]);
    assert.deepEqual(worklet.credits(), []);
    assert.deepEqual(worklet.render(5), [0, 100, 200, 300, 0]);
    assert.deepEqual(worklet.snapshot(), [3, 1]);
    assert.deepEqual(worklet.credits(), [3]);

    worklet.enqueue(400);
    assert.deepEqual(worklet.render(2), [400, 0]);
    assert.deepEqual(worklet.snapshot(), [4, 2]);
});

test("lead without prefill waits for the first frame before counting output positions", () => {
    const worklet = createWorklet(48000, 48000, 2, { initialOutputDelayFrames: 2 });
    assert.deepEqual(worklet.render(3), [0, 0, 0]);
    worklet.enqueue(100);
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.snapshot(), [0, 0]);
    assert.deepEqual(worklet.render(2), [100, 0]);
    assert.deepEqual(worklet.snapshot(), [1, 1]);
});

test("short EOF bypasses prefill and acknowledges its final valid sample after the lead", () => {
    const worklet = createWorklet(24000, 44100, 4, {
        minimumBufferedInputFrames: 4,
        initialOutputDelayFrames: 2
    });
    worklet.enqueue(1000);
    worklet.send("eof");
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.drains(), []);
    assert.deepEqual(worklet.credits(), []);
    assert.deepEqual(worklet.render(2), [1000, 0]);
    assert.deepEqual(worklet.drains(), [1]);
    assert.deepEqual(worklet.snapshot(), [1, 0]);
    assert.deepEqual(worklet.credits(), [1]);
});

test("empty EOF acknowledges during the next render callback regardless of configured lead", () => {
    const worklet = createWorklet(24000, 44100, 4, {
        minimumBufferedInputFrames: 4,
        initialOutputDelayFrames: 44100
    });
    assert.deepEqual(worklet.render(2), [0, 0]);
    worklet.send("eof");
    assert.deepEqual(worklet.drains(), []);
    assert.deepEqual(worklet.render(2), [0, 0]);
    assert.deepEqual(worklet.drains(), [0]);
    assert.deepEqual(worklet.credits(), []);
    assert.deepEqual(worklet.snapshot(), [0, 0]);
});

for (const [inputRate, outputRate, input] of [
    [24_000, 48_000, [2000, -2000, 4000]],
    [24_000, 44_100, [2000, -2000, 4000, -1000, 8000]],
    [48_000, 44_100, [2000, -2000, 4000, -1000, 8000]]
]) {
    test(`EOF keeps finite interpolation positions after split writes at ${inputRate} → ${outputRate} Hz`, () => {
        const worklet = createWorklet(inputRate, outputRate, input.length);
        worklet.enqueue(input[0]);
        assert.deepEqual(worklet.render(1), [input[0]]);
        worklet.enqueue(...input.slice(1));
        worklet.send("eof");

        const contentFrames = 1 + Math.floor((input.length - 1) * outputRate / inputRate);
        const final = worklet.render(contentFrames - 1);
        const expected = Array.from({ length: contentFrames - 1 }, (_, index) => {
            const numerator = (index + 1) * inputRate;
            const left = Math.floor(numerator / outputRate);
            const fraction = (numerator % outputRate) / outputRate;
            const value = fraction === 0
                ? input[left]
                : input[left] + (input[left + 1] - input[left]) * fraction;
            return Math.fround(value);
        });
        assert.deepEqual(final, expected);
        assert.deepEqual(worklet.drains(), [contentFrames]);
        assert.deepEqual(worklet.render(2), [0, 0]);
        assert.deepEqual(worklet.snapshot(), [contentFrames, 0]);
    });
}
