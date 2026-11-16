# Architekturentscheidung: Paket- und Adaptergrenze

Vier Pakete trennen den portablen Vertrag, die Browser-Aufnahme, die Betriebssystem-Aufnahme und den begrenzten PCM-Verlauf.

## Grenze

Vier Pakete, ein Vertrag:

| Paket | Öffnet | Adapter |
| --- | --- | --- |
| `MicroSharp` | kein Gerät und kein Browserdokument | nur Vertrag und PCM-Sitzung |
| `MicroSharp.Browser` | Browserdokument über JavaScript, kein Betriebssystemgerät | `BrowserMicrophoneCapture`, `CapturesOperatingSystemDevice` false, `RequiresBrowserHost` true |
| `MicroSharp.Native` | ein benanntes OpenAL-Aufnahmegerät, und getrennt davon eine Prozessquelle | `OpenAlMicrophoneCapture` true/false, `VirtualMicrophoneCapture` false/false |
| `MicroSharp.Processing` | kein Geraet | `PcmHistoryRing`, `Pcm16LeBlockAnalyzer` |

`VirtualMicrophoneCapture` bleibt die deterministische PCM-Pipeline. Sie ist kein Ersatz für die Betriebssystemaufnahme.

Der Browser-Adapter referenziert `Microsoft.AspNetCore.Components.Web` 11.0.0 und setzt `SupportedPlatform` auf `browser`. Eine `FrameworkReference` auf `Microsoft.AspNetCore.App` entfällt, weil ein WebAssembly-Consumer das gemeinsame ASP.NET-Framework nicht mitbringt.

## Native Aufnahme

`OpenAlMicrophoneCapture` ruft die im NuGet-Paket `OpenTK.Audio.OpenAL` 4.9.4, Asset `lib/netcoreapp3.1`, gelesenen Methoden auf: `ALC.GetString(AlcGetStringList.CaptureDeviceSpecifier)`, `ALC.CaptureOpenDevice`, `ALC.CaptureStart`, `ALC.GetInteger` mit `AlcGetInteger.CaptureSamples`, `ALC.CaptureSamples`, `ALC.CaptureStop`, `ALC.CaptureCloseDevice`. Format ist `ALFormat.Mono16`. Die native Bibliothek wird nicht ins Paket kopiert. Die Bindung sucht unter Linux `libopenal.so.1`, unter Windows `openal32.dll` und unter macOS standardmäßig Apples OpenAL-Framework.

Der Gerätename ist Pflicht und darf nicht leer sein. `null` würde in OpenAL das Standardgerät öffnen und wird von diesem Adapter nicht übergeben. Ein unbekannter Name oder `ALCaptureDevice.Null` wird `DeviceNotFound`. Es bleibt dann keine Sitzung zurück. `StopAsync` und `DisposeAsync` rufen `CaptureStop` und `CaptureCloseDevice` auf, auch wenn Start oder Lesen abbricht.

Der OpenAL-Adapter lehnt `CaptureBackpressure.Wait` ab. Der Aufnahmering läuft weiter, während der Leser wartet. Lebende Aufnahme nutzt `DropOldest`. Der virtuelle Adapter behält `Wait` für Tests ohne Gerät.

Die gemeinsame Warteschlange liegt in `MicroSharp` und bleibt dort. Sie kopiert jeden Block, haelt hoechstens die gesetzte Kapazitaet und zaehlt `DropOldest`. Der begrenzte PCM-Verlauf mit unabhaengigen Byte-Cursorn und die PCM16LE-Messung von Dauer und mittlerer absoluter Amplitude liegen in `MicroSharp.Processing`. Das Paket oeffnet kein Mikrofon und enthaelt keine Sprachschwelle, Segmentierung oder Dialogregel. Messungen der Warteschlange und des Verlaufs liegen in `benchmarks/MicroSharp.Benchmarks`. Das Projekt ist nicht paketierbar und gehoert nicht zum Release. Die Zahlen gelten fuer CoreCLR und sind keine Browser- oder WebAssembly-Messung.

## Plattformen

Linux und Windows prüfen den Ladeweg. Ob ein Host ein Aufnahmegerät hat, ist eine Laufzeitgrenze: ohne Gerät ist der Beleg der Fehlerpfad mit einem ausdrücklich ungültigen Namen, nicht eine erfolgreiche physische Aufnahme. `native-linux` installiert `libopenal1` und kann zusätzlich einen privaten PulseAudio-Eingang `microsharp_null` öffnen. Danach beendet dieser Lauf nur den Daemon, den er selbst gestartet hat. `native-windows` lädt OpenAL Soft 1.25.2, Win64, `soft_oal.dll` umbenannt nach `openal32.dll`, nur für den Testhost. Quelle, Version, Archivhash, DLL-Hash und Lizenz stehen in `native/openal-soft-windows.json`. Die Lizenzdatei im Archiv ist die GNU Library General Public License, Version 2. Diese DLL wird nicht ins Paket gelegt. Der Windows-Job hat keinen deterministischen virtuellen Eingang und behauptet keine physische Aufnahme. macOS bleibt aus der Unterstützungsaussage heraus, weil der Standardpfad der Bindung nicht OpenAL Soft ist und kein Lauf vorliegt. iOS, Android und der Browser gehören nicht zum nativen Paket.

`net11` packt und lädt die qualifizierten Pakete erst hoch, wenn `native-linux` und `native-windows` erfolgreich waren. Der Release prüft alle drei Jobnamen. Ein einzelner erfolgreicher Job genügt nicht.

Das einzige Ziel ist `net11.0`. Das OpenTK-Asset ist `lib/netcoreapp3.1` und wird von diesem Ziel wiederhergestellt. Ein aelteres Ziel wird nicht angeboten.

## Einseitige Verbrauchergrenze

`BrowserCaptureController` besitzt Erwerb, Wiederverwendung, Stopp und Freigabe einer Browserquelle. Das Format, die Wartungskapazitaet und der Zustand nach Abbruch kommen vom Aufrufer. `PcmFormat.Default` ist die Bibliotheksvorgabe und keine Annahme einer bestimmten Anwendung. `BrowserCaptureLease` liefert Bloecke aus der begrenzten Sitzung und haelt keine zweite Warteschlange. Ein Stopp beendet die Lieferung, gibt die Quelle aber nicht frei. Erst Freigabe oder ein fehlgeschlagener Start beendet die Pacht.

`PcmHistoryRing.Read` kopiert in einen vom Aufrufer gestellten Puffer. `ReadRightSized` liefert eine neue, genau passende Kopie und schiebt den Cursor vor. `Pcm16LeBlockAnalyzer` misst Dauer und mittlere absolute Amplitude aus dem Block und einer positiven Abtastrate. Ein `PcmFormat` bleibt gueltig. Die Chunk-Dauer ist keine Eingabe der Messung. Schwellen und Dialog bleiben ausserhalb der Bibliothek.

Ein Test durchsucht `src`, `samples`, `benchmarks` und `tests`. Ein Treffer auf den Namensraum der Sprachanwendung ist ein Fehler. Kommentare mit der rechtlichen Herkunft bleiben erlaubt. Projektdateien werden einschliesslich Kommentaren geprueft. Beispiele und CI binden keine Sprach- oder Dialoganwendung ein.
