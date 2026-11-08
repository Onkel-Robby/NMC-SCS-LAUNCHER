# Save-Editor – Phase 1: NMC Funktionskarten

Ausgangsbasis: `main` nach PR #97 (Commit `c7d60668f813ab925455d0dd1262e9e646998cf6`).
Öffentlich sichtbare Version: **1.0.0**.

## Ziel

Eigenständiges WPF-Funktionskartenkonzept, inspiriert durch die vom Benutzer gezeigte Navigationsform. **Keine Übernahme** von GPLv3-Code, Bildern, Icons, CSS oder Layoutdateien aus Truck Tools. Die Kacheln nutzen ausschließlich neue NMC-Markierungen und vorhandene WPF-Styles.

## Umfang dieser UI-Etappe

- Elf direkt anklickbare Funktionskarten in den fünf Fachkategorien; jede Karte hat ein eigenes, lokal eingebettetes, original generiertes Transport-/Werkstatt-/Logistikmotiv (ein 3×4-PNG-Atlas mit 11 unterschiedlichen Ausschnitten).
- Drei gleich breite WPF-Kartenspalten mit größerer Bild-/Symbolfläche, besser lesbaren Texten und begrenzter Gesamtbreite. Die einzelnen Karten besitzen keine fixe Breite und bleiben bei der vorgegebenen Fenster-Mindestbreite 1080 DIP verwendbar. Kategorien: Profil, Karriere, LKW, Anhänger, Fahrzeuge und Sicherheit.
- Direkte modale Bearbeitungsfenster für Geld, XP, Karriere-Skills, Truck-Wechsel/Reparatur/Kraftstoff/Kilometerstand, Trailer-Wechsel/Reparatur/Frachtgewicht, Kennzeichen und Restore; alle Buttons sind ausschließlich an die **bestehenden** Befehle gebunden. Kein verändertes SII-Mapping.
- Innerhalb des Save-Editors vorhandene Register weiter zugänglich, bestehende Profil- und Spielstand-Auswahl bleibt bestehen.
- Wiederherstellung des letzten geprüften Backups hat ein eigenes Register `Sicherungen`, ruft aber dasselbe vorhandene Restore-Kommando auf.
- Motor-/Getriebe-Bereiche und Komponentenkatalog bleiben unverändert durch den zentralen Feature-Schalter deaktiviert.

## Sicherheit und Grenzen

Die UI stellt **keine** unmittelbaren Schreibaktionen bereit: Karten navigieren lediglich zu bestehenden Editoren. Sämtliche bestehenden Validierungen, Bestätigungsdialoge, SHA-256-Prüfungen, Backups, atomaren Writes, Steam-Cloud-Sperren, LicenseHub sowie Installer bleiben unverändert.

Die ersten elf Bearbeitungsaktionen besitzen jetzt eigenständige Dialoginhalte. Die bisherigen vollständigen Bearbeitungsregister bleiben als Rückfallmöglichkeit bestehen; weitere funktionale Module, aufwändige Bild-Animationen und ein Umbau der globalen Seitennavigation folgen gegebenenfalls separat. Diese Aufgaben erfordern eine separate, visuell abgenommene Folgephase. Bilder und Code aus dem GPLv3-Projekt werden nicht kopiert.

## Abnahme

- Auf realen Windows-Rechnern bei 1080 und 1320 DIP Fensterbreite sowie maximiert prüfen: genau drei gleichmäßige Kartenspalten, gut lesbare Texte, keine horizontale Scrollbar oder überlaufenden Inhalte. Bei besonders hoher Anzeigeskalierung gesondert nachprüfen.
- Profil und Spielstand wie bisher über die Kopfzeile auswählen; anschließend Geld, Skills, Truck, Trailer, Kennzeichen und Sicherungen in den passenden Fachreitern über Karten direkt im jeweiligen modalen Dialog öffnen. Jede Aktion verwendet dieselben bestehenden Befehle/Bestätigungen; beim Schließen bleibt der Save-Status korrekt.
- Keine Motor-/Getriebebearbeitung sichtbar oder aufrufbar.
- Bestehenden Save-Editor-Regressionstest und vollständige Windows-CI, klassischen Installer sowie NMC-Custom-Installer prüfen.
- Kein Merge ohne Freigabe.

## Bildrechte und Datenschutz

Der Bildatlas `src/NmcScsLauncher.App/Assets/SaveEditorPreviews.png` wurde eigens für NMC als originelle stilisierte Illustration erstellt. Keine Truck-Tools-/SCS-/Steam-Grafiken oder GPLv3-Code übernommen. Die Vorschaubilder enthalten keine Nutzerprofile, Save-Daten oder fremde Kontoinformationen. Die 11 Bildausschnitte sind rein dekorativ und führen keine eigenständige Logik aus.

## UI-Feinschliff nach der Feldabnahme (08.11.2026)

- **Dunkle Eingabefelder:** Der explizite `EditorField`-Stil erbt nun den globalen NMC-TextBox-Stil (inkl. dunklem ControlTemplate, Fokusrahmen und Disabled-Zustand); das versehentliche Zurückfallen auf weiße Windows-TextBoxen wird per Windows-STA-Test geprüft.
- **Dunkle native Titelleisten:** `WindowsTitleBarTheme` aktiviert nach `SourceInitialized` die dokumentierten DWM-Attribute für das Hauptfenster und die Save-Editor-Dialoge. Die Windows-Fensterfunktionen bleiben unangetastet; nicht unterstützte Systeme nutzen weiterhin die native Standardtitelleiste.
- **Berechenbare Dialoggröße:** `SizeToContent=Manual`, mit einer an den verfügbaren Windows-Arbeitsbereich angepassten Höhe, nach Bedarf verschieb- und skalierbar. Der Formularinhalt befindet sich in der einzig scrollbaren mittleren Grid-Zeile; der Status und **Schließen** bleiben in der festen unteren Zeile.
- **Skills:** `Skills übernehmen` sitzt im dauerhaft sichtbaren Abschlussbereich, nicht unterhalb der letzten Eingabefelder. In dieser UI-Phase wird **keine** fachliche Speicher-, Prüf- oder Backup-Logik verändert.
- **Regression:** Der WPF-Test prüft an tatsächlich instanziierten Dialogen dunkle TextBox-Farben, Scrollbereich, Skills-Button und Höhenbegrenzung; der vorherige Pack-URI-Absturztest bleibt erhalten.

Manuelle Abnahme: normale und lange Dialoge mit 100 %/125 % Windows-Skalierung sowie unterschiedlichen Fenster- und Bildschirmhöhen kontrollieren. Die Windows-CI verifiziert Implementierung und Laufzeitressourcen, aber keine vollständige Darstellung auf dem Anwenderbildschirm.

## Kategorie-Navigation – Funktionskarten an die richtige Stelle

Die Kategoriezuordnung wurde nach der Abnahme von PR #98 auf die vorhandenen Save-Editor-Bereiche umgestellt:

| Kategorie | Funktionskarten |
|---|---|
| Profil & Karriere | Geld verwalten, Erfahrungspunkte, Fähigkeiten |
| Truck | Truck wechseln, Truck reparieren, Kraftstoff, Kilometerstand |
| Trailer | Trailer verwalten, Frachtgewicht |
| Kennzeichen | Kennzeichen (Truck und Trailer im vorhandenen Dialog) |
| Sicherungen | Backup wiederherstellen |

Der überflüssige Reiter **Übersicht** wurde auf Nutzerwunsch vollständig entfernt. Der Save-Editor beginnt direkt im Reiter **Profil & Karriere**. Über die sichtbaren Reiter **Truck**, **Trailer**, **Kennzeichen** und **Sicherungen** wird unmittelbar in die jeweilige Kategorie gewechselt. Jede Kategorie zeigt ihre zugewiesenen 3-Spalten-Funktionskarten; `Öffnen` startet weiterhin die bestehenden modalen NMC-Dialoge.

Die bisherigen Eingabemasken sind pro Bereich unter **Erweiterte Bearbeitung (bisheriger Editor)** weiter zugänglich. Das aktuelle Tab-Index-Schema ist **0 Profil & Karriere**, **1 Truck**, **2 deaktivierter Komponentenkatalog**, **3 Trailer**, **4 Kennzeichen**, **5 Sicherungen**. Im Hintergrund wird der Katalogindex für das deaktivierte Motor-/Getriebemodul beibehalten. Insbesondere bleibt die Motor-/Getriebe-Sperre aktiv.

Änderungsumfang: Präsentation, Kachelsortierung und Navigation. **Keine** neuen Save-Schreiboperationen, geänderten Datenmodelle, Lizenzprüfungen oder Update-Funktionen. Die WPF-Strukturtests prüfen die einmalige Zuordnung aller elf Karten und die bestehende Feature-Flag-Isolation.

### Vereinfachung auf Nutzerwunsch

Die zwischenzeitlich geplante Kategorieauswahl in der **Übersicht** entfällt. Es gibt nur noch fünf sichtbare Fachreiter. Die ViewModel-Daten für die überflüssigen Übersichtskarten wurden entfernt; die elf Bearbeitungskarten, ihre Bildausschnitte, die sicheren Dialoge und das Expander-Fallback bleiben identisch. UI-Regressionen prüfen den ersten Reiter, sämtliche Indizes sowie die unveränderte Feature-Sperre.
