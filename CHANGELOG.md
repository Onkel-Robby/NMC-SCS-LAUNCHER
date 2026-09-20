# Changelog

Alle wesentlichen Änderungen am NMC SCS LAUNCHER werden hier dokumentiert.

## 1.0.0

### Release
- Erste stabile 1.0.0-Version des NMC SCS LAUNCHER für Windows 10/11 x64.
- Self-contained Windows-Publish; auf Zielsystemen ist kein separates .NET Runtime-Paket erforderlich.
- Versionierte Windows- und LicenseHub-Release-Artefakte inklusive SHA-256-Sidecar werden durch CI erzeugt.
- README, Release-Dokumentation und Roadmap auf den tatsächlichen Funktionsumfang aktualisiert.

### Added
- Read-only Abruf öffentlicher Steam-Workshop-Metadaten für lokal gefundene PublishedFileIds; echte Mod-Titel und Steam-Änderungszeit werden ohne Steam-Login oder API-Key ergänzt.
- Workshop-Suche über Titel, ID und lokalen Pfad sowie Sortierung nach Name, Änderungsdatum oder PublishedFileId und Filter `Alle` / `Mit Titel` / `Nur ID`.
- Robuster ID-Fallback, wenn Steam-Metadaten nicht erreichbar oder für einen Workshop-Eintrag nicht verfügbar sind.
- Eigene Steam-Workshop-Seite für lokal installierte ETS2-/ATS-Workshop-Inhalte mit Spielauswahl, Aktualisierung, Explorer-Aktion und Link zur zugehörigen Steam-Workshop-Seite.
- Sichere kanonische Workshop-Link-Erzeugung mit Tests; keine Subscribe/Unsubscribe-Schreibaktionen ohne verifizierten Steam-Auth-/API-Vertrag.
- Klassischer Per-User-Windows-Installer unter `%LOCALAPPDATA%\\Programs\\NMC SCS LAUNCHER` mit Startmenü-Verknüpfung und optionalem Desktop-Shortcut.
- CI-Erstellung des Setup-EXE inklusive SHA-256-Sidecar und automatisiertem Silent-Install/Uninstall-Smoke-Test.
- Persistenter Product-API-Key-Store im Windows Credential Manager für verwaltete Erstprovisionierung und spätere direkte EXE-Starts.
- Automatischer Test, dass ein einmal provisioniertes Product Credential bei einem späteren direkten Start ohne Umgebungsvariable wiederverwendet wird.
- Release-Identitätsprüfung in CI: ProductVersion, Launcher/Updater und self-contained Runtime-Dateien werden vor Artefakt-Upload validiert.

### Changed
- Neue und bearbeitete Modsets verwenden den vom Benutzer ausgewählten Pfad direkt als Mod-Ordner; es wird weder `Euro Truck Simulator 2`/`American Truck Simulator` noch `mod` an diesen Pfad angehängt.
- Lokale und Steam-Profile bleiben in den normalen SCS-Dokumentenpfaden und werden bei direkten Modsets nicht pro Modset dupliziert.
- Der SCS-`-homedir` bleibt intern launcherverwaltet; der direkte Mod-Ordner und die Standardprofilordner werden zur Laufzeit sicher eingebunden.
- Backup und Duplizierung direkter Modsets arbeiten auf dem exakten Mod-Ordner und erzeugen keine zusätzliche SCS-Verzeichnisverschachtelung.
- Produktive Builds erzwingen LicenseHub unabhängig von `NMC_LICENSEHUB_REQUIRED`.
- Updateprüfung verwendet ausschließlich den bereits vorhandenen LicenseHub-Vertrag; am LicenseHub-Server ist keine NMC-SCS-LAUNCHER-spezifische Erweiterung erforderlich.
- Veröffentlichte Artefaktnamen enthalten die echte Projektversion.

### Fixed
- Workshop-Erkennung verwendet zusätzlich den tatsächlich erkannten bzw. gespeicherten ETS2-/ATS-Installationspfad, um die zugehörige Steam-Library und `steamapps\\workshop\\content\\<AppId>` zu finden.

### Security
- Startup-Gate ist jetzt auch bei unerwarteten Ausnahmen fail-closed: Ein Fehler bei Initialisierung, Credential-Zugriff oder Aktivierungsdialog kann nicht mehr dazu führen, dass das Hauptfenster trotzdem geöffnet wird.
- Lizenzstatus wird weiterhin beim Programmstart und unmittelbar vor dem Spielstart serverseitig bestätigt.
- Product API Key und Benutzer-Lizenzschlüssel werden nicht im Repository oder in `settings.json` gespeichert.
- Updatepakete werden beim Download und unmittelbar vor dem Anwenden erneut per SHA-256 geprüft.

### External verification status
- Die komplette Codebasis, Unit Tests, Windows-Builds und Publish-Artefakte werden automatisiert geprüft.
- Ein Live-End-to-End-Test gegen produktive LicenseHub-Credentials sowie ein realer ETS2-/ATS-Praxistest sind externe Feldtests und werden nicht als in CI durchgeführt dargestellt.

## 0.7.0-dev

### Added
- Read-only Steam-Workshop-Grundlage für ETS2 und ATS.
- Erkennung lokaler Workshop-Content-Roots über alle erkannten Steam-Libraries.
- Erfassung numerischer Workshop-/PublishedFileId-Verzeichnisse ohne Änderung von Steam-Abonnements.
- Tests für getrennte ETS2-/ATS-Workshop-Inhalte und fehlende Workshop-Verzeichnisse.
- LicenseHub-Desktop-Client für Aktivierung, Validierung und Deaktivierung von Lizenzen.
- Produktgebundene Maschinen-ID auf SHA-256-Basis, ohne den rohen Windows-MachineGuid an LicenseHub zu übertragen.
- Lokale Ablage des Benutzer-Lizenzschlüssels im Windows Credential Manager.
- LicenseHub-Aktivierungsdialog mit Status, Ablaufdatum und Aktivierungszahlen.
- Fail-closed Lizenz-Runtime für produktiv erzwungene Builds.
- Lizenzprüfung als Teil der Startprüfung und zusätzliche erneute Prüfung direkt vor der Prozesserzeugung.
- LicenseHub-Update-Client über den verifizierten Project-Update-Contract mit kurzlebigem signiertem Download-Endpunkt.
- SHA-256-Verifikation während des Update-Downloads.
- Separater self-contained `NmcScsLauncher.Updater.exe` für den Austausch der Anwendung nach Beenden des Launchers.
- Zweite SHA-256-Verifikation direkt im externen Updater vor dem Anwenden des Pakets.
- Sichere ZIP-Staging-/Verzeichnis-Tauschlogik mit Schutz gegen Path Traversal.
- Rollback-Backup der vorherigen Anwendung und automatische Wiederherstellung bei fehlgeschlagenem Austausch bzw. Neustart.
- LicenseHub-Updatefenster mit installierter/verfügbarer Version, Changelog, Fortschritt sowie „Installieren & neu starten“.
- Automatische Updateprüfung nach Programmstart, wenn die vollständige LicenseHub-Updatekonfiguration vorhanden ist.
- CI-Publish des self-contained Updaters und automatische Aufnahme in das Windows-Launcher-Artefakt.
- Tests für LicenseHub-Client, Runtime-Enforcement, sicheren Update-Tausch, ZIP-Traversal, Rollback und unkonfigurierte Update-Builds.
- Dokumentierter Contract für vollständige ZIP-Updatepakete unter `docs/UPDATE_PACKAGE.md`.

### Changed
- Installer ist jetzt ausdrücklich hinter LicenseHub-Lizenzschutz und LicenseHub-Updateintegration blockiert.
- Der geplante generische Update-Mechanismus wurde durch die LicenseHub-basierte Release-/Updateintegration ersetzt.
- Entwicklungsbuilds ohne LicenseHub-Update-Token führen keinen Update-Netzaufruf aus.
- Produktive Builds können über `NMC_LICENSEHUB_REQUIRED` fail-closed auf eine bestätigte LicenseHub-Lizenz geschaltet werden.

### Security
- Keine echten LicenseHub-Credentials werden im öffentlichen Repository gespeichert.
- Benutzer-Lizenzschlüssel werden nicht in `settings.json` abgelegt.
- LicenseHub-Ausfall, Timeout oder ungültige Antworten ergeben bei aktivierter Lizenzpflicht keinen gültigen Nutzungsstatus.
- Updatepakete werden zweimal gegen denselben LicenseHub-SHA-256-Digest geprüft: beim Download und direkt vor dem Anwenden.
- Der externe Updater läuft außerhalb des zu ersetzenden Anwendungsverzeichnisses und wartet auf das Ende des Launchers.
- Alte Programmdateien werden bei einem Update nicht direkt überschrieben, sondern als Rollback-Verzeichnis erhalten.

### Verification pending
- Reales NMC-SCS-LAUNCHER-Produkt/API-Credential in LicenseHub provisionieren und Lizenz-End-to-End-Test durchführen.
- Reales vollständiges ZIP-Release in LicenseHub provisionieren und Update/Neustart/Rollback Ende-zu-Ende testen.
- Realer Praxistest mit installierter Steam-Version von ETS2 und/oder ATS.

## 0.6.0-dev

### Added
- ZIP-Backups pro Modset mit `nmc-backup.json`-Manifest.
- Standardbackup für Konfiguration und lokale/Steam-Profile; Mods optional.
- Sichere Restore-Vorschau mit Quell-Modset, Spiel, Datum, Dateizahlen und vorhandenen Zieldateien.
- Restore-Validierung für NMC-Manifest, Spieltyp, ZIP-Einträge, Dateigrößen und sichere relative Pfade.
- Explizite Benutzerfreigabe vor dem Überschreiben vorhandener Dateien.
- Backup-/Restore-Dialoge und Fortschrittsanzeige in der Modset-Verwaltung.
- Tests für Backup-Inhalte, Manifest, Restore-Bestätigung und Spielisolation.

### Security
- Backup/Restore folgt keinen Links oder Junctions.
- Restore entfernt keine zusätzlichen Dateien aus dem Ziel-Modset.
- Unbestätigte Restore-Vorgänge überschreiben keine vorhandenen Dateien.

## 0.5.0-dev

### Added
- Vollständige Sidebar-Navigation mit separaten Bereichen für Übersicht, ETS2, ATS, Modsets, Einstellungen und Info.
- Dashboard mit Spielstatus, Modset-Zahlen und zuletzt verwendeten Modsets.
- Eigene ETS2-/ATS-Seiten mit Installationsstatus und spielbezogenen Modsets.
- Erweiterte Modset-Verwaltungsseite mit Start-, Bearbeitungs- und Inspektionsaktionen.
- Einstellungen für Standard-Modset-Pfad, Startprüfung, Modanzahl und Merken des letzten Modsets.
- Wiederherstellung des zuletzt ausgewählten Modsets nach Neustart, wenn aktiviert.
- Info-Seite mit Produkt-/Markenhinweisen.
- Konsistenteres Dark-Theme für Navigation, Buttons, Eingaben und Tabellen.
- Modset-Duplizierung mit frei wählbarer Kopie von Konfiguration, Mods, Profilen, Screenshots und optionalen Logs.
- Asynchrone Dateikopie mit Byte-/Dateifortschritt in der Modset-Ansicht.
- Eigener Duplizierungsdialog für Namen, Zielpfad und Kopierumfang.
- Pfadschutz gegen identische oder ineinander verschachtelte Quell-/Zielverzeichnisse.
- Reparse-Points wie Junctions und Links werden bei der Duplizierung nicht verfolgt.
- Tests für Pfadisolation und ausgewählte Kopierinhalte.

### Changed
- Kritische Startfehler blockieren den Start auch bei deaktiviertem Startprüfungsdialog.
- Modanzahl kann über die Launcher-Einstellungen ausgeblendet werden.
- Duplizierte Modsets verwenden unabhängige Dateikopien statt Junctions oder Hardlinks.

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
