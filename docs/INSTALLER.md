# Windows-Installer

Der klassische Windows-Installer ist eine zusätzliche Bereitstellungsform neben dem portablen ZIP.

## Installationsmodell

- Per-User-Installation ohne Administratorpflicht.
- Standardziel: `%LOCALAPPDATA%\Programs\NMC SCS LAUNCHER\`.
- Startmenü-Verknüpfung wird angelegt.
- Desktop-Verknüpfung ist optional.
- Der bestehende self-contained Launcher und `NmcScsLauncher.Updater.exe` werden gemeinsam installiert.
- Launcher-Daten unter `%LOCALAPPDATA%\NMC Network\NMC SCS Launcher\` werden bei einer Deinstallation nicht gelöscht.
- LicenseHub-Credentials im Windows Credential Manager werden durch den Installer nicht verändert.

Die Installation unter `%LOCALAPPDATA%` ist absichtlich gewählt. Der LicenseHub-Updater kann dadurch die installierten Programmdateien im Benutzerkontext austauschen, ohne für normale Updates eine Administrator-Elevation zu benötigen.

## CI-Artefakt

GitHub Actions erzeugt zusätzlich zum portablen Build:

```text
NMC-SCS-LAUNCHER-1.0.0-Setup.exe
NMC-SCS-LAUNCHER-1.0.0-Setup.exe.sha256
```

Der CI-Smoke-Test installiert das Setup still in ein temporäres Verzeichnis, prüft Launcher, Updater und self-contained Runtime-Dateien und deinstalliert die Testinstallation anschließend wieder.

## Signierung

Der Installer ist aktuell nicht code-signiert. Für eine spätere öffentliche Verteilung sollte ein Windows-Code-Signing-Zertifikat ergänzt werden. Die Signierung ist bewusst nicht mit Platzhalter-Credentials im Repository vorbereitet.
