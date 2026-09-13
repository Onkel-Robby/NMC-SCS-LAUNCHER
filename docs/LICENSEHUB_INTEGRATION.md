# LicenseHub-Integration – NMC SCS LAUNCHER

## Status

Diese Integration basiert auf dem aktuellen Quellstand des privaten Hauptprojekts `Onkel-Robby/Licensehub` und nicht auf erfundenen Endpoints.

Geprüfter LicenseHub-main-Stand bei der initialen Integration:

- Commit: `90729c60312a55362e3d43bffb71cb0c36351ed1`
- LicenseHub Produktmarker: 1.0.0

Der Bot-spezifische `/api/v1`-M2M-Contract ist für den verteilten Windows-Client **nicht** der primäre Aktivierungsweg. Die Desktop-Lizenzierung verwendet die bereits vorhandenen Client-Endpunkte im LicenseHub-Hauptprojekt.

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

Produkt-/Update-API-Konfiguration ist davon getrennt und darf nicht versehentlich in Logs ausgegeben werden.

## Updates

Für sichere Launcher-Updates wird der aktuelle projektbezogene LicenseHub-Pfad verwendet:

`POST /api/version/update.php`

Authentifizierung:

- `Authorization: Bearer <update api token>`
- erforderlicher serverseitiger Scope: `downloads.secure`

Request:

- `product`
- `version`
- `channel`

Bei verfügbarem Update liefert LicenseHub unter anderem:

- `version`
- `channel`
- `download_endpoint`
- `checksum`
- `changelog`
- `released_at`

LicenseHub erzeugt für `download_endpoint` ein kurzlebiges signiertes Download-Token. Der Server verwendet dafür aktuell 300 Sekunden TTL.

Der Launcher akzeptiert einen Update-Download erst nach erfolgreicher SHA-256-Prüfung gegen den von LicenseHub gelieferten Digest.

## Download-Sicherheit

Der vom Update-Check gelieferte `download_endpoint` muss HTTPS verwenden und zum konfigurierten LicenseHub-Origin gehören.

Der signierte Endpoint `/api/releases/secure_download.php` validiert Token, Release-ID, Reseller-/Tenant-Bindung und Channel und leitet anschließend auf die hinterlegte Release-Datei weiter.

Der Launcher führt heruntergeladene Daten nicht ungeprüft aus.

## Noch zu provisionieren

Vor Aktivierung des produktiven Lizenz-Gates werden außerhalb des Sourcecodes benötigt:

- LicenseHub Base URL
- Product Slug für NMC SCS LAUNCHER
- Product API Key für Lizenzaktivierung/-validierung
- Update API Token mit minimal notwendigem `downloads.secure`-Scope
- mindestens ein Test-Lizenzschlüssel
- Release-/Update-Testdatensatz mit SHA-256

Diese Werte werden nicht als echte Secrets in das öffentliche GitHub-Repository committed.

## Release-Gate

Der spätere Installer darf erst umgesetzt/freigegeben werden, wenn:

1. Lizenzaktivierung und regelmäßige Validierung mit einem realen LicenseHub-Testprodukt funktionieren.
2. ungültige, gesperrte, abgelaufene und nicht aktivierte Lizenzen korrekt gesperrt werden.
3. LicenseHub-Ausfallverhalten explizit freigegeben ist.
4. Update-Check, signierter Download und SHA-256-Prüfung mit einem realen Testrelease funktionieren.
5. die produktive Credential-Provisionierung festgelegt ist.
