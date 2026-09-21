# Architektur

## Ziel

NMC SCS LAUNCHER ist eine Windows-Desktop-Anwendung zur sicheren Verwaltung voneinander getrennter ETS2-/ATS-Modset-Umgebungen.

## Projekte

### NmcScsLauncher.Core
Enthält technologieunabhängige Modelle und Verträge. Aktuell: Spieltypen, Spieldefinitionen, Installationsmodelle, Einstellungen, Save-Editing-Verträge sowie Interfaces für Persistenz, Logging und Game Detection. Für textuelle SCS-SII-Dateien steht ein kleiner, strikt validierender Scalar-Editor zur Verfügung.

### NmcScsLauncher.Infrastructure
Implementiert Dateisystem- und Windows-nahe Funktionen. Aktuell: App-Pfade, JSON-Persistenz, Dateilogging, Steam-Library-Erkennung, Steam-basierte ETS2-/ATS-Installationserkennung sowie die sichere Save-Datei-Transaktion mit Backup, SHA-256 und atomarem Dateiaustausch.

### NmcScsLauncher.App
WPF/MVVM-Oberfläche. Die UI greift über Interfaces auf Fach- und Infrastrukturservices zu. Dateisystem- oder Erkennungslogik gehört nicht in Code-Behind.

## Game Detection

Steam wird best-effort über Benutzer-/Maschinen-Registry und den üblichen Steam-Pfad gesucht. Zusätzliche Bibliotheken werden aus `steamapps/libraryfolders.vdf` gelesen. Spielinstallationen werden anhand des passenden `appmanifest_<appid>.acf`, `installdir` und der vorhandenen x64-Executable validiert.

Gespeicherte Installationspfade werden beim Start zuerst validiert. Eine automatische Neuerkennung ignoriert den gespeicherten Pfad und durchsucht Steam erneut. Manuell ausgewählte Pfade werden nur gespeichert, wenn die erwartete x64-Executable existiert.

## Persistenz

Launcher-Einstellungen werden atomar als JSON unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\settings.json` gespeichert. Keine Datenbank ist für den aktuellen Umfang erforderlich.

## Save-Editing

Save-Editing ist ab der Nach-1.0.0-Entwicklung als explizite, abgesicherte Funktion vorgesehen. Der erste technische Stand bearbeitet ausschließlich `game.sii` und `profile.sii` und nur dann, wenn die Datei bereits als unterstütztes textuelles `SiiNunit` gelesen werden kann.

Vor jeder Änderung wird ein Backup unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\save-editor-backups` erzeugt. Die Quelldatei und das Ergebnis werden per SHA-256 erfasst. Geschrieben wird zunächst in eine temporäre Datei im selben Verzeichnis; erst nach erneuter Formatvalidierung wird die Zieldatei per Replace ausgetauscht. Läuft ETS2 oder ATS, wird der Schreibvorgang blockiert.

Binäre/verschlüsselte Saves werden über einen getrennten Decoder-Adapter gelesen. Verwendet wird die gepinnte Windows-Version von DecryptTruck 1.3.7 (MIT). Die Release-Binary wird ausschließlich im CI-/Release-Build heruntergeladen, gegen die fest hinterlegte SHA-256-Prüfsumme verifiziert und zusammen mit Lizenz/Notice unter `third-party/decrypt-truck` ausgeliefert. Vor jeder Ausführung prüft der Launcher dieselbe SHA-256-Prüfsumme erneut.

Der Decoder erhält die Originaldatei nur als Eingabe und schreibt ausschließlich in eine temporäre Ausgabedatei. Erst wenn diese Datei als textuelles `SiiNunit` validiert wurde, wird sie an die vorhandene NMC-Save-Pipeline weitergegeben. Backups, Spiel-läuft-Sperre, Pfadvalidierung und atomarer Austausch bleiben damit die alleinige Schreibgrenze. Decoderfehler, fehlende Binary, Hash-Abweichungen, Timeout oder ungültige Ausgabe führen fail-closed zum Abbruch.

Die Profil-/Save-Erkennung durchsucht innerhalb eines ausgewählten SCS-Home-Verzeichnisses ausschließlich `profiles` und `steam_profiles`, ignoriert Reparse-Point-Profil-/Save-Verzeichnisse und liefert nur Saves mit vorhandener `game.sii`. Profil-Verzeichnisnamen werden nur dann als UTF-8-Hex dekodiert, wenn die Dekodierung eindeutig gültig ist.

Die ersten Schreiboperationen sind bewusst klein: Profil-Anzeigename in `profile.sii` sowie `money_account` und `experience_points` in `game.sii`. Ein Scalar wird nur verändert, wenn er exakt einmal gefunden wird; fehlende oder mehrdeutige Felder führen fail-closed zum Abbruch.

Für Fahrzeugfunktionen wird der aktive Zustand über die Save-Referenzkette `assigned_vehicles -> player_vehicles -> vehicle/trailer` aufgelöst. Unit-IDs müssen eindeutig sein. Truck-Reparatur setzt ausschließlich die bekannten Wear-Felder des aktiven Trucks sowie dessen nummerierte Wheel-Wear-Felder auf 0. Der Kraftstoffwert `fuel_relative` wird nur im aktiven Truck verändert und ist auf den Bereich 0..1 begrenzt. Trailer-Reparatur folgt der aktiven `slave_trailer`-Kette bis maximal 20 Units, erkennt Zyklen und verändert ausschließlich bekannte Trailer-/Chassis-/Wheel-Wear-Felder. Andere Trucks oder Trailer im Save bleiben unverändert.

Die Fahrzeugauswahl liest die Besitz-Arrays `trucks[n]` und `trailers[n]` ausschließlich aus der eindeutigen `player`-Unit mit `assigned_vehicles`. Jede referenzierte Unit muss existieren, den erwarteten Unit-Typ besitzen und darf im jeweiligen Besitz-Array nur einmal vorkommen.

Ein Trailer-Wechsel ist zunächst nur zulässig, wenn bereits ein aktiver Trailer vorhanden ist und das Ziel im `trailers[n]`-Besitz-Array steht. Alle `player_vehicles`-Units, die auf den aktiven Truck zeigen, werden vor dem Schreiben auf konsistente Trailer-Referenzen geprüft. Unterschiedliche bestehende Trailer-Zuordnungen führen fail-closed zum Abbruch. Der Zieltrailer inklusive Slave-Kette wird vorab vollständig validiert; erst danach werden die Referenzen über die zentrale Backup-/Replace-Pipeline geändert.

Der Truck-Wechsel arbeitet ebenfalls nur mit einem Truck aus dem validierten `trucks[n]`-Besitz-Array. Vor dem Schreiben werden der aktuelle und der Ziel-Truck jeweils genau einem `garage`-Fahrzeug-Slot zugeordnet. Für beide Slots muss ein korrespondierender `drivers[n]`-Eintrag existieren. Anschließend werden alle `player_vehicles`-Units des bisher aktiven Trucks auf den Ziel-Truck umgestellt, die beiden Driver-Slots vertauscht und `hq_city` auf die Stadt der Zielgarage gesetzt. Die Garage-ID muss dazu eindeutig dem Schema `garage.<city>` folgen. Doppelte Garage-Referenzen, fehlende Driver-Slots oder nicht auflösbare HQ-Städte führen fail-closed zum Abbruch. Nach der Änderung werden aktive Truck-Referenz, Driver-Swap und HQ-Stadt erneut validiert, bevor die zentrale Backup-/Replace-Pipeline schreiben darf.

Der Kilometer-Editor ordnet den aktiven Truck zusätzlich über dessen Slot im `trucks[n]`-Array dem korrespondierenden `truck_profit_logs[n]`-Eintrag zu. `odometer` und `acc_distance_on_job` sind dabei Pflichtfelder; `integrity_odometer`, `trip_distance_km` und `acc_distance_free` werden nur geändert, wenn sie im Save vorhanden sind. Negative Kilometerstände werden abgelehnt. Für aktive Trailer wird `cargo_mass` ausschließlich innerhalb der bereits validierten aktiven Trailer-/Slave-Kette geändert; fehlen dort sämtliche `cargo_mass`-Felder, wird fail-closed abgebrochen.

Kennzeichen werden nur über vorhandene `license_plate`-Felder geändert. Der Launcher erzeugt daraus einen kontrollierten SCS-String mit Kennzeichentext, Ländercode sowie Hintergrund-/Textfarbe. Kennzeichentext ist auf 1..20 Zeichen und ASCII-Buchstaben, Ziffern, Leerzeichen sowie Bindestriche begrenzt; Farbangaben müssen exakt sechs Hex-Zeichen enthalten, der Ländercode nur Kleinbuchstaben, Ziffern und Unterstriche. Dadurch können keine SII-Zeilenumbrüche, Quotes oder Markup-Fragmente über Benutzereingaben eingeschleust werden. Beim Trailer werden nur `license_plate`-Felder innerhalb der aktiven Trailer-/Slave-Kette geändert; andere Trailer bleiben unverändert.

Motor und Getriebe werden über die `accessories[n]`-Referenzen des aktiven Truck-Units aufgelöst. Jede Accessory-ID muss eindeutig sein; ihre referenzierte Unit wird ausschließlich über ein vorhandenes, gequotetes `data_path` klassifiziert. Genau eine Accessory muss das Pfadsegment `/engine/` und genau eine das Segment `/transmission/` enthalten. Mehrdeutige oder fehlende Zuordnungen führen fail-closed zum Abbruch. Neue Definition-Pfade müssen unter `/def/vehicle/truck/` liegen, auf `.sii` enden, ausschließlich kontrollierte Pfadzeichen enthalten und exakt der erwarteten Kategorie Engine oder Transmission entsprechen. Der Backend-Kern prüft damit Struktur und Kategorie, jedoch noch nicht, ob die Zieldefinition in der konkret installierten ETS2-/ATS-Version oder in einem Mod tatsächlich existiert und mit dem Truck-Modell kompatibel ist; dafür folgt separat ein Definition-Katalog/Scanner.

## Sicherheitsregeln

- Savegame-Änderungen nur über den zentralen Save-Editing-Service.
- Kein Schreiben bei laufendem ETS2/ATS.
- Vor jeder Save-Änderung automatisches Backup.
- Unbekannte oder nicht validierte SII-Formate werden nicht verändert.
- Keine Steam-Cloud-Manipulation.
- Keine automatischen Löschvorgänge an Benutzerdateien.
- Externe Pfade werden validiert, bevor sie persistiert oder später für Starts verwendet werden.

## Tests

Core- und Infrastructure-Tests verwenden temporäre Verzeichnisse und künstliche Steam-Strukturen. Es werden keine realen Benutzerprofile oder echten Steam-Installationen verändert.
