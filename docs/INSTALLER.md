# Windows-Installer

Neben dem portablen ZIP existieren zwei Installerpfade: der neue **NMC Custom Installer** auf WiX 5/Burn mit eigener .NET-10-WPF-Oberfläche sowie das bisherige Inno Setup als vorläufiger Fallback bis zum abgeschlossenen Feldtest.

## Installationsmodell

- Per-User-Installation ohne Administratorpflicht.
- Standardziel: `%LOCALAPPDATA%\Programs\NMC SCS LAUNCHER\`.
- Startmenü-Verknüpfung wird angelegt.
- Desktop-Verknüpfung ist optional.
- Der bestehende self-contained Launcher und `NmcScsLauncher.Updater.exe` werden gemeinsam installiert.
- Launcher-Daten unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\` werden bei einer Deinstallation nicht gelöscht.
- LicenseHub-Credentials im Windows Credential Manager werden durch den Installer nicht verändert.

Die Installation unter `%LOCALAPPDATA%` ist absichtlich gewählt. Der LicenseHub-Updater kann dadurch die installierten Programmdateien im Benutzerkontext austauschen, ohne für normale Updates eine Administrator-Elevation zu benötigen.

## NMC Custom Installer

Der neue Installer besitzt eine vollständig eigene NMC-Oberfläche mit Branding, Installationspfad, optionaler Desktop-Verknüpfung, Fortschrittsanzeige sowie Install/Repair/Uninstall. Die Installationsengine bleibt WiX/MSI-basiert; die UI ist nicht an die festen Inno-Wizard-Flächen gebunden. Details stehen in `docs/NMC_CUSTOM_INSTALLER.md`.

Das bestehende Inno Setup bleibt bis zur realen Windows-Feldverifikation als Fallback verfügbar.

## CI-Artefakte

GitHub Actions erzeugt zusätzlich zum portablen Build:

```text
NMC-SCS-LAUNCHER-1.0.0-Setup.exe
NMC-SCS-LAUNCHER-1.0.0-Setup.exe.sha256
```

Für beide Installerpfade existieren automatisierte Smoke-Tests. Der neue NMC Custom Installer wird still installiert, Launcher/Updater und self-contained Runtime werden geprüft und anschließend über Burn/MSI wieder deinstalliert.

## Signierung

Der Installer ist aktuell nicht code-signiert. Für eine spätere öffentliche Verteilung sollte ein Windows-Code-Signing-Zertifikat ergänzt werden. Die Signierung ist bewusst nicht mit Platzhalter-Credentials im Repository vorbereitet.
