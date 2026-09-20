# NMC Custom Installer

Der neue **NMC Installer** ersetzt langfristig die sichtbare Inno-Setup-Oberfläche durch eine vollständig gebrandete WPF-Oberfläche.

## Architektur

- **WiX Toolset 5 / Burn** als Bootstrapper- und Installationsengine.
- **.NET 10 WPF** als vollständig eigene Installer-Oberfläche.
- **MSI per-user** als verwaltetes Installationspaket.
- Keine Administratorpflicht für die normale Installation.
- Standardziel: %LOCALAPPDATA%\Programs\NMC SCS LAUNCHER\.
- Eigene Installationspfad-Auswahl.
- Optionale Desktop-Verknüpfung.
- Startmenü-Verknüpfung.
- Install, Repair und Uninstall.
- Silent-/Quiet-Modus für CI und automatisierte Bereitstellung.

## Branding

Die Oberfläche verwendet ausschließlich die freigegebenen Projektassets:

- branding/nmc-scs-launcher-logo.png
- branding/nmc-scs-launcher.ico
- branding/nmc-it-service-logo.png

Damit sind Installer, Launcher, Updater und Firmenbranding visuell konsistent.

## Migration vom bisherigen Inno Setup

Der NMC Installer erkennt die bisherige Inno-Installation über deren Uninstall-Registry-Eintrag.

Bei einer erstmaligen Migration:

1. der bisherige Installationspfad wird als Vorgabe übernommen;
2. der vorhandene Inno-Uninstaller wird vor der neuen WiX/MSI-Installation still ausgeführt;
3. anschließend installiert das neue MSI in den gewählten Pfad.

Nicht berührt werden:

- Launcher-Daten unter %LOCALAPPDATA%\NMC Network\NMC SCS Launcher\;
- LicenseHub-Credentials im Windows Credential Manager;
- vom Benutzer verwaltete Mod-Ordner.

## Build

Voraussetzung ist das .NET 10 SDK. WiX 5 wird als SDK/NuGet-Abhängigkeit der Installer-Projekte aufgelöst.

    .\scripts\build_wix_installer.ps1

Ausgabe:

    artifacts\nmc-installer\NMC-SCS-LAUNCHER-<version>-Setup.exe
    artifacts\nmc-installer\NMC-SCS-LAUNCHER-<version>-Setup.exe.sha256

## Bestehendes Inno Setup

Das bisherige Inno Setup bleibt vorerst als **Fallback** im Repository, bis der neue NMC Installer auf einem realen Windows-System vollständig feldgetestet wurde.

Nach bestätigtem Feldtest kann der neue NMC Installer zum alleinigen Release-Installer werden.

## Code Signing

Der neue Installer ist wie der bisherige Build noch nicht code-signiert. Vor breiter öffentlicher Verteilung sollte ein Windows-Code-Signing-Zertifikat ergänzt werden.
