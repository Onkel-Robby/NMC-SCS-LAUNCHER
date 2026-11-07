# Kennzeichen-Auswahl: Implementierungsplan

> Umsetzung mit `superpowers:executing-plans`; unabhängige App- und Core-/Infrastructure-Aufgaben werden parallel bearbeitet.

**Ziel:** Bestehende Kennzeichen korrekt laden, pro Fahrzeug bearbeiten und bei Kontextwechseln keine fremden Eingaben übernehmen.

**Architektur:** Die vorhandene Fahrzeuginspektion liefert zusätzlich die unveränderten Kennzeichen-Scalars. Ein reiner Core-Parser erkennt native Kennzeichen und die vom Launcher erzeugten deutschen und Custom-Formate. Zwei Instanzen desselben Editor-ViewModels halten Truck und Trailer getrennt. Ein wiederverwendetes WPF-Control zeigt diese Zustände. Veraltete asynchrone Leseergebnisse werden verworfen.

**Technik:** C#/.NET 10, WPF/MVVM, xUnit; bestehende Save-Schreibdienste unverändert.

**Spezifikation:** Auftrag 1 aus der Projektübergabe: 36 ETS2-Länder, 21 ATS-Bundesstaaten, Custom-RGB und manuelle Map-Mod-Codes; Profil-/Save-/Spiel-/Fahrzeugwechsel; keine unbeabsichtigten Standardwerte; Windows-Build, Tests, Installer, eigener PR ohne Merge.

## Grenzen

- Version 1.0.0, LicenseHub, Backup, SHA-256, Validierung, Live-Write-Policy und Cloud-Schreibsperren bleiben erhalten.
- Nur synthetische Saves für automatisierte Tests. Ingame-Ergebnisse bleiben externe Feldtests.
- Kein Merge ohne Nutzerfreigabe. Auftrag 2 beginnt nach Fertigstellung dieses PR und erhält ein getrenntes Arbeitspaket ohne dieselben Dateien.

## Prüfschwerpunkte

- Custom wird im Save mit `|belgium` gespeichert; nur erkanntes Custom-Markup darf als Custom geladen werden.
- Deutsche Material-Tags dürfen Ortskürzel und Rest nicht zusammenziehen.
- Unbekannte Codes bleiben sichtbar; fehlende oder unlesbare Angaben erhalten keinen stillen Standard.
- Truck und Trailer können verschiedene Länder und Farben haben.
- Spät eintreffende Antworten eines früher ausgewählten Saves/Profils dürfen den aktuellen Zustand nicht überschreiben.

## Aufgaben

- [x] Core: `ScsLicensePlateValue.Parse(string?)` mit Tests für native, deutsche, Custom-, unbekannte und unlesbare Formate. `ScsPlateRegionCatalogTests` prüft alle Codes und Anzahlen.
- [x] Infrastructure: `ScsActiveVehicleState` um optionale Kennzeichen-Scalars erweitern; Inspektion liest aktiven Truck und erste kennzeichenführende Unit der aktiven Trailer-Kette. Tests prüfen Read-only-Verhalten und bestehende Schreib-/Lese-Rundläufe für ETS2/ATS.
- [x] App: separate `LicensePlateEditorViewModel`-Instanzen, wiederverwendetes Control, bestehende Befehle mit deren Werten verbinden; Reset-/Lade- und Generationenschutz. Neues Windows-App-Testprojekt in Solution, deterministische Regressions- und Bindingtests.
- [x] Dokumentation: README, Architektur, Roadmap und konkrete ETS2-/ATS-Testanleitung an tatsächlichen Stand anpassen.
- [ ] Verifikation: Fehler zuerst reproduzieren, dann fokussierte Tests, vollständiger Release-Build und Tests, unabhängiges Review, PR erstellen, Windows-CI und NMC Custom Installer kontrollieren.
