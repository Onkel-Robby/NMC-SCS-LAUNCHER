# NMC SCS LAUNCHER – LicenseHub Update Package Contract

This document defines the package consumed by the launcher updater. It is intentionally separate from the future Windows installer.

## Release source

The launcher checks LicenseHub through the verified project update endpoint and uses the short-lived signed `download_endpoint` returned by LicenseHub. The downloaded file is verified against the SHA-256 value supplied by LicenseHub before it is considered installable.

The external updater verifies the same SHA-256 value a second time immediately before replacing application files.

## Required package format

A launcher application update MUST be a ZIP archive.

The ZIP root is the application directory root. It MUST contain at least:

```text
NmcScsLauncher.App.exe
NmcScsLauncher.Updater.exe
NmcScsLauncher.App.dll / dependencies as produced by dotnet publish
...remaining published application files...
```

`NmcScsLauncher.App.exe` MUST exist at the archive root because it is the restart target after a successful update.

The package MUST contain a complete publish output, not only changed files. The updater replaces the application directory as one complete version so obsolete files do not remain accidentally.

## Security requirements

- HTTPS is required by the LicenseHub client.
- The download endpoint must resolve to the configured LicenseHub origin.
- The LicenseHub SHA-256 value is checked while downloading.
- The external updater re-checks SHA-256 immediately before applying the package.
- ZIP entries may not escape the staging directory (`../`, absolute/path traversal attacks are rejected).
- The active application directory is never modified in-place file-by-file.
- The old application directory is renamed to a rollback backup before the staged version becomes active.
- If activation of the staged directory fails, the directory swap is rolled back.
- If launching the updated application fails, the external updater attempts rollback.
- Successful updates retain the previous application directory as a rollback backup for later cleanup.

## Application-data boundary

Update packages only replace the executable application directory. User data remains outside that directory and MUST NOT be included in update ZIPs:

- launcher settings
- modset metadata
- SCS home directories / profiles / mods
- backups
- logs
- downloaded update staging files
- Windows Credential Manager license entry

These remain under the existing per-user storage locations and are not part of an application bundle update.

## LicenseHub release publishing

For an NMC SCS LAUNCHER release, LicenseHub must store the SHA-256 checksum for exactly the ZIP file referenced by the release download source. The checksum must not describe an unpacked executable or another artifact.

The current updater channel is `stable`. Additional channels must be implemented explicitly before they are published.

## Installer boundary

This application-update mechanism is not the Windows installer. The installer remains a later release gate and must not be used to bypass the LicenseHub licensing/update work that precedes it.
