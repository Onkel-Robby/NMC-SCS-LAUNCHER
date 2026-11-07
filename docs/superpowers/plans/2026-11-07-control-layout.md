# Save-Editor: gemeinsamer Control-Layout-Plan

**Ziel:** Einheitliche Eingabehöhen und korrekte Innenabstände im bestehenden Dark-Theme.

**Spezifikation:** Auftrag 2 der Projektübergabe: vorhandene Ressourcen wiederverwenden, ein begrenztes UI-Paket umsetzen, deutsche Bedienung/Status und bestehende Validierungs-/Schreiblogik erhalten; eigener PR, kein Merge ohne Freigabe.

**Befund:** Offline unter Windows gerendert: Profil-/Skill-Textfelder mit `Padding=8,6` messen 44,62 DIP, zugehörige Buttons werden mitgestreckt; Selektoren messen 40 DIP, Backup-Button 41 DIP. Der `PART_ContentHost` übernimmt die TextBox-Paddingwerte zusätzlich als Margin. Damit wird der Innenabstand doppelt angewandt.

**Ansatz:** Ausschließlich die bestehenden impliziten Styles in `App.xaml` korrigieren: ContentHost ohne Zusatzmargin, TextBox/ComboBox mindestens 40 DIP, einzeilige TextBox-Inhalte vertikal mittig, Buttons mit 16/9 DIP Padding. Mehrzeilige oder umbrochene Textfelder behalten obere Ausrichtung. Schreibgeschützte Textfelder erhalten die vorhandenen gedämpften Themefarben.

**Abgrenzung:** Die Tabellen-Styles aus PR #85 bleiben bestehen. Hauptfenster, ViewModels, Saves, LicenseHub, Version, Texte und Befehle ändern sich nicht. Diese Datei, `App.xaml` und die eigene UI-Prüfdokumentation überschneiden sich nicht mit PR #87.

- [x] Bestehende Styles und alle Dialognutzungen prüfen.
- [x] Gemeinsame Styles korrigieren, ohne zusätzliche Designs einzuführen.
- [x] Windows-Rendervergleich und gemessene Control-Höhen prüfen; schmalen Save-Editor, Modset-Dialog und mehrzeilige Eingabe einbeziehen.
- [ ] Release-Build und bestehende Tests ausführen, Diff unabhängig prüfen, eigenen PR erstellen und Windows-CI/Custom Installer kontrollieren.

Keine neuen Unit-Tests für reine Stylewerte; die tatsächliche WPF-Layoutmessung und Sichtprüfung dienen als gezielte Verifikation. Reale Windows-DPI-/Tastatur-/ETS2-/ATS-Feldtests bleiben getrennt zu dokumentieren.
