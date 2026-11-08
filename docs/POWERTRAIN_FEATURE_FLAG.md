# Vorübergehende Deaktivierung von Motoren und Getrieben

Stand: 8. November 2026, Version 1.0.0. Ausgangspunkt ist `main` nach den zusammengeführten PRs #90–#96.

## Zentrale Freigabe

`LauncherFeatureFlags.Current` in `src/NmcScsLauncher.Core/LauncherFeatureFlags.cs` setzt `PowertrainEditingEnabled` auf `false`. Die Konfiguration ist unveränderlich. Es gibt keinen Schalter in Einstellungen, Umgebungsvariablen, Tastaturaktionen oder Startparametern. Eine spätere Freigabe ist eine kontrollierte Codeänderung an dieser zentralen Stelle und benötigt erneut Tests und fachliche Abnahme. Die bisherigen DLC-/Kompatibilitätssperren werden dadurch nicht aufgehoben.

## Oberfläche und Aufrufwege

- Der gesamte Motor-/Getriebebereich im Truck-Tab ist ausgeblendet; die verbleibenden Fahrzeugfunktionen nutzen die volle Breite.
- Der Komponentenkatalog-Tab und beide eigenständigen Powertrain-Views sind inklusive Suche, Auswahl, Status, Diagnose und Einbauaktion ausgeblendet, deaktiviert und aus der Tastaturnavigation entfernt.
- Das ViewModel startet keine automatische Powertrain-Inspektion oder Katalogabfrage. Befehle prüfen die Freigabe sowohl in `CanExecute` als auch bei direktem Methodenaufruf. Alte Kommandoreferenzen und manipulierte Auswahlwerte ersetzen diese Freigabe nicht.
- Discovery, Inspektion, Verifikationsöffnung, Paar-Einbau sowie die älteren getrennten Motor-/Getriebe-Lese- und Schreibmethoden prüfen die Freigabe vor Dateizugriffen und vor Backup-/Schreibaktionen.

Archivparser, Snapshot-Reader, Regeln, Diagnoseimplementierungen und alle bisherigen Tests bleiben vorhanden. Instanzlokale, nur für die Testassemblies zugängliche Konfigurationen halten die aktivierten Implementierungspfade prüfbar; sie verändern niemals den globalen Release-Schalter.

## Unveränderte Funktionen und Grenze

Kennzeichen, Kilometerstand, Tank, Reparatur, Truck-/Trailerwechsel und die anderen freigegebenen Save-Editor-Funktionen bleiben verfügbar. LicenseHub und Version 1.0.0 werden nicht verändert.

Der generische `ScsSaveEditService.ApplyAsync` und Restore bleiben unverändert: Sie bilden die gemeinsame Transaktionsschicht für alle Save-Funktionen. Die normale Oberfläche stellt keinen beliebigen SII-Texteditor bereit; ihre Aufrufe erzeugen Inhalte über die jeweiligen fachlichen Dienste. Alle fachlichen Motor-/Getriebeschreibwege sind gesperrt, einschließlich der älteren `SetActiveTruckEngineAsync`-/`SetActiveTruckTransmissionAsync`-Methoden. Ein vertrauenswürdiger In-Process-Aufrufer, der eigenmächtig beliebige SII-Inhalte erstellt, liegt außerhalb dieser Feature-Grenze. Die Feature-Konfiguration ist keine Sandbox für fremden Code.

Bestehende Backups bleiben wiederherstellbar. Das Wiederherstellen eines früheren Zustands ist keine neu freigegebene Motor-/Getriebebearbeitung; die bestehenden Restore-, SHA-256- und Prozessprüfungen gelten unverändert.

## Regressionen

Neue Tests prüfen verborgene und deaktivierte Views, vollständiges Verschwinden des Katalog-Tabs, die ausfüllende Truck-Anordnung, Tastaturzugriff, fehlende Hintergrundaufrufe, direkte Kommandorausführung, zurückbehaltene Kommandos und die Service-Sperre vor jeder Abhängigkeit. Die vorhandenen aktivierten Powertrain-, Parser-, Kompatibilitäts-, Schreib-, Backup- und Restore-Tests laufen weiterhin; andere Save-Editor-Tests verwenden standardmäßig die deaktivierte Release-Konfiguration.

Der lokale Windows-Release-Build besteht ohne Warnungen oder Fehler. Die vollständige lokale Suite umfasst 952 Tests (18 zusätzliche Fälle): Core 34/34, App 278/278 und Infrastructure 637/640 bestanden. Ausschließlich die drei bereits zuvor reproduzierten, von der realen registrierten Steam-Installation beeinflussten Installationserkennungstests schlagen lokal fehl. Keine Tests wurden entfernt oder übersprungen.

Eine isolierte WPF-Vorschau mit echten MainWindow-/View-Klassen und den vollständigen App-Ressourcen wurde bei 1080 und 1320 Pixeln Breite gerendert und visuell geprüft. Kein Katalog-Tab, kein Antriebsbereich und kein leerer rechter Bereich bleiben sichtbar. Die Vorschau verwendet ausschließlich synthetische Daten und ruft weder LicenseHub noch Spiele oder echte Spielstände auf.

Vollständige Windows-CI und NMC Custom Installer werden für den PR geprüft; die konkreten abschließenden Ergebnisse stehen im PR-Bericht.
