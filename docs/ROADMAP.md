# Roadmap

Status: `[ ]` geplant · `[~]` externe Verifikation offen · `[x]` implementiert und automatisiert geprüft

## Version 1.0.0
- [x] Projektbasis, WPF-Shell, DI, Persistenz und Logging
- [x] Steam-/ETS2-/ATS-Erkennung inklusive zusätzlicher Steam-Libraries
- [x] manuelle Spielpfade
- [x] getrennte Modsets pro Spiel
- [x] direkter Mod-Ordner pro Modset ohne zusätzliche SCS-Pfadverschachtelung
- [x] Standardpfade für lokale und Steam-Profile bei direkten Modsets
- [x] interne Runtime-Home-Zuordnung für den SCS-`-homedir`-Start
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
- [x] Live-Lizenztest gegen produktive LicenseHub-Credentials: Aktivierung mit Benutzer-Lizenzschlüssel erfolgreich; gespeicherter Lizenzschlüssel wird nach Neustart automatisch aus dem Windows Credential Manager geladen und serverseitig validiert
- [x] Live-Test für gesperrte/abgelaufene Lizenz: Launcher bleibt fail-closed und zeigt erneut den Lizenz-Key-Dialog statt die Hauptoberfläche freizugeben; Wechseltest bestätigt: gesperrter gespeicherter Key kann durch einen anderen gültigen Key ersetzt werden, und wird anschließend auch dieser neue Key serverseitig blockiert, erscheint erneut der Lizenz-Key-Dialog
- [x] Live-Test der LicenseHub-Deaktivierung: Geräteaktivierung wurde serverseitig bestätigt, lokaler Lizenz-Key anschließend entfernt, Launcher automatisch beendet und beim nächsten Start korrekt wieder der Lizenz-Key-Dialog angezeigt
- [x] reales LicenseHub-Release 1.0.0 → 1.0.1: Update erkannt, Paket heruntergeladen, SHA-256 erfolgreich verifiziert, externer Updater angewendet und Launcher automatisch als Version 1.0.1 neu gestartet
- [x] realer Rollback-Test des externen Updaters: absichtlich ungültiges 1.0.3-Paket wurde nach erfolgreicher SHA-256-Prüfung angewendet, Neustart der ungültigen EXE scheiterte erwartungsgemäß und der vorherige funktionsfähige Stand 1.0.2 wurde wiederhergestellt
- [x] Reparaturinstallation des finalen 1.0.0-Stands auf Windows bestätigt: Launcher zeigt Version 1.0.0 und die vom Inno-Installer erzeugten `unins000.exe`/`unins000.dat` sind im Installationsverzeichnis wieder vorhanden

## Nach 1.0.0
- [x] optionaler klassischer Windows-Installer inklusive CI-Smoke-Test
- [x] lokale Steam-Workshop-Verwaltung: ETS2/ATS-Auswahl, Refresh, lokaler Ordner und Steam-Seite
- [x] öffentliche Steam-Workshop-Metadaten: Mod-Titel, Suche, Sortierung und Titel-Filter mit ID-Fallback
- [x] lokale Mod-Verwaltung pro Modset: Mod-Liste mit Datei/Ordner, Typ, Größe, Änderungsdatum, Pfad, Suche, Sortierung, Typ-Filter und Explorer-Aktion
- [ ] SteamUGC-Schreibaktionen wie Subscribe/Unsubscribe erst mit verifiziertem Steam-Auth-/API-Vertrag
- [ ] weitere UI- und Komfortverbesserungen nach Praxiserfahrung

**Release-Stand:** Der finale Produktstand wird wieder als Version 1.0.0 geführt. Die temporären Versionen 1.0.1 bis 1.0.3 dienten ausschließlich den realen LicenseHub-Update-, Deaktivierungs- und Rollback-Feldtests; deren erfolgreich verifizierte Funktionen sind Bestandteil des finalen 1.0.0-Stands. Externe Feldtests bleiben separat dokumentiert und ändern nichts am automatisiert geprüften Code-/Build-Stand.
