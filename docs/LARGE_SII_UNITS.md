# Große Save-Units in der Motor-/Getriebeprüfung

Version 1.0.0; abhängige Fortsetzung auf dem vollständigen Stand von PR #94. Die Größenprüfung wurde korrigiert, ohne DLC-Freigaben, Schreibberechtigungen oder Transaktionsschutz zu verändern.

## Nachgewiesene Ursache

Im aktuellen ATS-Quicksave überschritt die erste benötigte `economy`-Unit die Grenze des Detailparsers:

| Messung | Ergebnis |
| --- | ---: |
| Unit-Text, auf LF normalisierte Zeilenenden | 15.822.230 UTF-16-Zeichen |
| UTF-8-Größe derselben Unit | 15.822.230 Bytes |
| Bisheriger vollständiger Unit-Text einschließlich Parser-Wrapper | 15.822.243 Zeichen |
| Textlimit von `ScsDefinitionDocument.Parse` | 8.388.608 Zeichen |
| Gesamter dekodierter Save | 17.753.092 Zeichen / UTF-8-Bytes |
| Größte Arrayfamilie: `discovered_items` | 337.595 Einträge |

`ScsTruckPowertrainSnapshotReader.SaveIndex.Required` setzte bisher alle Zeilen einer benötigten Unit zu einem neuen Text zusammen und übergab diesen an den Definitionsparser. Die Gesamtgrenze des Snapshot-Readers von 128 MiB galt weiterhin, aber der kleinere Detailparser stoppte bereits die `economy`-Unit. Selbst nach einer bloßen Erhöhung seiner Textgrenze wäre die Liste größer als dessen Arraygrenze von 65.536 Einträgen gewesen. Der gewöhnliche Save-Editor verwendet einen anderen Unit-Zugriff und konnte den Truck deshalb weiterhin identifizieren.

Die Messung erfolgte an eindeutig benannten temporären Kopien. Originale von Profil, Save-Info und Spielstand wurden über SHA-256, Länge und Zeitstempel vor und nach der Untersuchung verglichen. Private IDs und SII-Feldwerte sind nicht Bestandteil dieses Dokuments oder der Testdaten.

## Sichere Verarbeitung

Der Snapshot-Reader verwendet jetzt die unveränderlichen Zeilen des vorhandenen `ScsSiiUnitDocument`, ohne den Save erneut in ein zweites Zeilenarray aufzuteilen. Dessen bisherige doppelte Textaufteilung zur bloßen Kopfprüfung entfällt ebenfalls. Der gewöhnliche Unit-Parser wird nicht als alleinige Sicherheitsprüfung eingesetzt: Er überspringt bislang unbekannte Feldzeilen und genügt dafür nicht.

`ScsSaveUnitProjection` prüft jede Feldzeile jeder Unit. Nicht benötigte Werte werden syntaktisch gelesen und verworfen, einschließlich sehr großer Strings und Arrays. Strings durchlaufen eine strenge inkrementelle UTF-8-/Escape-Prüfung mit kleinen Puffern. Für indizierte Arrays werden begrenzte Bitmaps verwendet; ungeordnete Indizes bleiben zulässig, doppelte Indizes, Lücken und widersprüchliche Zähler werden exakt erkannt. Doppelte Skalare, gemischte Arraynotation, beschädigte Tupel, abgeschnittene Strings, Direktiven und verschachtelte Units bleiben unzulässig.

Nur benötigte Felder werden für den unveränderten Definitionsparser zusammengestellt: die Player-Referenz, Besitz- und Zuordnungsreferenzen, Zubehörlisten, `data_path` sowie Slot-/Hookup-Felder. Die Namen aller übrigen Felder bleiben erhalten, damit unbekannte Zubehörsemantik weiterhin sperrt. Referenzexistenz, erwartete Unit-Typen, eindeutiger eigener Truck, doppelte Referenzen, Motor/Getriebe-Modellzuordnung und zusätzliche Zubehörbesitzer werden weiterhin im bekannten Truck-/Zubehörgraph geprüft. Unbekannte Feldwerte werden nicht pauschal als Unit-Referenzen interpretiert.

Pro physischer Feldzeile ist genau ein Feld erlaubt. Das entspricht dem bestehenden Writer und verhindert, dass beim Ersetzen eines `data_path` ein weiteres Feld auf derselben Zeile verloren geht. Leser, Vorschau, Textänderung und Prüfung des geschriebenen Saves verwenden dieselbe `SnapshotReader.Parse`-Interpretation.

## Explizite Grenzen

| Grenze | Wert |
| --- | ---: |
| Physischer Save / dekodierter Text | weiterhin höchstens 128 MiB, auch UTF-8-Größe geprüft |
| Units pro Save | weiterhin höchstens 500.000 |
| Feldfamilien pro Unit | 4.096 |
| Feldzeilen pro Unit | 4.000.000 |
| Einträge einer großen, nicht materialisierten Arrayfamilie | 2.000.000 |
| Gleichzeitig reservierte Bitmap-Indexplätze pro Unit | 8.000.000 Bits, rund 1 MiB Nutzdaten |
| Materialisierte Projektion pro benötigter Unit | höchstens 4 MiB Zeichen |
| Benötigte Referenzlisten | weiterhin höchstens 10.000 Einträge |
| Aktives Zubehör | weiterhin 3 bis 4.096 Einträge |
| Tupel | höchstens 8 Ebenen / 64 Elemente je Tupel |

Die Grenzen des Definitionsparsers bleiben unverändert, einschließlich 8 MiB Text, 750.000 Tokens, 1 MiB decodiertem Stringinhalt und 65.536 Arrayeinträgen. Sie gelten auch für die kleine materialisierte Projektion. Große irrelevante Werte erreichen diesen AST nicht. Es gibt keine unbegrenzte Speichererweiterung.

## Lokale Diagnose und Datenschutz

Erfolgreich verarbeitete Units über der früheren Textgrenze liefern ausschließlich Unit-Typ, LF-normalisierte Zeichen-/UTF-8-Bytezahl und Phase (`StructureValidation` / `FieldProjection`). Die lokale technische Anzeige begrenzt diese Angaben auf 32 Zeilen. Struktur- und Projektionsfehler tragen denselben Größen-/Phasenkontext. Auch Typfehler beim Lesen der benötigten Graphfelder geben keine privaten Unit-IDs oder Werte mehr aus. Die neuen Diagnosedaten werden nicht in den JSON-Export aufgenommen; es erfolgt kein Upload.

## Verifikation und verbleibende Sperren

45 neue Tests ergänzen synthetische ATS- und ETS2-Saves: benötigte Units über 8 MiB, 120.000 Einträge in einem irrelevanten Array, große Strings, ungültige Unicode-/UTF-8-Escapes an Puffergrenzen, doppelte IDs und Referenzen, Arraygrenzen, beschädigte späte Felder, abgeschnittene Daten, Abbruch und sichere Diagnosen. Tatsächliche Resolver-/VFS-/Regel-/Installer-Dienste prüfen je zwei Motor- und Getriebeoptionen; ein verifiziertes Paar lässt sich ausschließlich in synthetischen Saves installieren. Backup und Restore bleiben bytegenau, einschließlich BOM und CRLF. Beschädigte Saves erreichen keine Backup-/Schreibaktion.

Der vollständige Release-Build hat null Warnungen und Fehler. Lokal bestehen 899 von 902 Tests (Core 32, App 263, Infrastructure 604/607); dieselben drei schon auf der Basis reproduzierten Steam-Erkennungstests kollidieren mit der real registrierten Installation. Alle neuen Tests bestehen. Abschließende Windows-CI- und Installer-Links stehen im zugehörigen PR.

Die echte ATS-Leseprüfung erkennt den Kenworth T680 2022 mit 60 Zubehörteilen. Die globale Konfiguration ist geprüft, die Definitionsprüfung läuft und zeigt **11 Motoren sowie 6 Getriebe**. Es gibt keine globale Sperre durch das Textlimit mehr. Die Kandidaten und das aktuelle Paar bleiben wegen nicht nachgewiesener DLC-/Definitionsverfügbarkeit unbestätigt (`dlc_availability_unverified`, `definition_availability_unverified` und daraus folgende `accessory_verification_failed`). Vorhandene DLC-Dateien werden dabei weiterhin nicht zu einem Berechtigungsnachweis. Es fand kein Einbau in einen echten Nutzersave statt.

SHA-256-Bindung, eindeutige Auswahl, Quellenprüfung, Spielprozess-Sperre, Backup, atomare Schreibtransaktion und Restore bleiben erhalten. Kein automatischer Merge.
