import assert from "node:assert/strict";
import { test } from "node:test";
import { createBorrowedPlayer, createPlayer } from "../../src/MicroSharp.Browser.Playback/wwwroot/microsharp-playback.js";

function setGlobal(t, name, value) {
    const previous = Object.getOwnPropertyDescriptor(globalThis, name);
    Object.defineProperty(globalThis, name, { configurable: true, writable: true, value });
    t.after(() => {
        if (previous) {
            Object.defineProperty(globalThis, name, previous);
        } else {
            delete globalThis[name];
        }
    });
}

async function startPlayer(t, borrow = false) {
    const nodes = [];
    setGlobal(t, "isSecureContext", true);
    setGlobal(t, "AudioContext", class {
        sampleRate = 48000;
        state = "suspended";
        audioWorklet = { addModule: async () => {} };
        destination = {};
        async resume() { this.state = "running"; }
        async close() { this.state = "closed"; }
    });
    setGlobal(t, "AudioWorkletNode", class {
        messages = [];
        port = {
            postMessage: (message, transfer) => {
                this.messages.push(structuredClone(message, { transfer }));
                if (message.kind === "snapshot") {
                    queueMicrotask(() => this.emit("snapshot", { request: message.request }));
                }
            },
            close() {}
        };

        constructor(_context, _name, { processorOptions }) {
            this.generation = processorOptions.generation;
            nodes.push(this);
        }

        emit(kind, fields) {
            this.port.onmessage?.({ data: { kind, generation: this.generation, ...fields } });
        }
        connect(destination) { this.destination = destination; }
        disconnect() { this.destination = undefined; }
    });

    const context = borrow ? new AudioContext() : undefined;
    const destination = borrow ? { context } : undefined;
    if (borrow) {
        await context.resume();
        context.resume = context.close = () => assert.fail("Borrowed context lifetime belongs to the caller.");
    }
    const player = borrow ? createBorrowedPlayer(context, destination, 24000, 2) : createPlayer(24000, 2);
    await player.start();
    t.after(() => player.stop());
    return { player, nodes, context, destination };
}

test("drain follows PCM and closes reservations even before the render reply", { timeout: 1000 }, async t => {
    const { player, nodes: [node] } = await startPlayer(t);
    assert.equal(player.reserve(3), 2);
    await assert.rejects(player.drain(), /write is already in progress/);
    player.enqueue(Uint8Array.of(1, 2, 3, 4));

    const draining = player.drain();
    assert.deepEqual(node.messages.map(message => message.kind), ["snapshot", "pcm", "eof"]);
    assert.equal(node.messages.at(-1).generation, node.generation);
    assert.throws(() => player.reserve(0), /input has already ended/);
    await assert.rejects(player.drain(), /input has already ended/);
    node.emit("drained");
    await draining;
    assert.throws(() => player.reserve(1), /input has already ended/);
    assert.equal(await player.start(), 48000);
    assert.throws(() => player.reserve(1), /input has already ended/);
});

test("stop rejects drain and late replies cannot complete a fresh generation", { timeout: 1000 }, async t => {
    const { player, nodes } = await startPlayer(t);
    const [oldNode] = nodes;
    const deliverOld = oldNode.port.onmessage;
    const stopped = assert.rejects(player.drain(), /Playback stopped/);
    await player.stop();
    await stopped;

    await player.start();
    const newNode = nodes[1];
    assert.notEqual(newNode.generation, oldNode.generation);
    assert.equal(player.reserve(1), 1);
    player.cancelReservation();
    let finished = false;
    const draining = player.drain().then(() => { finished = true; });
    deliverOld({ data: { kind: "drained", generation: oldNode.generation } });
    newNode.emit("drained", { generation: oldNode.generation });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(finished, false);
    newNode.emit("drained");
    await draining;
});

test("processor error rejects a pending drain", { timeout: 1000 }, async t => {
    const { player, nodes: [node] } = await startPlayer(t);
    const failing = assert.rejects(player.drain(), /audio processor failed/);
    node.onprocessorerror();
    node.emit("drained");
    await failing;
});

test("missing render acknowledgment fails at the watchdog and leaves input sealed", { timeout: 1000 }, async t => {
    const { player, nodes: [node] } = await startPlayer(t);
    t.mock.timers.enable({ apis: ["setTimeout"] });
    let settled = false;
    const timedOut = assert.rejects(player.drain().finally(() => { settled = true; }), /drain timed out/);
    t.mock.timers.tick(4999);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(settled, false);
    t.mock.timers.tick(1);
    await timedOut;
    node.emit("drained");
    assert.throws(() => player.reserve(1), /input has already ended/);
    await assert.rejects(player.drain(), /input has already ended/);
});

test("borrowed Float32 admission is bounded, validates views and transfers a private copy", { timeout: 1000 }, async t => {
    const { player, nodes: [node], context, destination } = await startPlayer(t, true);
    assert.throws(() => createBorrowedPlayer(context, { context: {} }, 24000, 2), /must belong/);
    assert.throws(() => createBorrowedPlayer(context, destination, 24000, 1), /capacity/);
    assert.equal(node.destination, destination);
    assert.throws(() => player.reserve(1.5), /nonnegative integer/);
    assert.equal(player.reserve(3), 2);
    assert.throws(() => player.enqueue(new Int16Array(2)), /Float32/);
    assert.throws(() => player.enqueue(new Float32Array(3)), /reservation/);

    const backing = Float32Array.of(9, 0.12345678, -0.00000021, 8);
    const view = backing.subarray(1, 3);
    const expected = Array.from(view);
    player.enqueue(view);
    backing.fill(0);
    const message = node.messages.at(-1);
    assert.equal(message.kind, "float32");
    assert.deepEqual(Array.from(message.samples), expected);
    assert.equal(message.samples.buffer.byteLength, 8);
    assert.equal(player.reserve(1), 0);
    node.emit("credit", { retired: 1 });
    assert.equal(player.reserve(2), 1);
    player.cancelReservation();
    await player.stop();
    assert.equal(node.destination, undefined);
    assert.equal(context.state, "running");
});

test("stopping during borrowed startup fences late module loading", { timeout: 1000 }, async t => {
    const { player, nodes, context } = await startPlayer(t, true);
    await player.stop();
    let loaded;
    context.audioWorklet.addModule = () => new Promise(resolve => { loaded = resolve; });
    const stopped = assert.rejects(player.start(), /Playback stopped/);
    await player.stop();
    loaded();
    await stopped;
    assert.equal(nodes.length, 1);
    assert.equal(context.state, "running");
});
