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

## Phase 6 – Backup / Restore
- [x] ZIP-Backup mit NMC-Manifest
- [x] Konfiguration und Profile standardmäßig sichern
- [x] Mods optional sichern
- [x] sichere Restore-Vorschau
- [x] Restore nur für passendes Spiel
- [x] explizite Freigabe vor Überschreiben vorhandener Dateien
- [x] Pfad-/ZIP-Sicherheitsprüfung
- [x] Backup-/Restore-UI mit Fortschritt

## Phase 7 – Workshop-Grundlage
- [x] lokale Steam-Workshop-Content-Roots pro Spiel erkennen
- [x] numerische Workshop-/PublishedFileId-Verzeichnisse read-only erfassen
- [x] mehrere Steam-Libraries berücksichtigen
- [x] Links/Junctions nicht verfolgen
- [x] keine Workshop-Abonnements verändern
- [x] keinen Subscription-Status aus bloßer Ordnerexistenz ableiten
- [ ] spätere Workshop-UI / bestätigte SteamUGC-Integration bei Bedarf

## Vor Installer verpflichtend

### LicenseHub – Lizenzschutz
- [x] endgültige LicenseHub-Produktionsdomain festgelegt: `https://licensehub.nmc-it-service.cloud`
- [x] reale LicenseHub-Desktop-Contracts für Aktivierung, Validierung und Deaktivierung verifiziert
- [x] LicenseHub als Lizenzautorität für produktiv erzwungene Builds angebunden
- [x] maschinengebundene Aktivierung mit produktgebundenem SHA-256-Identifier
- [x] Lizenzschlüssel lokal über Windows Credential Manager statt Klartext-JSON gespeichert
- [x] Aktivierungs-/Statusdialog mit Ablaufdatum und Aktivierungszahlen
- [x] Start-/Nutzungs-Gate bei ungültiger, blockierter, abgelaufener oder nicht aktivierter Lizenz
- [x] zweites Lizenz-Gate unmittelbar vor der Prozesserzeugung
- [x] fail-closed Verhalten bei Timeout, Unerreichbarkeit, Protokollfehler oder fehlender Required-Konfiguration
- [x] Unit Tests und Windows-CI für Client, Runtime und Start-Gates
- [~] Product Slug/API-Credential und Testlizenz für den realen End-to-End-Lauf vollständig provisionieren
- [ ] End-to-End-Test mit echter LicenseHub-Lizenz: aktivieren → validieren → sperren/ablaufen → Start muss blockieren → deaktivieren

### LicenseHub – Updates
- [x] Launcher auf lizenz- und maschinengebundenen Desktop-Updatevertrag umgestellt
- [x] langlebigen Update-Bearer-Token aus dem Desktop-Design entfernt
- [x] verfügbare Launcher-Version über LicenseHub abfragen
- [x] kurzlebigen signierten LicenseHub-Download-Endpunkt verwenden
- [x] SHA-256 während des Downloads verifizieren
- [x] SHA-256 im externen Updater unmittelbar vor dem Anwenden erneut verifizieren
- [x] Update als vollständiges ZIP-Publish-Bundle herunterladen und sicher entpacken
- [x] ZIP-Traversal/absolute Pfade außerhalb des Staging-Verzeichnisses blockieren
- [x] Anwendung über separaten self-contained Updater austauschen
- [x] alte Anwendung vor Aktivierung der neuen Version als Rollback-Backup erhalten
- [x] automatisches Rollback bei fehlgeschlagenem Verzeichnistausch oder fehlgeschlagenem Neustart
- [x] Updatefenster mit Version, Changelog, Fortschritt und kontrolliertem Neustart
- [x] automatische Prüfung nach Programmstart nur bei vollständiger LicenseHub-Konfiguration und gespeicherter Lizenz
- [x] Entwicklungsbuild ohne vollständige Product-Konfiguration führt keinen Update-Netzaufruf aus
- [x] Unit Tests und Windows-CI inklusive Updater-Publish und Bundle-Prüfung
- [x] GitHub Actions erzeugt validiertes LicenseHub-Release-ZIP plus SHA-256-Sidecar
- [~] lizenzgebundene Desktop-Update-Endpunkte im LicenseHub-Hauptprojekt prüfen und gegen reale Umgebung verifizieren
- [ ] echtes NMC-SCS-LAUNCHER-Release als vollständiges ZIP in LicenseHub provisionieren
- [ ] End-to-End-Test: LicenseHub-Release erkennen → signiert laden → SHA-256 prüfen → anwenden → neue Version startet → Rollback-Test

## Danach
- [ ] Installer
- [ ] visuelles Release-Finishing
- [ ] Praxistest auf echter ETS2-/ATS-Installation abschließen
- [ ] Release Candidate / 1.0.0

**Installer-Blocker:** Installer-Arbeit beginnt erst, wenn die beiden offenen LicenseHub-End-to-End-Gates (reale Lizenz sowie reales Update-Release) erfolgreich verifiziert und dokumentiert sind.
