# Motor-/Getriebe-Lesekatalog – Auftrag 3

Version **1.0.0**. Phasen B/C ergänzen den vorhandenen Editor. Neue Archivfunde bleiben read-only; der bisherige Katalog aus eigenen Trucks desselben Modells im ausgewählten Save bleibt die Quelle für Motor-/Getriebewechsel.

## Quellen und Bedeutung

| Quelle | Erkennung | Aussagegrenze |
| --- | --- | --- |
| Ausgewählter Save | Bisheriger Fahrzeugdienst; aktueller Motor/Getriebe markiert | Im Save verwendet, keine neue vollständige Zubehörprüfung |
| ETS2-/ATS-Installation | Bestehender Installationsdetektor; vorhandene `.scs`-Dateien | Spiele getrennt; keine erfundenen Definitionspfade |
| DLC | Tatsächlich vorhandene `dlc_*.scs`-Dateien | Lokale Datei belegt weder Besitz noch Aktivierung |
| Lokale Mods | `mod` im ausgewählten Profil-Home und konfigurierte Modset-Ordner desselben Spiels | Kein Schluss auf Aktivierung in diesem Profil |
| Workshop | Spielgebundene, lokal vorhandene Pakete über den bestehenden Locator | Paketversion, Aktivierung und effektive Priorität bleiben ungeprüft |

Der Lesekatalog zeigt Namen aus Definitionen, Modellkennung aus dem tatsächlichen Truck-Verzeichnis, Herkunft, Pfad, aktuelle Verwendung und Prüfstatus. Nicht aufgelöste Sprachschlüssel werden nicht übersetzt oder durch erfundene Produktnamen ersetzt. Suche und Filter dienen ausschließlich der Anzeige. Der Scan kann abgebrochen werden; beim Wechsel von Spiel, Profil oder Save werden alte Ergebnisse verworfen.

## Archiv- und Definitionserkennung

- HashFS v1 und v2: Verzeichnislisten und normale, unkomprimierte oder zlib-komprimierte Textdateien. Dateioffsets werden auch oberhalb von 4 GB berücksichtigt. Salt und unbekannte/verschlüsselte Flags bleiben ausgeschlossen. Bei v1 werden nur die Verzeichnis-/Kompressionsflags 0..3 unterstützt; die upstream als `Verify`/`Encrypted` benannten Zusatzflags 4/8 bleiben ausgeschlossen. V1 wurde mit synthetischen Archiven geprüft; die hier installierten und gelesenen Spielarchive sind v2.
- Normale ZIP-Dateien (`.scs`/`.zip`) und ausgepackte Ordner, einschließlich tatsächlich vorhandener `base`-/`dlc_*`-Teilpakete. Bedingte Pakete werden inventarisiert, ihre Aktivierung wird nicht behauptet. ZIP64 und mehrteilige, verschlüsselte oder verschleierte Archive werden nicht repariert oder freigegeben.
- Tatsächlich aufgelistete Motor-/Getriebe-`.sii`-Dateien unter `/def/vehicle/truck/<modell>/engine/` bzw. `/transmission/`, bestätigt durch passenden Unit-Typ.
- Begrenzte, quelleninterne Text-Includes. Fehlende quellenübergreifende Includes, Zyklen, unbekannte Syntax und Mehrdeutigkeiten werden nicht als verifizierte Kompatibilität ausgegeben.
- Keine Texturrekonstruktion, GDeflate-Unterstützung, Volltext-Extraktion auf Datenträger oder Änderung von Archiven. Fehlende Verzeichnislisten können die Erkennung begrenzen.
- Grenzen: 4 MiB je Text/Verzeichnisliste, 8 MiB komprimierter Text, 64 MiB Text-/Listenlesebudget pro Quelle, 64 MiB Archivmetadaten, maximal eine Million HashFS-Indexeinträge und 100.000 aufgelistete Pfade. Includes sind auf 16 Ebenen, 128 Dateien und 2 Mi Zeichen begrenzt. Ein Scan inventarisiert höchstens 512 Quellen, 2.048 Dateisystemverzeichnisse, 64.000 Verzeichniseinträge und 30.000 externe Komponentenfunde. Erreichte Grenzen erzeugen Hinweise; die Erkennung kann dann unvollständig sein. Unzulässige virtuelle Pfade und Links/Junctions werden abgelehnt. Fehler einer Quelle entfernen den Save-Katalog nicht.

Mehrfach gefundene Pfade behalten ihre Herkunft. Unterschiedliche Definitionen oder ungeklärte Überschreibreihenfolgen erhalten keine willkürlich gewählte Gewinnerquelle. Ein passender Modellordner wird als **nicht verifiziert** gekennzeichnet; ein anderer Modellordner als nicht passend.

## Warum externe Komponenten noch nicht schreibbar sind

Die SCS-Zubehörregeln können weitere vorhandene Komponenten voraussetzen, ausschließen, ersetzen oder entfernen. Eine Änderung nur am Engine-/Transmission-`data_path` wäre dafür unzureichend. Die aktuelle Architektur kennt außerdem weder die vollständige aktive Mod-Reihenfolge noch DLC-Berechtigungen und die zur Spielversion aktive Workshop-Paketvariante.

Phase D erfordert einen verifizierten Quellenverbund, aufgelöste Includes und Abhängigkeiten, vollständige Prüfung des betroffenen Trucks sowie eine erneute Prüfung unmittelbar vor der bestehenden Save-Transaktion. Bis dahin erzeugt der neue Dienst ausschließlich Lesedaten. Backup, SHA-256, Live-Write-Policy, Steam-`userdata`-Sperren und LicenseHub werden nicht ersetzt.

## Parser- und Lizenzrecherche

Die offiziellen [SCS-Archivwerkzeuge](https://modding.scssoft.com/wiki/Documentation/Tools/Game_Archive_Packer) beschreiben HashFS v2 seit Spielversion 1.50. Die [SII-Spezifikation](https://modding.scssoft.com/wiki/Documentation/Engine/Units) beschreibt Units und Text-Includes; die [Zubehörregeln](https://modding.scssoft.com/wiki/Documentation/Engine/Units/accessory_data), der [Mod-Manager](https://modding.scssoft.com/wiki/Documentation/Engine/Mod_manager) und [Workshop-Paketvarianten](https://modding.scssoft.com/wiki/Documentation/Engine/Units/package_version_info) begründen die verbleibende Kompatibilitätsgrenze.

TruckLib.HashFs und TruckLib.Sii wurden untersucht. Ihre gesamten Bibliotheken stehen unter GPL-2.0 und werden nicht als Abhängigkeit übernommen. Nur die separat ausdrücklich MIT-lizenzierte [CityHash-Datei am geprüften Commit](https://github.com/sk-zk/TruckLib.HashFs/blob/c064f1d37fd75ba9a5f6bbbd1ec6df8a30b142ac/TruckLib.HashFs/CityHash.cs) wird mit erhaltenem Hinweis verwendet. Sie implementiert die von SCS benötigte ältere CityHash-Variante. Der schmale Archivleser ist projektspezifisch; keine neue GPL-Runtime und keine gebündelten proprietären SCS-Werkzeuge. Lizenzhinweise: [`../THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md).

## Feldtest in ETS2 und ATS

1. Je Spiel ein lokales Testprofil bzw. einen kopierten manuellen Save öffnen. Aktuellen Truck, Motor und Getriebe kontrollieren. Die bestehenden Auswahlfelder müssen weiterhin nur die zuvor im Save vorhandenen Komponenten enthalten.
2. Lesekatalog starten. Modell, Namen, aktuelle Markierung, Quellen und tatsächliche Pfade prüfen; nach einem bekannten Motor/Getriebe suchen. DLC-/Mod-Komponenten dürfen keine neue Schreibauswahl ermöglichen.
3. Bekannte lokale und Workshop-Mods prüfen. Bei mehreren Varianten oder überschreibenden Pfaden muss die ungeklärte Priorität sichtbar bleiben. Fehlende bzw. nicht unterstützte Pakete dürfen den übrigen Editor nicht blockieren.
4. Während des Scans Save, Profil und Spiel wechseln; alte Ergebnisse dürfen nicht in den neuen Kontext gelangen. Abbruch und erneuten Scan prüfen.
5. Den bestehenden Wechsel auf eine bereits im Save vorhandene Komponente an einer Testkopie ausführen. Backup und Meldung kontrollieren, anschließend im jeweiligen Spiel laden. Keine extern erkannte, ungeprüfte Komponente schreiben.
6. Kennzeichen sowie die übrigen Editorfunktionen kurz auf Regressionen prüfen. Steam-`userdata` muss weiterhin schreibgeschützt bleiben.

Die vom Nutzer gemeldeten erfolgreichen ETS2-/ATS-Tests von PR #87/#88 betreffen Kennzeichen und Oberfläche. Sie sind keine Feldabnahme dieses neuen Katalogs. Lesetests installierter Spieldateien und synthetische UI-Tests sind ebenfalls keine Ingame-Tests.

## Verifikation

Stand 07.11.2026, Windows x64 / .NET 10:

- Release-Build: erfolgreich, **0 Warnungen / 0 Fehler**.
- Lokaler vollständiger Lauf: **430/433 erfolgreich** (Core 32/32, App 187/187, Infrastructure 211/214). Die drei unveränderten Baseline-Fehler betreffen `SteamGameInstallationDetectorTests`: registriertes echtes ETS2 übersteuert deren synthetische Pfade. Sie bestanden bereits auf dem unveränderten Ausgangsstand. Keine Tests deaktiviert; saubere Windows-CI wird im PR separat ausgewiesen.
- **80 neue Tests**: 34 Archivleser, 25 Discovery/Definitionen und 21 App-Zustands-/Schreibgrenzen. Enthalten sind HashFS v1/v2 jenseits 4 GiB, beschädigte Archive neben gültigen Quellen, falsches Spiel, fehlende DLC-Dateien, Includes/Zyklen, bedingte Pakete, doppelte Units/Pfade, unaufgelöste Priorität und Erhalt der Save-Auswahl.
- Offline-WPF-Render bei 1.320 und 1.080 DIP Breite einschließlich Detailansicht: Suchfeld, alle Tabellenspalten, Pfad und Quellen lesbar; ausschließlich synthetische Daten und keine produktiven Dienste gestartet.

Read-only-Integrationsprobe mit synthetischem leerem Save-Katalog und tatsächlich installierten Quellen:

| Spiel | Vorhandene Spielarchive | Komponenten nach Zusammenführung | Fundquellen mit Komponenten | Laufzeit dieses Rechners |
| --- | ---: | ---: | --- | ---: |
| ETS2 | 115 | 1.252 (460 Motoren / 792 Getriebe) | Spiel, 9 DLC-Archive, 11 lokale Workshop-Pakete | 38,4 s |
| ATS | 61 | 358 (177 Motoren / 181 Getriebe) | 19 DLC-Archive | 6,7 s |

Die ETS2-Inventur enthält auch lokal vorhandene Workshop-Versionsvarianten; sie meldete 518 mehrdeutige und 2 unvollständig auflösbare Pfade. Drei nicht unterstützte/geschützte Workshop-ZIPs wurden mit Hinweis übersprungen. Diese Zahlen belegen Erkennung, **keine** aktive Mod-Konfiguration oder Kompatibilität. Keine realen Saves wurden dafür geöffnet oder verändert. Alle Spielarchive behielten Größe und Änderungszeit; `def.scs` beider Spiele behielt zusätzlich denselben SHA-256.

Separate Archivprobe: ETS2 `def.scs` lieferte 312 Motor-/Getriebetexte, ATS `dlc_kenworth_w900.scs` 25, jeweils ohne Lesefehler und mit unverändertem SHA-256. ATS `def.scs` enthält selbst keine Motor-/Getriebedefinitionen; die Truck-Dateien liegen in separaten Archiven. Die 10,5-GB-ETS2- und 9,5-GB-ATS-`base.scs`-Indizes ließen sich ebenfalls lesen; der Truck-Definitionsbaum fehlt dort erwartungsgemäß. Keine vollständige Hashprüfung der mehrgigabytegroßen Basisarchive durchgeführt.

Die normalen SCS-Komponentennamen können reine `@@...@@`-Sprachschlüssel sein. Ohne verifizierte Locale-Auflösung zeigt der Katalog dann den tatsächlichen Dateinamen. HashFS v1 ist durch synthetische Tests abgedeckt, nicht durch einen realen installierten v1-Datensatz.
