# Herkunft

Quelle der fachlichen Aufnahme ist https://github.com/sprechen-io/sprechen-io, Commit `1d603f3a75f408d1d11010cd49190e110979803b`, Lizenz MIT, Copyright (c) 2026 sprechen.io.

Übernommen als Vorbild, nicht als Kopie der Anwendung:

- Signed-16-Bit-Little-Endian, ein Kanal, Blöcke und der Medientyp.
- Die Zuordnung der Browserfehlernamen `NotFoundError`, `DevicesNotFoundError`, `OverconstrainedError`, `NotAllowedError`, `PermissionDeniedError` und `SecurityError`.
- Das Fenster-sinc-Resampling und die Int16-Skalierung im AudioWorklet. Die Datei nennt den Quellpfad und den Commit.

Nicht übernommen: Sprachdienst, Zugangsdaten, Turn-Kennungen, Mikrofon-Freigabegatter, Keep-Alive-Oszillator, unbegrenzte Kanäle, Diagnose, Anwendungen und Studienartefakte.

Die OpenAL-Aufnahme ist neu. Sie bindet `OpenTK.Audio.OpenAL` 4.9.4 und kopiert die native Bibliothek nicht. Die Warteschlangenmessung betrifft diese Bibliothek. Der begrenzte PCM-Verlauf mit unabhaengigen Byte-Cursorn und die Messung von Dauer und mittlerer absoluter Amplitude eines PCM16LE-Blocks sind aus demselben Commit abgeleitet. Sprachsegmentierung, Schwellen, TTS, Wakeword und Dialog sind nicht uebernommen.

Die Quellanwendung hat in ihrer eigenen CI historisch .NET 10 verwendet. MicroSharp unterstützt das nicht. Aktuelle Projekte, Beispiele, Tests, Pakete und CI dieser Bibliothek zielen nur auf `net11.0`.
