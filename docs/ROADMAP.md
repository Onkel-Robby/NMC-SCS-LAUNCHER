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
Diese Punkte benötigen eine reale Zielumgebung und werden nicht als durch CI ausgeführt dargestellt. Die vollständige Abschlussreihenfolge steht in [`FINAL_FIELD_TEST.md`](FINAL_FIELD_TEST.md).

- [x] realer ETS2-Praxistest auf einem Windows-PC mit installierter Steam-Version: Spielstart über ein ausgewähltes Modset erfolgreich; es wurde ausschließlich der direkt gewählte Mod-Ordner eingebunden
- [x] realer ATS-Praxistest auf einem Windows-PC mit installierter Steam-Version: Spielstart über ein ausgewähltes Modset erfolgreich; es wurde ausschließlich der direkt gewählte Mod-Ordner eingebunden
- [x] Live-Lizenztest gegen produktive LicenseHub-Credentials: Aktivierung mit Benutzer-Lizenzschlüssel erfolgreich; gespeicherter Lizenzschlüssel wird nach Neustart automatisch aus dem Windows Credential Manager geladen und serverseitig validiert
- [x] Live-Test für gesperrte/abgelaufene Lizenz: Launcher bleibt fail-closed und zeigt erneut den Lizenz-Key-Dialog statt die Hauptoberfläche freizugeben; Wechseltest bestätigt: gesperrter gespeicherter Key kann durch einen anderen gültigen Key ersetzt werden, und wird anschließend auch dieser neue Key serverseitig blockiert, erscheint erneut der Lizenz-Key-Dialog
- [x] Live-Test der LicenseHub-Deaktivierung: Geräteaktivierung wurde serverseitig bestätigt, lokaler Lizenz-Key anschließend entfernt, Launcher automatisch beendet und beim nächsten Start korrekt wieder der Lizenz-Key-Dialog angezeigt
- [x] reales LicenseHub-Release 1.0.0 → 1.0.1: Update erkannt, Paket heruntergeladen, SHA-256 erfolgreich verifiziert, externer Updater angewendet und Launcher automatisch als Version 1.0.1 neu gestartet
- [x] realer Rollback-Test des externen Updaters: absichtlich ungültiges 1.0.3-Paket wurde nach erfolgreicher SHA-256-Prüfung angewendet, Neustart der ungültigen EXE scheiterte erwartungsgemäß und der vorherige funktionsfähige Stand 1.0.2 wurde wiederhergestellt
- [x] Reparaturinstallation des finalen 1.0.0-Stands auf Windows bestätigt: Launcher zeigt Version 1.0.0 und die vom Inno-Installer erzeugten `unins000.exe`/`unins000.dat` sind im Installationsverzeichnis wieder vorhanden

## Nach 1.0.0
- [x] optionaler klassischer Windows-Installer inklusive CI-Smoke-Test
- [x] eigener NMC Custom Installer auf WiX Toolset 5/Burn mit vollständig gebrandeter .NET-10-WPF-Oberfläche, per-user MSI, Installationspfad, optionaler Desktop-Verknüpfung, Repair/Uninstall, Inno-Migration und eigenem Install/Uninstall-CI-Smoke-Test
- [x] realer Windows-Feldtest des neuen NMC Custom Installers: Inno-Migration, Installation, Repair vor dem LicenseHub-Update, Desktop-Verknüpfung, LicenseHub-Update und Uninstall mit erhaltenen Benutzerdaten bestätigt
- [x] Windows-Feldtest der Installations-Erkennung nach PR #69: Der Installer aus `main`-Build #94 zeigt bei einer vorhandenen älteren Installation die Aktion „Aktualisieren“, und der Launcher ließ sich laut Nutzer anschließend starten. Die automatisierte CI prüft zusätzlich den Ersatz der Programmdateien bei gleicher öffentlicher Version 1.0.0; eine separate Dateihash-Prüfung auf dem Nutzerrechner ist nicht dokumentiert.
- [ ] Repair nach einem LicenseHub-Update testen, damit MSI-Dateibesitz und Updater-Dateibesitz keinen Versions-Rollback verursachen
- [x] lokale Steam-Workshop-Verwaltung: ETS2/ATS-Auswahl, Refresh, lokaler Ordner und Steam-Seite
- [x] öffentliche Steam-Workshop-Metadaten: Mod-Titel, Suche, Sortierung und Titel-Filter mit ID-Fallback
- [x] lokale Mod-Verwaltung pro Modset: Mod-Liste mit Datei/Ordner, Typ, Größe, Änderungsdatum, Pfad, Suche, Sortierung, Typ-Filter und Explorer-Aktion
- [x] Save-Editor-Grundlage: nur game.sii/profile.sii, textuelles SiiNunit, automatisches Backup, SHA-256 und atomarer Austausch/Restore; manuelle Saves und Quicksaves unterstützen abgesicherte Live-Writes, Autosaves/unklare Save-Typen und profile.sii bleiben bei laufendem ETS2/ATS gesperrt
- [x] Decoder-Adapter für binäre/verschlüsselte SCS-Saves: DecryptTruck 1.3.7 (MIT), gepinnte SHA-256, Runtime-Integritätsprüfung, temporäre Ausgabe und fail-closed Fallback
- [x] Save-Editor-Performance: unveränderte verschlüsselte `game.sii` wird pro Dateistand nur einmal extern decodiert und anschließend über einen automatisch invalidierenden In-Memory-Cache wiederverwendet
- [~] realer Feldtest des Decoders: aktueller verschlüsselter ETS2-Save erfolgreich gelesen, geändert und anschließend wieder im Spiel geladen; ATS-Save noch offen
- [x] Profil-/Save-Erkennung für `profiles` und `steam_profiles` mit validierten Save-Referenzen
- [x] erste Profil-Editor-Funktionen für Klartext-Saves: Profilname, Geld und XP
- [x] Profil-Editor: Karriere-Skills ADR, Fernfahrten, hochwertige Fracht, zerbrechliche Fracht, Eilaufträge und Eco-Driving mit validierten Wertebereichen
- [ ] Profil-Editor: weitere Karrierewerte erst nach konkretem Bedarf und realer Save-Verifikation
- [x] Truck-Editor-Grundlage: aktiven Truck eindeutig auflösen, reparieren und Kraftstoffwert 0..1 setzen
- [x] Fahrzeug-Inventar: eigene Trucks/Trailer aus den validierten `trucks[n]`-/`trailers[n]`-Referenzen lesen und aktiven Zustand markieren
- [x] Fahrzeugnamen in der Save-Editor-UI: read-only Modellbezeichnung aus bereits gespeicherten Zubehör-/Definition-Pfaden, mit neutralem Fallback statt `_nameless`-Referenzen; ETS2-Feldtest bestätigt Anzeige von `Scania S 2016` und `Kassbohrer Scx`.
- [x] Truck-Editor: sicheren Wechsel auf einen vorhandenen eigenen Truck inklusive player_vehicles-, Garage-/Driver- und HQ-Konsistenzprüfung
- [x] Truck-Editor: Kilometerstand des aktiven Trucks konsistent über `odometer`, vorhandene Fahrzeug-Kilometerfelder und den slotgleichen `truck_profit_logs[n]`-Eintrag aktualisieren; Truck-Manager-Historien aus `stats_data[n] -> profit_log_entry.distance` werden als Offset berücksichtigt und nicht verändert; `*_float_part`-Felder bleiben unangetastet
- [x] Truck-Editor: Kennzeichen des aktiven Trucks mit validiertem SCS-Format ändern
- [x] Truck-Editor-Backend: aktive Engine-/Transmission-Accessory eindeutig über `accessories[n]` und `data_path` auflösen und validierten Definition-Pfad ändern
- [x] sicherer Powertrain-Auswahlkatalog: Motoren/Getriebe aus bereits vorhandenen eigenen Trucks desselben Modells im aktuellen Save ableiten und als UI-Auswahl anbieten
- [ ] **Später:** erweiterter Motor-/Getriebe-Katalog aus installierten ETS2-/ATS-Game- und Mod-Archiven; erst nach verifiziertem SCS-Archivparser, Kompatibilitätsprüfung und Freigabe. Die bisherige sichere Auswahl aus bereits im Save vorhandenen Komponenten bleibt unverändert.
- [~] realer ETS2-/ATS-Feldtest für Engine-/Transmission-`data_path` auf aktuellen Saves
- [x] Trailer-Editor-Grundlage: aktiven Trailer samt Slave-Kette eindeutig auflösen und reparieren
- [x] Trailer-Editor: sicheren Wechsel auf einen vorhandenen eigenen Trailer mit Konsistenzprüfung der player_vehicles-Referenzen
- [x] Trailer-Editor: `cargo_mass` der aktiven Trailer-/Slave-Kette sicher ändern
- [x] Trailer-Editor: Kennzeichen der aktiven Trailer-/Slave-Kette mit validiertem SCS-Format ändern
- [~] realer Feldtest der Kennzeichen-Formatierung: Truck-/Trailer-Kennzeichentext sowie native deutsche Länderoptik in ETS2 bestätigt; ATS noch offen
- [x] Save-Editor-UI im Launcher mit ETS2/ATS-Profil-/Save-Auswahl, Änderungsbestätigung, Profile/Karriere, Truck/Trailer, Kennzeichen, Kilometer, cargo_mass, Engine/Transmission und Wiederherstellung des letzten Sitzungs-Backups
- [~] **Save-Editor-Arbeiten pausiert**, bis ein externer ATS-Tester konkrete Ergebnisse liefert. Keine fachlichen Änderungen am Save-Editor während des UI-Designpakets.
- [~] **ETS2-/ATS-Funktionsparität verbindlich:** dieselbe Save-Editor-UI und dieselben Funktionsangebote in beiden Spielen (Profil/Karriere, Geld, XP, Skills, Truck/Trailer, Reparatur, Kraftstoff, Kilometer, Kennzeichen, Frachtgewicht, Backup/Restore und aktuelle Engine-/Transmission-Grundfunktionen). Gemeinsame Implementierung vorhanden; ATS-Saveformate und tatsächliche Ergebnisse für jede Funktion müssen gesondert im Praxistest verifiziert werden. Keine ATS-Funktion absichtlich weglassen; offene ATS-Abweichungen vor Abschluss beheben.
- [~] reale Save-Editor-Feldtests: ETS2 Read-only, Profil/Karriere und Backup/Restore bestätigt; Kraftstoff, Truck-Reparatur, Kennzeichentext, **Truck-Wechsel und Trailer-Wechsel** sowie der synchronisierte Kilometerstand für Tacho + Truck-Manager vom Nutzer als erfolgreich bestätigt. Profil-/Save-Laden spürbar schneller und Save-Änderungen im ETS2-Feldtest nach PR #66 nach Nutzerangabe ca. 80 % schneller; bestätigte neue Dialogtexte. Lesbare Truck-/Trailer-Modellnamen nach PR #67 im ETS2-Screenshot bestätigt. Die vollständige End-to-End-Verifikation der Live-Write-Policy (Änderung bei laufendem Spiel und danach im Spiel neu laden) sowie weitere Truck-/Trailer-Funktionen und ATS-Tests bleiben offen.
- [ ] SteamUGC-Schreibaktionen wie Subscribe/Unsubscribe erst mit verifiziertem Steam-Auth-/API-Vertrag
- [~] moderne WPF-Benutzeroberfläche: konsistentes dunkles Designsystem (Farben, Typografie, Buttons, Eingabefelder), vektorbasierte Sidebar-Navigation, aktiver Navigationsindikator und Dashboard-Feinschliff; visuelle Abnahme auf realem Windows-PC steht aus
- [ ] weitere UI- und Komfortverbesserungen nach Screenshots und Praxiserfahrung

**Release-Stand:** Der finale Produktstand wird wieder als Version 1.0.0 geführt. Die temporären Versionen 1.0.1 bis 1.0.3 dienten ausschließlich den realen LicenseHub-Update-, Deaktivierungs- und Rollback-Feldtests; deren erfolgreich verifizierte Funktionen sind Bestandteil des finalen 1.0.0-Stands. Externe Feldtests bleiben separat dokumentiert und ändern nichts am automatisiert geprüften Code-/Build-Stand.
