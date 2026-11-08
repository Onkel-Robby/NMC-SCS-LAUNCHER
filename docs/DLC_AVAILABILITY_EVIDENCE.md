# DLC-Verfügbarkeit: Machbarkeit und Nachweisgrenzen

Stand: 8. November 2026. Auftrag 8 auf dem vollständigen Stand von PR #95; Launcher-Version 1.0.0.

## Ergebnis

Die vorhandenen lokalen Daten belegen installierte DLCs und deren Verwendung in einer vergangenen Spielsitzung. Ein an das aktuelle Konto, die aktuelle Installation und den Einbauzeitpunkt gebundener Berechtigungsnachweis fehlt. Deshalb entsteht aus dieser Untersuchung keine neue Einbaufreigabe. Das bedeutet **unbestätigt**, nicht **nicht berechtigt**.

Ein zuverlässiger Nachweis ist grundsätzlich mit einer passenden Steamworks- oder SCS-Integration möglich. Der Launcher besitzt derzeit keinen solchen Provider. Weder ein vorhandenes Archiv noch ein kostenloser Storeeintrag ersetzt ihn.

## Was die einzelnen Quellen belegen

| Quelle | Aussage | Grenze |
| --- | --- | --- |
| SCS-Archiv und `dlc_package`-Manifest | Paketidentität, Spielzuordnung und deklarierte Steam-App-ID; lesbare enthaltene Definitionen | Keine Aussage über die aktuelle Lizenz oder Aktivierung. Manifestmetadaten authentifizieren kein Benutzerkonto. |
| Steam-Installationsmetadaten und installierte Depots | Zuordnung der lokal erfassten Installation zu App, Build und Depot | Installationszustand ist kein aktueller Lizenznachweis. Steam-Depotinstallation ist außerdem etwas anderes als das Einhängen eines Archivs in das SCS-Dateisystem. |
| Save-Abhängigkeiten `dlc` / `rdlc` | Referenzen des gespeicherten Zustands | Keine aktuelle Aktivierung und kein Besitznachweis. |
| Spielprotokoll mit explizitem Archiv-Mount | Beobachtete Verwendung des genannten Archivs in dieser protokollierten Spielsitzung | Nicht automatisch an aktuelles Konto, aktuelle Archivbytes oder den späteren Einbau gebunden. Archivprüfung, Mount und tatsächlich gewinnende Definition sind verschiedene Aussagen. |
| Wirksame Definition | Konkreter Inhalt nach Spielversion, Aktivierung, Includes und Überschreibungsreihenfolge | Auch ein berechtigtes und installiertes DLC kann durch andere Quellen überschrieben werden oder zur gewählten Version nicht passen. |

Valve erlaubt ausdrücklich, DLC-Inhalte gemeinsam mit dem Basisspiel an alle Besitzer auszuliefern und ihre Verwendung erst über die Lizenzprüfung zu steuern. Dateivorhandensein reicht deshalb auch bei unveränderten offiziellen Dateien nicht aus. Die dokumentierten Depotregeln beschreiben die Auslieferung und ihre Reihenfolge, nicht die endgültige SCS-Definitionsauflösung. [Steamworks: DLC](https://partner.steamgames.com/doc/store/application/dlc), [Steamworks: Depots](https://partner.steamgames.com/doc/store/application/depots).

## Verfügbare Schnittstellen und ihre Voraussetzungen

- **Steamworks-Client:** `BIsDlcInstalled` prüft Besitz und Installation. `BIsSubscribedApp` prüft die Lizenz einer angegebenen App. Die DLC-Dokumentation nennt ausdrücklich den DLC-Besitzcheck aus einem anderen Spiel; ein allgemeines Cross-App-Verbot wäre falsch. `BIsAppInstalled` dagegen beweist keinen Besitz und ist nicht für DLCs bestimmt. [ISteamApps](https://partner.steamgames.com/doc/api/ISteamApps), [DLC und Cross-App-Abfragen](https://partner.steamgames.com/doc/store/application/dlc).
- **Initialisierung:** `SteamAPI_Init` benötigt einen laufenden Steam-Client, einen gültigen eigenen App-ID-Kontext, denselben OS-Benutzerkontext und eine Lizenz für diese App. Ein eigener regulärer Steamworks-Kontext wäre ein zu prüfender Integrationsweg für NMC; er ist derzeit nicht vorhanden. Das Eintragen der ATS-/ETS2-App-ID oder einer SDK-Test-App-ID schafft keinen eigenen Integrationsvertrag. Die DLL allein ist keine allgemeine externe Berechtigungsschnittstelle. [Steamworks: Initialisierung](https://partner.steamgames.com/doc/api/steam_api#SteamAPI_Init).
- **Publisher-WebAPI:** `ISteamUser/CheckAppOwnership/v4` liefert den aktuellen Lizenzstatus einschließlich temporärer Lizenzen. Der Aufruf benötigt einen Publisher-Schlüssel mit Rechten an der Ziel-App und muss auf einem sicheren Server erfolgen. Ein gewöhnlicher Benutzer-WebAPI-Schlüssel oder ein in den Launcher eingebetteter Schlüssel ersetzt diese Voraussetzung nicht. Für ATS/ETS2 wäre eine entsprechende Publisher-Integration erforderlich. [ISteamUser: CheckAppOwnership](https://partner.steamgames.com/doc/webapi/ISteamUser#CheckAppOwnership).
- **Steam-Anmeldung:** OpenID bestätigt die Steam-Identität. Die Eigentumsprüfung bleibt ein zusätzlicher Schritt mit den genannten Zugriffsrechten; Anmeldung allein genügt nicht. [Steamworks: Authentication and Ownership](https://partner.steamgames.com/doc/features/auth).
- **SCS Telemetry SDK:** Die öffentlichen Header des aktuellen SDK 1.16 wurden geprüft. Sie stellen Fahrzeug-, Spiel- und Eingabetelemetrie bereit, aber keine dokumentierte DLC-Lizenzprüfung, vollständige Archiv-Mountliste oder Liste der gewinnenden Definitionsquellen. `scs_sdk_init_params_t.game_version` bezeichnet die API-Kompatibilitätsversion und ausdrücklich nicht den Spielpatchstand. [SCS: Telemetry SDK](https://modding.scssoft.com/wiki/Documentation/Engine/SDK/Telemetry), [offizielles SDK 1.16 mit Headern](https://download.eurotrucksimulator2.com/scs_sdk_1_16.zip).

SCS dokumentiert, dass DLC-spezifische Modteile nur bei entsprechendem Besitz gemountet werden. Das begründet die notwendige Trennung von vorhandenem und wirksamem Inhalt, definiert aber keinen allgemeinen Berechtigungsvertrag für einzelne Logmeldungen. In den untersuchten öffentlichen SCS-Unterlagen wurde kein solcher Vertrag für das Einhängen offizieller Basisarchive gefunden. [SCS: Mod Manager](https://modding.scssoft.com/wiki/Documentation/Engine/Mod_manager).

## Kenworth T680 2022

`ats_t680_22` gehört zum offiziellen DLC mit Steam-App-ID `2720080`. Die offizielle Storeseite kennzeichnet es als kostenloses DLC und setzt ATS voraus. Der Preis sagt nichts darüber aus, ob das DLC im gerade verwendeten Konto aktiviert und in dieser Installation wirksam ist. [SCS-Angebot auf Steam](https://store.steampowered.com/app/2720080/American_Truck_Simulator_Kenworth_T680_2022/).

Die lokale Untersuchung fand das Archiv mit gültigem Manifest und einen expliziten Mount in der zuletzt protokollierten ATS-1.62.1.1s-Sitzung. Am Sitzungsende wurde es wieder ausgehängt. Das ist ein konkreter historischer Laufzeitnachweis, aber kein aktueller, mit der Einbauprüfung verbundener Lizenzstatus. Private Pfade, Kontodaten und Originalprotokollzeilen werden nicht in das Repository übernommen.

Insgesamt enthält diese Sitzung 51 offizielle DLC-Mounts. Die installierte EXE stimmt mit Version und Revision des Logs überein. Steam führt das Ziel-DLC auch in den lokalen Depotinformationen. Der geloggte HashFS-Wert ist jedoch nicht der SHA-256 der gesamten heutigen Archivdatei; daraus wird keine Bindung an unveränderte Archivbytes abgeleitet. Das Log bestätigt dasselbe Profil, aber das Laden von **Autosave**, während NMC **QuickSave** prüft. Damit ist auch der konkrete Save-Snapshot nicht durch diese Sitzung bestätigt.

Die ETS2-Gegenprobe bestätigt entsprechende Installationsdaten und 105 historische DLC-Mounts. Diese reale ETS2-Sitzung war nicht modfrei; modfreie ATS-/ETS2-Fälle werden gesondert mit synthetischen Daten geprüft.

Die bereits erkannten elf Motoren und sechs Getriebe sind dadurch nicht zusätzliche verifizierte Einbauoptionen. Erst ein belastbarer Verfügbarkeitsnachweis und die erneute vollständige Kombinationsprüfung können diesen Status ändern.

## Anforderungen an eine spätere Integration

Ein Provider muss seine belegte Aussage präzise liefern: **Lizenz vorhanden**, **installiert** und **in einer bestimmten Sitzung wirksam** dürfen keine austauschbaren Flags sein. Er benötigt:

1. Eine dokumentierte und tatsächlich verfügbare Quelle mit regulärem Zugriff: eigener geeigneter Steamworks-Kontext, berechtigter Publisher-Dienst oder eine von SCS bereitgestellte Aktivierungsschnittstelle.
2. Die Bindung an Spiel-App-ID, DLC-App-ID, geprüftes Konto beziehungsweise aktive Lizenz und Sitzung. Ein Kontowechsel oder das Ende einer temporären Lizenz macht einen älteren positiven Befund unbrauchbar.
3. Die konkrete Spielversion beziehungsweise Buildidentität, die eindeutig zugehörige Installation sowie Fingerprints der tatsächlich geprüften Archive und Metadaten. Ein Dateizeitstempel allein genügt nicht.
4. Die endgültig aktiven Quellen und ihre Reihenfolge für das ausgewählte Profil. Mountbeobachtung allein bestätigt noch keine gewinnende Definition; Konflikte, bedingte DLC-Teile und unbekannte Überschreibungen bleiben gesperrt.
5. Eine nachvollziehbare Gültigkeitsdauer und erneute Prüfung unmittelbar vor und nach dem Schreiben. Bei Quellen-, Konto-, Versions- oder Aktivierungsänderung darf keine bestätigte Installation gemeldet werden; die bestehenden Abbruch-, Backup- und Restore-Regeln gelten weiter.

Eine direkt beobachtete neue Spielsitzung könnte bessere Laufzeitbelege liefern als ein vorhandenes Protokoll. Dafür müssten Prozess, vollständige Aktivierungsphase, Installation, Version, Quellenreihenfolge und unveränderte Archivbytes nachweisbar zusammengehören. Ein bloß aktuelles Logdatum erfüllt diese Bedingungen nicht. Ein solcher Provider wird hier mangels vorhandenen verlässlichen Zugriffsvertrags nicht durch Heuristiken ersetzt.

Kostenlose und kostenpflichtige DLCs unterliegen derselben Nachweisprüfung. Fehlende Nachweise bleiben gesperrt; die bestehende vollständige Zubehör-, Motor-/Getriebe-, SHA-256-, Backup- und Restore-Validierung bleibt erforderlich.

## Umsetzung dieses PRs

`ScsContentLayer.DlcSteamAppId` bewahrt die optionale positive, unquotierte 32-Bit-App-ID eines eindeutig gelesenen DLC-Manifests auf. Fehlend oder nullwertig bedeutet unbekannt; fehlerhafte Werte machen die Metadaten unbestätigt. Daraus entsteht weder eine Preis- noch eine Besitzklassifizierung. Der manuell ausgelöste Diagnoseexport ergänzt `sources[].dlcSteamAppId` für validierte DLC-Metadaten; andere Quellen und unbekannte IDs erhalten `null`. Es werden keine Kontokennungen oder weiteren Manifestinhalte exportiert.

Die bestehende globale Konfigurationsprüfung und die Komponentenprüfung bleiben getrennt. Alle DLC-Layer behalten ihre bisherige Priorität und `AvailabilityVerified = false`. Unbestätigte mögliche Überschreibungen werden weiterhin berücksichtigt, widersprüchliche Definitionen bleiben gesperrt. Vor und nach einer synthetischen Einbautransaktion werden Quellen, Save-SHA-256 und Truck-Zuordnung wie bisher erneut geprüft. Kein vorhandenes Log, Steam-ACF oder Storepreis wird als Freigabequelle eingeführt.

## Erneute Prüfung und Regressionen

Die neue Release-Implementierung wurde am realen ATS-QuickSave ausschließlich lesend geprüft: Version 1.62.1.1, 0 aktive Mods, 0 Workshop-Quellen, 51 gültige DLC-Manifeste und 35 Save-Abhängigkeiten (26 DLC, 9 RDLC). `ats_t680_22` liefert die erwartete App-ID 2720080. Der T680 enthält 60 gespeicherte Zubehörteile; 11 Motoren, 6 Getriebe und alle 66 daraus gebildeten Paare wurden erneut dem Prüfpfad zugeführt. **Keines der Paare erhält eine Einbaufreigabe.**

Es gibt keine globale Konfigurationssperre. Die Definitionsprüfung erreicht weiterhin `definition_availability_unverified`, daraus entstehen die Zubehör- und Paarsperren. Solange diese Quellen nicht bestätigt sind, kann die nachgelagerte Regelprüfung keine vollständige Kompatibilitätsbestätigung liefern; bisher verdeckte weitere Regeln sind damit nicht ausgeschlossen. Profil-, Info- und Save-Datei sind im SHA-256-Vorher-/Nachhervergleich unverändert. Der zusätzliche lokale Quellen-Audit bestätigte ebenfalls alle 14 untersuchten Originaldateien unverändert. Keine echte Datei wurde einem automatisierten Einbau- oder Restore-Test zugeführt.

32 neue Testfälle decken die öffentliche Identität, kostenlose/kostenpflichtige ATS-/ETS2-Pakete, fehlende/nullwertige/fehlerhafte IDs, unbestätigte aktuelle und veraltete Mount-/Depotinformationen, identische unbekannte Überschreibungen, Änderungen nach der Vorschau und den datensparsamen Diagnoseexport ab. Die vorhandenen Konflikt-, Precommit-, Rollback-, Backup- und bytegenauen Restore-Tests laufen in der vollständigen Suite weiter.

Lokaler Release-Build: 0 Warnungen, 0 Fehler. Vollsuite: 931 von 934 Tests bestanden (Core 32/32, App 269/269, Infrastructure 630/633). Die drei bereits vor dieser Änderung reproduzierten `SteamGameInstallationDetectorTests` werden von der realen, auf diesem Rechner registrierten Steam-Spielinstallation beeinflusst. Windows-CI und NMC Custom Installer werden für den abhängigen PR separat ausgeführt; ihre abschließenden Resultate werden im PR verlinkt.
