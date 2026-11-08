# Save-Editor – Phase 1: NMC Funktionskarten

Ausgangsbasis: `main` nach PR #97 (Commit `c7d60668f813ab925455d0dd1262e9e646998cf6`).
Öffentlich sichtbare Version: **1.0.0**.

## Ziel

Eigenständiges WPF-Funktionskartenkonzept, inspiriert durch die vom Benutzer gezeigte Navigationsform. **Keine Übernahme** von GPLv3-Code, Bildern, Icons, CSS oder Layoutdateien aus Truck Tools. Die Kacheln nutzen ausschließlich neue NMC-Markierungen und vorhandene WPF-Styles.

## Umfang dieser UI-Etappe

- Neuer Einstieg `Übersicht` mit 11 direkt anklickbaren Funktionskarten; jede Karte hat ein eigenes, lokal eingebettetes, original generiertes Transport-/Werkstatt-/Logistikmotiv (ein 3×4-PNG-Atlas mit 11 unterschiedlichen Ausschnitten).
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
- Profil, Spielstand, Geld, Skills, Truck, Trailer, Kennzeichen und Sicherungen über Karten direkt im passenden modalen Dialog öffnen. Jede Aktion verwendet dieselben bestehenden Befehle/Bestätigungen; beim Schließen bleibt der Save-Status korrekt.
- Keine Motor-/Getriebebearbeitung sichtbar oder aufrufbar.
- Bestehenden Save-Editor-Regressionstest und vollständige Windows-CI, klassischen Installer sowie NMC-Custom-Installer prüfen.
- Kein Merge ohne Freigabe.

## Bildrechte und Datenschutz

Der Bildatlas `src/NmcScsLauncher.App/Assets/SaveEditorPreviews.png` wurde eigens für NMC als originelle stilisierte Illustration erstellt. Keine Truck-Tools-/SCS-/Steam-Grafiken oder GPLv3-Code übernommen. Die Vorschaubilder enthalten keine Nutzerprofile, Save-Daten oder fremde Kontoinformationen. Die 11 Bildausschnitte sind rein dekorativ und führen keine eigenständige Logik aus.
