# Effective powertrain selection Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans or subagent-driven-development task by task. Each package receives its own tests and PR; no merge without user approval.

**Goal:** Select and install every fully verified supported engine/transmission combination directly in the existing Truck editor.

**Architecture:** D1 resolves the selected profile's intended active sources and builds a bounded read-only virtual filesystem. D2 evaluates the entire truck against those effective definitions and presents a searchable selection. D3 re-evaluates the selected pair and commits it through the existing save pipeline with game-stopped, source and save fingerprint checks.

**Tech Stack:** C#/.NET 10, WPF, existing HashFS/ZIP readers and pinned SCS decoder. No new third-party runtime.

**Spec:** User's Entwicklungsauftrag 4, 07.11.2026. Base main `6fc34b0`; PR89 merged.

## Global constraints

- Keep version 1.0.0, existing theme, LicenseHub, updater, backup/hash/Cloud protections.
- D1 then D2 then D3, separate reviewable PRs. Later PRs may depend on earlier unmerged branches; no merge is authorized.
- Actual definitions and evidence only. Unknown compatibility, availability or priorities cannot authorize an external write.
- Newly discovered external installations require both games stopped. No real-save modifications during development; only synthetic transaction fixtures.
- If D3 cannot safely cover a case, finish verified subsets and document the exact remaining boundary.

## Approaches and chosen design

Inventory alone cannot prove activation. Reconstructing the whole game runtime or using stale logs as an entitlement oracle would introduce unsupported assumptions. The implementation instead reads current profile activation and selected-save dependency evidence, resolves unique local packages and documented Workshop versions, then evaluates a strict supported definition subset. Session logs may corroborate evidence but cannot certify current file bytes or current Steam DLC ownership. This is an implementation choice within the requested fail-closed scope.

Unknown DLC/conditional layers remain possible overrides. They are never silently omitted to manufacture a verified base-game winner. Definitions shadowed by uncertain layers remain unavailable; independent, unshadowed base-game or active-mod definitions may be verified.

## Review focus

1. Same mod identity in multiple folders, meaningful trailing spaces and outdated Workshop variants: unique exact identity or explicit failure.
2. Historical dependencies/logs versus current profile: never infer activation or entitlement from a file's presence.
3. Unknown DLC overlay affecting a base definition/include: retain uncertainty at effective resolution.
4. Accessory rules on unchanged truck parts: evaluate the final pair against the complete graph, not just two model paths.
5. Save/source changes or game startup between preview and commit: reject before replace; retain rollback until post-write validation succeeds.

## D1 – Active sources and effective definitions

Files: `Core/ScsEffectiveContent.cs`; new Infrastructure SII metadata parser, active-content resolver and effective-content service; corresponding Infrastructure tests.

Interfaces: `IScsActiveContentResolver.ResolveAsync(ScsEffectiveContentRequest)` returns ordered identities, physical package layers, verification reasons and fingerprint evidence. `IScsEffectiveContentService.OpenAsync` yields a disposable session with `GetFiles`, `ReadDefinition`, and `VerifyUnchangedAsync`; it resolves any relevant virtual path, including includes and retained truck accessories.

- [x] Test exact ordered profile identities, selected-save agreement, missing/duplicate packages, version selection/ambiguity and conditional layers.
- [x] Read current game version from validated installation metadata; select the documented Workshop matching variant or unique universal fallback. Preserve unresolved DLC availability explicitly.
- [x] Implement bounded effective lookup, unknown-overlay rejection, source-local mount roots and cross-source include access; recheck evidence and relevant bytes.
- [x] Release build, full suite, independent review, docs and D1 PR with Windows CI/installer result. PR90; CI265 all539 passing; NMC installer135 upgrade/uninstall passed.

## D2 – Compatibility and Truck selection

Files: new powertrain compatibility contracts/service and strict truck snapshot validation; existing MainViewModel/Truck view plus App and Infrastructure tests.

- [x] Test full accessory count/index/reference integrity and independent truck `data.sii` model proof.
- [x] Resolve all retained definitions; verify require/conflict/suitable/default/override rules for the final pair. Reject unsupported cascades, hookups, engine-type changes and unknown syntax.
- [x] Show names/locale where proven, source, current parts and reliable torque/gear facts directly in searchable Truck choices. Keep technical details optional and retain existing save-derived actions.
- [x] Release build, full suite, visual review, docs and D2 PR with Windows CI/installer result. PR91; CI266 all649 passing; NMC installer136 upgrade/uninstall passed.

## D3 – Verified pair installation

Files: existing save request/pipeline and vehicle service, new verified installation service, existing Truck command, transaction regression tests.

- [x] Add opt-in expected original SHA, precommit validation and post-write validation to the existing pipeline; preserve old callers.
- [x] Reacquire and verify D1/D2 evidence for the selected save, truck and pair. Block Steam userdata and running games at service and commit boundaries.
- [x] Apply both paths in one transaction/backup; validate written graph and restore on failed postvalidation without deleting needed recovery evidence.
- [x] Test changed bytes with unchanged timestamps, changed source/include/order, late game start, forged approval, successful pair and rollback cases.
- [x] Release build, full suite, independent review, docs and supported subset/field tests. All100 added D3 tests pass; local746/749 with the same three pre-existing Steam detector conflicts. Actual-source synthetic Volvo installation and exact backup restore passed.

D3 is published separately on `feat/powertrain-verified-install`, based on D2. Windows CI and installer results are recorded in the pull request after their runs finish; no merge is authorized.
