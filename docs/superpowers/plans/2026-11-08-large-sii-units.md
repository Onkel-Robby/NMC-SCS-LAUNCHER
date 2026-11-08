# Large SII units implementation plan

> **For agentic workers:** Root integrates the shared snapshot path; independent agents own read-only real-save evidence, the bounded field scanner, synthetic transaction tests and final review. No merge is authorized.

**Goal:** Read large valid ATS/ETS2 save units without constructing an unbounded metadata AST, with identical interpretation before and after powertrain writes.

**Architecture:** Reuse the existing save document's immutable line storage. Index and strictly validate every unit and field; retain only needed fields in a bounded projection for the unchanged definition parser. Preserve attribute names for unsupported-accessory checks and exact array-index validation for ignored fields. Diagnostic evidence contains unit type, normalized size and phase only.

**Tech Stack:** Existing .NET 10/C#, WPF, xUnit and SII readers.

**Spec:** User request “Große SII-Units sicher unterstützen”, 08.11.2026. Exact base PR #94: `e9e22558fc4acbb39529b7a99fabe4db2b79d317`.

## Constraints and evidence

- Keep version 1.0.0, the 128 MiB save bound and the metadata parser's independent 8 MiB limit.
- No original profile/save/game writes. Real reproduction uses uniquely named decoder copies, private DLL copies and before/after SHA-256 checks.
- Preserve unique truck ownership, cross-owner accessory checks, bounded reference lists, SHA/context/process guards, transaction backups and byte-exact restore.
- Never log SII values, unit IDs, private content or whole units. No private save fixtures in the repository.
- The real `economy` unit is 15,822,230 normalized characters/UTF-8 bytes; the previous wrapper adds 13 characters. Its `discovered_items` array has about 337,595 entries. Whole decoded save: 17,753,092 characters/bytes.

## Review focus

- An ignored late field must still reject invalid syntax, escapes, duplicate scalars/indices and count mismatches.
- Out-of-order indices need exact duplicate/gap detection; bound allocated bitmap space across all field families.
- Single-line field syntax must agree with the existing writer, which replaces one physical line.
- Large ignored strings must validate UTF-8 incrementally without retaining decoded data.
- Unknown accessory field names must survive projection as blockers, while errors never disclose values or unit IDs.

## Task 1: Reproduce and establish RED

- [x] Confirm current profile/save/model read-only and measure the first failing unit and wrapper overhead.
- [x] Synthetic ATS/ETS2 large economy and large-array scenarios fail the old reader; malformed scenarios remain blocked (4 expected failures, 2 passes).

## Task 2: Bounded complete validation and projection

Files: new `ScsSaveUnitProjection.cs`; `ScsSiiUnitDocument.cs`; `ScsTruckPowertrainSnapshotReader.cs`.

- [x] Add strict single-field line validation, streamed discarded values, bounded family/index accounting and limited retained fields.
- [x] Reuse immutable save line storage; check every unit ID/header and every field before resolving the truck graph.
- [x] Preserve the shared reader for original, proposed and committed text validation; retain unknown accessory names.
- [x] Test large valid units, arrays beyond 65,536 entries, damaged ignored fields, duplicates, gaps, invalid references and projection limits.

## Task 3: Local safe diagnostics

Files: Core snapshot evidence, snapshot-reader failure context, App local technical details and tests.

- [x] Carry only unit type, normalized UTF-16 character count/UTF-8 byte count and validation phase for large units; no IDs or content.
- [x] Show successful large-unit processing locally and identify structural/projection failures safely. Do not add raw evidence to exports.

## Task 4: Transaction and delivery verification

Files: `ScsPowertrainLargeUnitScenarioTests.cs`, existing partial scenario fixture, documentation.

- [x] Both games: actual discovery, verified pair, install touching only the two selected paths, byte-exact backup and restore including BOM/CRLF.
- [x] Malformed large units cannot reach backup/write; originals remain unchanged.
- [x] Full Release and suite, independent review, fresh real ATS read-only probe and remaining blocker report.
- [x] Prepare the dependent PR and enable both existing CI workflows for its exact base branch `feat/ats-dlc-provenance`. Final remote run evidence is recorded in the PR; no merge.

## Verification record

- Release: zero warnings/errors. Local suite 899/902: Core 32, App 263, Infrastructure 604/607; only the same three host-dependent Steam detector failures. All 45 added tests pass.
- RED/GREEN includes four original large-unit failures, ignored malformed fields, writable-line ambiguity and three private-ID error cases.
- Independent security review and focused re-review complete; no remaining actionable findings. 147 additional UTF-8 boundary comparisons matched the unchanged definition parser.
- Offline UI rendered and inspected at 1080/1320 widths; both large-unit phases are readable.
- Final actual read-only ATS test: economy 15,822,230 characters/bytes, 11 engines/6 transmissions, no global parser blocker. Existing DLC/definition/accessory availability guards still prevent installation. All three original files unchanged.
