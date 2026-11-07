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

## Stand

D1: implementiert und unabhängig geprüft. Der .NET-10-Release-Build läuft ohne Warnungen und Fehler; alle 106 neuen D1-Tests bestehen. Die vollständige lokale Suite umfasst 539 Tests: 536 bestehen, drei bereits vor D1 reproduzierte Steam-Erkennungstests kollidieren mit der real registrierten ETS2-Installation. Registry und Tests wurden dafür nicht verändert; der vollständige Windows-CI-Lauf wird im D1-PR dokumentiert.

D2: Auswahl und Kompatibilitätsprüfung folgen. D3: verifizierter Einbau folgt; nicht sicher prüfbare Fälle bleiben gesperrt. Version 1.0.0 bleibt unverändert.
