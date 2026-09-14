# NMC SCS LAUNCHER

Windows-Desktop-Launcher für **Euro Truck Simulator 2 (ETS2)** und **American Truck Simulator (ATS)** mit getrennten Mod-/Profil-Umgebungen auf Basis des SCS-Parameters `-homedir`.

Aktuelle Version: **1.0.0**

## Funktionen

- ETS2- und ATS-Installationen über Steam erkennen, inklusive zusätzlicher Steam-Libraries.
- Installationspfade bei Bedarf manuell setzen.
- Beliebig viele getrennte Modsets pro Spiel verwalten.
- Jedes Modset verwendet eine eigene SCS-Home-Basis und wird mit genau einem launcherverwalteten `-homedir` gestartet.
- Mods und lokale/Steam-Profile read-only inspizieren.
- Modsets duplizieren, ohne Junctions oder Hardlinks zu verwenden.
- ZIP-Backups erstellen und mit Sicherheitsprüfung wiederherstellen.
- Lokale Steam-Workshop-Verzeichnisse read-only erfassen, ohne Abonnements zu verändern.
- LicenseHub-Lizenzierung mit Maschinenbindung und Fail-Closed-Verhalten.
- Lizenzschlüssel und Product API Credential getrennt im Windows Credential Manager speichern.
- Anwendungsupdates über den vorhandenen LicenseHub-Updatevertrag prüfen, herunterladen und per SHA-256 verifizieren.
- Externer self-contained Updater mit Rollback bei fehlgeschlagenem Austausch oder Neustart.

## Systemanforderungen

- Windows 10 oder Windows 11 x64
- ETS2 und/oder ATS optional für die Launcher-Nutzung
- Internetverbindung für die erforderliche LicenseHub-Lizenzprüfung

Der veröffentlichte Windows-Build ist **self-contained**. Auf dem Zielsystem muss deshalb kein separates .NET Runtime-Paket installiert werden.

## Start / portable Bereitstellung

Das CI erzeugt ein vollständiges Windows-x64-Publish-Artefakt sowie ein versioniertes ZIP-Paket:

```text
NMC-SCS-LAUNCHER-1.0.0-win-x64.zip
```

Das ZIP wird in einen eigenen Ordner entpackt. Startdatei:

```text
NmcScsLauncher.App.exe
```

`NmcScsLauncher.Updater.exe` muss im gleichen Programmordner verbleiben.

## LicenseHub

Produktionsbasis:

```text
https://licensehub.nmc-it-service.cloud
```

Product Slug:

```text
NMC-SCS-LAUNCHER
```

Der Product API Key wird **nicht** im Repository hinterlegt. Bei der verwalteten Erstbereitstellung kann er einmalig über die Prozessumgebung gesetzt werden:

```powershell
$env:NMC_LICENSEHUB_PRODUCT_API_KEY="<PRODUCT-API-KEY>"
.\NmcScsLauncher.App.exe
```

Der Launcher übernimmt das Credential anschließend in den Windows Credential Manager. Spätere direkte Starts der EXE verwenden das lokal gespeicherte Credential; die Umgebungsvariable ist dann nicht mehr erforderlich.

Der Benutzer-Lizenzschlüssel wird separat im Windows Credential Manager gespeichert. Eine aktive Lizenz wird beim Start und zusätzlich unmittelbar vor einem Spielstart erneut gegen LicenseHub geprüft. Unerreichbarkeit, Timeout oder ungültige Serverantworten führen bei der produktiven Lizenzpflicht nicht zu einem gültigen Nutzungsstatus.

Weitere Details: [`docs/LICENSEHUB_INTEGRATION.md`](docs/LICENSEHUB_INTEGRATION.md)

## Lokale Daten

Launcher-Daten liegen unter:

```text
%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\
```

Dazu gehören insbesondere Einstellungen, Logs, Modset-Metadaten, Update-Staging und weitere lokale Launcher-Daten. Vorhandene SCS-Spielstände, `.scs`-Pakete und SII-Dateien werden nicht automatisch verändert.

## Sicherheitsgrenzen

- Keine direkte Bearbeitung von `.scs`, `profile.sii`, `game.sii` oder Savegames.
- Steam Cloud wird nicht stillschweigend manipuliert.
- Workshop-Inhalte werden nur lokal gelesen; Abonnements werden nicht automatisch geändert.
- Backup/Restore folgt keinen Junctions oder symbolischen Links.
- Restore blockiert ZIP Path Traversal und absolute Archivpfade.
- Updatepakete werden beim Download und unmittelbar vor dem Anwenden erneut per SHA-256 geprüft.
- Der produktive Launcher bleibt bei Fehlern im LicenseHub-Start-Gate geschlossen.

## Build

Voraussetzungen für Entwickler:

- Windows x64
- .NET 10 SDK

```powershell
dotnet restore NMC-SCS-LAUNCHER.sln
dotnet build NMC-SCS-LAUNCHER.sln -c Release
dotnet test NMC-SCS-LAUNCHER.sln -c Release
```

GitHub Actions führt Restore, Build, Tests, self-contained Publish, Updater-Publish, Versionsprüfung sowie die Erstellung des versionierten Release-ZIP mit SHA-256-Sidecar aus.

## Architektur

```text
src/
├─ NmcScsLauncher.App
├─ NmcScsLauncher.Core
├─ NmcScsLauncher.Infrastructure
└─ NmcScsLauncher.Updater

tests/
├─ NmcScsLauncher.Core.Tests
└─ NmcScsLauncher.Infrastructure.Tests
```

Weitere Details: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

Roadmap und Verifikationsstatus: [`docs/ROADMAP.md`](docs/ROADMAP.md)

## Datenschutz

Keine Telemetrie und kein Tracking. Netzwerkzugriffe dienen der LicenseHub-Lizenzprüfung und dem LicenseHub-basierten Updatecheck.

## Hinweis zu Marken

NMC SCS LAUNCHER ist kein offizielles Produkt von SCS Software oder Valve und steht in keiner offiziellen Verbindung mit diesen Unternehmen.

## Lizenz

Für das öffentliche Repository wurde keine Open-Source-Lizenz erteilt. Es gelten die gesetzlichen Urheberrechte; die öffentliche Sichtbarkeit des Quellcodes stellt keine Nutzungserlaubnis dar.

Copyright © 2026 NMC Network / Onkel_Robby
