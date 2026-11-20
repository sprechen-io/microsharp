# MicroSharp

MicroSharp is a C# microphone-capture library for browser and ordinary .NET projects. Micro names the microphone, and the package texts say so because the same short name is also used by an unrelated microcontroller compiler.

The portable package does not open a device. `MicroSharp.Browser` captures in a browser document through JavaScript. `MicroSharp.Native` opens a named OpenAL capture device and, separately, can play PCM from an in-process source.

## Packages

| Package | Role |
| --- | --- |
| `MicroSharp` | PCM contract and session. No microphone and no browser API. |
| `MicroSharp.Browser` | Blazor and WebAssembly capture through `IJSRuntime`. |
| `MicroSharp.Native` | `OpenAlMicrophoneCapture` and `VirtualMicrophoneCapture`. |
| `MicroSharp.Processing` | Begrenzter PCM-Verlauf mit Byte-Cursorn und PCM16LE-Blockmessung. Kein Mikrofon und keine Sprachschwelle. |

`MicroSharp.Processing` haelt einen festen PCM-Verlauf mit unabhaengigen Byte-Cursorn und misst Dauer sowie mittlere absolute Amplitude eines PCM16LE-Blocks. Die lebende Aufnahmefolge bleibt in `MicroSharp`. Sprachschwellen, Segmentierung und Dialog bleiben ausserhalb dieses Pakets.

The only target is `net11.0`. The browser package references `Microsoft.AspNetCore.Components.Web` 11.0.0 and declares the `browser` platform. It does not reference the shared ASP.NET framework. Runtime Async stays off: on SDK 11.0.100 the WebAssembly interpreter rejected a runtime-async entry point and a runtime-async `WriteAsync`. The C# 15 collection-expression capacity argument is used for the bounded chunk queue.

## Audio

Delivered audio is signed 16-bit little-endian, one channel, at 8000, 16000, 24000, or 48000 Hz. The default is 16000 Hz and 20 ms, which is 640 bytes. `StopAsync` ends capture without an error and leaves queued chunks readable. `DisposeAsync` may be called more than once. Cancelling `StartAsync` releases a partial acquisition. Cancelling `ReadChunksAsync` stops only that reader.

`DropOldest` is the live-capture backpressure mode. Browser and OpenAL reject `Wait` because their sources cannot pause. The virtual source accepts `Wait`.

## OpenAL

`OpenAlMicrophoneCapture` requires an explicit device name. An empty name is rejected so the OpenAL default device is never opened by accident. Pass a name from `ListCaptureDevices()`. `ALCaptureDevice.Null` and an unknown name become `DeviceNotFound`, and no session remains.

The binding is `OpenTK.Audio.OpenAL` 4.9.4 (MIT). It calls `ALC.CaptureOpenDevice`, `CaptureStart`, `CaptureSamples`, `CaptureStop`, and `CaptureCloseDevice` with `ALFormat.Mono16`. The native library is not part of the NuGet package.

On macOS the adapter checks the requested name against OpenAL's capture list before opening it because Apple's framework can open a device for an absent name.

| System | Library the binding looks for | This repository's claim |
| --- | --- | --- |
| Linux | `libopenal.so.1` | CI loads the library. An explicit invalid name is device-not-found. A prepared virtual source named `microsharp_null` is a separate PCM check and is not a physical microphone. |
| Windows | `openal32.dll` | CI loads OpenAL Soft 1.25.2 Win64 as `openal32.dll` and checks the same invalid name. That is the load path. It is not a physical recording. |
| macOS 15 (Apple Silicon) | Apple's OpenAL framework | The [`native-macos` CI job](.github/workflows/ci.yml) checks OpenAL loading and rejection of an explicit absent capture device. Lifecycle and in-process PCM tests use managed fixtures. Physical microphone recording is outside this CI job's scope. |

On Linux and Windows, install OpenAL Soft before opening a device. On Linux that is `libopenal.so.1`. On Windows the binding loads `openal32.dll`. A missing library is `Unavailable`, which is distinct from `DeviceNotFound`, and a test that needs the library fails in that case. The native library is not part of the NuGet package. The Windows CI download is OpenAL Soft 1.25.2, recorded in `native/openal-soft-windows.json`.

`benchmarks/MicroSharp.Benchmarks` measures the shared PCM queue, the PCM history ring, and PCM16LE block analysis on CoreCLR. It is not a package. The CI Dry run only starts the harness. Measured runs are the manual `Benchmarks` workflow. Those numbers are not browser or WebAssembly results.

`VirtualMicrophoneCapture` does not open an operating-system device. Use it for tests and for PCM that already exists in the process.

## Browser

The module is served from `_content/MicroSharp.Browser/microsharp-capture.js`. The worklet resamples to the requested rate and posts 16-bit chunks. Register `AddMicroSharpBrowser` only when `IJSRuntime` is already registered, or construct `BrowserMicrophoneCapture` directly. This adapter cannot capture in a console process.

`BrowserCaptureController` besitzt Erwerb, Wiederverwendung, Stopp und Freigabe einer Browserquelle. Das Blazor-Beispiel zeigt diese Steuerung mit der gewaehlten Rate. `ReadRightSized` liefert die passende Kopie aus dem PCM-Verlauf.

### Browser playback

`MicroSharp.Browser.Playback` plays mono PCM16LE through an owned audio context. Await the final `EnqueueAsync`, then optionally call `DrainAsync(cancellationToken)` once to close input and wait for the render worklet. The drain watchdog reports a timeout if the worklet does not acknowledge within five seconds. Stop and dispose interrupt a pending drain. After end-of-input is sent, canceling the wait leaves input closed. Call `StopAsync` and then `StartAsync` to write again.

The worklet renders source positions through the last real sample, inclusive. Fractional positions use two real samples. Zero-valued PCM counts as content, and silence after the terminal position is padding. Empty input has zero content frames. Drain covers the worklet output boundary. Device latency and any application limiter, fade or other DSP tail have their own timing.

Browser JavaScript hosts can import `createBorrowedPlayer(context, destination, inputRate, capacity)` from the playback module. Activate the context before starting the player and supply an input-capable node in that context. Reserve frames, then pass exactly the reserved prefix as a `Float32Array` view to `enqueue`. The player copies that view and renders it at Float32 precision. Stop detaches the playback worklet from the caller's graph. The caller manages the context and its other nodes.

A single borrowed writer can call `waitForCredit(signal)` when the FIFO is full, then call `reserve` again to claim frames. The wait accepts an optional `AbortSignal` and has a five-second deadline. A host that wraps its context can pass a matching worklet node constructor as the fifth argument to `createBorrowedPlayer`.

The optional sixth argument sets `minimumBufferedInputFrames` and `initialOutputDelayFrames`. Both default to zero. The prefill cannot exceed FIFO capacity. The lead starts after prefill and is limited to one second in context output frames. With lead alone, the first input frame starts the countdown. Short streams can start below prefill at EOF, and empty streams skip the lead. Startup silence leaves the content and underrun counters unchanged.

## Samples

`samples/MicroSharp.Console` is an ordinary .NET consumer:

```text
dotnet run --project samples/MicroSharp.Console -- --device <openal-capture-name>
```

`--list` prints capture names and does not open a device. `--virtual` reads one in-process tone chunk and copies it with `ReadRightSized`. An unknown `--device` name exits with code 3 and does not fall back to the default device.

`samples/MicroSharp.BlazorWasm` is the WebAssembly consumer. It starts capture only from the button.

## License

The C# code and the JavaScript port are MIT, Copyright (c) 2026 sprechen.io. See `NOTICE` for the microphone example they were derived from and for the OpenAL license boundary.
