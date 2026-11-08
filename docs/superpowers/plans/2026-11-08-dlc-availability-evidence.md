# DLC availability evidence implementation plan

> **For agentic workers:** Use the existing authorized task scope; implement and review the independent tasks below. No merge is authorized.

**Goal:** Establish which DLC evidence NMC can actually obtain, retain useful manifest identity metadata, and prevent unsupported availability grants.

**Architecture:** The feasibility fallback applies: the current launcher has no legitimate Steamworks client context or authorized ownership server. DLC availability remains unknown. Optional manifest Steam App IDs are immutable diagnostic metadata, never entitlement; existing VFS and transactional write validation remain authoritative.

**Tech Stack:** C#/.NET 10, WPF, xUnit, Windows GitHub Actions and NMC Custom Installer.

**Spec:** User Auftrag 8; technical findings in `docs/DLC_AVAILABILITY_EVIDENCE.md`.

## Global constraints

- Base: complete PR #95, commit `347320f4957963667f6bfc7ce7bfdca6cae809cc`.
- Version 1.0.0; no merges; no writes to real user saves or Steam/game configuration.
- Retain source revalidation, SHA-256, unique truck binding, backup/rollback/restore and fail-closed behavior.
- Missing/zero/positive App IDs, free store pricing, installed archives and historical mounts do not grant availability.
- Publish only sanitized evidence; no local paths, account identifiers, manifest secrets or original saves.

## Review focus

- Invalid/missing/zero App IDs must not imply availability or a free/paid classification.
- A historical or wrong-version mount and Steam depot installation are not current entitlement.
- Unknown higher-priority DLC content must not silently fall back to base content.
- Metadata or consumed sources changed after preview invalidate the session.
- Diagnostic JSON must preserve numeric public identity while retaining existing privacy and size limits.

## Task 1: DLC identity and source regression coverage

**Files:** `src/NmcScsLauncher.Core/ScsEffectiveContent.cs`, `src/NmcScsLauncher.Infrastructure/ScsActiveContentResolver.cs`, resolver/VFS/scenario tests.

**Interface:** Add optional `uint? DlcSteamAppId = null` to `ScsContentLayer`; only a valid positive unquoted `steam_app_id` is retained. Absent/zero values remain null. Malformed metadata is reported without granting availability or exposing raw content.

- [ ] Add ATS/ETS2 tests for public manifest identity and absent/zero/invalid IDs; observe failure for the new metadata behavior.
- [ ] Preserve validated App IDs without changing availability or priority decisions.
- [ ] Add focused negative tests for untrusted installation/mount clues, unknown overlays and source changes; reuse existing transactional safety tests.
- [ ] Run focused tests and inspect failures.

## Task 2: Diagnostic export

**Files:** `src/NmcScsLauncher.App/Services/TruckPowertrainDiagnosticReport.cs`, `tests/NmcScsLauncher.App.Tests/TruckPowertrainDiagnosticsTests.cs`.

- [ ] Add a failing JSON regression for public DLC App ID with availability still false and sensitive metadata absent.
- [ ] Export nullable `dlcSteamAppId` only for a validated DLC manifest; keep current redaction and report limits.
- [ ] Run the diagnostic tests.

## Task 3: Feasibility and verification

**Files:** `docs/DLC_AVAILABILITY_EVIDENCE.md`; private local audit helpers outside Git.

- [ ] Document primary Valve/SCS sources, legitimate integration prerequisites and evidence freshness limits.
- [ ] Repeat the real ATS read-only accessory and powertrain inspection with before/after save hashes.
- [ ] Run Release build and full regression suite; report host-dependent failures explicitly.
- [ ] Review the complete diff, create a dependent PR, and verify Windows CI plus NMC Custom Installer.
- [ ] Report the final option counts, remaining blockers and unchanged version; leave PR unmerged.
