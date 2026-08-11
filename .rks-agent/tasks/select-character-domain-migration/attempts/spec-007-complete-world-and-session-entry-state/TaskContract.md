---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-3
depends_on: []
---

# TaskContract: Complete World, Content and Session Entry State

## Context

### Source Request

The operator resumed the paused root migration — 「先看看之前的任务契约没完成的后续任务，我们要继续完成」 — and, shown the option of taking only the now-unblocked static-content half of G5, chose the complete G5 group instead, including channel admission and the session-adjacent read views, on the current branch.

### Current Situation

G5 is the only unmet business-data group in the root migration; G6 and G7 depend on it. The
previous attempt, `spec-006`, was sealed `blocked` and its product changes rolled back,
because complete quest/activity/collection/pet definitions required an absent `Script.pvf`
and because live town/PvP/rejoin/expert-store state had no normalized source.

The first cause is resolved. The operator supplied the archive, and the separately
authorized `pvf-content-infrastructure` task delivered a verified read path: a
domain-free format library plus four content sources behind Abstractions-level interfaces
(`IQuestDefinitionSource`, `IDailyActivityDefinitionSource`, `ICollectionBoxDefinitionSource`,
`ICreatureDefinitionSource`). That task's evidence proves the definitions can be combined
with character progress to produce the business results matrix rows 12, 14, 32/33 and 58
carry.

The second cause was re-classified rather than supplied. Town presence, PvP lobby,
dungeon-rejoin candidates and expert-store membership are server-owned runtime state, not an
absent external artifact, so they must be built. The legacy implementations to reverse them
from are present in the main worktree and are enumerated in the root map's Resume Record.

At the current baseline the G5 slots in the business result are placeholders:
`GameWorldEntryState` (`Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:53`) holds
only a `PreparedAt` timestamp, and `CharacterEntryService` constructs it and
`CharacterSessionEntryState` from nothing but that timestamp and the identity pair. A
repo-wide search finds no admission service, no session directory, and no town-presence,
PvP-lobby, dungeon-rejoin or expert-store registry; `IChannelCatalog` supplies only the
login-time channel list.

Two scope facts were established by reading the legacy sources before this contract was
written:

- Spawn resolution for matrix row L-04 reads `town/town.lst`, the referenced town files and
  `map/*` through `PvfArchiveAccessor` (`Server/DfoServer/GameWorld/Town.cs`). Town and area
  geometry is therefore **archive content that the four delivered sources do not cover**; a
  further content source is required.
- Channel admission is narrow. `GameChannelAdmissionPolicy` carries exactly one rule — the
  channel-100 listener refuses characters below level 70 — and `GameChannelSpawnPolicy`
  derives spawn, town-entry permission and position persistence from the same listener
  identity.

## Goal

The select-character business result carries complete, honestly modeled world/content and
session-adjacent entry state, composed before publication from driven sources and real
runtime registries, so that Networking can later project the G5 packets without querying
content, world or session services.

## Scope

### In

- Compose world/content entry state into the selection result, replacing the
  `GameWorldEntryState` placeholder, sourced through the delivered content interfaces for
  matrix rows 12, 14, 32, 33 and the row-58 content portion.
- Add a town/area definition source behind an Abstractions-level interface, implemented over
  the archive in Infrastructure, sufficient for spawn resolution.
- Resolve a listener to a channel identity before command dispatch, and produce a typed
  admission outcome that either accepts or rejects before any publication (matrix X-08).
- Resolve spawn, town-entry permission and the position-persistence decision from channel
  identity (L-04), and expose the PvP-profile applicability that a free-duel listener
  implies (L-06).
- Provide town-presence, PvP-lobby, dungeon-rejoin and expert-store read views (X-09, X-10,
  X-15 through X-18) as queries over runtime registries that genuinely exist, returning
  explicit empty state when they hold no entries.
- Represent the fixed and unresolved protocol baselines of rows 17, 31, 34, 49 through 53
  and 56 as explicitly unconfirmed or constant data, distinguishable from modeled domain
  state.
- Fail explicitly, before publication, when a required source is missing or malformed.
- Add tests for the above and keep the existing solution tests passing.

### Out

- Networking projection of any G5 packet (G6).
- Lifecycle mutation: session registration and replacement, displaced-session cleanup,
  daily refill, post-entry synchronization and rollback (G7). This attempt supplies the
  query side and the registries; it does not wire the writers.
- Character-owned and account-owned progress, which G3 and G4 already deliver.
- Assigning business meaning to any archive field or protocol slot whose meaning is not
  confirmed.
- Content categories beyond those the listed matrix rows require.
- Merge, push, pull request, or any write to another branch.

## Constraints

- No fabricated empty domain state. A registry that exists and holds no entries is honest
  loaded-empty; an absent registry rendered as an empty view is not. `null` means genuinely
  inapplicable; a load failure remains an error. This is the constraint that sealed
  `spec-006` and it governs this attempt directly.
- World/content and session data must stay outside `ActiveCharacterState`, as sibling limbs
  of the result.
- No PVF format concept may appear in `Dfo.Server.Abstractions`, `Dfo.Server.Application` or
  `Dfo.Server.Networking`; Infrastructure remains the only layer that knows the format.
- Types crossing the infrastructure boundary must survive both a change of underlying
  storage and a change of game rule without edit.
- Protocol claims are recorded as code-reversed unless packet-capture evidence exists; no
  packet capture is available.
- Existing accepted G1 through G4 behavior is the baseline; it may change only where the
  migration matrix shows a correction is required.
- The archive is located by configuration and never committed.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | The selection result carries world/content entry state sufficient to compute the business results of matrix rows 12, 14, 32, 33 and the row-58 content portion, composed before publication. | Tests that combine the composed result with stand-in character progress and produce each row's business result, not merely prove the definitions were read. |
| SC-2 | Spawn resolution is driven by a town/area source behind a domain-facing interface, and a missing or malformed town definition fails explicitly. | A test resolving a known town's gate against the real archive, plus an explicit-failure test for an absent or malformed definition. |
| SC-3 | A listener resolves to a channel identity, and admission produces either an accepted typed view or a rejection that occurs before any publication. | Tests for an admitted listener and for the channel-100 under-level rejection, the latter asserting nothing was published. |
| SC-4 | Spawn, town-entry permission, position persistence and PvP-profile applicability are decided from channel identity. | Tests covering the transient-spawn listener and an ordinary listener, asserting the differing spawn, persistence and applicability outcomes. |
| SC-5 | Town-presence, PvP-lobby, dungeon-rejoin and expert-store read views query registries that exist, return explicit empty state when unpopulated, and mutate nothing. | For each view, a populated-registry test and an empty-registry test, plus evidence that the query path performs no mutation. |
| SC-6 | Fixed and unresolved protocol baselines are distinguishable from modeled domain state, and no unconfirmed field carries a business name. | Type review of the composed state and the documented list of items carried as constant or unconfirmed. |
| SC-7 | A missing or malformed required source fails before publication rather than yielding partial state. | A test per required source class asserting failure precedes publication. |
| SC-8 | Layering holds and nothing regresses. | Dependency check showing no PVF concept outside Infrastructure, plus a full solution test run with the archive path configured. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root TaskContract `task` row, whose Scope/In includes "Define and implement the character, account, world/content, and session service boundaries required by the select-character flow", together with the operator's resumption 「我们要继续完成」 and their selection of the complete-G5 boundary on the current branch. | Implement complete G5 in the existing root task worktree `/workspace/DNF-JP-86-Server-pvf` on branch `codex/pvf-content-infrastructure`, including the new town/area content source, admission and channel identity, the session-adjacent registries and their read views, corresponding tests, and one verified local commit. |

## Requires Confirmation

- Merge, push, pull request, or any write to another branch.
- Extending content sources beyond what the listed matrix rows require.
- Implementing any G6 projection or G7 lifecycle mutation.

## Assumptions

- The four delivered content sources remain the authoritative interface for quest, daily
  activity, collection-box and creature definitions; this attempt consumes them rather than
  reworking them.
- The runtime registries this attempt creates will be empty on the production path until G7
  wires their writers. Their read views are therefore proven with directly populated
  registries and with empty ones; an end-to-end populated path is a G7 outcome.
- `PVF_ARCHIVE_PATH` must be exported for archive-backed verification, because the archive
  is gitignored and lies outside this worktree's parent-directory search path. An unset run
  skips those tests rather than failing them.
- Matrix rows 49 through 53 have unconfirmed meaning and stay unconfirmed here; row 49–51
  data is persisted per character and its G3 limb is already accepted.
