# Powertrain diagnostics and source resolution Implementation Plan

> **For agentic workers:** Use subagent-driven development for independent resolver, archive and UI tasks; root owns the diagnostic contracts, rule/service integration and final verification. No merge is authorized.

**Goal:** Explain why the selected truck has no verified alternatives and correct proven source-resolution defects without weakening installation guards.

**Architecture:** Keep the D3 source/compatibility/transaction boundary. Extend detached evidence with typed failures and explicit “not attempted” state, fix narrowly demonstrated local package/ZIP cases, and expose a bounded local diagnostic export. Unreadable active sources still block the configuration unless their irrelevance is proved.

**Tech Stack:** Existing C#/.NET10 WPF application, archive readers, pinned decoder and save pipeline. Version1.0.0.

**Spec:** User's Entwicklungsauftrag5, 07.11.2026. GitHub PR90→91→92 verified open before changes; base D3 `e39882c0a5d75aefe266124806933deae819c628`. New dependent branch `feat/powertrain-diagnostics-and-resolution` targets PR92's branch.

## Global constraints

- No changes to real saves, mod packages or Steam data. Decoder runs only on unique copies; automated writes use synthetic saves.
- Preserve backup, SHA-256, atomic replace, rollback, fresh source checks, identity binding, game-stopped and Steam-userdata guards.
- Archive discovery does not establish activation, availability or compatibility. Never ignore unknown source contents.
- Do not upload private inventories, saves, logs, full archives, profile/account IDs or secrets. Diagnostic export is local and uses selected fields and root aliases.
- Keep earlier PRs unchanged; one dependent PR for this task, no automatic merge.

## Evidence and approach

The catalog inventories broad sources independently. Truck rules start with current paths and intentionally skip all definition loading when `ConfigurationVerified` is false. Generic per-row reasons currently hide that no component test occurred. A later service failure also loses the known game version.

Read-only forensic checks distinguish genuine absent ATS Workshop files from stricter-than-game ETS2 handling of optional local manifests, a `.scs`/`.zip` duplicate, legacy ZIP filename encoding and a precise duplicate end-record suffix. An obfuscated archive with inconsistent headers remains unreadable; its claimed index cannot prove irrelevance.

Choose narrow fixes plus structured diagnostics. A permissive ZIP fallback or merging broad catalog entries into the writable list would manufacture proof; a wholesale replacement of the source pipeline is unnecessary. These are implementation choices within the user's explicit authorization to proceed autonomously.

## Review focus

1. Absent files versus duplicate candidates and wrong game/library: give precise causes, retain ambiguity when precedence is unproved.
2. Legacy ZIP names versus definition text: CP437 applies only to non-UTF8 names; strict text decoding, bounds, completeness and CRC remain enforced.
3. Global failure before enumeration versus failed individual definitions: represent “not attempted” and preserve the actual source/version/context.
4. Unknown source with an apparently unrelated name: never treat a corrupt index or title as a complete content inventory.
5. Local report with user paths or arbitrary exception values: alias roots, whitelist fields and exclude raw metadata/exception dumps.

## Task1 – Local and Workshop package resolution

Files: `ScsActiveContentResolver.cs`, resolver regression tests.

- [x] Reproduce optional local manifest and `.scs`/`.zip` selection failures with synthetic fixtures.
- [x] Permit absent local metadata without inventing a package version; still reject malformed metadata and missing required Workshop manifests.
- [x] Implement only the demonstrated unique `.scs` over same-stem `.zip` case; retain other ambiguous layouts and revalidate selection changes.
- [x] Record typed package failures with name/identity, candidate root/path, selected version and specific missing/duplicate/version reasons. Verify high Workshop IDs and missing-root cases.

## Task2 – Strict ZIP compatibility

Files: `ScsDefinitionSource.cs`, archive regression tests.

- [x] Reproduce non-UTF8 names and the exact 14-byte duplicate end-record suffix.
- [x] Support standard CP437 names when bit11 is unset, UTF8 names when set; retain strict UTF8 content and verify requested payload CRC.
- [x] Accept the exact bounded suffix only after complete index/local-header agreement; reject altered suffixes, encrypted/ambiguous/inconsistent indexes and unknown formats.
- [x] Recheck actual sources read-only; retain original hashes/metadata and keep proprietary bytes outside the repository.

## Task3 – Diagnostic evidence, UI and local export

Files: new Core diagnostic contracts; `ScsEffectiveContentService.cs`, `ScsTruckPowertrainRules.cs`, `ScsTruckPowertrainService.cs`; Truck view/viewmodel; local export service and tests.

Interfaces: `ScsPowertrainDiagnostic` carries stable code, stage, scope, user message and optional local package/source/version/virtual-path context. `ScsActiveContentEvidence.DiagnosticDetails`, option/assessment diagnostic details and `IScsTruckPowertrainPreview.InspectionReport` preserve typed context. The report records configuration validity and whether definition checks were attempted; it never authorizes installation.

- [x] Preserve global/package/component/pair failures and known version through service fallback; no invented version failure after unrelated I/O error.
- [x] Display global blockers prominently and component reasons on their rows; explain why only current saved entries are visible when enumeration was skipped.
- [x] Add explicit local export with selected fields, root aliases and no raw save/profile/evidence/config/log/exception contents. Verify redaction and cancellation/stale preview behavior.

## Task4 – Scenario/regression validation and delivery

- [x] Cover A ETS2 no mods; B ETS2 local+Workshop priority; C ATS no mods; D ATS active/missing/ambiguous Workshop. Assert shown candidates and pair eligibility separately.
- [x] Preserve unknown-source and unknown-DLC blocks, accessory/control-state rules and all D3 transaction regressions.
- [x] Release build and full suite; reproduce Windows-checkout line endings, independent review and offline UI verification.
Roadmap and technical limits are updated. Delivery requires a dependent PR, green Windows CI and NMC installer upgrade on its final commit; links and final results belong in the PR description and completion report. The report must distinguish synthetic verification from outstanding in-game field acceptance.

## Verification record before publication

Release build: zero warnings/errors. Full local suite: 832/835 pass (Core32, App252, Infrastructure548/551); the same three host-dependent Steam detector failures were already reproduced on main with the registered ETS2 installation. No registry or test weakening. Eight real-resolver scenario tests and ZIP/source/diagnostic regressions pass. Independent source and diagnostics reviews completed; findings fixed and reviewed again. Offline WPF views checked at1080/1320px. Real archive reads and a synthetic original-dealer save transaction/restore passed with original files unchanged. Fresh actual ETS2 result is4→2 blockers (three original causes fixed, one previously tolerated inconsistent ZIP newly detected); ATS still13 missing packages.
