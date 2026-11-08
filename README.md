# NMC SCS LAUNCHER

Windows-Desktop-Launcher für **Euro Truck Simulator 2 (ETS2)** und **American Truck Simulator (ATS)** mit getrennten Mod-Ordnern und den normalen lokalen/Steam-Profilen von SCS.

Aktuelle Version: **1.0.0**

## Funktionen

- ETS2- und ATS-Installationen über Steam erkennen, inklusive zusätzlicher Steam-Libraries.
- Installationspfade bei Bedarf manuell setzen.
- Beliebig viele getrennte Modsets pro Spiel verwalten.
- Bei neuen Modsets ist der vom Benutzer ausgewählte Pfad **direkt der Mod-Ordner**; der Launcher hängt weder den Spielnamen noch `mod` an.
- Lokale und Steam-Profile bleiben in den normalen SCS-Dokumentenpfaden (`profiles` / `steam_profiles`).
- Für den Spielstart erzeugt der Launcher intern eine verwaltete Laufzeit-Home-Basis und bindet den direkten Mod-Ordner sowie die Standardprofile ein.
- Mods und lokale/Steam-Profile read-only inspizieren.
- Lokale Mods pro Modset als Liste mit Datei-/Ordnername, Typ, Größe, Änderungsdatum und Pfad anzeigen; Suche, Sortierung, Typ-Filter und Explorer-Aktion sind enthalten.
- Modsets duplizieren, ohne Junctions oder Hardlinks zu verwenden.
- ZIP-Backups erstellen und mit Sicherheitsprüfung wiederherstellen.
- Lokale Steam-Workshop-Verzeichnisse für ETS2/ATS erfassen und im eigenen Workshop-Bereich verwalten.
- Workshop-Einträge nach Spiel anzeigen, mit echten Steam-Workshop-Titeln anreichern, durchsuchen, nach Name/Änderungsdatum/ID sortieren und nach Titelverfügbarkeit filtern.
- Workshop-Einträge lokal im Explorer öffnen oder die zugehörige Steam-Workshop-Seite im Browser öffnen; Abonnements werden nicht verändert.
- LicenseHub-Lizenzierung mit Maschinenbindung und Fail-Closed-Verhalten.
- Release-Builds erhalten das LicenseHub-Product-Credential beim CI-Publish aus einem GitHub-Actions-Secret; Benutzer geben ausschließlich ihren Lizenzschlüssel ein.
- Der Benutzer-Lizenzschlüssel wird nach erfolgreicher Aktivierung im Windows Credential Manager gespeichert.
- Anwendungsupdates über den vorhandenen LicenseHub-Updatevertrag prüfen, herunterladen und per SHA-256 verifizieren.
- Externer self-contained Updater mit Rollback bei fehlgeschlagenem Austausch oder Neustart.
- Integrierter Save-Editor für ETS2 und ATS: Profil/Karriere, Geld/XP/Skills, eigene Trucks und Trailer, Reparatur, Kraftstoff, Kilometer, Frachtgewicht, Kennzeichen und vorhandene Motor-/Getriebe-Auswahl; Änderungen mit automatischem Backup und Validierung.
- Kennzeichen-Auswahl aus PR #86 mit 36 ETS2-Ländern und 21 ATS-Bundesstaaten sowie Custom-RGB und manuellen Map-Mod-Codes. Vorhandene Kennzeichen werden für aktiven Truck und Trailer getrennt geladen; unbekannte Codes bleiben erhalten. Die Definition muss im jeweiligen Spiel/DLC/Map-Mod vorhanden sein.
- Erweiterter Motor-/Getriebe-Lesekatalog: Spielarchive, vorhandene DLC-Dateien, lokale Profil-/Modset-Ordner und heruntergeladene Workshop-Pakete durchsuchen; Herkunft, Modell, aktuelle Komponente und Prüfstatus anzeigen. Zusätzliche Funde bleiben schreibgeschützt. [Umfang und Feldtest](docs/POWERTRAIN_CATALOG.md).
- Grundlage für die direkte Truck-Auswahl (D1): aktive Profil-Mods, passende Workshop-Paketversionen und wirksame Definitionen mit Prioritäten und Änderungsnachweisen auflösen. Ungeklärte DLC-Aktivierung oder Quellen geben keinen Einbau frei. [Umsetzung und Grenzen](docs/POWERTRAIN_INSTALLATION.md).
- Direkte Truck-Vorschau (D2): Motoren/Getriebe automatisch im Truck-Reiter suchen, aktuelle Teile und Herkunft anzeigen und das gewählte Paar gegen die gesamte Zubehörkonfiguration prüfen. Ein gemeinsamer Einbau (D3) prüft Save, Quellen und Paar erneut und schreibt beide Komponenten über die vorhandene Backup-/SHA-Pipeline. Unterstützte Wechsel erhalten die bekannten Schalt- und Bremssteuerungsbereiche; beide Spiele müssen beendet sein.
- Truck-Diagnose (Auftrag 5): globale Quellensperren von noch nicht begonnenen oder fehlgeschlagenen Komponentenprüfungen unterscheiden, konkrete Paket-/Definitionsgründe anzeigen und einen begrenzten Diagnosebericht lokal speichern. [Unterstützte Quellen und verbleibende Grenzen](docs/POWERTRAIN_DIAGNOSTICS.md).
- Steam-Cloud-Saves direkt unter `Steam/userdata` bleiben schreibgeschützt. [Kennzeichen-Testanleitung für ETS2/ATS](docs/SAVE_EDITOR_PLATES_TEST.md).

## Systemanforderungen

- Windows 10 oder Windows 11 x64
- ETS2 und/oder ATS optional für die Launcher-Nutzung
- Internetverbindung für die erforderliche LicenseHub-Lizenzprüfung

Der veröffentlichte Windows-Build ist **self-contained**. Auf dem Zielsystem muss deshalb kein separates .NET Runtime-Paket installiert werden.

## Windows-Installer

Zusätzlich zum portablen ZIP erzeugt CI einen klassischen Per-User-Installer:

```text
NMC-SCS-LAUNCHER-1.0.0-Setup.exe
```

Standardziel:

```text
%LOCALAPPDATA%\Programs\NMC SCS LAUNCHER\
```

Der Installer benötigt standardmäßig keine Administratorrechte, legt eine Startmenü-Verknüpfung an und kann optional eine Desktop-Verknüpfung erstellen. Die Per-User-Installation ist bewusst gewählt, damit der vorhandene LicenseHub-Updater Programmdateien ohne zusätzliche UAC-Elevation aktualisieren kann. Launcher-Daten und LicenseHub-Credentials werden bei der Deinstallation nicht automatisch gelöscht.

Weitere Details: [`docs/INSTALLER.md`](docs/INSTALLER.md)

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

Der Product API Key wird **nicht** im öffentlichen Repository hinterlegt. Für veröffentlichte Builds wird er beim GitHub-Actions-Publish aus dem Repository-Secret `NMC_LICENSEHUB_PRODUCT_API_KEY` in das Release eingebettet. Der Aktivierungsdialog fragt deshalb **nur nach dem Benutzer-Lizenzschlüssel**.

Der Benutzer-Lizenzschlüssel wird nach erfolgreicher Aktivierung im Windows Credential Manager gespeichert. Eine aktive Lizenz wird beim Start und zusätzlich unmittelbar vor einem Spielstart erneut gegen LicenseHub geprüft. Unerreichbarkeit, Timeout oder ungültige Serverantworten führen bei der produktiven Lizenzpflicht nicht zu einem gültigen Nutzungsstatus.

Für lokale Entwicklungs-/Migrationsszenarien bleibt die bisherige Umgebungsvariable `NMC_LICENSEHUB_PRODUCT_API_KEY` sowie ein bereits vorhandenes Product-Credential im Windows Credential Manager als Fallback unterstützt. Release-Builds verwenden jedoch vorrangig das beim Build eingebettete Credential.

Weitere Details: [`docs/LICENSEHUB_INTEGRATION.md`](docs/LICENSEHUB_INTEGRATION.md)

## Lokale Daten

Launcher-Daten liegen unter:

```text
%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\
```

Dazu gehören insbesondere Einstellungen, Logs, Modset-Metadaten, interne Runtime-Homes, Update-Staging und weitere lokale Launcher-Daten. Der vom Benutzer gewählte Mod-Ordner bleibt an seinem ursprünglichen Ort. Vorhandene SCS-Spielstände, `.scs`-Pakete und SII-Dateien werden nicht automatisch verschoben oder verändert.

## Sicherheitsgrenzen

- Spielarchive und Mods werden ausschließlich gelesen. Änderungen an `profile.sii` und `game.sii` erfolgen nur nach ausdrücklicher Benutzeraktion über die abgesicherte Save-Editor-Pipeline.
- Steam Cloud wird nicht stillschweigend manipuliert.
- Workshop-Inhalte werden nur lokal gelesen; Abonnements werden nicht automatisch geändert.
- Backup/Restore folgt keinen Junctions oder symbolischen Links.
- Restore blockiert ZIP Path Traversal und absolute Archivpfade.
- Updatepakete werden beim Download und unmittelbar vor dem Anwenden erneut per SHA-256 geprüft.
- Der produktive Launcher bleibt bei Fehlern im LicenseHub-Start-Gate geschlossen.
- Ein in eine Desktop-Anwendung eingebettetes Product-Credential ist trotz fehlender Klartextablage im Repository grundsätzlich aus dem ausgelieferten Binary extrahierbar; es muss deshalb serverseitig auf die minimal benötigten LicenseHub-Produktaktionen beschränkt bleiben.

## Build

Voraussetzungen für Entwickler:

- Windows x64
- .NET 10 SDK

```powershell
dotnet restore NMC-SCS-LAUNCHER.sln
dotnet build NMC-SCS-LAUNCHER.sln -c Release
dotnet test NMC-SCS-LAUNCHER.sln -c Release
```

GitHub Actions führt Restore, Build, Tests, self-contained Publish, Updater-Publish, Versionsprüfung, die Erstellung des versionierten Release-ZIP sowie den Build und einen Install/Uninstall-Smoke-Test des Windows-Installers aus. ZIP und Installer erhalten jeweils ein SHA-256-Sidecar.

## Architektur

```text
src/
├─ NmcScsLauncher.App
├─ NmcScsLauncher.Core
├─ NmcScsLauncher.Infrastructure
└─ NmcScsLauncher.Updater

tests/
├─ NmcScsLauncher.Core.Tests
├─ NmcScsLauncher.Infrastructure.Tests
└─ NmcScsLauncher.App.Tests
```

Weitere Details: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

Roadmap und Verifikationsstatus: [`docs/ROADMAP.md`](docs/ROADMAP.md)

## Datenschutz

Keine Telemetrie und kein Tracking. Netzwerkzugriffe dienen der LicenseHub-Lizenzprüfung, dem LicenseHub-basierten Updatecheck und – nur beim manuellen Aktualisieren der Workshop-Ansicht – der read-only Abfrage öffentlicher Steam-Workshop-Metadaten für die bereits lokal gefundenen PublishedFileIds.

## Hinweis zu Marken

NMC SCS LAUNCHER ist kein offizielles Produkt von SCS Software oder Valve und steht in keiner offiziellen Verbindung mit diesen Unternehmen.

## Lizenz

Für das öffentliche Repository wurde keine Open-Source-Lizenz erteilt. Es gelten die gesetzlichen Urheberrechte; die öffentliche Sichtbarkeit des Quellcodes stellt keine Nutzungserlaubnis dar.

Copyright © 2026 NMC Network / Onkel_Robby
