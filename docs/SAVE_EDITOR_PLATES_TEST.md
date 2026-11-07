# Kennzeichen-Auswahl: Windows-Feldtest

Diese Anleitung gilt für ETS2 und ATS. Automatisierte Tests prüfen synthetische Saves, Zustandswechsel und WPF-Bindings. Sie bestätigen keine Darstellung oder Funktionsfähigkeit in einem echten Spiel.

## Vorbereitung

1. Den Installer des zu prüfenden PR-Commits aus dem erfolgreichen Workflow **NMC Custom Installer** verwenden. Build/Commit im Testprotokoll festhalten; die öffentliche Version bleibt **1.0.0**.
2. Mit einem gesicherten lokalen Testprofil einen manuellen Save anlegen. Für den ersten Durchlauf das Spiel beenden. Einen aktiven eigenen Truck und Trailer mit unterschiedlichen vorhandenen Kennzeichen verwenden.
3. Launcher starten, Spiel, Profil und Save auswählen, dann **Kennzeichen** öffnen. Gelesenen Wert und bearbeitbare Eingaben für Truck und Trailer vergleichen.

## Prüfungen je Spiel

| Schritt | Erwartung |
| --- | --- |
| Vorhandenes natives Kennzeichen laden | Text und vorhandenes Land bzw. Bundesstaat erscheinen; keine Ersetzung durch Deutschland/Kalifornien. |
| ETS2: Deutschland und z. B. Großbritannien auswählen | `germany` bzw. `uk` wird verwendet. Deutscher Testtext: `SI AA 998`; britischer Testtext: `YN16 MSO`. |
| ATS: z. B. Kalifornien und Texas auswählen | `california` bzw. `texas` wird verwendet. Testtext: `NMC-42`. Nur im Spiel vorhandene Regionen praktisch prüfen. |
| Truck und Trailer unterschiedlich bearbeiten | Jeweils eigener Text, eigene Region und eigene Farben; Truck setzen verändert keine Trailer-Eingabe und umgekehrt. Trailer setzen betrifft weiterhin die aktive Trailer-/Slave-Kette. |
| **Eigene Farben** wählen | Hintergrund `12abef`, Text `345678`, Kennzeichen `NMC-42` setzen; erneut laden: Custom und beide Farben bleiben erhalten. |
| **Anderer … / Map-Mod** wählen | Einen tatsächlich installierten Regionscode einsetzen; nach Speichern/Neuladen bleibt dieser Code erhalten. Unbekannter Code wird nicht stillschweigend ersetzt. |
| Unlesbares oder abweichendes Markup | Rohwert bleibt erkennbar; bearbeitbarer Text wird nicht geraten. Vor einer Änderung Text und gewünschte Region ausdrücklich eingeben. |
| Anderen Save / anderes Profil wählen | Nur dessen Kennzeichen erscheinen; alte ungespeicherte Entwürfe und manuelle Codes verschwinden. |
| ETS2 → ATS → ETS2 wechseln | Passender Katalog; bis zum Laden keine Kennzeichen aus dem vorherigen Spiel. Auch bei schnellem Wechsel keine spät auftauchenden alten Daten. |
| Anderes Fahrzeug nur in der Liste markieren | Kennzeichenfelder beziehen sich weiterhin auf das aktive Fahrzeug. Erst **Wechseln** und erfolgreicher Reload ändern den bearbeiteten Truck/Trailer. |
| Ungültige Eingaben: `<`, leerer Code, RGB `zzzzzz` | Kein erfolgreicher Schreibvorgang; verständliche Fehlermeldung und unveränderte Save-Datei. |

Nach einer bestätigten Änderung den ausgewählten Save im Spiel neu laden und Kennzeichen am Fahrzeug prüfen. Danach das letzte NMC-Backup wiederherstellen und den Ausgangszustand kontrollieren. Für Live-Bearbeitung separat einen lokalen manuellen Save/Quicksave bei laufendem Spiel prüfen; gleichzeitiges Speichern durch das Spiel muss einen veralteten Stand ablehnen. Autosaves, unbekannte Save-Typen und Profilumbenennung bleiben während des Spiels gesperrt. Dateien direkt unter `Steam/userdata` müssen auch für Restore schreibgeschützt bleiben.

## Protokoll

Pro Spiel Build/Commit, Spielversion, verwendete DLCs/Map-Mods, Save-Typ, Kennzeichencode, Ergebnis nach Reload und Ergebnis nach Restore notieren. Nicht ausgeführte Fälle als **offen** markieren; Screenshots ohne Lizenzschlüssel oder private Profilpfade beifügen.
