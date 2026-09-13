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
- [ ] SCS-Home-Auflösung
- [ ] Startprüfung
- [ ] `-homedir`
- [ ] zusätzliche Startparameter
- [ ] sicherer Prozessstart

## Phase 4 – Profile und Mods
- [ ] `profiles`
- [ ] `steam_profiles`
- [ ] Mod-Verzeichnis
- [ ] Modzählung

## Phase 5 – UI-Ausbau
- [ ] vollständiges Dashboard
- [ ] Navigation
- [ ] Modset-Karten und Detailseiten
- [ ] Einstellungen

## Danach
- [ ] Modset-Duplizierung
- [ ] Backup
- [ ] Workshop-Grundlage
- [ ] Installer
- [ ] Update-Mechanismus
