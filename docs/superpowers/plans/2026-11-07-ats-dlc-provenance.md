# ATS DLC provenance Implementation Plan

> **For agentic workers:** Use subagent-driven development for the independent metadata/installation investigations, App context changes and synthetic scenarios. Root owns the Core/resolver evidence contract and final verification. No merge is authorized.

**Goal:** Establish the origin of the thirteen reported ATS package identities, distinguish active profile mods from typed historical save dependencies and installed DLCs, and make the actual inspected selection reviewable.

**Architecture:** Keep activation and entitlement decisions unchanged unless evidence demonstrates a defect. Preserve the existing parser's `mod`/`dlc`/`rdlc` distinctions in detached diagnostic evidence. Display the selected profile/save and join historical DLC IDs only to exact installed manifest IDs. Add regression scenarios proving that no-mod ATS DLC references never trigger Workshop lookup. Fix the independently demonstrated stale game-home display when switching profiles.

**Tech Stack:** Existing C#/.NET10 WPF, bounded SII/archive readers and synthetic fixtures.

**Spec:** User request “ATS-DLC-Erkennung überprüfen”, 07.11.2026. PR90→91→92→93 verified open and unchanged; base93 `c2b213d71e6f3706b44e5a6f5e181a2b683d3155`. New branch `feat/ats-dlc-provenance` targets93's branch.

## Constraints

- Never write original profiles, saves, game archives or Steam data. Decoder operates on unique copies only.
- No name-based conversion of `mod_workshop_package.*` to DLC. Preserve raw identity and source field semantics.
- `info.sii` dependencies describe the save; they neither activate mods nor prove current DLC entitlement. Preserve `rdlc` as its distinct on-disk kind without inventing semantics.
- Installed DLC files and Steam app IDs are metadata, not current ownership proof. Preserve all D3 backup/SHA/transaction/context/game-running safeguards, LicenseHub and version1.0.0.
- Keep private inventories, profile names/IDs, save contents, logs and manifest secrets out of repository/PR/export. Full paths belong only to local technical UI details.

## Evidence and decisions

The current resolver already separates the three dependency tags and only resolves profile `active_mods` as active packages. Fifty-one installed ATS DLC archives have valid distinct ATS manifest IDs; none equals the thirteen reported Workshop IDs. The previous helper selected an existing save independently of the user's actual UI selection, which is not persisted. The available game log is not contemporaneous with the reported test. Do not equate that helper context with the user's test.

A classification rewrite or accepting downloaded DLCs as owned would lack evidence. The supported change is provenance and regression coverage. The UI's displayed game-home can remain stale after selecting a different profile; resolve that display from the actual selected profile while preserving the existing request binding.

## Task1 – Fresh read-only investigation

- [x] Compare all currently discoverable profiles/saves using aliases and original-file fingerprints; distinguish current profile entries from historical dependencies and record unresolved user selection.
- [x] Map all installed DLC archive manifests by exact ID/game/app ID without reading or publishing secret fields; compare every reported Workshop identity.
- [x] Check primary SCS/Steam sources and public Workshop items; record limits rather than infer `rdlc` entitlement or current activation from a log.

## Task2 – Preserve diagnostic provenance

Files: Core `ScsSaveDependencies.cs`, `ScsEffectiveContent.cs`, `ScsPowertrainDiagnostics.cs`; Infrastructure resolver/VFS/rules; focused tests.

- [x] Add `ScsSaveDependencyKind { Mod, Dlc, Rdlc }` and immutable `ScsSaveDependency(Kind, Identity, DisplayName, Index)`.
- [x] Append optional dependency lists to evidence and inspection report. Populate from the already validated typed metadata, copy at snapshot boundaries and retain through failure paths. Do not alter source selection or grants.
- [x] Verify mixed kinds, display-name collisions, historical-only mods and immutable detached evidence.

## Task3 – Local selected context and dependency display

Files: App Truck diagnostics/report and profile-selection viewmodel, App regression tests.

- [x] Display selected profile/save/storage/timestamp and paths in local technical details. Clearly distinguish Launcher selection from unproved in-game selection.
- [x] Label profile-active mods, historical mod references, `dlc`, `rdlc`, installed DLC matches and unverified availability separately.
- [x] Keep export bounded and whitelisted; export typed provenance and source aliases, never personal context or raw messages.
- [x] Fix stale displayed game-home after profile change; preserve stale-result/write guards.

## Task4 – Synthetic ATS scenarios and delivery

- [x] ATS without mods and with several official-style DLC manifests: no Workshop lookup, typed dependencies retained, installed DLC layers included, unshadowed base pair permitted.
- [x] Unknown DLC override stays unavailable; active Workshop named like DLC stays Workshop; historical-only mod mismatch remains blocked.
- [x] Full Release build/tests, independent review, offline UI check and fresh truck preview probes. Original hashes unchanged.
- [x] Document findings and limits. Publish the dependent PR and track final Windows CI/installer evidence in its description; no merge. The final report must qualify the unresolved in-game save selection.

## Verification record

- Release build: 0 warnings, 0 errors; version remains 1.0.0.
- Local full suite: 854/857 (Core 32, App 260, Infrastructure 562/565). The three Steam installation detector failures are the previously reproduced host-environment baseline; no registry or test workaround was applied. All 22 added tests pass.
- Independent review found and resolved two diagnostic issues: unread history must retain null, and DLC metadata validity must survive diagnostic truncation. RED/GREEN regressions cover both, including 300 wrong-game DLC manifests plus 512 missing profile packages. Re-review has no remaining actionable findings.
- Fresh read-only previews of both confirmed-profile saves retain 62 dependencies (13 Mod, 33 Dlc, 16 Rdlc), all 51 DLC sources and the 13 missing-Workshop blockers. Neither preview grants a new pair. Original files remain unchanged.
- Offline WPF display is checked at 1080/1320 widths using synthetic context only. CI and installer run links are recorded in the dependent PR after their completion.
