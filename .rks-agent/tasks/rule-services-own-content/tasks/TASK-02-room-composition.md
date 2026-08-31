# TASK-02 — Room composition answers come from one rule service

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: One rule service owns what a room declares. `IDungeonRoomActorSource` and `IDungeonRoomDropSource` reach the server only through it, and the AI-actor-type and actor-sequence-range constants live in it with their existing justifications.
- Referenced spec requirements: RQ-001, RQ-004, RQ-005, RQ-008
- Referenced plan sections: T-002; boundaries BD-002, BD-013, BD-014, BD-015, BD-017
- Owned boundary: BD-002 room-composition rule seam in `Server/src/Dfo.Server.Abstractions/World/` and `Server/src/Dfo.Server.Application/World/`; the room-source portions of BD-013 and BD-014; the room registration in BD-015; room construction sites in BD-017.
- Permitted files or interfaces: the new room-composition rule service and its interface; `MonsterDropService.cs`; `DungeonEntrySlice.cs`, `MonsterDeathSlice.cs`, `GroundPickupSlice.cs` and the domain services owning the dungeon run and ground pickup; `ApplicationModule.cs`; affected test construction sites.
- Dependencies: TASK-01
- Required dependency outputs: the placement rule service, because dungeon entry and ground pickup are redirected in both tasks and must not be reshaped twice.
- Acceptance conditions: the only consumers of both room sources in `Server/src` are the room-composition rule service and the infrastructure implementations; no protocol or command handler injects a room source; both constants live in the rule service with unchanged values and justifications; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-004 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-005 non-command enumeration of both room sources' consumers.
- Required evidence: suite result against 1690/2/0, the CK-005 enumeration, and the owned diff.

## Execution Result — round 1

### What was built

`IDungeonRoomRuleService` (`Server/src/Dfo.Server.Abstractions/World/`) and `DungeonRoomRuleService`
(`Server/src/Dfo.Server.Application/World/`), taking `IDungeonRoomActorSource` and
`IDungeonRoomDropSource`. It carries both constants with their justifications verbatim —
`FirstAiCharacterActorType = 5` and `ActorSequenceRange = 60000` — and answers six questions:
`DescribeAsync`, `Describe`, `FindDeclaredActorAsync`, `IsCurrentRoomClearedAsync`,
`RemainingGroundDropsAsync`, `DeclaredDropsAsync`, plus `FirstActorSequenceFor`.

Two new carrier types crossing the seam: `RoomActorDescription(DungeonRoomActor, bool IsAiCharacter)`
and `DungeonRoomContents(Actors, GroundDrops, FirstActorSequence)`. The rule answer — is this actor
an ally — travels with the actor, so the frame that draws the room keeps the wire encoding and stops
holding the boundary.

`DungeonEntryPayloadCodec.FirstActorSequence` moved into the rule service as
`FirstActorSequenceFor`, mixing function unchanged line for line.

### Consumers redirected

| Consumer | Layer | Before | After |
|---|---|---|---|
| `MonsterDropService` | Application | `IDungeonRoomDropSource` | `IDungeonRoomRuleService.DeclaredDropsAsync` |
| `SelectDungeonHandler` | Protocol handler | both sources + codec derivation | `DescribeAsync(run, firstEntry: true)` |
| `MoveMapHandler` | Protocol handler | both sources + codec derivation, with its own revisit branch | `DescribeAsync(run, firstEntry)` |
| `MonsterDeathHandler.KilledAsync` | Protocol handler | actor source + cross-slice `FirstActorSequence` | `FindDeclaredActorAsync` |
| `MonsterDeathHandler.ClearedAsync` | Protocol handler | actor source + cross-slice `FirstActorSequence` + `run.IsCleared` | `IsCurrentRoomClearedAsync` |
| `PickUpGroundDropCommandHandler` | Command handler | drop source + `run.FindGroundDrop` | `RemainingGroundDropsAsync` |

### A slice-rule violation resolved on the way

`MonsterDeathSlice` reached into `Game/DungeonEntry/`'s payload codec for `FirstActorSequence`,
which `AGENTS.md` forbids: "切片内不跨协议引用。要共用就上提到 endpoint 层或领域服务". It held only
because both sides were literally one call. Lifting the derivation into the rule service removes it.

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-004 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-005 | `grep -rn` over both room sources in `Server/src` | Only `DungeonRoomRuleService`, plus the interface declarations, the two PVF implementations and their `InfrastructureModule` registrations |

### Owned diff

18 tracked files changed, +178/−141 cumulative with TASK-01, plus the two new source files. The only
`Server/tests` line that touches an assertion is `Assert.Equal(first, Rooms.FirstActorSequenceFor(AtStart))`,
where the changed text is the symbol being called, not the expected value.

### Cross-task findings

1. **Two more cross-slice references remain.** `GroundPickupSlice` and `MonsterDeathSlice` still call
   `DungeonEntryPayloadCodec.DropInstance` / `MonsterDropInstance`. Same slice-rule violation as the
   one resolved here, but these are wire-value derivations rather than rule constants, so they are
   outside this Task's declared inputs. Recorded as an input of TASK-09 by amendment there.
2. **`DungeonRunState.LastHostileActorType = 3`** is a third reading of the monster-or-ally boundary,
   alongside `MonsterExperienceRules.IsMonster` (`<= MonsterRank.Boss`) and the
   `FirstAiCharacterActorType = 5` moved here. Actor type 4 is therefore an ally for clearing, not a
   monster for experience, and a monster for packet numbering. That is present behaviour and is
   preserved untouched. Recorded as an input of TASK-11's RQ-004 inventory by amendment there.
3. **`DungeonRunState.FindGroundDrop` is now reached only from a test.** The rule service composes
   `RemainingGroundDrops` directly. Not removed: the test that uses it asserts the renumbering
   semantics, and removing the method would mean changing that test's subject, which this Spec's
   behaviour-preservation reading forbids. Recorded for TASK-11.

## Executor Self-check — round 1

- Acceptance "both room sources consumed only by the rule service": met, CK-005.
- Acceptance "no protocol or command handler injects a room source": met. Four handlers now take
  `IDungeonRoomRuleService`; TASK-09 owns removing rule services from handlers.
- Acceptance "both constants live in the rule service with unchanged values and justifications": met,
  values 5 and 60000, doc comments copied verbatim.
- Acceptance "suite green at the baseline count": met, 1690/2/0.
- Behaviour preservation, the three places it was not a pure substitution:
  - `MoveMapHandler`: the revisit branch used to skip `FloorPieceValues.DescribeAsync` entirely.
    It is now always called, with an empty drop list on a revisit, and that method returns `[]`
    immediately when the list is empty. Same value, one extra no-op call.
  - `MonsterDeathHandler.KilledAsync`: the "not one of the N this room declares" warning still
    reports the declared count, which is why `DeclaredActorLookup` carries it rather than returning
    a bare nullable actor.
  - `PickUpGroundDropCommandHandler`: `run.FindGroundDrop(declared, n)` is defined as
    `RemainingGroundDrops(declared).FirstOrDefault(c => c.Number == n)`; the handler now takes the
    remaining list from the rule service and applies the same predicate. Identical by definition.
- Deliberately not done: `DungeonRunDomainService` was left alone. The plan's step 3 said to route
  the handlers "through the domain service that owns the run", but 13 of that service's 15
  construction sites are in tests that exercise only the in-memory run registry, and giving it
  archive access would have forced a room stub into every one of them. The composition it would have
  performed is content plus a handed-in run fact producing a value, which is a rule service by the
  repository's own taxonomy, so it went there instead. The plan's Assumption covers this: a use case
  with no owning domain service gains one rather than leaving the read in the handler — and here the
  owner turned out to be the rule layer.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, not an independent context, for the reason recorded in TASK-01's review
  round. Performed as a separate read-only pass over the Task file, the owned diff and plan T-002.

### Findings

1. **The derivation moved without a character change.** `FirstActorSequenceFor` reproduces the two
   multiplies and two shifts, the same constants, and `1 + (mixed % 60000)`. The codec tests that pin
   its scattering and stability now call it through the rule service and still pass, which is the
   check that would have caught a transcription slip.
2. **The AI-character boundary is production's, not the fixture's.** The codec tests were rewired to
   `Rooms.Describe(actors)` rather than to hand-built `RoomActorDescription` values, so the packet
   index assertions still test the boundary rather than agree with whatever the test asserted. This
   was the one place the migration could have made a test vacuous, and it was the reason `Describe`
   was put on the interface separately from `DescribeAsync`.
3. **The revisit path was checked specifically**, because it is the one behaviour a live session
   caught before: walking back out of the boss room used to resurrect five and four monsters under
   the numbers they died with. `DescribeAsync(run, firstEntry: false)` returns empty actors and empty
   drops and still the same sequence base, which is what the handler's branch produced.
4. **The `MonsterDropService` redirect is one call for one call**, and the `declared.Count` it feeds
   to `AllocateMonsterDrops` comes from the same source through the same read.
5. **Scope check against the sealed contract.** No file outside the permitted list was changed. The
   two things found and not done — the remaining cross-slice codec calls, and the third actor-type
   boundary — are both recorded as amendments on the Tasks that own them, not left in prose.

### Round identity

Result round 1, its self-check, this review and the working tree at the 1690/2/0 run all describe the
same state: four new source files across TASK-01 and TASK-02, 18 changed tracked files.
