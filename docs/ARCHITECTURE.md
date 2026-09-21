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

Binäre/verschlüsselte Saves werden im ersten Stand bewusst fail-closed abgelehnt. Ein Decoder-Adapter folgt separat und darf die bestehenden Backup-/Validierungsregeln nicht umgehen.

Die Profil-/Save-Erkennung durchsucht innerhalb eines ausgewählten SCS-Home-Verzeichnisses ausschließlich `profiles` und `steam_profiles`, ignoriert Reparse-Point-Profil-/Save-Verzeichnisse und liefert nur Saves mit vorhandener `game.sii`. Profil-Verzeichnisnamen werden nur dann als UTF-8-Hex dekodiert, wenn die Dekodierung eindeutig gültig ist.

Die ersten Schreiboperationen sind bewusst klein: Profil-Anzeigename in `profile.sii` sowie `money_account` und `experience_points` in `game.sii`. Ein Scalar wird nur verändert, wenn er exakt einmal gefunden wird; fehlende oder mehrdeutige Felder führen fail-closed zum Abbruch.

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
