# TASK-09 — The Protocol layer stops reaching rules directly

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: The four protocol-layer calls that reach a rule service directly go through the domain service that owns the flow. `DungeonSelectionRules`, shared today by two slices in violation of the slice rule that forbids cross-slice reference, gains an owning rule service.
- Referenced spec requirements: RQ-003, RQ-005, RQ-008
- Referenced plan sections: T-009; boundaries BD-012, BD-014, BD-015, BD-017
- Owned boundary: BD-012 dungeon-selection rule seam; the remaining BD-014 protocol redirects; the selection registration in BD-015; affected construction sites in BD-017.
- Permitted files or interfaces: the new dungeon-selection rule service and its interface; `DungeonSelectionRules`; `DungeonSelectionSlice`, `DungeonEntrySlice`, `MoveItemSpaceSlice`, `QuestOfferRefresher`, `TownProjectionSlice` and the domain services owning those flows; `ApplicationModule.cs` and `ProtocolModule.cs`; affected test construction sites.
- Dependencies: TASK-08
- Required dependency outputs: a tree green at TASK-08's accepted state.
- Acceptance conditions: no protocol or command handler declares a rule service or rule-input dependency except a sanctioned handler capability that is individually identifiable as such; endpoint resolution through the channel entry policy is left as the sanctioned capability it already is; `DungeonSelectionRules` is called only from its rule service; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-018 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-019 non-command enumeration of rule-service and rule-input dependencies declared by protocol and command handlers.
- Required evidence: suite result against 1690/2/0, the CK-019 enumeration with each remaining dependency named as a sanctioned capability, and the owned diff.

## Contract Amendment 1 — this Task's real size, measured from TASK-01 and TASK-02

- Raised by: TASK-01 and TASK-02, from their handler redirects.
- Contract revision: 2

The Plan scoped this Task around four known Protocol-layer calls into rule services. That list is now
a floor rather than the whole. TASK-01 and TASK-02 took the content sources out of the handlers by
giving those handlers a rule service instead, which satisfies each of those Tasks' own acceptance and
leaves the RQ-005 half here. As of TASK-02 the handlers holding a rule service are:

| Handler | Kind | Rule services held |
|---|---|---|
| `SelectDungeonHandler` | protocol | `IDungeonLayoutRuleService`, `IDungeonRoomRuleService`, `IItemPlacementRuleService` |
| `MoveMapHandler` | protocol | `IDungeonRoomRuleService`, `IItemPlacementRuleService` |
| `MonsterDeathHandler` | protocol | `IDungeonRoomRuleService` |
| `PickUpGroundDropCommandHandler` | command | `IDungeonRoomRuleService`, `IItemPlacementRuleService` |
| `MoveItemSpaceCommandHandler` | command | `IItemPlacementRuleService`, `IItemSlotRuleService` |
| `QuestOfferRefresher`, `TownProjectionSlice`, `DungeonSelectionSlice` | protocol | as the Plan already recorded |

Satisfying RQ-005 for the dungeon ones means those use cases acquiring an owning business service,
because none exists today: `SelectDungeonHandler` and `MoveMapHandler` assemble the room description
inline and dispatch no command at all, which `AGENTS.md` already forbids under "handler 不得承载业务
语义". That is a larger piece of work than the four call sites this Task was written around, and it
touches frame ordering, so it is called out here rather than discovered mid-execution.

Added to this Task's inputs: every rule-service dependency declared by a protocol or command handler
at the time this Task starts, enumerated fresh rather than taken from the list above.

## Contract Amendment 2 — two remaining cross-slice references

- Raised by: TASK-02.
- Contract revision: 3

`GroundPickupSlice` calls `DungeonEntryPayloadCodec.DropInstance` and `MonsterDropInstance`;
`MonsterDeathSlice` calls `MonsterDropInstance`. Both reach into another slice's payload codec, which
`AGENTS.md` forbids for the same reason `DungeonSelectionRules` is already listed here. They are wire
derivations rather than rule inputs, so they are not RQ-001 or RQ-004 work; they belong to this
Task's slice-boundary cleanup, alongside `DungeonSelectionRules`. Resolve by lifting them to the
`Game` endpoint layer, not by moving them into a rule service.

## Controller Disposition — the grown scope is authorized

- Decided by: the user, at the TASK-05 checkpoint, shown the table in Amendment 1.
- Choice: do it all in this Task, per the original plan.

The user was shown that satisfying RQ-005 for `SelectDungeonHandler` and `MoveMapHandler` means
creating business services for those use cases and moving the inline room assembly into them, and
that this is the one part of the refactor with real behaviour risk because it touches the order in
which frames are assembled. They chose the full route rather than splitting RQ-005 into a successor
task or accepting command handlers as business services.

Execution constraint that follows from the way it was put to them: reuse the domain services that
already own each flow rather than inventing new orchestration, change one handler at a time, and run
the full suite green after each. The frame order is what the suite covers here, so a red is the
signal and there is no second oracle.

## Execution Progress — round 1, part 1 of several

This Task is being done in pieces, each ending with the full suite green. This section records what
is finished and what the next executable action is, so the work is resumable.

### Done: the dungeon entry and move-map flow

`IDungeonVisitDomainService` / `DungeonVisitDomainService` — the use case two protocol handlers used
to assemble inline. It orchestrates `IDungeonLayoutRuleService`, `IDungeonRunDomainService`,
`IDungeonRoomRuleService` and `IItemPlacementRuleService`, and answers with values:

- `EnterAsync(characterId, dungeonId, difficulty, facts, ct)` → `DungeonEntry(Layout, Visit)`, or
  null when the archive declares no enterable dungeon. It draws the run's seed, resolves the layout,
  begins the run and describes the first room — the three steps that have to happen in that order
  because the layout cannot be recomputed later and still describe the run the client is in.
- `MoveAsync(characterId, target, ct)` → `DungeonRoomVisit`, or null when the maze declares no room
  at that cell.

Two carriers cross the seam: `FloorDropDescription(GroundDrop, bool IsEquipment)` and
`DungeonRoomVisit(Run, FirstEntry, Actors, FloorDrops, FirstActorSequence)`.

`FloorPieceValues.DescribeAsync` became `FloorPieceValues.Describe` — synchronous and pure. It is
left with the one thing that is genuinely the wire's: which of the two meanings the drop record's
dual-purpose value field carries. Deciding *which pieces are equipment* moved to the domain service,
where the placement rules answer it.

`SelectDungeonHandler` went from five injected services to three and no longer resolves a layout,
begins a run, draws a seed or describes a room. `MoveMapHandler` went from four to three. Both keep
their guards — `runs.Find` is a read-only query, which `AGENTS.md` sanctions — and both keep every
log line and every field in it.

Suite after this piece: 1690 passed, 2 skipped, 0 failed.

### Why this piece went first

It is the only part of this Task with real behaviour risk. The two handlers assemble a five-frame
delivery in an order that a live session was used to settle — "A21's order, which is not the one this
used to send" — and moving the assembly is exactly the change that could reorder it. It was done
first so that the risk was taken against a fresh green baseline rather than stacked on top of the
rest of the sweep.

### Next executable action

Continue the sweep, in this order, each ending with the full suite green:

1. `MonsterDeathHandler` — holds `IDungeonRoomRuleService` and `IExperienceRuleService`; the kill
   valuation and the room-clearing test belong behind a domain service for the kill use case.
2. `TaggedIdReportHandler` — holds `ITutorialRewardRuleService`. TASK-08 recorded why the flag
   lookup was not moved into `GrantTutorialRewardCommand`: the no-session warning reports the
   declared count and no command can be sent on that path. Settle both together here.
3. `TutorialLevelUpSlice`, `TownProjectionSlice`, `QuestOfferRefresher` — one rule service each.
4. The command handlers: quest accept/finish/trigger, kill/clear experience, ground pickup, move
   item space, and the three channel commands.
5. `DungeonSelectionRules`, shared by `DungeonSelectionSlice` and `DungeonEntrySlice`, and the two
   remaining cross-slice payload-codec calls recorded in Amendment 2.
6. CK-018 and CK-019 over the finished state.

`IChannelEntryPolicy` in `OnlineUserConnectionObserver`, `SelectCharacterHandler` and
`TownProjectionSlice` stays: endpoint resolution is one of the four capabilities `AGENTS.md`
sanctions, and this Task's acceptance says so explicitly.

## Execution Progress — round 1, parts 2 to 5

### Done: the remaining protocol handlers

| Handler | Held | Now reaches |
|---|---|---|
| `MonsterDeathHandler` | room + experience rules | `IMonsterKillDomainService` — which declared actor a number names, whether it pays, and whether the room is clear |
| `TaggedIdReportHandler` | tutorial reward rules | `ITutorialRewardDomainService` |
| `GrantTutorialLevelCommandHandler` | experience rules | `ICharacterExperienceGrant.ThresholdForLevel`, added to the service that already levels against the same table |
| `SetUserAreaHandler` | entry placement rules | `ITownMovementDomainService` |
| `EnterSelectDungeonHandler`, `SelectDungeonHandler` | `DungeonSelectionRules` across a slice boundary | `IDungeonVisitDomainService.DefersSelectionState` |

**`QuestOfferRefresher` moved out of the Protocol layer entirely.** It was a `public sealed class`
in `Protocol/Game/Quests/` holding a rule service and a domain service, injected into five handlers
— a business service living on the wrong side of the seam. It is now
`IQuestOfferRefreshDomainService` / `QuestOfferRefreshDomainService` in `Application/Quests/`,
registered by `ApplicationModule` rather than `ProtocolModule`.

### Done: the command handlers

| Command handler | Held | Now reaches |
|---|---|---|
| `PickUpGroundDropCommandHandler` | room + placement rules | `IGroundPickupDomainService.FindOnFloorAsync` |
| `MoveItemSpaceCommandHandler` | placement + slot rules | `IItemMovementDomainService` |
| accept / finish / set-trigger quest | quest content rules | `IQuestDefinitionDomainService` |
| kill and clear experience | experience rules | `IExperienceValuationDomainService` |
| the three channel commands | channel content rules | `IChannelDirectoryDomainService` |

Some of these seams carry little of their own, and that is worth saying plainly rather than dressing
up: `IQuestDefinitionDomainService`, `IExperienceValuationDomainService` and
`IChannelDirectoryDomainService` forward to one rule service each. They exist because RQ-005 asks
that a handler reach a rule answer through the service that owns the use case, and the user chose
full satisfaction over the alternatives when shown this cost. Two of them are not thin:
`IGroundPickupDomainService` answers "what is lying under this number", which needs the run and two
rule services and was previously spread across a private helper and a carry-over method;
`IItemMovementDomainService` turns two rule calls into the question the flow actually asks, and its
`MainSlotAdmission` keeps the two refusals apart, which the client draws different sentences for.

### Done: the cross-slice references

Four, all of which `AGENTS.md` forbids under "切片内不跨协议引用。要共用就上提到 endpoint 层".

- `DungeonSelectionRules` — lifted to `IDungeonSelectionRuleService`, reached by both slices through
  `IDungeonVisitDomainService`. The `TutorialLevel = 1` constant went with it.
- `DungeonEntryPayloadCodec.DropInstance` / `MonsterDropInstance` — lifted to
  `Game/GameDropIdentity.cs`, the endpoint layer, where the room that announces a piece, the kill
  that drops one and the pick-up that banks one all reach the same derivation. The mixing is
  unchanged.
- `DungeonClearPayloadCodec.SecretShopNpcId` — lifted to `GameProtocol`.

What remains between slices is message *types* — `DungeonEntrySlice` builds two
`EnterSelectDungeon*` frames and `MonsterDeathSlice` builds two clear-screen frames. Those are
outbound message records rather than rules or shared derivations, they were not in this Task's
declared inputs, and moving them would be a protocol-catalogue change rather than a relocation.
Recorded here rather than silently left.

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-018 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-019 | dependency enumeration over `Server/src/Dfo.Server.Protocol` | Three declarations remain, all `IChannelEntryPolicy`, in `OnlineUserConnectionObserver`, `SelectCharacterHandler` and `TownProjectionSlice` |
| CK-019 | static rule classes called from the Protocol layer | **None** |

The three remaining declarations are endpoint resolution, which `AGENTS.md` names as one of the four
capabilities an ordinary handler may exercise and which this Task's acceptance excludes by name.

Two non-rule configuration types also remain in Protocol and are named here so the enumeration is
complete rather than convenient: `ExperienceDiagnostics` in `MonsterDeathSlice`, classified as
instrumentation by TASK-04's Amendment 1, and `SelectCharacterDeliveryOptions` in
`SelectCharacterHandler`, classified as protocol delivery behaviour in the Plan's Assumptions.

### The flake from TASK-05 now has a name

`QuestLogTests.ProgressRecordedAgainstAValueTheQuestNoLongerHoldsFails` failed once during this
Task's full-suite run and passed in isolation (33/33) and on the two full re-runs that followed. It
is SQLite-backed, which matches the read-then-write contention `AGENTS.md` documents as a
load-dependent false red. TASK-05 recorded an unidentified flake in the same project; this is very
likely the same test. Still not diagnosed — the exception text was not captured either time — but it
is no longer anonymous, and TASK-11's verification run should watch for it.

## Executor Self-check — round 1

- Acceptance "no protocol or command handler declares a rule service or rule-input dependency
  except a sanctioned handler capability": met, CK-019, with each remaining declaration named.
- Acceptance "endpoint resolution through the channel entry policy is left as the sanctioned
  capability it already is": met, untouched in all three places.
- Acceptance "`DungeonSelectionRules` is called only from its rule service": met — the static class
  no longer exists; its one rule and its one constant are in `DungeonSelectionRuleService`.
- Acceptance "suite green at the baseline count": met, 1690/2/0.
- Behaviour preservation, the places it was not a pure substitution:
  - `SelectDungeonHandler` and `MoveMapHandler` no longer assemble the run. The five-frame order,
    the conditional selection-state frame, and every log field are unchanged; the seed is still
    drawn once per run and carried.
  - `MonsterDeathHandler`'s two refusal logs still report the declared count and the actor number,
    which is why `KillIdentification` carries the count rather than returning a bare nullable.
  - `PickUpGroundDropCommandHandler`'s instance derivation still keys a kill's piece on the actor
    and a declared piece on the object index — the branch moved from a private helper into
    `Announced`, and the domain service carries which of the two it was rather than deciding it.
  - `MoveItemSpaceCommandHandler`'s three refusals — `NotWearable`, `ItemCannotOccupySlot`,
    `DisplacedCannotOccupySlot` — are produced by the same conditions in the same order.
- Recovered exception: two over-eager `using` removals. The first dropped
  `Dfo.Server.Protocol.Game.DungeonSelection` and `...DungeonClear` from two slices that still need
  those namespaces for *message types*; caught by the compiler and restored. The second was mine
  inserting `Dfo.*` above `System.*` in eight files; caught by reading the diff and reordered so
  `System` usings stay first, as this repository writes them. Neither reached the result.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **The dungeon-entry extraction is the one that mattered and it is clean.** The handler now sends
   the same five frames built from one value object, and the suite that pins those bytes is green.
   It was done first, against a fresh baseline, for exactly this reason.
2. **`QuestOfferRefresher` leaving the Protocol layer is the largest structural correction here**
   and was not in the Plan's list. It was a business service in the wrong assembly, reached by five
   handlers, and no requirement would have caught it — RQ-005 asks about handlers, and this was not
   one. It was found by reading what actually held rule services rather than by checking the list.
3. **Three of the new seams are pass-throughs.** Recorded as such above rather than described as if
   they earned their keep. The honest accounting is that RQ-005 costs three thin types here, and
   that cost was put to the user before it was paid.
4. **The `GameDropIdentity` lift was checked digit by digit** against the codec it came from: the
   same two multiplies, the same shift widths, the same `& int.MaxValue`, the same zero-to-one
   substitution. The tests that pin a dropped shoulder's value are green.
5. **Scope check.** No file outside the permitted list changed, except `GameProtocol.cs` and the new
   `GameDropIdentity.cs`, which Amendment 2 authorised as the endpoint layer the cross-slice
   references had to move to.
