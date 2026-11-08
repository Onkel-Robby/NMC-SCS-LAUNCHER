# ATS: DLC-Dateien, Profilmods und Save-Abhängigkeiten

Version 1.0.0; Fortsetzung auf dem vollständigen Stand von PR #93. Keine geänderte DLC-Berechtigungslogik, keine abgeschaltete Prüfung und keine Änderungen an ursprünglichen Spieldateien.

## Ergebnis der erneuten Untersuchung

Die dreizehn zuvor als fehlend gemeldeten Pakete sind in der untersuchten `profile.sii` ausdrücklich unter `active_mods` mit Kennungen des Namensraums `mod_workshop_package.<16 Hexziffern>` eingetragen. Dieselben Kennungen stehen in identischer Reihenfolge in beiden vorhandenen Saves als `mod|<Kennung>|<Titel>`. Der nachträglich vom Nutzer genannte Profilname wurde gegen das frisch dekodierte `profile_name` geprüft und stimmt mit diesem Profil überein. Der konkrete im Spiel verwendete Save ist damit noch nicht bestätigt.

Die beiden Saves sind vom Mai 2024, die Profildatei vom Juli 2024. Ein anderer vorhandener Profilkontext hat null aktive Mods, aber keine Saves. Das verfügbare Spielprotokoll ist älter als der gemeldete aktuelle Test und gehört zu diesem anderen Profil; es ist kein Beleg für die aktuelle Spielauswahl. Versions-Backupkopien sind bytegleich mit den zugehörigen Live-Dateien und belegen keinen neueren Spielzustand. Die bisherige Diagnose darf daher nicht als Beobachtung des laufenden Spiels dargestellt werden.

Anzeigenamen werden nicht zur Klassifizierung benutzt. Eine Workshop-Mod kann beispielsweise ein offizielles DLC modifizieren und dessen Namen im Titel tragen. Das macht ihre PublishedFileId nicht zur Steam-App-ID des DLCs. Aus dem Workshop-Namensraum wird ausschließlich die ursprüngliche hexadezimale PublishedFileId dekodiert; es wird keine Zuordnung nach ähnlich klingendem Titel vorgenommen.

## Die Datenarten bleiben getrennt

| Herkunft | Bedeutung in dieser Prüfung | Folge |
| --- | --- | --- |
| `profile.sii / active_mods` | Im ausgewählten Profil zur Aktivierung eingetragene Mods und ihre Reihenfolge | Nur diese Pakete werden als aktive lokale/Workshop-Quellen aufgelöst. |
| `info.sii / dependencies / mod` | Mod-Referenz des gespeicherten Zustands | Abgleich mit Profilidentität und Reihenfolge; eine historische Referenz aktiviert keinen Mod. |
| `info.sii / dependencies / dlc` | Gespeicherte DLC-Referenz | Exakter Vergleich mit installierter DLC-Manifest-ID, keine Workshop-Suche. |
| `info.sii / dependencies / rdlc` | Eigenständiger auf der Platte gespeicherter DLC-Referenztyp | Separat erhalten und anzeigen; keine erfundene Aussage über aktuelle Aktivierung, Entbehrlichkeit oder Besitz. |
| `dlc_*.scs / dlc_package` | Vorhandenes Archiv mit DLC-ID, Spielkennung und Steam-App-Metadaten | Als DLC-Quelle berücksichtigt; Dateiexistenz ist kein Berechtigungsnachweis. |

Bei unbekanntem Tag oder ungültiger Struktur bleibt die Metadatenprüfung gesperrt. Ein `mod`-Datensatz wird nicht durch seinen Titel zu `dlc` oder `rdlc`. Ein historischer DLC-Verweis wird nicht nachträglich in `active_mods` eingetragen.

## Vorhandene Installation

Die ATS-Installation enthält 51 `dlc_*.scs`-Archive mit 51 eindeutigen lesbaren `dlc_package`-Manifesten, jeweils `game_id: ats`. Der ursprüngliche Resolver aus PR #93 nimmt bereits alle 51 mit der richtigen Manifest-ID als DLC-Quellen auf; kein installiertes DLC fehlt in dieser Erkennung, und es gibt keinen DLC-Metadatenfehler. Keine der dreizehn Workshop-Identitäten stimmt mit einer DLC-Manifest-ID oder deren Steam-App-ID überein.

Beide untersuchten Saves enthalten zusätzlich zu den dreizehn Mod-Referenzen 33 `dlc`- und 16 `rdlc`-Referenzen. Von diesen 49 IDs passen 41 exakt zu heutigen installierten Manifest-IDs; acht haben keinen exakten Treffer. Umgekehrt stehen zehn heute installierte IDs nicht in den alten Saves. Drei Anzeigenamen haben sich trotz gleicher ID verändert. Deshalb sind Name, Anzahl und historischer Dateistand keine zuverlässigen Identitätsschlüssel. „Kein Manifest-ID-Treffer“ behauptet für sich weder fehlenden Besitz noch unverändert fehlenden Spielinhalt.

Die Installation wird damit korrekt berücksichtigt. Nicht bewiesen ist die aktuelle DLC-Berechtigung/Aktivierung. Es wird weder aus historischen Save-Referenzen noch aus vorhandenen Dateien oder einem alten Log eine solche Freigabe erzeugt.

## Änderung im Launcher

Die bereits korrekt geparsten `mod`-/`dlc`-/`rdlc`-Daten bleiben nun als typisierte, unveränderliche Diagnoseinformationen bis zur Truck-Vorschau erhalten. Die Quellenauswahl und die Einbauprüfung verwenden diese neue Darstellung nicht als Berechtigungsnachweis.

Nicht gelesene oder ungültige Save-Metadaten bleiben ausdrücklich „nicht ermittelt“; nur erfolgreich gelesene leere Abhängigkeiten werden als leer dargestellt. Die Gültigkeit einer DLC-Manifest-ID und ihrer Spielzuordnung wird unabhängig von gekürzten Meldungslisten festgehalten. Dadurch kann das Diagnoselimit eine ungültige DLC-Zuordnung nicht versehentlich als bestätigt erscheinen lassen.

Die lokalen technischen Details nennen das tatsächlich ausgewählte Profil, dessen Datei und Home-Verzeichnis, den ausgewählten Save mit Pfad, Speicherart und Zeitstempel sowie den Unterschied zur nicht bestätigten letzten Spielauswahl. Eine nachgewiesene veraltete Home-Anzeige nach Profilwechsel ist korrigiert. Die Produktanfrage war bereits an das ausgewählte Profil gebunden; es wurde keine automatische Profilverwechslung im Resolver nachgewiesen.

Aktive Profileinträge, historische Mod-Referenzen, DLC-/RDLC-Referenzen und installierte Quellen werden getrennt angezeigt. Zuordnungen verwenden exakt dieselbe Manifest-ID und schließen bekannte ungültige Manifeste aus. Kein, ein oder mehrere Treffer bleiben unterscheidbar. Ein zusätzlicher lokaler Export verwendet nur begrenzte Typ-/ID-/Statusfelder und Quellen-Aliase; persönliche Profil-/Savenamen, Originalpfade, Zeitstempel, Rohdaten und Manifest-Geheimnisse werden nicht ergänzt. Kein automatischer Upload.

## Regressionen und Grenzen

Die automatisierten Szenarien verwenden ausschließlich selbst erzeugte ATS-Profile/Saves und DLC-Archive. Sie prüfen: mehrere DLCs ohne Mods und ohne einen einzigen Workshop-Aufruf; gleiche Anzeigenamen und numerische ID-Kollisionen in verschiedenen Namensräumen; historische Mod-Referenzen bei leerem `active_mods`; fehlende/mehrdeutige Manifest-ID-Treffer; unbekannte DLC-Überschreibungen; unveränderliche Diagnoseaufnahmen und datensparsamen Export.

Unüberschriebene, vollständig bestätigte Basisdefinitionen können im synthetischen Szenario als zusätzliche Motor-/Getriebeoptionen freigegeben werden. Eine durch ungeklärtes DLC überschriebene Definition bleibt unbestätigt. Beim tatsächlichen alten Profil bleibt die globale Sperre wegen der dreizehn fehlenden, im Profil eingetragenen Workshop-Pakete bestehen. Ein Test ohne Mods muss mit dem dazugehörigen aktuell gespeicherten Profil-/Save-Zustand erneut geprüft werden; es werden dafür keine Einträge aus Originaldateien entfernt.

## Primärquellen

- [SCS-Entwickler Komat: `profile.sii` steuert Aktivierung/Priorität; `info.sii` beschreibt die Save-Abhängigkeiten](https://forum.scssoft.com/viewtopic.php?p=920077).
- [SCS: Mod-Metadaten, DLC-Abhängigkeiten und bedingte Pakete](https://modding.scssoft.com/wiki/Documentation/Engine/Mod_manager).
- [Steamworks: `BIsDlcInstalled` prüft Besitz und Installation](https://partner.steamgames.com/doc/api/ISteamApps#BIsDlcInstalled). Der Launcher führt hier keinen vorgetäuschten Steam-API-Berechtigungsnachweis ein.

Die vollständige lokale Zuordnung der dreizehn Kennungen sowie die privaten Originalnachweise verbleiben außerhalb des Repositorys.
