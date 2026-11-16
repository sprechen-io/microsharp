# MicroSharp Constitution

## Core Principles

### I. Bibliothek zuerst
MicroSharp bleibt der Name der allgemeinen Mikrofonbibliothek. Der Vertrag, die Browser-Aufnahme und die Betriebssystem-Aufnahme bleiben getrennte, wiederverwendbare Bibliotheksgrenzen. Aufrufer duerfen die Typen direkt erzeugen. Abhaengigkeitsinjektion darf angeboten werden und darf nicht erzwungen werden. Sprachdienst-Zugangsdaten, Sprachdienst-Vertraege, Anwendungen und Studienartefakte duerfen nicht in diese Bibliothek uebernommen werden.

### II. Ausschliesslich .NET 11
Aktuelle Projekte, Beispiele, Tests, Pakete, Dokumentation und CI dieser Bibliothek MUESSEN ausschliesslich `net11.0` verwenden. `net10.0`-Ziele, Kompatibilitaetspfade und Matrixjobs sind unzulaessig. Ein reines Zielmoniker ist keine Nutzung einer Sprach- oder Laufzeitfaehigkeit. Historische Herkunft darf aeltere Ziele der Quellanwendung nennen und darf daraus keine aktuelle Unterstuetzung ableiten.

### III. Getrennte echte Aufnahmewege
Der portable Vertrag beschreibt Start, Stopp, Abbruch, Fehler, Berechtigung, Format und Freigabe. Die Quelle enthaelt nur die Browser-Aufnahme. Die Betriebssystem-Aufnahme ist neu und wird nicht als extrahierter Adapter beschrieben. Der Browser-Adapter darf Browser-Faehigkeiten nur auf einem Browser-Host beanspruchen. Der native Adapter MUSS ein echtes Betriebssystem-Aufnahmegeraet ueber eine gepflegte Aufnahmebibliothek oeffnen. Gemeinsame Audiostuecke werden kopiert, begrenzt und bei Verlust gezaehlt. Die technische Pufferform darf sich zwischen Browser und System unterscheiden. Ein virtueller PCM-Lieferant bleibt Testunterstuetzung und ersetzt diesen Adapter nicht.

### IV. Nachweis statt Behauptung
Lebenszyklus, verweigerte Berechtigung, Abbruch, Freigabe und tatsaechlicher Audiofluss MUESSEN geprueft werden. Die Browserpruefung nutzt einen lokalen Browser und ein simuliertes Mikrofon. Das Mikrofon des Hosts darf ohne ausdrueckliche Zustimmung nicht geoeffnet werden. Der Fehlerpfad eines nativen Geraets MUSS einen ausdruecklich ungueltigen Geraetenamen verwenden. Ein Lauf, der die Aufnahmebibliothek laedt und diesen Namen als `DeviceNotFound` von `Unavailable` unterscheidet, belegt den Ladeweg und keine physische Aufnahme. Fehlt die Bibliothek, ist der Lauf fehlgeschlagen. Ein vorbereitetes virtuelles Geraet darf nur unter seinem eigenen Namen geoeffnet werden. Fehlende physische Hardware ist eine benannte Grenze. macOS-Unterstuetzung darf ohne einen Lauf auf macOS nicht zugesagt werden. SIMD, Threads und AOT duerfen nur versprochen oder konfiguriert werden, wenn eine konkrete Browser- oder SDK-Probe auf dem Host sie traegt.

### V. Herkunft und schmale Aenderungen
Uebernommene Aufnahmeideen behalten die MIT-Herkunft der Quelle mit Urheberhinweis und Quellcommit. Jeder oeffentliche C#-Typ liegt in einer eigenen Datei. Oeffentliche Markdown-Prosa ist Deutsch mit ae/oe/ue. Ausnahmetexte sind Englisch. Projektdateien duerfen keinen Benutzer-Homepfad als Fallback oder Installationsvoraussetzung festschreiben. Persoenliche Betriebsbelege bleiben ausserhalb von Git.

### VI. Qualifiziertes NuGet.org-Release und Umgebungsbeispiele
Die qualifizierten Bibliothekspakete werden auf NuGet.org veroeffentlicht. Vor einem Push MUESSEN Paketkennungen, Version, Lizenz, Lieferumfang, native Abhaengigkeiten und die vorgesehenen Tests reviewbar belegt sein. Fremde Pakete duerfen nicht uebernommen oder ueberschrieben werden. Geheimnisse duerfen nicht in Quellen oder Logs stehen. Ein fehlender autorisierter Zugang macht den Release nicht fertig. Pack, lokaler Consumer-Nachweis und der Publishing-Workflow bleiben auch dann verpflichtend. Pro zugesagter Umgebung gibt es ein ausfuehrbares Beispiel, das die veroeffentlichten Paketkennungen und Versionen einbindet. Vor dem Push prueft ein isolierter Consumer genau die gepackten Dateien. Nach dem Push stellt ein sauberer Consumer dieselben Pakete von NuGet.org wieder her.

### VII. Kommentare ausschliesslich Englisch
Alle Kommentare in Quelle, Tests, Beispielen, Skripten und Konfiguration MUESSEN ausschliesslich Englisch sein. Das umfasst Inline-Kommentare, Blockkommentare und XML-Dokumentationskommentare. XML-Dokumentationskommentare sind ausdruecklich Englisch. Oeffentliche Markdown-Prosa bleibt Deutsch mit ae/oe/ue. Ausnahmetexte bleiben Englisch. Rechtliche Herkunfts- und Urhebervermerke bleiben erhalten.

### VIII. Einseitige Verbrauchergrenze
MicroSharp besitzt die wiederverwendbare Aufnahme: Geraet und Browserquelle, Aufnahmegraf und Blocklieferung, Format, Lebenszyklus und Besitz einer Mikrofonquelle, begrenzte Aufnahmewarteschlange, PCM-Verlauf und Lesemechanik sowie Blockmessung. Projekte und Typen dieser Bibliothek duerfen nicht von einer Sprach- oder Dialoganwendung, deren Vertraegen oder einem Quellcheckout einer solchen Anwendung abhaengen. Eine Anwendung verbraucht versionierte Pakete und oeffentliche Bibliotheks-APIs. Schwellen, Wake-Word, Dialog, Sprachdienst und die Entscheidung, wann gehoert oder eine Aeusserung abgeschlossen wird, bleiben bei der Anwendung. Die Bibliothek enthaelt keinen anwendungsspezifischen Shim, nur damit alte Tests bestehen. Beispiele, Tests und CI laufen ohne eine solche Anwendung. Ein Architekturtest weist eine solche Abhaengigkeit zurueck. Rechtliche Herkunftskommentare bleiben erlaubt.

## Plattform- und Laufzeitgrenzen

Aktuelle Produktartefakte duerfen nur Faehigkeiten nutzen, die eine offizielle .NET-11-, C#- oder Blazor-Quelle als verfuegbar ausweist und die ein Build, eine Veroeffentlichung oder eine Consumer-Pruefung beobachtet. Vorschau-Speichersicherheit bleibt aus. Die unterstuetzte PCM-Form ist vorzeichenbehaftetes 16-Bit-Little-Endian-Mono. Linux und Windows pruefen den OpenAL-Ladeweg in der CI. Physische Aufnahme bleibt eine eigene Grenze. macOS bleibt ohne Lauf aus der Unterstuetzungsaussage heraus.

## Qualitaets- und Nachweistore

Rote Tests duerfen nicht durch Weglassen, gesenkte Schwellen oder falsche Erfolgsmeldungen geheilt werden. Schwere Builds und Tests laufen seriell und begrenzt. Zeitmessungen der gemeinsamen Warteschlange liegen in einem nicht paketierbaren Projekt. Eine Dauer auf einem geteilten Rechner ist keine Abnahmeschwelle. Messzahlen unter CoreCLR sind keine Browser-Messung.

## Governance

Diese Verfassung ersetzt fuer MicroSharp entgegenstehende aeltere Doppellauf- und Zielvorgaben. Sie gilt nicht fuer andere abgeschlossene Auftraege. Aenderungen werden in dieser Datei festgehalten, semantisch versioniert und mit dem Aenderungsdatum versehen. Eine inkompatible Streichung oder Umdeutung eines Grundsatzes hebt die Hauptversion. Ein neuer Grundsatz oder ein wesentlich erweiterter Abschnitt hebt die Nebenversion. Reine Klarstellungen heben die Patchversion. Reviews pruefen die Einhaltung: nur `net11.0`, ehrliche Plattformaussagen, getrennte echte Aufnahmewege, Herkunft, qualifiziertes NuGet.org-Release, englische Kommentare einschliesslich XML-Dokumentation und die einseitige Verbrauchergrenze.

**Version**: 2.1.0 | **Ratified**: 2026-11-15 | **Last Amended**: 2026-11-16T19:36:39Z
