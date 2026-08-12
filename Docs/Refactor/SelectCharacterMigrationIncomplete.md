# Select Character Migration — Historical Pause Record

## Current status

This file formerly recorded the 2026-08-11 pause at G4. That pause is resolved and the
document is retained only so old links do not misrepresent the present implementation.

- Current implementation path: `codex/select-character-domain-migration-completion`
- Accepted integration baseline for the final lifecycle stage: `1ab5f0dd95ff03816a87ce55edc1fc03665b23e6`
- G1–G7 status: **implemented, code-reversed**
- Remaining external acceptance: real-client packet/lifecycle validation

The authoritative implementation and evidence are now described by:

- `Docs/Refactor/SelectCharacterMigrationMatrix.md`
- `Docs/Refactor/SelectCharacterG6Coverage.md`
- `Docs/Refactor/SelectCharacterG7Coverage.md`
- `.rks-agent/tasks/select-character-domain-migration/attempts/spec-010-integrate-entry-lifecycle/`

## Resolution of the old blockers

The real `Script.pvf` archive is now consumed lazily by the successor Infrastructure
sources. G5 maps quest, activity, collection, creature, town and entry-lifecycle content
into typed application-facing facts. G6 owns the ordered 58-row static stream and typed
X01–X18 projection codecs without repository/PVF access. G7 explicitly coordinates
session replacement, dungeon-effect recovery, dirty inventory lease persistence/reload,
position policy, daily refill, publication, quest/pet transitions, PvP/rejoin flows,
cleanup, rollback and connection close ordering.

All protocol descriptions remain honest reverse engineering from legacy code. Passing
real-PVF, SQLite, Networking and Host checks demonstrates successor consistency, but does
not convert code-reversed wire assumptions into captured-client proof. Real-client
validation may still require protocol corrections; it is not an implementation gap in
G1–G7.
