# Architekturentscheidung: Paket- und Adaptergrenze

Drei Pakete trennen den portablen Vertrag, die Browser-Aufnahme und die Betriebssystem-Aufnahme.

## Grenze

Drei Pakete, ein Vertrag:

| Paket | Öffnet | Adapter |
| --- | --- | --- |
| `MicroSharp` | kein Gerät und kein Browserdokument | nur Vertrag und PCM-Sitzung |
| `MicroSharp.Browser` | Browserdokument über JavaScript, kein Betriebssystemgerät | `BrowserMicrophoneCapture`, `CapturesOperatingSystemDevice` false, `RequiresBrowserHost` true |
| `MicroSharp.Native` | ein benanntes OpenAL-Aufnahmegerät, und getrennt davon eine Prozessquelle | `OpenAlMicrophoneCapture` true/false, `VirtualMicrophoneCapture` false/false |

`VirtualMicrophoneCapture` bleibt die deterministische PCM-Pipeline. Sie ist kein Ersatz für die Betriebssystemaufnahme.

Der Browser-Adapter referenziert `Microsoft.AspNetCore.Components.Web` 11.0.0 und setzt `SupportedPlatform` auf `browser`. Eine `FrameworkReference` auf `Microsoft.AspNetCore.App` entfällt, weil ein WebAssembly-Consumer das gemeinsame ASP.NET-Framework nicht mitbringt.

## Native Aufnahme

`OpenAlMicrophoneCapture` ruft die im NuGet-Paket `OpenTK.Audio.OpenAL` 4.9.4, Asset `lib/netcoreapp3.1`, gelesenen Methoden auf: `ALC.GetString(AlcGetStringList.CaptureDeviceSpecifier)`, `ALC.CaptureOpenDevice`, `ALC.CaptureStart`, `ALC.GetInteger` mit `AlcGetInteger.CaptureSamples`, `ALC.CaptureSamples`, `ALC.CaptureStop`, `ALC.CaptureCloseDevice`. Format ist `ALFormat.Mono16`. Die native Bibliothek wird nicht ins Paket kopiert. Die Bindung sucht unter Linux `libopenal.so.1`, unter Windows `openal32.dll` und unter macOS standardmäßig Apples OpenAL-Framework.

Der Gerätename ist Pflicht und darf nicht leer sein. `null` würde in OpenAL das Standardgerät öffnen und wird von diesem Adapter nicht übergeben. Ein unbekannter Name oder `ALCaptureDevice.Null` wird `DeviceNotFound`. Es bleibt dann keine Sitzung zurück. `StopAsync` und `DisposeAsync` rufen `CaptureStop` und `CaptureCloseDevice` auf, auch wenn Start oder Lesen abbricht.

Der OpenAL-Adapter lehnt `CaptureBackpressure.Wait` ab. Der Aufnahmering läuft weiter, während der Leser wartet. Lebende Aufnahme nutzt `DropOldest`. Der virtuelle Adapter behält `Wait` für Tests ohne Gerät.

## Plattformen

Linux ist die Plattform, deren Bibliotheksname zur Bindung passt. Ob ein Host ein Aufnahmegerät hat, ist eine Laufzeitgrenze: ohne Gerät ist der Beleg der Fehlerpfad mit einem ausdrücklich ungültigen Namen, nicht eine erfolgreiche physische Aufnahme. Windows ist ungetestet, weil `openal32.dll` hier nicht ausgeführt wurde. macOS bleibt aus der Unterstützungsaussage heraus, weil der Standardpfad der Bindung nicht OpenAL Soft ist und kein Lauf vorliegt. iOS, Android und der Browser gehören nicht zum nativen Paket.

Das einzige Ziel ist `net11.0`. Das OpenTK-Asset ist `lib/netcoreapp3.1` und wird von diesem Ziel wiederhergestellt. Ein aelteres Ziel wird nicht angeboten.
