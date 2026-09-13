# Roadmap

Status: `[ ]` geplant · `[~]` in Arbeit · `[x]` fertig

## Phase 0 – Projektbasis
- [x] Solution und Projekttrennung
- [x] WPF-App-Shell
- [x] Dependency Injection
- [x] lokale JSON-Einstellungen
- [x] lokales Logging
- [x] GitHub Actions
- [x] initiale Tests

## Phase 1 – Game Detection
- [x] `GameType` und Spieldefinitionen
- [x] Steam-Root-Erkennung
- [x] zusätzliche Steam-Libraries aus `libraryfolders.vdf`
- [x] ETS2-Erkennung
- [x] ATS-Erkennung
- [x] manuelle Installationspfade
- [x] Persistenz erkannter/manueller Pfade
- [x] Tests für Erkennungslogik

## Phase 2 – Modsets
- [x] Modset-Datenmodell
- [x] atomare JSON-Persistenz
- [x] sichere Create-/Import-/Update-/Remove-Fachlogik
- [x] Modset-Liste und Auswahl in der UI
- [x] Erstellen-Dialog
- [x] Importieren-Dialog
- [x] Bearbeiten-Dialog
- [x] Entfernen-Bestätigung ohne Dateilöschung
- [x] Home-Verzeichnis im Explorer öffnen
- [x] Tests für Persistenz und Sicherheitsregeln

## Phase 3 – Game Launch
- [x] SCS-Home-Auflösung und `-homedir`-Semantik
- [x] Startprüfung
- [x] sichere Argumenterzeugung ohne Shell-Stringverkettung
- [x] Schutz vor überschriebenem `-homedir`
- [x] Warnung bei bereits laufendem Spielprozess
- [x] Startprüfungsdialog
- [x] Start-Button und Prozessstart
- [x] `LastStartedAt`-Persistenz
- [ ] Praxistest mit realer ETS2-/ATS-Installation

## Phase 4 – Profile und Mods
- [x] read-only SCS-Datenordner-Auflösung
- [x] `.scs`-Modzählung
- [x] entpackte Mod-Verzeichnisse zählen
- [x] `profiles` erkennen
- [x] `steam_profiles` erkennen
- [x] fehlende Ordner ohne Schreibzugriff behandeln
- [x] Tests für Inspektion
- [x] Anzeige für Mods und Profile
- [x] manuelles Aktualisieren
- [x] Explorer-Aktionen für Mod-/Profilordner

## Phase 5 – UI-Ausbau
- [x] echte Sidebar-Navigation
- [x] Dashboard/Übersicht
- [x] getrennte ETS2- und ATS-Seiten
- [x] Modset-Verwaltungsseite mit Detailbereich
- [x] Einstellungen-Seite
- [x] Info-Seite und Disclaimer
- [x] Startprüfung/Modanzahl/letztes Modset über Settings steuerbar
- [~] visuelles Feintuning zum finalen Mockup

## Danach
- [x] Modset-Duplizierung
- [~] Backup
- [ ] Workshop-Grundlage
- [ ] Installer
- [ ] Update-Mechanismus
