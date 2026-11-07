# Auftrag 4: wirksame Quellen und geprüfter Motor-/Getriebewechsel

Version 1.0.0; Umsetzung in D1, D2 und D3 mit eigenen Pull Requests. Kein Merge ohne Nutzerfreigabe. Der bestehende Lesekatalog aus PR89 wird weiterverwendet. Das Produktziel bleibt die direkte Auswahl im Truck-Editor.

## D1 – Quellenvertrag

`ScsEffectiveContentRequest` benennt ausdrücklich Spiel, Profil, Save, Installation und den für den nächsten Start gewählten lokalen Mod-Ordner. Es werden nicht sämtliche konfigurierten Modsets als aktiv angenommen. `ScsActiveContentResolver` liest geordnete Paketkennungen aus dem ausgewählten `user_profile` und prüft die historischen Mod-Abhängigkeiten des ausgewählten `save_container`. Unterschiede bleiben als ungeklärter Kontext sichtbar.

Lokale Paketkennungen werden unverändert aufgelöst; Leerzeichen am Ende können Bestandteil eines tatsächlichen Dateinamens sein. Mehrere passende Archive/Ordner oder mehrere unterschiedliche lokale Mod-Wurzeln werden nicht durch eine alphabetische Auswahl entschieden. Workshop-Kennungen enthalten die hexadezimale PublishedFileId. Die ausgewählte Paketvariante muss zur überprüften installierten Spielversion passen; mehrere Treffer sind ein Fehler. Eine eindeutige universelle Variante kommt nur zum Einsatz, wenn kein expliziter Versionsbereich passt. Informational-Pakete sind keine Fahrzeugquelle.

Spielversionen werden aus der EXE und dem tatsächlichen `version.scs`-/`version.sii`-Inhalt gelesen. Die `version`-Werte in Profil- und Save-Metadaten sind dagegen Formatnummern und keine semantischen Spielversionen.

Originalarchive werden nur anhand der bekannten, in beiden lokalen Spielinstallationen und ihren Mount-Logs bestätigten Basisnamen zugeordnet. Zusätzliche beliebige `.scs`-Dateien im Installationsordner sind kein Aktivierungsnachweis und bleiben unsichere mögliche Quellen. Die `pack_verifiers` in `version.sii` enthalten keine Dateinamen; daraus wird keine nicht belegte Archivzuordnung abgeleitet.

## Wirksame Definitionen und Unsicherheit

Eine geöffnete `IScsEffectiveContentSession` löst beliebige relevante virtuelle Pfade auf. Aktive Mods liegen in der Profilreihenfolge über den Originalquellen; ein später geladener Mod hat höhere Priorität. Gleichrangige unterschiedliche Inhalte werden als mehrdeutig abgewiesen. Dateinamen, Mount-Wurzeln, Herkunft und Versionsangaben bleiben Bestandteil des Nachweises. Includes können damit später über die wirksamen Quellen hinweg gelesen werden.

DLC-Dateien sowie `dlc_*`-Teilpakete werden nicht allein wegen ihrer Existenz als verfügbar behandelt. Ihre tatsächlichen Archivmanifeste liefern Identitäten und Steam-App-Zuordnungen, aber keinen aktuellen Besitznachweis. Ungeklärte bedingte Quellen bleiben als mögliche Überschreibungen erhalten. Eine Basisdefinition kann nur verifiziert werden, wenn sie keine solche Quelle überschreiben könnte. Ein eindeutig höherer, verifizierter Mod kann dagegen eine darunterliegende unsichere Definition vollständig ersetzen. Die gleiche Prüfung gilt für später aufgelöste Includes.

Es werden keine neuen Steam-SDK-Abhängigkeiten eingeführt. Insbesondere werden kostenlose Truck-Pakete nicht allein aus ihrem Namen als frei verfügbar eingestuft. Die Bedeutung von `rdlc` wird nicht geraten; es bleibt eine historische DLC-Angabe.

Verschachtelte `base.zip`-/`dlc_*.zip`- oder entsprechende `.scs`-Teilarchive sind derzeit nicht unterstützt. Ein aktives Paket dieser Form sperrt die Konfiguration ausdrücklich, statt seine möglichen Überschreibungen zu übersehen. Entsprechende Verzeichnis-Mounts werden unterstützt.

## Unveränderlichkeit

Metadaten erhalten SHA-256-Nachweise. Bei Archivmetadaten wird der Hash dem konkreten virtuellen Pfad zugeordnet; mehrgigabytegroße Texturarchive werden nicht für jede Suchanfrage vollständig gehasht. Geöffnete Archivleser und relevante Dateiinhalte bilden einen begrenzten Lesekontext. `VerifyUnchangedAsync` prüft Metadaten, benutzte Inhalte und relevante Dateilisten erneut, einschließlich neu hinzugefügter möglicher Überschreibungen. Diese Nachweise schützen die aktuelle Aufnahme; sie behaupten keine rückwirkende Byteidentität mit einem früheren Spielstart.

## Verifizierte Formatquellen

- [SCS-Entwickler: Profil aktiviert Mods, Save-Info beschreibt historische Abhängigkeiten](https://forum.scssoft.com/viewtopic.php?p=920246).
- [SCS-Entwickler: höchste Mod-Priorität wird zuletzt geladen](https://forum.scssoft.com/viewtopic.php?p=406483). Die Richtung der tatsächlichen `active_mods`-Indizes wurde zusätzlich gegen die lokale 41-Einträge-Konfiguration und deren Mount-Reihenfolge geprüft; keine allgemeine formale Dateiformatspezifikation wird behauptet.
- [Workshop-Paketvarianten](https://modding.scssoft.com/wiki/Documentation/Engine/Units/package_version_info), [Versionsmuster](https://modding.scssoft.com/wiki/Tutorials/SCS_Workshop_Uploader/How_to_upload_new_mod%3F), [Mod-Manager und bedingte Pakete](https://modding.scssoft.com/wiki/Documentation/Engine/Mod_manager).
- [Steam: BIsDlcInstalled prüft Besitz und Installation](https://partner.steamgames.com/doc/api/ISteamApps). Lokale Dateiexistenz und BIsAppInstalled sind dafür kein Ersatz.

## Lokale Untersuchung und Feldtest

Reale Profil-/Save-Dateien wurden ausschließlich gelesen; verschlüsselte Dateien wurden über den bereits gepinnten Decoder an temporären Kopien untersucht. Die Original-Hashes blieben identisch. Private Profilkopien und Spielassets werden nicht ins Repository übernommen.

Das aktuelle ETS2-Profil enthält 41 aktive Mods; Reihenfolge und Identitäten stimmen mit dem untersuchten Save und den Mount-Einträgen des Logs überein. Bei ATS stammen die vorhandenen Profile aus 2024, während das jüngere Log keine passende Save-Ladung enthält. Es wäre falsch, dieses Log ohne Bezug auf das ausgewählte Profil anzuwenden. Selbst ein passender Log-Pfad kann nach einem Autosave andere Bytes bezeichnen.

Der vollständige D1-Leseversuch verweigert beim tatsächlichen ETS2-Profil die Freigabe wegen vier aktiven Paketen: nicht unterstütztes Archivformat, uneindeutiges/fehlendes Mod-Manifest, fehlendes/mehrdeutiges lokales Paket und nicht lesbares ZIP-Inhaltsverzeichnis. Beim tatsächlichen ATS-Profil mit Saves sind alle 13 Workshop-Pakete nicht eindeutig vorhanden. Diese Ergebnisse werden nicht durch Weglassen der problematischen Mods korrigiert.

Ein gesonderter Test verwendet ausdrücklich synthetische Profil-/Save-Metadaten ohne Mods mit den echten installierten Archiven. ETS2 liefert 14 Dateien im Volvo-FH16-Motorverzeichnis; drei gelesene Beispiele sind verfügbar verifiziert. ATS liefert 20 Dateien im Kenworth-W900-Motorverzeichnis; die drei Beispiele bleiben wegen ungeklärter DLC-Aktivierung gesperrt. Beide Kontexte bestehen die erneute Quellenprüfung. In allen vier Szenarien bleiben die geprüften Original-Hashes sowie Größen/Zeitstempel der Installationsarchive unverändert. Dies sind Leseprüfungen, keine Ingame-Einbautests.

D1 ist die technische Grundlage. Die sichtbare Feldabnahme erfolgt mit D2 im Truck-Editor: bekanntes Profil und vorgesehenes Modset auswählen, aktive Reihenfolge und tatsächlich gewählte Workshop-Variante vergleichen, ein fehlendes/mehrdeutiges Paket prüfen und bestätigen, dass ungeklärte Quellen keine Freigabe erzeugen. Der Einbau wird erst mit D3 an kopierten manuellen Saves geprüft; dabei muss das Spiel beendet sein.

## D2 – Truck-Vorschau und Konfigurationsprüfung

Die Auswahl erscheint automatisch im Truck-Reiter nach dem Laden des aktiven eigenen Trucks. Motoren und Getriebe besitzen getrennte Suchfelder, aktuelle Markierungen, Herkunft, technische Angaben und begründete Sperren. Die Prüfung bewertet das gewählte Paar gemeinsam; eine einzeln gültige Komponente darf zunächst ausgewählt werden, auch wenn dazu noch der passende Partner fehlt. Technische Pfade und Prüfdetails bleiben bei Bedarf sichtbar.

Der lokale Quellordner stammt ausschließlich aus dem ausgewählten, zum Profil passenden Start-Modset oder aus dem Home-Verzeichnis dieses Profils. Andere konfigurierte Modsets werden nicht als aktiv betrachtet. Spiel-, Profil-, Save-, Truck-, Modset- und Installationswechsel verwerfen ältere Ergebnisse. Die abgetrennte Vorschau hält keine Dateihandles offen und ist keine spätere Schreibberechtigung.

`ScsTruckPowertrainSnapshotReader` bindet die gelesenen Originalbytes an SHA-256, decodiert eine frische temporäre Kopie und prüft danach das Original erneut. Saves bis 128 MiB werden über den vorhandenen Save-Parser und gezielt geparste Units gelesen. Besitzliste, aktive Zuordnung, vollständige Zubehöranzahl, fortlaufende Indizes, eindeutige Referenzen und zusätzliche Besitzer derselben Zubehör-Unit werden geprüft. Das Modell muss unabhängig durch die Truck-`data.sii` belegt sein. Motor, Getriebe und Truck-Daten müssen die passende gespeicherte Zubehörklasse haben.

Die Regelprüfung liest die wirksamen Definitionen und Includes aller verbleibenden Zubehörteile sowie des gewählten Paars. `suitable_for`, `conflict_with`, `require`, `defaults` und `overrides` werden auf diese gesamte Zielkonfiguration angewendet. Das Hinzufügen oder Entfernen weiteren Zubehörs wird nicht automatisch vorgenommen. Ein erforderlicher solcher Folgeumbau bleibt ein Sperrgrund. Gleichnamige unterschiedliche Definitions-Units, unbekannte Felder, ungeklärte Quellen und nicht auflösbare Owner-Referenzen geben keine Kombination frei. Nicht leere Slot-/Hookup-Verknüpfungen sind noch nicht unterstützt.

Mehrere `suitable_for`-Einträge desselben eindeutig benannten Zubehörtyps sind Alternativen. Unterschiedliche Typgruppen müssen jeweils erfüllt sein; mehrdeutige Wildcard-Typgruppen bleiben gesperrt. Die Alternativen wurden gegen die vollständige Original-Volvo-Konfiguration und die [Erklärung des SCS-Entwicklers Max zu mehreren passenden Motoren](https://forum.scssoft.com/viewtopic.php?p=338512) geprüft. Eine pauschale UND-Verknüpfung aller Einträge würde bereits die zwei erlaubten Kabinenvarianten eines Original-Interieurs fälschlich ausschließen.

Drehmoment stammt aus der numerischen Definition; Getriebeinformationen aus den tatsächlichen Gängen, Übersetzungen und Retarder-Stufen. Eine Leistung wird nur bei eindeutig angegebenen kW in den Definitionsinformationen umgerechnet und als Angabe laut Definition bezeichnet. `hp` wird nicht ungeprüft als metrische PS ausgegeben. Es wird keine Getriebe-Drehmomentgrenze erfunden.

Formatgrundlagen: [allgemeine Zubehörregeln](https://modding.scssoft.com/wiki/Documentation/Engine/Units/accessory_data), [Truck-Daten](https://modding.scssoft.com/wiki/Documentation/Engine/Units/accessory_truck_data), [Motor](https://modding.scssoft.com/wiki/Documentation/Engine/Units/accessory_engine_data), [Getriebe](https://modding.scssoft.com/wiki/Documentation/Engine/Units/accessory_transmission_data), [SII und Includes](https://modding.scssoft.com/wiki/Documentation/Engine/Units). Ergänzende Leseprüfungen verwenden die tatsächlich installierten Originaldefinitionen; Spielassets und private Saves werden nicht ins Repository kopiert.

Der vollständige Original-Volvo-FH16 aus `truck_desktop` wurde nur außerhalb des Repositorys in synthetische Profil-/Save-Metadaten eingebettet. Seine 44 unveränderten Zubehörteile ergeben mit den echten installierten Quellen acht Motoren, acht Getriebe und acht vollständig passende Vorschau-Kombinationen. Die weiteren Motorvarianten benötigen jeweils ein anderes Badge und bleiben deshalb gesperrt. Original-Archivhash sowie Größen/Zeitstempel bleiben unverändert. Das Schema wurde außerdem an 571 Originaldefinitionen und ihren Includes geprüft. Unterstützt werden Spielversionen 1.39–1.62 und direkte Dateien unter `engine/` bzw. `transmission/`; unbekannte spätere Versionen, verschachtelte Kandidatenverzeichnisse, weitere Motortypen und nicht unterstützte Owner-Typen erhalten keine Freigabe.

D2 wurde mit fehlerfreiem Release-Build und 110 zusätzlichen Tests geprüft. Lokal bestehen 646/649 Tests; ausschließlich die drei bekannten Steam-Erkennungskonflikte bleiben. Die gesamte App-Suite besteht mit 205 Tests. Die Truck-Oberfläche wurde offline bei 1080, 1320 und 1600 Pixeln geprüft. Der neue Bereich ist in D2 eine Vorschau; die vorhandenen Save-basierten Einzelaktionen stehen vorübergehend in einer eingeklappten separaten Gruppe. D3 ersetzt diesen Übergang durch den erneut geprüften gemeinsamen Einbau.

## Stand

D1: implementiert und unabhängig geprüft. Der .NET-10-Release-Build läuft ohne Warnungen und Fehler; alle 106 neuen D1-Tests bestehen. Die vollständige lokale Suite umfasst 539 Tests: 536 bestehen, drei bereits vor D1 reproduzierte Steam-Erkennungstests kollidieren mit der real registrierten ETS2-Installation. Registry und Tests wurden dafür nicht verändert; der vollständige Windows-CI-Lauf wird im D1-PR dokumentiert.

D1 wurde als [PR #90](https://github.com/Onkel-Robby/NMC-SCS-LAUNCHER/pull/90) veröffentlicht. [Windows-CI #265](https://github.com/Onkel-Robby/NMC-SCS-LAUNCHER/actions/runs/42403512660) besteht mit allen 539 Tests; [NMC Custom Installer #135](https://github.com/Onkel-Robby/NMC-SCS-LAUNCHER/actions/runs/42403512761) besteht einschließlich Upgrade bei gleicher öffentlicher Version und Uninstall-Smoke-Test. Kein Merge durchgeführt.

D2: Auswahl und Kompatibilitätsprüfung implementiert. D3: verifizierter Einbau folgt; nicht sicher prüfbare Fälle bleiben gesperrt. Version 1.0.0 bleibt unverändert.
