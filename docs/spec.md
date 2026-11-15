# Anforderungen MicroSharp

## Auftrag

Allgemeine Mikrofonbibliothek für mehrere C#-Projekte, mit demselben PCM-Vertrag für Browser/WebAssembly und für gewöhnliches .NET. Vorbild ist die PCM-Aufnahme der Anwendung `sprechen-io` im Commit `1d603f3a75f408d1d11010cd49190e110979803b`: vorzeichenbehaftetes 16-Bit-PCM, ein Kanal, Blöcke, und die Klassifikation der `getUserMedia`-Fehlernamen. Nicht übernommen werden Sprachdienst, Zugangsdaten, Turn-Kennungen, Mikrofon-Freigabegatter, Keep-Alive-Oszillator, unbegrenzte Kanäle, Anwendungen und Studienartefakte.

## Format

Geliefert wird nur signed 16-bit little-endian, ein Kanal. Raten sind 8000, 16000, 24000 und 48000 Hz. Die Blockdauer ist eine ganze Zahl von 10 bis 200 Millisekunden und ergibt an der Rate eine ganze Zahl von Abtastwerten. Vorgabe ist 16000 Hz und 20 ms, also 320 Abtastwerte und 640 Bytes. Der Medientyp ist `audio/pcm;rate=...;channels=1;encoding=signed-integer;bits=16;endian=little`. Ein Block mit anderer Länge beendet die Sitzung mit `InvalidChunk`.

## Lebenszyklus

`StartAsync` gibt eine Sitzung zurück oder wirft, bevor eine Sitzung zurückbleibt. Bricht der Token vor der Rückgabe ab, wird jede schon angefasste Quelle freigegeben und `OperationCanceledException` geworfen. Der Lesetoken beendet nur den Leser. `StopAsync` beendet ohne Fehler und lässt bereits eingereihte Blöcke lesbar. `DisposeAsync` ist idempotent und gibt die Quelle frei. Eine zweite gleichzeitige Aufnahme desselben Adapters wirft `Unavailable`. Ein zweiter Leser einer Sitzung wirft `InvalidOperationException`.

Berechtigung, fehlendes Gerät, Format und Backpressure haben eigene Fehlercodes: `PermissionDenied`, `DeviceNotFound`, `UnsupportedFormat`, `UnsupportedBackpressure`. `Unavailable` bleibt der übrige Betriebsfehler.

## Puffer

Die Warteschlange ist begrenzt, Vorgabe 50 Blöcke, genau ein Leser. `DropOldest` verwirft den ältesten Block und zählt ihn. `Wait` blockiert den Produzenten, bis Platz ist. Zeitstempel kommen von einem `TimeProvider`. Browser und OpenAL unterstützen nur `DropOldest`, weil ihre Quelle nicht pausiert. Die virtuelle Quelle unterstützt beide Modi.

## Adapter

Der Browser-Adapter spricht nur über das mitgelieferte JavaScript-Modul und einen AudioWorklet. Er behauptet nicht, in einem Konsolenprozess aufzunehmen. Fehler von `getUserMedia` werden wie im Vorbild eingeordnet: nicht gefunden auf `DeviceNotFound`, verweigert auf `PermissionDenied`, sonst `Unavailable`.

`OpenAlMicrophoneCapture` öffnet ausschließlich den übergebenen Gerätenamen. Leere Namen werden abgelehnt, bevor OpenAL aufgerufen wird. Die Liste der Aufnahmegeräte darf gelesen werden, ohne ein Gerät zu starten. `GetPermissionAsync` meldet `Granted` nur, wenn dieser Name in der Liste steht. Eine fehlende Bibliothek ist `Unavailable` und nicht `DeviceNotFound`.

`VirtualMicrophoneCapture` liest eine vom Aufrufer gesetzte PCM-Quelle. Verweigerte Berechtigung und fehlendes Gerät lesen die Quelle nicht. Die Quelle wird von der Sitzung nicht freigegeben.

Abhängigkeitseinbindung ist optional. Die Typen lassen sich mit `new` erzeugen.

## Beispiele und Prüfungen

Ein gewöhnliches .NET-Beispiel verwendet `OpenAlMicrophoneCapture`. Ein nur virtuelles Beispiel erfüllt das nicht. Ein Blazor-WebAssembly-Beispiel verwendet den Browser-Adapter. Tests prüfen Lebenszyklus, Fehler, Abbruch, Freigabe und tatsächliche PCM-Blöcke. Der positive Audioweg ohne Gerät läuft über die virtuelle Quelle. Der OpenAL-Weg auf einem Host ohne Aufnahmegerät prüft den Fehler mit einem ausdrücklich ungültigen Namen und öffnet dabei nicht das Standardgerät. Die Browseraufnahme wird mit einem headless Browser und einem simulierten Mikrofon geprüft, ohne das Host-Mikrofon zu öffnen.
