# LicenseHub-Integration – NMC SCS LAUNCHER

## Status und feste Grenze

LicenseHub selbst bleibt unverändert. Der Launcher verwendet ausschließlich die bereits im produktiven LicenseHub vorhandenen API-Endpunkte.

Produktive Basis-URL:

`https://licensehub.nmc-it-service.cloud`

Product Slug:

`NMC-SCS-LAUNCHER`

Öffentliche Defaults können für isolierte Testumgebungen über `NMC_LICENSEHUB_BASE_URL` und `NMC_LICENSEHUB_PRODUCT_SLUG` überschrieben werden.

## Lizenzierung

Der Launcher verwendet die bestehenden LicenseHub-Endpunkte:

- Aktivierung: `POST /api/license/activate.php`
- Validierung: `POST /api/license/validate.php`
- Deaktivierung: `POST /api/license/deactivate.php`

Die Requests verwenden die vom vorhandenen Serververtrag verlangten Felder `product_slug`, `api_key`, `license_key` und `machine_id`; Aktivierung und Validierung senden zusätzlich die jeweils vorgesehenen Geräte-/Versionsfelder.

LicenseHub bleibt die Autorität für Lizenzstatus, Ablauf, Maschinenaktivierung und Aktivierungslimit. Netzwerk-, Konfigurations- und Protokollfehler werden in erzwungenen Release-Builds fail-closed behandelt.

## Maschinenidentität

Der Launcher sendet nicht den rohen Windows `MachineGuid`.

Lokal wird berechnet:

`SHA-256("NMC-SCS-LAUNCHER|" + normalizedMachineGuid)`

## Lokale Credential-Ablage

Lizenzschlüssel und Product API Key werden getrennt im Windows Credential Manager gespeichert und nicht in `settings.json` geschrieben.

Targets:

- Lizenzschlüssel: `NMC Network/NMC SCS LAUNCHER/LicenseKey`
- Product API Key: `NMC Network/NMC SCS LAUNCHER/ProductApiKey`

Der Product API Key kann bei der Erstprovisionierung über `NMC_LICENSEHUB_PRODUCT_API_KEY` bereitgestellt werden. Erkennt der Launcher diesen Wert, speichert er ihn in Windows Credential Manager. Spätere direkte EXE-Starts laden ihn von dort, sodass keine PowerShell-Umgebungsvariable für jeden Start notwendig ist.

Echte Product API Keys und Lizenzschlüssel werden weder committed noch geloggt.

### Sicherheitsgrenze

Da der unveränderte LicenseHub-Server den Product API Key zwingend für den Desktop-Lizenzvertrag verlangt, muss dieses Credential auf dem Client verfügbar sein. Windows Credential Manager verhindert Klartextablage in Projektdateien oder JSON-Konfiguration, bietet aber keinen Schutz gegen einen ausreichend privilegierten lokalen Administrator. Diese Grenze ist eine direkte Folge des bestehenden Serververtrags und wird nicht durch einen eingebetteten zusätzlichen Bearer-Token verschärft.

## Release-Enforcement

Release-Builds erzwingen LicenseHub bereits zur Buildzeit. Fehlt das lokale Product Credential oder kann keine gültige Lizenz bestätigt werden, wird die Hauptoberfläche nicht freigegeben.

Debug-Builds dürfen weiterhin den Entwicklungs-Bypass verwenden.

Zusätzlich zum Startup-Gate wird die Lizenz unmittelbar vor dem eigentlichen Spielprozess erneut geprüft.

## Updates mit unverändertem LicenseHub

Für Updates verwendet der Launcher ebenfalls nur bestehende Serverfunktionen.

Ablauf:

1. gespeicherten Lizenzschlüssel und Maschinen-ID ermitteln;
2. Lizenz unmittelbar über `POST /api/license/validate.php` erneut validieren;
3. nur bei aktiv bestätigter Lizenz den vorhandenen Update-Check aufrufen;
4. `GET /api/update/check.php` mit `product_slug`, `api_key`, aktueller Version und Channel;
5. Version, Downloadziel und SHA-256 aus der vorhandenen LicenseHub-Antwort auswerten;
6. Update herunterladen und SHA-256 während des Downloads verifizieren;
7. externer Updater prüft denselben SHA-256-Digest unmittelbar vor dem Anwenden erneut.

Es werden keine neuen `/api/desktop-*`-Endpunkte vorausgesetzt und kein langlebiger separater Update-Bearer-Token in die Anwendung eingebettet.

Der Launcher akzeptiert nur HTTPS-Downloadziele vom konfigurierten LicenseHub-Origin. Ein abweichender Origin wird fail-closed abgelehnt.

## Release-Paket

GitHub Actions erzeugt neben dem normalen Publish-Artefakt ein vollständiges Update-Bundle:

- ZIP mit `NmcScsLauncher.App.exe` im Archiv-Root
- self-contained `NmcScsLauncher.Updater.exe` im Archiv-Root
- SHA-256-Sidecar
- CI-Prüfung des ZIP-Layouts

Dieses Paket ist ein Anwendungsupdate und nicht der spätere Windows-Installer.

## Laufzeitkonfiguration

Öffentliche Defaults:

- Base URL: `https://licensehub.nmc-it-service.cloud`
- Product Slug: `NMC-SCS-LAUNCHER`

Provisionierung:

- Product API Key einmalig außerhalb des Sourcecodes, danach Windows Credential Manager
- Test-/Produktivlizenz über den normalen Aktivierungsdialog

Unterstützte Umgebungsvariablen:

- `NMC_LICENSEHUB_REQUIRED`
- `NMC_LICENSEHUB_BASE_URL`
- `NMC_LICENSEHUB_PRODUCT_SLUG`
- `NMC_LICENSEHUB_PRODUCT_API_KEY`

Für veröffentlichte Release-Builds kommt die Lizenzpflicht aus dem Build und kann nicht durch Weglassen von `NMC_LICENSEHUB_REQUIRED` umgangen werden.

## Release-Gate

Der Installer bleibt blockiert, bis folgende reale End-to-End-Tests bestanden sind:

1. Product API Key einmalig provisionieren und bei späterem direktem EXE-Start aus Windows Credential Manager wiederverwenden.
2. Lizenz aktivieren und nach Neustart serverseitig validieren.
3. gesperrte, abgelaufene oder nicht aktivierte Lizenz blockiert den Launcher fail-closed.
4. direkter EXE-Doppelklick kann die Lizenzprüfung nicht umgehen.
5. reales LicenseHub-Release wird über den bestehenden Update-Endpunkt erkannt, heruntergeladen, per SHA-256 verifiziert und erfolgreich angewendet.
6. Rollback des externen Updaters ist praktisch verifiziert.
