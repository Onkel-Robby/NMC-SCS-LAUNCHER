# Architektur

## Ziel

NMC SCS LAUNCHER ist eine Windows-Desktop-Anwendung zur sicheren Verwaltung voneinander getrennter ETS2-/ATS-Modset-Umgebungen.

## Projekte

### NmcScsLauncher.Core
Enthält technologieunabhängige Modelle und Verträge. Aktuell: Spieltypen, Spieldefinitionen, Installationsmodelle, Einstellungen sowie Interfaces für Persistenz, Logging und Game Detection.

### NmcScsLauncher.Infrastructure
Implementiert Dateisystem- und Windows-nahe Funktionen. Aktuell: App-Pfade, JSON-Persistenz, Dateilogging, Steam-Library-Erkennung und Steam-basierte ETS2-/ATS-Installationserkennung.

### NmcScsLauncher.App
WPF/MVVM-Oberfläche. Die UI greift über Interfaces auf Fach- und Infrastrukturservices zu. Dateisystem- oder Erkennungslogik gehört nicht in Code-Behind.

## Game Detection

Steam wird best-effort über Benutzer-/Maschinen-Registry und den üblichen Steam-Pfad gesucht. Zusätzliche Bibliotheken werden aus `steamapps/libraryfolders.vdf` gelesen. Spielinstallationen werden anhand des passenden `appmanifest_<appid>.acf`, `installdir` und der vorhandenen x64-Executable validiert.

Gespeicherte Installationspfade werden beim Start zuerst validiert. Eine automatische Neuerkennung ignoriert den gespeicherten Pfad und durchsucht Steam erneut. Manuell ausgewählte Pfade werden nur gespeichert, wenn die erwartete x64-Executable existiert.

## Persistenz

Launcher-Einstellungen werden atomar als JSON unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\settings.json` gespeichert. Keine Datenbank ist für den aktuellen Umfang erforderlich.

## Sicherheitsregeln

- Keine Savegame-Manipulation.
- Keine Steam-Cloud-Manipulation.
- Keine automatischen Löschvorgänge an Benutzerdateien.
- Externe Pfade werden validiert, bevor sie persistiert oder später für Starts verwendet werden.

## Tests

Core- und Infrastructure-Tests verwenden temporäre Verzeichnisse und künstliche Steam-Strukturen. Es werden keine realen Benutzerprofile oder echten Steam-Installationen verändert.
