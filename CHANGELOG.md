# Changelog

Alle wesentlichen Änderungen am NMC SCS LAUNCHER werden hier dokumentiert.

## 0.4.0-dev

### Added
- Read-only Inspektion des spielbezogenen SCS-Datenordners pro Modset.
- Zählung von `.scs`-Paketen und entpackten Mod-Verzeichnissen ohne rekursive Vollanalyse.
- Erkennung lokaler Profile unter `profiles` und Steam-Profile unter `steam_profiles`.
- Fehlertolerante Inspektion bei fehlenden oder nicht lesbaren Ordnern.
- Detailbereich mit Mod-/Profilzahlen für das ausgewählte Modset.
- Manuelle Aktualisierung sowie Explorer-Aktionen für Mod-, lokale Profil- und Steam-Profilordner.
- Tests, die sicherstellen, dass die Inspektion keine fehlenden SCS-Datenordner erzeugt oder SII-Dateien verändert.

## 0.3.0-dev

### Added
- SCS-konforme `-homedir`-Startlogik mit Home-Basis oberhalb des spielbezogenen Datenordners.
- Startprüfung für Spielzuordnung, x64-Executable, Home-Pfad, Schreibrechte und bereits laufende Prozesse.
- Eigener Startprüfungsdialog mit Sperre bei kritischen Fehlern und ausdrücklicher Warnungsbestätigung.
- Sicherer Prozessstart über `ProcessStartInfo.ArgumentList` statt Shell-Stringverkettung.
- Blockierung eines benutzerdefinierten `-homedir`, da dieser Parameter vom Launcher verwaltet wird.
- Start-Button für das ausgewählte Modset.
- Persistenz und Anzeige von `LastStartedAt` nach erfolgreicher Prozesserzeugung.
- Tests für SCS-Home-Auflösung, Argumentparser und Startprüfungen.

### Verification pending
- Realer Praxistest mit installierter Steam-Version von ETS2 und/oder ATS.

## 0.2.0-dev

### Added
- Modset-Datenmodell mit stabilen GUIDs und Spielzuordnung.
- Atomare lokale `modsets.json`-Persistenz.
- Sichere Create-, Import-, Update- und Remove-Fachlogik.
- Importierte Modsets werden beim Entfernen niemals vom Datenträger gelöscht.
- Duplikatschutz für Modset-Namen pro Spiel.
- Modset-Tabelle mit Auswahl und Verwaltungsaktionen.
- Dialoge für Erstellen, Importieren und Bearbeiten.
- Explizite Entfernen-Bestätigung mit Hinweis, dass Dateien erhalten bleiben.
- Öffnen des Home-Verzeichnisses über Windows Explorer.
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
