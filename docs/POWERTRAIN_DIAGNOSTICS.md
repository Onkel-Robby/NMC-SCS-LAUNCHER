# Auftrag 5: Quellenfehler und Einbaufreigabe

Ergänzende Untersuchung: [ATS-DLCs, Profilmods und Save-Provenienz](ATS_DLC_PROVENANCE.md). Der vom Nutzer genannte Profilname ist inzwischen direkt geprüft; der aktuelle Ingame-Save bleibt unbestätigt.

Version 1.0.0. Dieser Auftrag baut auf D3 auf; die bisherigen Pull Requests bleiben unverändert und ungemergt. Die Diagnosevorschau autorisiert keinen Schreibvorgang. Beim Einbau werden Save, Truck, Quellen und Kombination weiterhin frisch geprüft.

## Warum der Katalog mehr findet als der Truck-Editor

Der Lesekatalog inventarisiert auch inaktive und unbestätigte Quellen. Seine Fundzahl ist keine Zahl installierbarer Teile. Die Truck-Auswahl beginnt mit den beiden im Save gespeicherten Pfaden. Solange die aktive Konfiguration ungeklärt ist, werden keine weiteren Definitionen gelesen. Der bisherige Text „Die wirksame Definition ist nicht bestätigt“ verdeckte, dass die Komponentenprüfung gar nicht begonnen hatte.

| Stufe | Nachweis | Verhalten bei fehlendem Nachweis |
| --- | --- | --- |
| 1. Archiv gefunden | Datei und lesbares, vollständiges Inhaltsverzeichnis | Eine aktive unlesbare Quelle sperrt die Konfiguration. |
| 2. Aktivierung | Profil-/Save-Identität, Reihenfolge, Installation, Paketvariante | Nur gespeicherte Komponenten; weitere Prüfung nicht begonnen. |
| 3. Wirksame Definition | Priorität, Verfügbarkeit, lesbarer Inhalt und Includes | Konkrete Quelle und virtueller Fehlerpfad werden angezeigt. |
| 4. Truck-Modell | Definition im Verzeichnis des aktuellen Modells, erwarteter Zubehörtyp | Kein bestätigter Kandidat für diesen Truck. |
| 5. Zubehörregeln | Gesamtes erhaltenes Zubehör und Abhängigkeiten | Unbekannte Regeln, Hookups und notwendige Folgeumbauten sperren. |
| 6. Kombination | Gemeinsam passender Motor und Getriebe, bekannte Steuerungsbereiche | Einzelne gültige Definition ist noch keine Paarfreigabe. |
| 7. Einbau | Frischer Save-/Quellennachweis und geschützter Schreibablauf | Keine Änderung bei veraltetem Kontext oder fehlendem Nachweis. |

## Nachgewiesene lokale Ursachen

Die Untersuchung erfolgte lesend. Verschlüsselte Metadaten wurden ausschließlich in getrennten temporären Kopien entschlüsselt. Private Inventare, Logdateien, Saves und Spielassets werden nicht eingecheckt.

**ETS2:** Vier aktive Pakete scheiterten zuvor an unterschiedlichen Stellen. Drei davon enthalten nachweislich lesbare Inhalte, die durch zu strenge Regeln abgewiesen wurden:

- Ein lokales Paket ohne `manifest.sii`: Dieses Manifest ist bei lokalen Mods optional. Fehlende Metadaten erzeugen jetzt keine erfundene Paketversion und keine automatische Sperre. Ein vorhandenes, aber ungültiges oder uneindeutiges Manifest bleibt ein Fehler; Workshop-Pakete benötigen weiterhin ihr Manifest.
- Ein gleichnamiges lokales `.scs`-/`.zip`-Paar: Für genau ein solches Paar wird die `.scs` verwendet, wie im untersuchten Spiel-Mount-Log belegt. Weitere Archiv-/Ordnerduplikate bleiben mehrdeutig. Die ausgewählte `.scs` enthält außerdem einen gültigen CP437-Dateinamen; UTF8 wird nur bei gesetztem ZIP-UTF8-Bit erzwungen. Definitionstexte bleiben strikt UTF8.
- Ein Workshop-ZIP mit einer exakt bestimmten 14-Byte-Endmarkierung: Nur diese begrenzte Variante wird nach vollständiger Übereinstimmung von Zentralverzeichnis und lokalen Headern akzeptiert. Dateibereiche, Methodik, Flags, Namen und Größen müssen konsistent sein. Gelesene Nutzdaten erhalten eine CRC-Prüfung. Beliebige angehängte Bytes, manipulierte Endmarkierungen, verschlüsselte Einträge und widersprüchliche Verzeichnisse bleiben gesperrt.

Das vierte aktive Archiv hat eine nicht unterstützte AEM-Struktur mit widersprüchlichen Index-/Headerangaben. Zusätzlich erkennt die vollständige ZIP-Prüfung ein zuvor toleriertes Paket mit widersprüchlichen Verschlüsselungsflags, Kompressionsmethoden und CRC-Angaben in allen 347 Einträgen. Ein erfolgreicher Textzugriff des bisherigen Lesers beweist hier keine konsistente Archivstruktur. Beide Pakete bleiben globale Sperren; ihr Titel oder scheinbarer Inhaltsindex beweist keine Irrelevanz für den Truck. Die frische Gesamtprüfung ergibt daher **zwei statt vier Sperren**: drei frühere Ursachen sind behoben, eine weitere wird jetzt erkannt. Die unveränderte reale Konfiguration erhält weiterhin keine zusätzliche Einbaufreigabe.

**ATS:** Die 13 aktiven Workshop-IDs wurden korrekt gelesen, einschließlich IDs oberhalb von `Int32.MaxValue`. In der erkannten und konfigurierten Steam-Bibliothek fehlt der ATS-Workshop-Inhaltsordner samt zugehörigem Workshop-Inventar. Identität und Reihenfolge in Profil und Save stimmen überein. Die Versionsauswahl wird mangels Paketdateien gar nicht erreicht. Das ist ein tatsächlicher fehlender Downloadbestand, kein durch Raten lösbarer Versionsfehler. Meldungen unterscheiden jetzt fehlendes Paket, mehrere Fundorte, falsches Spiel und fehlende/mehrdeutige Versionsvariante.

Das gilt für die untersuchten lokalen Kontexte; ein später geänderter Downloadbestand oder ein anderes Profil muss neu eingelesen werden. Die Anwendung verändert oder ersetzt keine Mods.

## Welche Freigaben möglich sind

Vollständig bestätigte Basisdefinitionen und eindeutig aktive Mod-Definitionen erscheinen als weitere Kandidaten. Dazu gehören nun auch Definitionen aus den oben unterstützten lokalen/ZIP-Fällen. Ihre tatsächliche Paarfreigabe hängt weiterhin vom gesamten Truck ab. Reproduzierbare synthetische ETS2-/ATS-Szenarien prüfen Kandidatenanzahl und Paarstatus getrennt.

Ein erneuter Lese-/Schreibtest mit der vollständigen Original-Händlerkonfiguration eines Volvo FH16 in einem ausschließlich synthetischen Save bestätigt 8 Motor- und 8 Getriebedefinitionen. Zwei Paare sind für dessen 44 Zubehörinstanzen freigegeben. Der Wechsel von `12_speed_ret_over.sii` auf `12_speed_ret.sii` mit `d16g750.sii` schreibt nur die erwarteten Pfade, erzeugt genau ein bytegleiches Backup und lässt sich bytegenau zurücksetzen. Die Originalarchive bleiben unverändert. Andere Originalmotoren verlangen unter anderem zusätzliche Emblemänderungen; andere Getriebe ändern Schalt-/Retarderbereiche und bleiben für diese Konfiguration gesperrt. Dieses Ergebnis ist keine Freigabe für einen anderen oder den realen ausgewählten Truck.

Ein vollständig lesbares, unsicher verfügbares DLC ohne relevante Überschreibung darf die bestätigte Konfiguration nicht pauschal sperren. Ein höher priorisierter bestätigter Inhalt kann eine darunterliegende unsichere Definition eindeutig überdecken. Jede relevante Definition, jeder Include und das erhaltene Zubehör werden weiterhin geprüft. Ein unlesbarer Gesamtindex bietet diesen Nachweis nicht. Installierte DLC-Dateien, kostenlose Paketnamen oder historische Save-Abhängigkeiten ersetzen keinen Verfügbarkeitsnachweis.

Weitere Sperren bleiben: nicht unterstützte Spielversionen, fehlende Includes, gleichrangige unterschiedliche Definitionen, ungeklärte DLC-Aktivierung, Zubehörabhängigkeiten, Hookups, elektrische/andere nicht bestätigte Antriebe und Änderungen der gespeicherten Schalt-/Bremssteuerungsbereiche. Konkrete Meldungen stehen beim Paket, der Komponente oder dem gewählten Paar.

## Diagnose in der Oberfläche

Ein hervorgehobener Prüfstatus trennt eine globale Sperre von fehlgeschlagenen Komponentenprüfungen. Bei übersprungener Prüfung sind die beiden gespeicherten Einträge ausdrücklich „Noch nicht geprüft“. Fehler erhalten Stufe, Bereich, Paketkennung, Version, Quellpfad und betroffenen virtuellen Pfad, soweit bekannt. Bei fehlenden Includes bleibt zusätzlich die Herkunft der verweisenden Definition erhalten. Vollständige lokale Pfade stehen in aufklappbaren technischen Details. Ein späterer Lesefehler verliert die bereits ermittelte Spielversion nicht mehr und erfindet keine zusätzliche Schema-Inkompatibilität. Scheitert erst die abschließende Save-/Quellenprüfung, wird das zuvor geprüfte Ergebnis ausdrücklich verworfen; es gilt nicht fälschlich als „nicht begonnen“ und erteilt keine Freigabe.

„Diagnose lokal speichern“ öffnet einen Dateidialog und schreibt ausschließlich nach ausdrücklicher Auswahl eine lokale JSON-Datei. Der Bericht enthält begrenzte, ausgewählte Statusfelder, geprüfte Paketangaben und Quellen-Aliase. Er enthält keine Save-/Profilinhalte, kompletten Dateiinventare, Lizenzdaten, Rohmeldungen, Exception-Dumps oder vollständigen lokalen Pfade. Bei Kontextwechsel wird ein laufender Export verworfen. Es gibt keinen automatischen Upload. Der Bericht kann gekürzt sein; Anzahl und Kürzungsstatus bleiben sichtbar.

## Prüfung und Feldabnahme

Automatisierte Szenarien verwenden selbst erzeugte Definitionen und Saves. Sie decken ETS2/ATS ohne Mods, ETS2 mit lokaler und Workshop-Priorität sowie ATS mit aktiven, fehlenden und mehrdeutigen Workshop-Paketen ab. Regressionen prüfen ZIP-Integrität, Paketidentität, Versionsauswahl, diagnostische Herkunft, Exportbegrenzung und Kontextwechsel. Die bisherigen D3-Tests für Backup, SHA-256, atomaren Austausch, Rollback, Save-/Truck-Identität, Quellenänderungen, Steam-userdata und laufende Spiele bleiben Bestandteil der vollständigen Suite.

Für die Windows-Feldabnahme:

1. Vorgesehenes Profil und Modset auswählen, manuelle Save-Kopie laden, Truck-Prüfung aktualisieren. Paketgründe und lokale technische Details mit dem tatsächlichen Bestand vergleichen.
2. Fehlende/ungeklärte Mods im Spiel bzw. Steam selbst korrigieren; keine Mod-Abhängigkeiten aus Save-Dateien entfernen. Danach Profil und Save mit übereinstimmender aktiver Reihenfolge verwenden.
3. Nach bestätigter Konfiguration Kandidaten, Quellen und gewähltes Paar prüfen. Ungeklärte DLCs, Hookups oder Folgeumbauten müssen weiterhin eine begründete Sperre erzeugen.
4. Beide Spiele beenden, einen freigegebenen Wechsel an einer manuellen Testkopie ausführen. Genau ein Backup und Wiederherstellung prüfen; anschließend den Truck im Spiel einschließlich Schalten, Retarder und Motorbremse prüfen.

Ein erfolgreicher synthetischer Schreibtest ersetzt diese Ingame-Abnahme nicht. LicenseHub und öffentliche Version bleiben unverändert.

## Formatbelege

- [SCS Mod Manager: optionale Metadaten lokaler Mods](https://modding.scssoft.com/wiki/Documentation/Engine/Mod_manager)
- [SCS Workshop: Paketvarianten und erforderliche Metadaten](https://modding.scssoft.com/wiki/Tutorials/SCS_Workshop_Uploader/How_to_upload_new_mod%3F)
- [PKWARE ZIP APPNOTE, insbesondere Appendix D: CP437/UTF8 für Namen](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT)
- [D1–D3: Quellen- und Transaktionsgrenzen](POWERTRAIN_INSTALLATION.md)
