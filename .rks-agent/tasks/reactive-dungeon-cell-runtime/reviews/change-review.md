---
kind: rks-review
verdict: approved
---

# Complete mixed-result review: reactive dungeon Cell runtime

## Target and scope

- Target: worktree `reactive-dungeon-cell-runtime`, current uncommitted result relative to
  baseline `f8ab165`.
- Governing sources: `TaskContract.md` revision 1, `spec.md`, `plan.md`, repository `AGENTS.md`.
- Applied criteria: complete code review plus deliverable review. Inspected the production and test
  diff, new Cell/loot model and services, protocol consumers, `Document/ReactiveDungeonCellRuntime.md`,
  and the stated verification surface.
- Independent check: the focused Application suite for `DungeonVisitDomainServiceTests`,
  `DungeonRunDomainServiceTests`, and `MonsterExperienceRulesTests` passed 91/91 in Release. This
  confirms the current tests are green, but those tests do not cover the blocking cases below.

## Verdict

`changes_required`. The change establishes a useful Cell-owned direction and removes the old Run
collections, but it changes an existing actor-type rule, retains a production bypass around the
complete-Cell invariant, and allows an in-flight pickup to commit against a room the party has
already left. These are correctness defects against RQ-001/RQ-003/RQ-007/RQ-010 and AC-001/AC-006,
not documentation-only gaps.

## Blocking findings

### F-001 — Actor type 4 now blocks clear and can create invisible loot

**Evidence**

- The pre-change behavior deliberately treated only actor types `<= 3` as clear-blocking; the
  repository's own compatibility note records type 4 as “not a monster, gives no experience” and
  “does not block room clear” (`Docs/新功能接入规范.md:408-412`,
  `MonsterExperienceRules.cs:87-94`).
- `DungeonCellState.IsCleared` now treats every `!IsAiCharacter` actor as hostile
  (`DungeonCellState.cs:79-81`). `DungeonRoomRuleService` marks only type 5 and above as AI, so type
  4 is now clear-blocking.
- `DungeonCellFactory` also prepares pending loot for every `!IsAiCharacter` actor
  (`DungeonCellFactory.cs:51-66`), while death projection still sends those added items only when
  `MonsterKillDomainService` classifies the actor as a monster (`MonsterDeathSlice.cs:523-527`,
  `787-792`). Type 4 is classified as non-monster there.

**Consequence**

A room containing a type-4 actor no longer has the old clear behavior and may remain closed until
that actor reports death. If it does report death, its precomputed pending loot is moved into the
Cell but omitted from the death notification, leaving server-owned, pickable floor state that the
clients were never told exists. This violates the no-business-rule-change boundary in RQ-010 and
can produce both progression blockage and client/server floor divergence.

**Minimum sufficient correction**

Do not derive clear/drop eligibility from the wire-facing `IsAiCharacter` flag. Preserve the three
existing actor-type readings explicitly in the Cell seed: at minimum a separate hostile/monster
fact used for clear and loot (`<= 3`/`IExperienceRuleService.IsMonster`), while retaining the
existing `>= 5` AI projection classification. Add regression tests for type 4 proving it does not
block clear, receives no pending loot, and produces no unannounced ground item.

### F-002 — Production code still publishes incomplete Cells and invents their contents later

**Evidence**

- `PreparePartyEntry(participants, layout, difficulty)` publishes
  `DungeonCellSeed.Empty(..., IsComplete: false)` (`DungeonRunDomainService.cs:48-61`).
- Both legacy `TryMove` overloads can publish a new target with no prepared seed; `TryMoveCore`
  substitutes another incomplete seed (`DungeonRunDomainService.cs:195-257`). No adoption path
  rejects `IsComplete == false`, a mismatched initial Cell, or a mismatched initial map.
- In an incomplete Cell, claiming an arbitrary floor number dynamically creates a one-gold
  placeholder (`DungeonRunDomainService.cs:1307-1324`), and claiming an arbitrary actor sequence
  dynamically creates a placeholder actor (`1336-1352`). That is construction after publication,
  triggered by runtime commands rather than content/rules.
- `DungeonVisitDomainService` makes `IDungeonCellFactory` optional and keeps the old room/content
  projection branch (`DungeonVisitDomainService.cs:16-23`, `65-116`, `136-181`). A missing DI
  registration therefore silently re-enables the incomplete path instead of failing closed.
- The design document states that the factory creates a complete unpublished seed and the Run
  adopts only that complete Cell, but it does not disclose these active production bypasses.

**Consequence**

The defining invariant of SC-1/AC-001 is not enforced by the domain boundary: production methods
can expose a Cell before actors and floor items exist, then manufacture facts from arbitrary client
identities. This also leaves content access in the runtime visit service, contrary to RQ-004 and the
documented responsibility split. Green protocol paths currently depend on DI selecting the factory
branch rather than on the model making the invalid state impossible.

**Minimum sufficient correction**

Make the factory mandatory in `DungeonVisitDomainService`, remove its content/placement fallback,
and require a complete seed whenever a Run or new Cell is published. Reject incomplete or
identity-mismatched seeds before mutating directory state. Remove dynamic placeholder Actor/item
creation; migrate tests and any compatibility callers to explicit complete empty or populated
seeds. Add negative tests proving incomplete/mismatched seeds cannot publish and arbitrary actor or
drop identifiers cannot create Cell state.

### F-003 — A pickup claim can commit after the party changes rooms

**Evidence**

- The claim checks `RunId` and `RoomVersion` only when it is acquired
  (`DungeonRunDomainService.cs:267-301`). `DungeonGroundDropClaim` carries no claimed room or Cell
  revision beyond the drop's Cell identity.
- Movement blocks active death claims but does not block ground-drop claims
  (`DungeonRunDomainService.cs:237-247`). `MoveMapHandler` calls the visit service directly rather
  than through the pickup transaction (`DungeonEntrySlice.cs:646-702`), so it can interleave while
  the pickup command awaits durable work.
- `CommitGroundDrop` and transaction `PrepareGroundDrop` look up `claim.Drop.Cell` directly and do
  not require it to remain the aggregate's current Cell or require the claimed Room/Cell revision
  (`DungeonRunDomainService.cs:305-335`, `962-1010`). The emitted change uses the aggregate's current
  `RoomVersion`, even when the removed drop belongs to the old Cell.

**Consequence**

The leader can leave Cell A while an A pickup is awaiting SQLite, enter Cell B, and then have the
old claim remove A's item and broadcast number `N` while everyone is in B. Because ground numbers
are Cell-local, that notification can remove or animate the wrong B item client-side. This violates
RQ-007 and AC-006's stale Run/Room/Cell-claim requirement.

**Minimum sufficient correction**

Linearize room movement with unprepared ground claims, or carry and validate the claimed
`RoomVersion`/Cell revision before transaction prepare so a stale claim releases without consuming
the old Cell. Preserve the existing post-prepare guarantee for a durable transaction if required,
but do not publish its removal as the new room's version. Add a concurrency regression covering
claim in A, move to B, then prepare/commit, including both Cell snapshots and the projected change.

## Non-blocking residual risks and consistency notes

- `DungeonCellActorState.PendingLoot` is exposed as `IReadOnlyList<RolledDrop>` but the factory
  stores the mutable `List` returned by `MonsterLootRuleService` without defensively freezing it.
  A snapshot consumer can cast and mutate the aggregate's pending loot without a Cell revision.
  Prefer immutable arrays or defensive copies at seed adoption and snapshot publication.
- The TaskContract asks for one source-neutral addition entry usable later by building/player-drop
  sources. The result defines `PlayerDrop` metadata but exposes no post-publication Cell operation
  for adding a general ground item; only actor-death commit can add one. This should be closed or
  explicitly reconciled before declaring the contract complete.
- `PickUpGroundDropCommandHandler` still documents the old “read floor from archive and remember
  only taken items” model (`GroundPickupSlice.cs:275-287`), contradicting the new Cell-owned design.
  Update it with the implementation so future maintenance does not reintroduce the removed model.

## Evidence disposition

- The supplied full-solution result (Application 1493, Protocol 926, Networking 65 and zero-fail
  build) is consistent with the focused 91-test check performed during review.
- Those suites do not contain a Cell type-4 compatibility case, an incomplete-seed rejection case,
  or the pickup-claim/move interleaving case, so passing them does not resolve F-001 through F-003.
- No protocol codec layout was changed in the production diff; the blocking compatibility issue is
  semantic state/projection ordering, not byte-layout drift.

## Re-review round 2

```yaml
kind: rks-review
verdict: changes_required
```

### Target and checks

- Re-reviewed the current uncommitted worktree result relative to baseline `f8ab165`, using the
  repository instructions, TaskContract revision 1, Spec, Plan, the current production/test diff,
  and `Document/ReactiveDungeonCellRuntime.md`.
- Fresh focused Application check passed 96/96 in Release for `DungeonCellFactoryTests`,
  `DungeonRunDomainServiceTests`, `DungeonVisitDomainServiceTests`,
  `MonsterExperienceRulesTests`, and `DungeonPickupTransactionTests`.
- Fresh focused Protocol check passed 235/235 in Release for dungeon entry/loading, monster death,
  ground pickup, and dungeon clear. `git diff --check` also passed.
- The controller reports a fresh all-solution green run. This is consistent with the focused checks,
  but does not eliminate the structural entry bypass below.

### Prior finding dispositions

#### F-001 — resolved

`DungeonCellActorState` now freezes `IsMonster` separately from wire-facing `IsAiCharacter`.
`DungeonCellFactory` obtains that fact from `IExperienceRuleService.IsMonster`, requests pending loot
only for monsters, and `DungeonCellState.IsCleared` filters on `IsMonster`. The new type-3/type-4
factory theory proves type 4 neither blocks clear nor receives pending loot; the existing experience
rule test continues to prove it grants no monster experience. Death identification also consumes the
frozen Cell fact. The original clear/loot divergence is closed.

#### F-002 — partially resolved; one blocking bypass remains

The real `DungeonVisitDomainService` path is corrected: `IDungeonCellFactory` is mandatory, initial
publication happens only after `CreateAsync` returns, a failed factory leaves no Run, first moves
require a complete identity-matched seed, and runtime actor/drop claims no longer invent placeholders.
The new negative tests cover incomplete/mismatched initial seeds and unprepared/incomplete target
moves.

However, production `DungeonRunDomainService.Begin`, `BeginParty`, and the three-argument
`PreparePartyEntry` still form a second public entry path that never receives a factory-produced seed.
`PreparePartyEntry` manufactures `DungeonCellSeed.CompleteEmpty(layout.StartCell,
layout.StartRoomMapId)` itself (`DungeonRunDomainService.cs:34-61`) and publishes it as complete.
Unlike an explicit complete-empty seed supplied by a test fixture, this overload has no fact proving
that the actual start room contains no actors or declared ground items. It can therefore publish a
content-bearing room as an empty authoritative Cell while satisfying the boolean `IsComplete` gate.
The interface also keeps the seed-aware default overload delegating to the seedless overload and
discarding `initialCell` (`IDungeonRunDomainService.cs:407-429`).

**Consequence:** the complete-Cell invariant remains conditional on callers choosing the newer
overload. A caller of the still-supported production domain API can create a Run whose authoritative
Cell omits all room actors and floor items, after which clear/pickup/revisit behavior consistently uses
the wrong empty truth. That conflicts with the TaskContract's requirement that first Cell creation use
content/rules and with RQ-001/RQ-003; it also leaves two semantics under one service contract.

**Minimum sufficient correction:** make production Run publication require an explicit complete
`DungeonCellSeed` (or a factory-backed preparation object) on every entry path. Move seedless
`Begin`/`BeginParty` convenience construction to test helpers or require callers to pass
`DungeonCellSeed.CompleteEmpty` explicitly when emptiness is a known fixture fact. The seed-aware
interface default must not silently discard the supplied seed. Add a structural/call-site check or a
small domain test proving the production service has no seedless publication route.

#### F-003 — resolved

`DungeonGroundDropClaim` now carries the acquired `RoomVersion` and `CellRevision`.
Acquisition captures both; direct commit and transaction prepare validate the current Cell plus exact
Run/Room/Cell versions. `TryMoveCore` refuses movement while either an unprepared or prepared ground
claim exists in the current Cell. The new A-claim/move-B regression proves the move is rejected until
the A claim commits and that B's same-numbered item remains intact. The post-prepare transaction path
retains its deliberate infallible commit against the captured aggregate, so durable persistence and
Cell removal are not split after prepare.

### Residual recommendation and documentation consistency

- Pending loot is now defensively copied both when a seed is adopted and when a snapshot is
  published; the mutation regression proves neither the caller's original list nor a returned array
  can mutate aggregate state.
- `TryAddGroundItems` and `DungeonGroundItemDraft` provide the requested source-neutral addition
  seam, use the same Cell collection/number allocator, and advance Cell and Run revisions. The new
  player-source test covers this path.
- The stale command-handler comment identified in round 1 is corrected. Two public abstraction
  comments remain inconsistent with the delivered model: `IGroundPickupDomainService` still says
  declared pieces are reread from the archive while monster drops alone exist in the Run
  (`IGroundPickupDomainService.cs:16-25`), and `IMonsterKillDomainService` still describes runtime
  actor/archive reconciliation (`IMonsterKillDomainService.cs:28-37`, `54`). Their implementations
  now read only the frozen current Cell. These comments do not create an additional runtime defect,
  but should be updated with the blocking correction so AC-008 does not leave misleading public
  contract documentation.

### Verdict

`changes_required`. F-001 and F-003 are closed and the substantive runtime portions of F-002 are
fixed. Approval is withheld only because the production Run service still exposes a seedless
publication path that can declare an unknown content-bearing room to be a complete empty Cell. Remove
or make that path explicitly seed-bearing, then re-run the focused entry/Run tests and review the
small remediation diff.

## Re-review round 3

```yaml
kind: rks-review
verdict: approved
```

### Target and scope

- Re-reviewed the current uncommitted result in `reactive-dungeon-cell-runtime` relative to
  `f8ab165`, with emphasis on the round-2 F-002 remediation and direct regressions introduced by
  changing the Cell creation context.
- Rechecked the current production interfaces/implementation, test-only entry helpers, Cell
  factory, room/drop/quest rules, public XML documentation, protocol consumers, and the governing
  TaskContract/Spec/Plan/design document.
- Fresh focused Application tests passed 112/112 in Release for Cell factory, Run, visit, pickup
  transaction, monster boundary, normal loot and quest loot. Fresh focused Protocol tests passed
  235/235 for entry/loading, death, pickup and clear. `git diff --check` passed. The controller's
  reported complete runs are Application 1497/1497 and Protocol 926/926.

### F-002 disposition — resolved

- `IDungeonRunDomainService` and its sole production implementation now expose only the explicit
  `PreparePartyEntry(participants, layout, difficulty, initialCell, runId, startedAt)` publication
  operation. The interface default fails closed with `NotSupportedException`; it no longer discards
  a supplied seed.
- Static production call-site inspection finds no `Begin`, `BeginParty`, or three-argument
  `PreparePartyEntry`. The seedless conveniences and `CompleteEmpty` construction now live in the
  linked test helper only, where empty Cell content is an explicit fixture choice.
- Production `PreparePartyEntry` rejects incomplete and identity/map-mismatched seeds before any
  participant detachment or directory publication. First movement still refuses an absent or
  incomplete target seed, and runtime claims cannot invent actors, items or coins.
- `DungeonRunState.CurrentCellState` now fails construction on null. The only production
  `DungeonRunState` construction is the aggregate snapshot path and supplies the current Cell
  snapshot, so there is no nullable fallback that can synthesize an empty runtime Cell.
- `DungeonCellFactory` no longer manufactures a provisional `DungeonRunState`. Room content and
  deterministic loot rules receive `DungeonCellCreationContext` directly; the migrated rule reads
  preserve the same layout seed, map, Cell, difficulty, participant and leader facts without
  publishing or pretending that a Run already exists.

The round-2 consequence is therefore eliminated: production cannot publish a Run without an
explicit complete Cell seed, and actual entry obtains that seed from the mandatory factory before
calling the Run service.

### Other prior findings and residuals

- F-001 and F-003 remain closed. Their type-4 and pickup/movement regression coverage stays green.
- Defensive pending-loot copies and the source-neutral `TryAddGroundItems` path remain present and
  covered.
- The stale `IGroundPickupDomainService` and `IMonsterKillDomainService` comments are corrected.
  Static search finds no production reference to the removed Run-level `MonsterDrops`,
  `TakenGroundDrops`, `DeadActors`, or their old archive-reconstruction wording.
- The prepared-Cell `TryMove` interface default remains a lightweight-double compatibility default,
  but the only production implementation overrides it and enforces complete target adoption. It is
  not a production bypass or an unresolved acceptance defect in the reviewed target.

### Verdict

`approved`. All three original blocking findings are resolved, the round-2 production seedless
publication path has been removed, and no new code, concurrency, transaction, content-dependency,
protocol-compatibility or documentation blocker was found in the remediation diff. This review
approves the current result at this revision; the controller still owns fresh completion verification
and downstream integration decisions.
