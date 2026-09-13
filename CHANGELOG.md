# Changelog

Alle wesentlichen Änderungen am NMC SCS LAUNCHER werden hier dokumentiert.

## 0.2.0-dev

### Added
- Modset-Datenmodell mit stabilen GUIDs und Spielzuordnung.
- Atomare lokale `modsets.json`-Persistenz.
- Sichere Create-, Import-, Update- und Remove-Fachlogik.
- Importierte Modsets werden beim Entfernen niemals vom Datenträger gelöscht.
- Duplikatschutz für Modset-Namen pro Spiel.
- Read-only-Modset-Anzeige in der Startoberfläche.
- Tests für Persistenz, Managed-Verzeichnisse und sichere Entfernung.

## 0.1.0-dev

### Added
- Initiale .NET-10-/WPF-Projektstruktur mit Core, Infrastructure und App.
- Dependency Injection, lokale JSON-Einstellungen und dateibasiertes Logging.
- GitHub-Actions-CI mit Restore, Build, Tests und Windows-Publish-Artefakt.
- Dark-Mode-App-Shell für die spätere Launcher-Oberfläche.
- Steam-Library-Erkennung über Registry, Standardpfad und `libraryfolders.vdf`.
- Automatische ETS2-/ATS-Erkennung über Steam-App-Manifeste.
- Validierung der 64-Bit-Executables von ETS2 und ATS.
- Manuelle Auswahl und persistente Speicherung von Spielinstallationspfaden.
- Game-Detection-Ansicht für ETS2 und ATS.
- Unit Tests für Spieldefinitionen, Steam-Libraries und Installationserkennung.

### Fixed
- Fehlende xUnit-Namespace-Imports in den initialen Tests.
