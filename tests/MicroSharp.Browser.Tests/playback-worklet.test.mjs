import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { runInNewContext } from "node:vm";

const worklet = new URL("../../src/MicroSharp.Browser.Playback/wwwroot/pcm-playback-processor.js", import.meta.url);
const source = readFileSync(worklet, "utf8");

function createWorklet(inputRate, outputRate, capacity) {
    const messages = [];
    const generation = 1;
    let Processor;
    runInNewContext(source, {
        sampleRate: outputRate,
        AudioWorkletProcessor: class {
            port = { postMessage: message => messages.push(message) };
        },
        registerProcessor(name, constructor) {
            assert.equal(name, "microsharp-pcm-playback");
            Processor = constructor;
        }
    }, { filename: worklet.pathname });

    const processor = new Processor({ processorOptions: { inputRate, capacity, generation } });
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
            return messages.map(message => {
                assert.equal(message.kind, "credit");
                assert.equal(message.generation, generation);
                return message.retired;
            });
        }
    };
}

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
