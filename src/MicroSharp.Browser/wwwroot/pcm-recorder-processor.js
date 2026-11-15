// Resampling and int16 chunking derived from sprechen-io
// src/speech/Sprechen.Speech.App.MicrophoneRecording/TypeScript/worklets/pcmRecorderProcessor.ts
// at commit 1d603f3a75f408d1d11010cd49190e110979803b (MIT, Copyright 2026 sprechen.io).
(() => {
    "use strict";

    const toPositiveNumber = (value, fallback) => {
        if (typeof value === "number" && Number.isFinite(value) && value > 0) {
            return value;
        }

        if (typeof value === "string") {
            const parsed = Number.parseFloat(value);
            if (Number.isFinite(parsed) && parsed > 0) {
                return parsed;
            }
        }

        return fallback;
    };

    const antiAliasHalfLength = 48;
    const antiAliasPassbandFraction = 0.95;

    const buildAntiAliasKernel = (ratio) => {
        if (!(ratio > 1)) {
            return null;
        }

        const length = antiAliasHalfLength * 2 + 1;
        const kernel = new Float32Array(length);
        const cutoff = (antiAliasPassbandFraction * 0.5) / ratio;
        let sum = 0;

        for (let index = 0; index < length; index++) {
            const offset = index - antiAliasHalfLength;
            const sinc = offset === 0
                ? 2 * cutoff
                : Math.sin(2 * Math.PI * cutoff * offset) / (Math.PI * offset);
            const windowValue = 0.54 - 0.46 * Math.cos((2 * Math.PI * index) / (length - 1));
            const tap = sinc * windowValue;
            kernel[index] = tap;
            sum += tap;
        }

        for (let index = 0; index < length; index++) {
            kernel[index] /= sum;
        }

        return kernel;
    };

    class PcmRecorderProcessor extends AudioWorkletProcessor {
        constructor(options) {
            super();
            const processorOptions = options && options.processorOptions ? options.processorOptions : {};
            this.targetSampleRate = toPositiveNumber(processorOptions.targetSampleRate, 16000);
            const chunkMs = toPositiveNumber(processorOptions.chunkMs, 20);
            this.ratio = sampleRate / this.targetSampleRate;
            this.chunkSamples = Math.max(1, Math.round(this.targetSampleRate * (chunkMs / 1000)));
            this.buffer = new Int16Array(this.chunkSamples);
            this.kernel = buildAntiAliasKernel(this.ratio);
            this.history = new Float32Array(this.kernel ? this.kernel.length : 1);
            this.historyIndex = 0;
            this.currentSample = 0;
            this.inputPosition = -1;
            this.nextOutputPosition = 0;
            this.bufferIndex = 0;
            this.probePosted = false;
        }

        filterNewest() {
            const kernel = this.kernel;
            const history = this.history;
            const length = history.length;
            const start = this.historyIndex;
            const first = length - start;
            let sum = 0;

            for (let tap = 0; tap < first; tap++) {
                sum += kernel[tap] * history[start + tap];
            }

            for (let tap = first; tap < length; tap++) {
                sum += kernel[tap] * history[tap - first];
            }

            return sum;
        }

        process(inputs) {
            const input = inputs[0];
            const channel = input && input[0];
            if (!channel || channel.length === 0) {
                return true;
            }

            const ratio = this.ratio;
            const kernel = this.kernel;
            const history = this.history;
            const historyLength = history.length;

            for (let position = 0; position < channel.length; position++) {
                const sample = channel[position];
                const previous = this.currentSample;
                let current = sample;
                let currentComputed = kernel === null;
                if (kernel !== null) {
                    history[this.historyIndex] = sample;
                    this.historyIndex = this.historyIndex + 1 === historyLength ? 0 : this.historyIndex + 1;
                }

                this.inputPosition++;

                while (this.nextOutputPosition <= this.inputPosition) {
                    const fraction = this.nextOutputPosition - (this.inputPosition - 1);
                    if (!currentComputed) {
                        current = this.filterNewest();
                        currentComputed = true;
                    }

                    const value = fraction === 1 ? current : (previous * (1 - fraction)) + (current * fraction);
                    const clamped = Math.max(-1, Math.min(1, value));
                    const encoded = clamped < 0 ? clamped * 32768 : clamped * 32767;
                    this.buffer[this.bufferIndex++] = encoded;

                    if (this.bufferIndex >= this.buffer.length) {
                        const out = this.buffer.buffer;
                        this.port.postMessage(out, [out]);
                        this.buffer = new Int16Array(this.chunkSamples);
                        this.bufferIndex = 0;
                        if (!this.probePosted) {
                            this.probePosted = true;
                            this.port.postMessage({ kind: "outputSampleRate", value: this.targetSampleRate });
                        }
                    }

                    this.nextOutputPosition += ratio;
                }

                if (!currentComputed && this.nextOutputPosition < this.inputPosition + 1) {
                    current = this.filterNewest();
                    currentComputed = true;
                }

                this.currentSample = current;
            }

            this.nextOutputPosition -= this.inputPosition + 1;
            this.inputPosition = -1;
            return true;
        }
    }

    registerProcessor("microsharp-pcm-recorder", PcmRecorderProcessor);
})();
