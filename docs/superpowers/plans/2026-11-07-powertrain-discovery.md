# Auftrag 3: read-only powertrain discovery

Approved scope: implement phases B/C on main c662c80, retain version 1.0.0 and the existing save transaction. No merge without user approval. PRs 87/88 are already merged and field-tested by the user.

## Design

The save-derived engine/transmission choices remain the only write selection. An independent, cancellable discovery service reads the installation for the selected game, that profile's game-home mod directory and local Workshop packages. Discovery rows carry actual paths, names, model, provenance, current status and a reason for their compatibility classification. They cannot authorize a save edit.

Support bounded folder/ZIP sources and HashFS v1/v2 plain/zlib definition payloads. Resolve source-local text includes with cycle/size limits. Retain ambiguous overrides and unresolved includes as unverified evidence; never guess a priority, active mod, DLC entitlement, or accessory dependency. Use the isolated MIT-licensed SCS CityHash primitive with its notices; do not introduce GPL libraries.

## Work and validation

1. Add independent contracts and archive readers; synthetic format, corruption, size and traversal tests.
2. Add source discovery, definition parsing, provenance/deduplication and conservative classification; game separation, absent DLC, ambiguity, includes and fallback tests.
3. Add a searchable WPF catalog with independent loading/cancellation and stale-context guards; preserve existing controls and test that discovery rows cannot be written.
4. Inspect installed ETS2/ATS archives read-only, render the WPF view offline, review the diff, update architecture/roadmap/test notes.
5. Release build, full tests, dedicated PR and Windows CI/installer verification. No in-game test claims without running the games.

## Known baseline

The clean starting revision passes 32 Core and 166 App tests. This machine has three pre-existing Steam installation detector test failures because its registered ETS2 installation takes precedence over synthetic paths (152/155 Infrastructure pass). Preserve these as baseline evidence; CI runs on a clean Windows host.

## Phase D boundary

Matching a model path is insufficient to prove accessory rules, includes across mounted sources, active mod ordering and DLC availability. External discovery stays read-only until these can be established and revalidated at write time through the existing pipeline.

## Validation outcome

Implementation and independent archive/service/UI reviews completed. Release build has no warnings/errors; 80 new tests pass. Final local full run is 430/433, with only the same three baseline Steam registry-dependent failures. Real read-only scan found 1,252 ETS2 and 358 ATS components; hashes/metadata and visual checks are recorded in POWERTRAIN_CATALOG.md. GitHub Windows CI and installer results belong to the PR checks. No merge authorized.
