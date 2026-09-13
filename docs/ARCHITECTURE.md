# Architektur – NMC SCS LAUNCHER

## Ziel

Der Launcher trennt UI, Fachlogik und Windows-/Dateisystemintegration. Kritische SCS- und Steam-Annahmen werden nicht in der UI hartcodiert.

## Komponenten

### `NmcScsLauncher.Core`
Enthält Domänenmodelle, Game-Typen und Abstraktionen. Der Core kennt weder WPF noch Steam- oder Windows-spezifische Implementierungsdetails.

### `NmcScsLauncher.Infrastructure`
Implementiert lokale JSON-Persistenz, Dateisystempfade, Logging und später Steam-/SCS-Erkennung sowie Prozessstart.

### `NmcScsLauncher.App`
WPF-Oberfläche mit MVVM. Code-behind bleibt auf reine View-Aufgaben beschränkt; Fachlogik gehört in Core/Services.

### Tests
Core- und Infrastructure-Tests sind getrennt. Dateisystemtests verwenden ausschließlich temporäre Verzeichnisse.

## Persistenz

Launcher-Einstellungen liegen unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher`. JSON ist für die frühen Versionen ausreichend. Eine Datenbank wird erst eingeführt, wenn ein konkreter Bedarf besteht.

## Spielstart

Die spätere Startlogik erhält eine eigene Abstraktion. `-homedir`, Executable-Pfade und Steam-Verhalten werden vor produktiver Implementierung gegen reale ETS2-/ATS-Installationen bzw. belastbare Quellen verifiziert.

## Sicherheitsregeln

- Savegames und SCS-Profildateien werden nicht ungefragt verändert.
- Importierte Verzeichnisse werden niemals automatisch rekursiv gelöscht.
- Steam Cloud und Workshop-Abonnements werden in frühen Versionen nur gelesen, nicht verändert.
- Pfade und Prozessargumente werden strukturiert statt über unsichere Stringverkettung verarbeitet.

## UI

Dark-Mode, linke Navigation und Modset-Karten bilden die Designrichtung. Fake-Fachdaten werden nicht als reale Daten angezeigt.
