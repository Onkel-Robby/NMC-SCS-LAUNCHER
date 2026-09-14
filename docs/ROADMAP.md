# Roadmap

Status: `[ ]` geplant · `[~]` externe Verifikation offen · `[x]` implementiert und automatisiert geprüft

## Version 1.0.0
- [x] Projektbasis, WPF-Shell, DI, Persistenz und Logging
- [x] Steam-/ETS2-/ATS-Erkennung inklusive zusätzlicher Steam-Libraries
- [x] manuelle Spielpfade
- [x] getrennte Modsets pro Spiel
- [x] korrekte SCS-`-homedir`-Startlogik
- [x] Startprüfung und Schutz vor zweitem benutzerdefiniertem `-homedir`
- [x] read-only Mod-/Profilinspektion
- [x] Modset-Duplizierung ohne Junctions/Hardlinks
- [x] Backup/Restore mit NMC-Manifest und ZIP-Pfadschutz
- [x] read-only Steam-Workshop-Grundlage
- [x] Dashboard, ETS2-/ATS-Seiten, Modsets, Einstellungen und Info
- [x] LicenseHub-Aktivierung, Validierung und Deaktivierung über den vorhandenen Serververtrag
- [x] produktgebundene Maschinen-ID
- [x] Lizenzschlüssel im Windows Credential Manager
- [x] Product API Credential getrennt im Windows Credential Manager
- [x] Wiederverwendung des einmal provisionierten Product Credentials bei direktem EXE-Start
- [x] Fail-Closed-Lizenzprüfung beim Programmstart
- [x] zweite Lizenzprüfung unmittelbar vor dem Spielstart
- [x] Fail-Closed auch bei unerwarteten Fehlern im Startup-Gate
- [x] Updateprüfung über den vorhandenen LicenseHub-Updateweg
- [x] SHA-256-Prüfung beim Download und erneut vor dem Anwenden
- [x] separater self-contained Updater
- [x] Rollback bei fehlgeschlagenem Update/Neustart
- [x] Version `1.0.0`
- [x] self-contained Windows-x64-Publish
- [x] versioniertes Release-ZIP und SHA-256-Sidecar
- [x] Windows-CI: Restore, Build, Tests, Publish, Release-Identitätsprüfung und Artefakte

## Externe Feldverifikation
Diese Punkte benötigen eine reale Zielumgebung und werden nicht als durch CI ausgeführt dargestellt:

- [~] realer ETS2-/ATS-Praxistest auf einem Windows-PC mit installierter Steam-Version
- [~] Live-Lizenztest gegen produktive LicenseHub-Credentials
- [~] Live-Test für gesperrte/abgelaufene Lizenz und Deaktivierung
- [~] reales LicenseHub-Release inklusive Update, Neustart und Rollback

## Nach 1.0.0
- [ ] optionaler klassischer Windows-Installer
- [ ] optionale SteamUGC-/Workshop-Verwaltungsfunktionen
- [ ] weitere UI- und Komfortverbesserungen nach Praxiserfahrung

**Release-Stand:** Version 1.0.0 ist als self-contained portable Windows-x64-Build umgesetzt. Externe Feldtests bleiben separat dokumentiert und ändern nichts am automatisiert geprüften Code-/Build-Stand.
