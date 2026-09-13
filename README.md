# NMC SCS LAUNCHER

Moderner Windows-Desktop-Launcher für **Euro Truck Simulator 2 (ETS2)** und **American Truck Simulator (ATS)** mit getrennten Mod-/Profil-Umgebungen.

Aktuelle Version: **0.1.0-dev**

## Projektziel

NMC SCS LAUNCHER soll beliebig viele getrennte SCS-Modsets verwalten. Jedes Modset erhält später ein eigenes Home-Verzeichnis und wird mit dem passenden SCS-Startparameter `-homedir` gestartet. Dadurch entfällt das manuelle Umbenennen von `mod`, `mod_old`, `mod_2` usw.

Beispiele für spätere Modsets:

- Standard
- ProMods
- Map Combo
- Truck Mods
- Multiplayer
- Test / Experimental

## Aktueller Stand

Phase 0 bildet ausschließlich die belastbare Projektbasis:

- .NET 10 / WPF
- MVVM-Grundstruktur
- Core / Infrastructure / App getrennt
- Dependency Injection
- lokale JSON-Konfiguration
- lokales Logging
- Unit Tests
- GitHub Actions CI
- Dark-Mode-App-Shell

**Noch nicht implementiert:** Steam-/ETS2-/ATS-Erkennung, Modsets und echter Spielstart. Diese Punkte werden erst in den folgenden Phasen ergänzt und nicht mit Mockdaten als fertig dargestellt.

## Geplante Architektur

```text
src/
├─ NmcScsLauncher.App
├─ NmcScsLauncher.Core
└─ NmcScsLauncher.Infrastructure

tests/
├─ NmcScsLauncher.Core.Tests
└─ NmcScsLauncher.Infrastructure.Tests
```

Weitere Details: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

Roadmap: [`docs/ROADMAP.md`](docs/ROADMAP.md)

## Lokale Daten

Launcher-Daten werden nicht im Programmverzeichnis abgelegt, sondern unter:

```text
%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\
```

Dort entstehen später u. a. `settings.json`, `logs\` und `cache\`.

## Build

Voraussetzungen:

- Windows 10 oder Windows 11 x64
- .NET 10 SDK

```powershell
dotnet restore NMC-SCS-LAUNCHER.sln
dotnet build NMC-SCS-LAUNCHER.sln -c Release
dotnet test NMC-SCS-LAUNCHER.sln -c Release
```

GitHub Actions führt Restore, Build, Tests und einen Publish-Test auf `windows-latest` aus.

## SCS / Steam

Kritische Details wie Executable-Pfade, Steam-Libraries und das genaue `-homedir`-Verhalten werden vor der produktiven Implementierung technisch verifiziert. Der Launcher soll vorhandene Savegames, Profile, Steam Cloud und Workshop-Daten nicht ungefragt verändern.

## Datenschutz

Version 1 ist als lokale Offline-Anwendung geplant. Keine Telemetrie und kein Tracking.

## Hinweis zu Marken

NMC SCS LAUNCHER ist kein offizielles Produkt von SCS Software oder Valve und steht in keiner offiziellen Verbindung mit diesen Unternehmen.

## Lizenz

Für das öffentliche Repository wurde noch keine Open-Source-Lizenz festgelegt. Bis eine Lizenz ausdrücklich gewählt wird, gelten die gesetzlichen Urheberrechte; aus der öffentlichen Sichtbarkeit des Quellcodes folgt keine Nutzungserlaubnis.

Copyright © 2026 NMC Network / Onkel_Robby
