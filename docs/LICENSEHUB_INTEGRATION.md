# LicenseHub-Integration – NMC SCS LAUNCHER

## Status

Diese Integration basiert auf dem aktuellen Quellstand des privaten Hauptprojekts `Onkel-Robby/Licensehub` und nicht auf erfundenen Endpoints.

Produktive LicenseHub-Basis-URL:

`https://licensehub.nmc-it-service.cloud`

Der Launcher verwendet diese URL als sicheren Default. Für isolierte Test-/Staging-Umgebungen kann sie weiterhin über `NMC_LICENSEHUB_BASE_URL` überschrieben werden.

Der Bot-spezifische `/api/v1`-M2M-Contract ist für den verteilten Windows-Client **nicht** der primäre Aktivierungsweg. Die Desktop-Lizenzierung verwendet die Client-Endpunkte im LicenseHub-Hauptprojekt.

## Lizenzierung

### Aktivierung

`POST /api/license/activate.php`

Request-Felder:

- `product_slug`
- `api_key`
- `license_key`
- `machine_id`
- `device_name`
- `app_version`

LicenseHub prüft Produkt, Lizenzstatus, Ablauf, Maschinenaktivierung und Aktivierungslimit.

### Validierung

`POST /api/license/validate.php`

Request-Felder:

- `product_slug`
- `api_key`
- `license_key`
- `machine_id`
- `app_version`

Relevante serverseitige Zustände umfassen unter anderem:

- `active`
- `blocked`
- `expired`
- `not_activated`

Eine nicht erreichbare oder syntaktisch ungültige Antwort wird vom Launcher niemals als gültige Lizenz interpretiert.

### Deaktivierung

`POST /api/license/deactivate.php`

Request-Felder:

- `product_slug`
- `api_key`
- `license_key`
- `machine_id`

## Maschinenidentität

Der Launcher sendet nicht den rohen Windows `MachineGuid`.

Stattdessen wird lokal berechnet:

`SHA-256("NMC-SCS-LAUNCHER|" + normalizedMachineGuid)`

Damit bleibt die Aktivierung für dieselbe Windows-Installation stabil, während der ursprüngliche Registry-Wert nicht an LicenseHub übertragen wird.

## Lokale Lizenzablage

Der vom Benutzer eingegebene Lizenzschlüssel wird nicht in `settings.json` gespeichert.

Vorgesehener Speicher:

- Windows Credential Manager
- Target: `NMC Network/NMC SCS LAUNCHER/LicenseKey`

Produktkonfiguration ist davon getrennt und darf nicht versehentlich in Logs ausgegeben werden.

## Desktop-Updates

Für verteilte Desktop-Builds wird **kein langlebiger Update-Bearer-Token in der Anwendung eingebettet**.

Der Launcher verwendet den lizenz- und maschinengebundenen Desktop-Updatevertrag:

`POST /api/desktop-update/check.php`

Request-Felder:

- `product_slug`
- `api_key`
- `license_key`
- `machine_id`
- `version`
- `channel`

Der Server prüft dabei erneut:

- Produktzuordnung
- Lizenzstatus und Ablauf
- Maschinenaktivierung
- Release-Kanal
- Rollout-Freigabe
- Release-Aktivität
- vorhandene SHA-256-Prüfsumme

Bei verfügbarem Update liefert LicenseHub unter anderem:

- `version`
- `channel`
- `download_endpoint`
- `sha256`
- `changelog`
- `released_at`

Der `download_endpoint` enthält **keinen Lizenzschlüssel**. Er enthält nur interne IDs, Ablaufzeit, Channel und eine HMAC-Signatur.

## Download-Sicherheit

Der Download läuft über:

`GET /api/desktop-update/download.php`

Der Server prüft vor der Ausgabe erneut:

- Signatur und Ablaufzeit
- Lizenzexistenz und Lizenzstatus
- Maschinenaktivierung
- Produkt-/Release-Zuordnung
- Release-Aktivität und Channel

Der vom Update-Check gelieferte `download_endpoint` muss HTTPS verwenden und zum konfigurierten LicenseHub-Origin gehören.

Der Launcher akzeptiert ein Update erst nach erfolgreicher SHA-256-Prüfung. Der externe Updater prüft denselben SHA-256-Digest unmittelbar vor dem Anwenden ein zweites Mal.

## Release-Paket

GitHub Actions erzeugt für LicenseHub zusätzlich zum normalen Publish-Artefakt ein vollständiges Update-Bundle:

- ZIP mit `NmcScsLauncher.App.exe` im Archiv-Root
- self-contained `NmcScsLauncher.Updater.exe` im Archiv-Root
- SHA-256-Sidecar
- CI-Prüfung des ZIP-Layouts vor Upload

Dieses Paket ist ein Anwendungsupdate und **nicht** der spätere Windows-Installer.

## Laufzeitkonfiguration

Öffentlicher Default:

- Base URL: `https://licensehub.nmc-it-service.cloud`

Außerhalb des Sourcecodes zu provisionieren:

- Product Slug für NMC SCS LAUNCHER
- Product API Key für Aktivierung, Validierung und Desktop-Update-Check
- mindestens ein Test-Lizenzschlüssel
- Release-/Update-Testdatensatz mit SHA-256

Umgebungsvariablen:

- `NMC_LICENSEHUB_REQUIRED`
- `NMC_LICENSEHUB_BASE_URL` – optionaler Override; ohne Override wird die produktive Base URL verwendet
- `NMC_LICENSEHUB_PRODUCT_SLUG`
- `NMC_LICENSEHUB_PRODUCT_API_KEY`

Es gibt bewusst keinen `NMC_LICENSEHUB_UPDATE_API_TOKEN` mehr.

Echte Product API Keys und Lizenzschlüssel werden nicht in das öffentliche GitHub-Repository committed.

## Server-Contract-Status

Die neuen lizenzgebundenen Desktop-Update-Endpunkte liegen zunächst isoliert im privaten LicenseHub-Repository und müssen vor Produktivfreigabe gegen die reale Umgebung verifiziert werden.

Produktivfreigabe erfolgt erst nach einem vollständigen End-to-End-Test.

## Release-Gate

Der spätere Installer darf erst umgesetzt/freigegeben werden, wenn:

1. Lizenzaktivierung und regelmäßige Validierung mit einem realen LicenseHub-Testprodukt funktionieren.
2. ungültige, gesperrte, abgelaufene und nicht aktivierte Lizenzen korrekt gesperrt werden.
3. LicenseHub-Ausfallverhalten explizit freigegeben ist.
4. Update-Check, lizenzgebundener signierter Download und SHA-256-Prüfung mit einem realen Testrelease funktionieren.
5. die produktive Product-Slug/API-Key-Provisionierung festgelegt und getestet ist.
