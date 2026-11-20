class PcmPlaybackProcessor extends AudioWorkletProcessor {
    constructor({ processorOptions: { inputRate, capacity, generation } }) {
        super();
        this.inputRate = inputRate;
        this.generation = generation;
        this.frames = new Float32Array(capacity);
        this.head = 0;
        this.count = 0;
        this.skip = 0;
        this.fraction = 0;
        this.retired = 0;
        this.reportedRetired = 0;
        this.creditPending = false;
        this.content = 0;
        this.underrun = 0;
        this.ended = false;
        this.drained = false;
        this.port.onmessage = ({ data }) => this.receive(data);
    }

    receive(message) {
        if (message.generation !== this.generation) {
            return;
        }

        switch (message.kind) {
            case "pcm":
            case "float32": {
                if (this.ended) {
                    return;
                }
                const floatInput = message.kind === "float32";
                const samples = floatInput
                    ? message.samples
                    : new DataView(message.bytes.buffer, message.bytes.byteOffset, message.bytes.byteLength);
                const frames = floatInput ? samples.length : samples.byteLength / 2;
                if (!Number.isInteger(frames) || frames > this.frames.length - this.count) {
                    throw new RangeError("PCM exceeds the playback reservation.");
                }
                let tail = (this.head + this.count) % this.frames.length;
                for (let index = 0; index < frames; index++) {
                    this.frames[tail] = floatInput ? samples[index] : samples.getInt16(index * 2, true) / 32768;
                    tail = (tail + 1) % this.frames.length;
                }
                this.count += frames;
                break;
            }
            case "eof":
                this.ended = true;
                break;
            case "creditAck":
                this.creditPending = false;
                this.reportCredit();
                break;
            case "snapshot":
                this.port.postMessage({
                    kind: "snapshot",
                    generation: this.generation,
                    request: message.request,
                    contentOutputFrames: this.content,
                    underrunOutputFrames: this.underrun
                });
                break;
        }
    }

    retireSkippedFrames() {
        const retired = Math.min(this.skip, this.count);
        this.head = (this.head + retired) % this.frames.length;
        this.count -= retired;
        this.skip -= retired;
        this.retired += retired;
    }

    reportCredit() {
        if (this.creditPending || this.retired === this.reportedRetired) {
            return;
        }
        this.creditPending = true;
        this.reportedRetired = this.retired;
        this.port.postMessage({ kind: "credit", generation: this.generation, retired: this.retired });
    }

    process(_inputs, outputs) {
        const output = outputs[0]?.[0];
        if (!output?.length) {
            return true;
        }
        if (this.drained) {
            output.fill(0);
            return true;
        }

        let written = 0;
        this.retireSkippedFrames();
        while (written < output.length && this.count > (this.fraction === 0 ? 0 : 1)) {
            const left = this.frames[this.head];
            let value = left;
            if (this.fraction !== 0) {
                const right = this.frames[(this.head + 1) % this.frames.length];
                value += (right - left) * (this.fraction / sampleRate);
            }
            output[written++] = value;

            // The fractional source position is an integer numerator over the context rate.
            const position = this.fraction + this.inputRate;
            this.skip = Math.floor(position / sampleRate);
            this.fraction = position % sampleRate;
            this.retireSkippedFrames();
        }
        output.fill(0, written);
        this.content += written;
        // At EOF a fractional position needs two real samples. An exact position needs only one.
        this.drained = this.ended && this.count <= (this.fraction === 0 ? 0 : 1);
        if (this.drained) {
            this.skip = this.count;
            this.retireSkippedFrames();
        } else {
            this.underrun += output.length - written;
        }
        this.reportCredit();
        if (this.drained) {
            this.port.postMessage({
                kind: "drained",
                generation: this.generation,
                contentOutputFrames: this.content
            });
        }
        return true;
    }
}

registerProcessor("microsharp-pcm-playback", PcmPlaybackProcessor);
