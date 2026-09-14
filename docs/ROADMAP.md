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
- [x] LicenseHub bleibt serverseitig unverändert; Launcher nutzt vorhandenen Vertrag
- [x] Produktionsdomain: `https://licensehub.nmc-it-service.cloud`
- [x] Product Slug: `NMC-SCS-LAUNCHER`
- [x] vorhandene `/api/license/activate.php`, `/validate.php`, `/deactivate.php` angebunden
- [x] Release-Build erzwingt LicenseHub unabhängig von `NMC_LICENSEHUB_REQUIRED`
- [x] maschinengebundene Aktivierung mit produktgebundenem SHA-256-Identifier
- [x] Lizenzschlüssel im Windows Credential Manager
- [~] Product API Key getrennt im Windows Credential Manager provisionieren und bei direktem EXE-Start wiederverwenden
- [x] Aktivierungs-/Statusdialog
- [x] Start-/Nutzungs-Gate bei ungültiger, blockierter, abgelaufener oder nicht aktivierter Lizenz
- [x] zweites Lizenz-Gate unmittelbar vor der Prozesserzeugung
- [x] fail-closed bei Timeout, Unerreichbarkeit, Protokollfehler oder fehlender Required-Konfiguration
- [x] Unit Tests und Windows-CI für Client, Runtime und Start-Gates vorhanden
- [ ] E2E: Product API Key einmal provisionieren → EXE direkt starten ohne PowerShell → aktive Lizenz startet
- [ ] E2E: Lizenz sperren/ablaufen → direkter EXE-Start muss blockieren → reaktivieren → Start wieder möglich
- [ ] E2E: Deaktivierung prüfen

### LicenseHub – Updates
- [x] LicenseHub-Server bleibt unverändert
- [x] vorhandenen `/api/update/check.php` als Update-Metadatenquelle identifiziert
- [x] Lizenz/Maschine wird unmittelbar vor Update-Check über vorhandenes `/api/license/validate.php` bestätigt
- [~] Launcher vom verworfenen Desktop-Updatevertrag auf vorhandenen Update-Endpunkt umstellen
- [x] kein langlebiger separater Update-Bearer-Token im Launcher
- [x] SHA-256 während des Downloads verifizieren
- [x] SHA-256 im externen Updater unmittelbar vor dem Anwenden erneut verifizieren
- [x] Update als vollständiges ZIP-Publish-Bundle herunterladen und sicher entpacken
- [x] ZIP-Traversal/absolute Pfade außerhalb des Staging-Verzeichnisses blockieren
- [x] Anwendung über separaten self-contained Updater austauschen
- [x] Rollback-Backup und automatisches Rollback bei Apply-/Restart-Fehlern
- [x] Updatefenster mit Version, Changelog, Fortschritt und kontrolliertem Neustart
- [x] GitHub Actions erzeugt validiertes Release-ZIP plus SHA-256-Sidecar
- [ ] reales `NMC-SCS-LAUNCHER`-Release in LicenseHub provisionieren
- [ ] E2E: vorhandener LicenseHub-Updatecheck → Download → SHA-256 → Apply → Neustart → Rollback-Test

## Danach
- [ ] Installer
- [ ] visuelles Release-Finishing
- [ ] Praxistest auf echter ETS2-/ATS-Installation abschließen
- [ ] Release Candidate / 1.0.0

**Installer-Blocker:** Installer-Arbeit beginnt erst, wenn Lizenz-Direktstart und reales Update-Release End-to-End erfolgreich verifiziert sind.
