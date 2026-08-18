# Select Character Migration — Historical Pause Record

## Current status

This file formerly recorded the 2026-08-11 pause at G4. That pause is resolved and the
document is retained only so old links do not misrepresent the present implementation.

- Current refactor path: `codex/refactor-select-character-state-synchronization`
- Refactor baseline: `40f701b1f27702d5cca6e5ee3e361378865f3cc2`
- G1–G7 status: **implemented, code-reversed**
- Remaining external acceptance: real-client packet/lifecycle validation

The authoritative implementation and evidence are now described by:

- `Docs/Refactor/SelectCharacterMigrationMatrix.md`
- `Docs/Refactor/SelectCharacterStateSynchronizationRefactor.md`
- `Docs/Refactor/SelectCharacterG6Coverage.md`
- `Docs/Refactor/SelectCharacterG7Coverage.md`
- `.rks-agent/tasks/select-character-domain-migration/attempts/spec-010-integrate-entry-lifecycle/`

## Resolution of the old blockers

The real `Script.pvf` archive is now consumed lazily by the successor Infrastructure
sources. G5 maps quest, activity, collection, creature, town and entry-lifecycle content
into typed application-facing facts. G6 now represents the reviewed 58-position stream and
X01–X18 state synchronization as concrete protocol messages without repository/PVF access.
G7 uses a Protocol handler to sequentially dispatch cohesive top-level commands and coordinate
session replacement, dungeon-effect recovery, dirty inventory lease persistence/reload,
position policy, daily refill, publication, quest/pet transitions, PvP flows,
cleanup, rollback and connection close ordering.

All protocol descriptions remain honest reverse engineering from legacy code. Passing
real-PVF, SQLite, Networking and Host checks demonstrates successor consistency, but does
not convert code-reversed wire assumptions into captured-client proof. Real-client
validation may still require protocol corrections; it is not an implementation gap in
G1–G7.
