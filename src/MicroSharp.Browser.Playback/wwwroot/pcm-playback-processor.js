class PcmPlaybackProcessor extends AudioWorkletProcessor {
    constructor({ processorOptions: { inputRate, capacity, generation } }) {
        super();
        this.inputRate = inputRate;
        this.generation = generation;
        this.frames = new Int16Array(capacity);
        this.head = 0;
        this.count = 0;
        this.skip = 0;
        this.fraction = 0;
        this.retired = 0;
        this.reportedRetired = 0;
        this.creditPending = false;
        this.content = 0;
        this.underrun = 0;
        this.port.onmessage = ({ data }) => this.receive(data);
    }

    receive(message) {
        if (message.generation !== this.generation) {
            return;
        }

        switch (message.kind) {
            case "pcm": {
                const bytes = new DataView(message.bytes.buffer, message.bytes.byteOffset, message.bytes.byteLength);
                const frames = bytes.byteLength / 2;
                if (!Number.isInteger(frames) || frames > this.frames.length - this.count) {
                    throw new RangeError("PCM exceeds the playback reservation.");
                }
                let tail = (this.head + this.count) % this.frames.length;
                for (let index = 0; index < frames; index++) {
                    this.frames[tail] = bytes.getInt16(index * 2, true);
                    tail = (tail + 1) % this.frames.length;
                }
                this.count += frames;
                break;
            }
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
        if (!output) {
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
            output[written++] = value / 32768;

            // The fractional source position is an integer numerator over the context rate.
            const position = this.fraction + this.inputRate;
            this.skip = Math.floor(position / sampleRate);
            this.fraction = position % sampleRate;
            this.retireSkippedFrames();
        }
        output.fill(0, written);
        this.content += written;
        this.underrun += output.length - written;
        this.reportCredit();
        return true;
    }
}

registerProcessor("microsharp-pcm-playback", PcmPlaybackProcessor);
