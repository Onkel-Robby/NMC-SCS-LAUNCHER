# Architektur

## Ziel

NMC SCS LAUNCHER ist eine Windows-Desktop-Anwendung zur sicheren Verwaltung voneinander getrennter ETS2-/ATS-Modset-Umgebungen.

## Projekte

### NmcScsLauncher.Core
Enthält technologieunabhängige Modelle und Verträge. Aktuell: Spieltypen, Spieldefinitionen, Installationsmodelle, Einstellungen, Modset-/Backup-Verträge sowie die Verträge für die sichere SCS-Profil-/Savebearbeitung.

### NmcScsLauncher.Infrastructure
Implementiert Dateisystem- und Windows-nahe Funktionen. Aktuell: App-Pfade, JSON-Persistenz, Dateilogging, Steam-Library-Erkennung, Steam-basierte ETS2-/ATS-Installationserkennung sowie die abgesicherte Save-Erkennung, Save-Backups und Save-Schreibpipeline.

### NmcScsLauncher.App
WPF/MVVM-Oberfläche. Die UI greift über Interfaces auf Fach- und Infrastrukturservices zu. Dateisystem-, Save- oder Erkennungslogik gehört nicht in Code-Behind.

## Game Detection

Steam wird best-effort über Benutzer-/Maschinen-Registry und den üblichen Steam-Pfad gesucht. Zusätzliche Bibliotheken werden aus `steamapps/libraryfolders.vdf` gelesen. Spielinstallationen werden anhand des passenden `appmanifest_<appid>.acf`, `installdir` und der vorhandenen x64-Executable validiert.

Gespeicherte Installationspfade werden beim Start zuerst validiert. Eine automatische Neuerkennung ignoriert den gespeicherten Pfad und durchsucht Steam erneut. Manuell ausgewählte Pfade werden nur gespeichert, wenn die erwartete x64-Executable existiert.

## Persistenz

Launcher-Einstellungen werden atomar als JSON unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\settings.json` gespeichert. Keine Datenbank ist für den aktuellen Umfang erforderlich.

Save-Editor-Backups werden getrennt von den SCS-Profilen unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\save-editor-backups` gespeichert.

## Save-Editor-Grenze

Save-Bearbeitung ist eine explizite Benutzeraktion und wird nicht beim normalen Spielstart ausgeführt.

Die erste Infrastrukturstufe:

- erkennt lokale `profiles` und `steam_profiles` innerhalb eines ausgewählten SCS-Home-Verzeichnisses;
- listet nur Save-Verzeichnisse mit vorhandener `game.sii`;
- verweigert Schreibzugriffe, solange ETS2 oder ATS läuft;
- akzeptiert nur eine `game.sii`, die tatsächlich innerhalb des ausgewählten Save-Verzeichnisses liegt;
- erstellt vor jeder Dateiersetzung ein vollständiges Backup des gewählten Save-Verzeichnisses;
- schreibt zunächst in eine temporäre Datei im selben Verzeichnis und ersetzt erst nach erfolgreicher Verifikation die aktive Datei;
- lehnt unbekannte, verschlüsselte oder binäre SII-Formate fail-closed ab, bis ein separat geprüfter Decoder integriert ist.

Direkte Codeübernahmen aus `CoffeSiberian/truck-tools` erfolgen nicht. Dessen GPLv3-Code dient nur als technische Referenz für das SCS-Datenmodell. Eine spätere Decoder-Komponente muss mit einer für das Projekt geeigneten Lizenz und klarer Drittanbieter-Dokumentation integriert werden.

## Sicherheitsregeln

- Keine stillen Savegame-Änderungen.
- Save-Bearbeitung nur nach expliziter Auswahl von Profil und Save.
- Vor jeder tatsächlichen Save-Änderung vollständiges Backup.
- Keine Save-Schreibzugriffe bei laufendem ETS2/ATS.
- Unbekannte oder mehrdeutige Save-Strukturen werden nicht verändert.
- Keine Steam-Cloud-Manipulation außerhalb der vom Benutzer ausgewählten lokalen Profildateien.
- Keine automatischen Löschvorgänge an Benutzerdateien.
- Externe Pfade werden validiert, bevor sie persistiert oder für Starts bzw. Save-Änderungen verwendet werden.

## Tests

Core- und Infrastructure-Tests verwenden temporäre Verzeichnisse und künstliche Steam-/SCS-Strukturen. Es werden keine realen Benutzerprofile oder echten Steam-Installationen verändert.
