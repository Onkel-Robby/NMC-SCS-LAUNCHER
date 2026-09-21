# Finaler Feldtest – NMC SCS LAUNCHER

Diese Checkliste deckt ausschließlich Punkte ab, die nicht sinnvoll durch CI simuliert werden können. Sie ist für einen realen Windows-PC mit installierter Steam-Version von ETS2 und/oder ATS gedacht.

## Voraussetzungen

- ETS2/ATS vollständig beenden.
- Steam darf nicht gerade denselben Save synchronisieren.
- Einen nicht kritischen Test-Save verwenden.
- Vor dem ersten Test zusätzlich eine manuelle Kopie des kompletten Profils außerhalb des SCS-Dokumentenordners anlegen.
- Launcher über den aktuellen NMC Custom Installer installieren.
- LicenseHub-Lizenz muss gültig sein.

## A. Read-only Kompatibilitätsprüfung

1. Save-Editor öffnen.
2. ETS2 wählen und Profile laden.
3. Lokales oder Steam-Profil auswählen.
4. Einen aktuellen Save auswählen.
5. Prüfen, ob aktiver Truck, Trailer, Fahrzeuglisten, Skills sowie Motor/Getriebe ohne Fehlermeldung gelesen werden.
6. Dasselbe mit ATS wiederholen.

Erwartung:
- verschlüsselte/binäre Saves werden über den gepinnten DecryptTruck-Decoder gelesen;
- unbekannte oder mehrdeutige Strukturen werden fail-closed abgelehnt;
- bis zu diesem Punkt wird keine Save-Datei verändert.

## B. Einfacher Schreibtest

Je Spiel zunächst nur eine einzelne ungefährliche Änderung durchführen, zum Beispiel Geld oder XP.

1. Änderung bestätigen.
2. Pfad des automatisch erzeugten Backups notieren.
3. Launcher schließen.
4. Spiel starten und den bearbeiteten Save laden.
5. Prüfen, ob der Save ohne Reparaturdialog oder Ladefehler startet.
6. Geänderten Wert im Spiel kontrollieren.
7. Spiel wieder vollständig beenden.

Dieser Test bestätigt insbesondere, dass ein decodierter Save nach dem Schreiben in der vom Launcher erzeugten Form von der aktuellen Spielversion akzeptiert wird.

## C. Profil/Karriere

Nacheinander testen:

- Profilname
- Geld
- XP
- ADR 0–63
- Fernfahrten 0–6
- hochwertige Fracht 0–6
- zerbrechliche Fracht 0–6
- Eilaufträge 0–6
- Eco-Driving 0–6

Nach mehreren Änderungen Save einmal im Spiel laden und Werte plausibilisieren.

## D. Truck

Mit einem Testprofil mit mindestens zwei eigenen Trucks:

1. Truck reparieren.
2. Kraftstoff ändern.
3. Kilometerstand ändern.
4. Truck-Kennzeichen ändern.
5. Auf einen anderen eigenen Truck wechseln.
6. Prüfen, ob Garage, Fahrer-Slot und HQ danach im Spiel konsistent sind.
7. Motor aus dem angebotenen Save-Katalog wechseln.
8. Getriebe aus dem angebotenen Save-Katalog wechseln.
9. Save laden und prüfen, ob Truck ohne fehlende Teile oder fehlerhafte Konfiguration erscheint.

Wichtig:
Der Powertrain-Katalog zeigt absichtlich nur Definitionen, die bereits bei eigenen Trucks desselben Truck-Modells im ausgewählten Save vorkommen.

## E. Trailer

Mit einem Testprofil mit mindestens zwei eigenen Trailern:

1. Trailer reparieren.
2. cargo_mass ändern.
3. Trailer-Kennzeichen ändern.
4. Auf einen anderen eigenen Trailer wechseln.
5. Bei Mehrfach-/Slave-Trailern komplette Kette prüfen.
6. Save im Spiel laden und Trailerzustand kontrollieren.

## F. Backup/Restore

1. Eine sichtbare Änderung durchführen.
2. Im Save-Editor „Letztes Backup wiederherstellen“ verwenden.
3. Save erneut auswählen.
4. Spiel starten.
5. Prüfen, ob der vorherige Zustand wiederhergestellt wurde.

## G. Negativtests

Folgende Fälle müssen ohne Save-Schreibvorgang abgewiesen werden:

- ETS2 oder ATS läuft während einer Änderung.
- Kraftstoff außerhalb 0–1.
- negative Kilometer oder cargo_mass.
- Skill-Level außerhalb der erlaubten Bereiche.
- ungültige Kennzeichenzeichen/Farbwerte.
- unbekannter Truck-/Trailer-Verweis.
- mehrdeutige Fahrzeug-/Accessory-Struktur.

## H. Installer/Updater – noch offener Architekturtest

Separat vom Save-Editor:

1. NMC Custom Installer Version 1.0.0 installieren.
2. Über LicenseHub auf ein Test-Update aktualisieren.
3. Aktualisierte Version starten und Benutzerdaten prüfen.
4. Danach im NMC Custom Installer **Repair** ausführen.
5. Prüfen, ob die per LicenseHub aktualisierte Version erhalten bleibt oder auf MSI-Dateien zurückgesetzt wird.
6. LicenseHub-Daten, Modsets und Einstellungen anschließend erneut prüfen.

Wenn Repair die alte MSI-Version wiederherstellt, besteht weiterhin ein Dateibesitz-Konflikt zwischen MSI und externem Updater und dieser Ablauf darf nicht als vollständig freigegeben gelten.

## Abschlusskriterium

Der Save-Editor gilt für den Produktivbetrieb als feldverifiziert, wenn mindestens ein aktueller ETS2- und ein aktueller ATS-Save die Abschnitte A–G ohne Datenverlust oder Ladefehler besteht. Der Installer-/Updater-Ablauf gilt erst nach erfolgreichem Abschnitt H als vollständig verifiziert.
