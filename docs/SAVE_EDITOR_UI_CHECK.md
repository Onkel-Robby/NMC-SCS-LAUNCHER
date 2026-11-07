# Save-Editor: Prüfung der gemeinsamen Eingabe-Styles

## Umfang von Auftrag 2

Die bestehenden Styles in `App.xaml` werden weiterverwendet. TextBox-Padding wurde zuvor zusätzlich als Margin des Inhaltsbereichs angewandt; dadurch waren Eingaben höher und stärker eingerückt als benachbarte Dropdowns. Der zusätzliche Abstand entfällt. Einzeilige Textfelder, Dropdowns und Buttons haben jetzt mindestens 40 geräteunabhängige Pixel (DIP) Höhe; einzeilige Eingabetexte sind vertikal zentriert. Mehrzeilige bzw. umbrochene Felder bleiben oben ausgerichtet. Schreibgeschützte Textfelder verwenden die vorhandenen gedämpften Themefarben und bleiben selektierbar.

Die Tabellenabstände aus PR #85, Beschriftungen, deutschen Status-/Fehlermeldungen, Bestätigungen und Schreibvalidierung werden weiterverwendet. Es gibt keine Änderung an ViewModels, Spielständen, Backups, Cloud-Sperren, LicenseHub oder Version 1.0.0. Die Kennzeichen-Vervollständigung ist ein separates Arbeitspaket; beide PRs ändern keine gemeinsamen Dateien.

## Durchgeführte lokale Layoutprüfung

Die tatsächlichen WPF-Ressourcen und Fenster wurden ohne Produktionsstart/LicenseHub und mit synthetischen Daten in PNGs gerendert. Dies ist eine Windows-Layoutprüfung, kein ETS2-/ATS-Ingame-Test.

| Ansicht | Ergebnis |
| --- | --- |
| Profil/Karriere, Breite 1320 DIP, vor Änderung | Textfelder und danebenliegende Buttons 44,62 DIP; Selektoren 40 DIP; separater Backup-Button 41 DIP. |
| Profil/Karriere, Breite 1320 DIP, nach Änderung | Alle 19 sichtbaren Eingabe-/Auswahl-/Aktions-Controls 40 DIP; einzeilige Texte mittig. |
| Profil/Karriere, Mindestbreite 1080 DIP | Eingaben und Aktionen passen in die Karten; vertikales Scrollen bleibt vorhanden. Der bereits vorhandene lange Hilfetext unter „Profil & Karriere“ wird hier weiterhin abgeschnitten; dessen Umbruch liegt außerhalb dieses Style-Pakets. |
| Modset-Dialog, 650 × 555 DIP Fenstergröße | Formular bleibt scrollbar; Aktionsleiste bleibt erreichbar. Das Beschreibungsfeld behält 70 DIP, obere Ausrichtung und drei eingegebene Zeilen. Die zuvor 34 DIP hohen Einzeiler werden passend zu den Buttons auf 40 DIP angehoben. |

Für diese kleine Ressourcenänderung wurden keine Unit-Tests eingeführt, die lediglich XAML-Werte wiederholen. Release-Build, vorhandene Unit-Tests und Windows-CI/Installer werden zusätzlich geprüft.

## Noch praktisch zu prüfen

Mit dem Installer des betreffenden PR-Commits die folgenden Fälle bei 100 %, 125 % und 150 % Windows-Skalierung durchgehen:

1. ETS2 und ATS auswählen; dieselben Tabs Profil/Karriere, Truck, Trailer und Kennzeichen öffnen. Textfelder, Dropdowns und Buttons müssen lesbar, gleichmäßig eingerückt und bedienbar sein.
2. Mit Tab/Shift+Tab navigieren, Dropdowns mit der Tastatur bedienen, lange Eingaben horizontal scrollen und kopieren. Schreibgeschützte Angaben müssen erkennbar bleiben.
3. Modset-Editor und Duplizieren öffnen, lange Pfade und mehrzeilige Beschreibung prüfen; keine abgeschnittenen Speichern-/Abbrechen-Buttons.
4. Nur in einem gesicherten lokalen Testsave einen gültigen Wert speichern: eindeutige Erfolgsmeldung und Backup prüfen. Eine ungültige Eingabe muss weiterhin abgelehnt werden. Dateien direkt unter Steam `userdata` bleiben schreibgeschützt.

Build/Commit, Windows-Skalierung und Ergebnis pro Spiel dokumentieren. Nicht ausgeführte Feldtests bleiben offen.
