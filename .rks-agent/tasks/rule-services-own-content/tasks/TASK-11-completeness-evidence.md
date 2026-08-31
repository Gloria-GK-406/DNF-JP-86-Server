# TASK-11 — The result is demonstrated complete

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: The inventories and suite evidence the Spec's evidence classes require: a rule-input inventory over every content source and game-rule configuration value with each entry resolved to its owning rule service, a call-site enumeration for every pure rule function, a per-constant record of home, value and justification, a dependency enumeration over rule services, and a full suite run with the archive reachable together with the behavioural-suite diff.
- Referenced spec requirements: RQ-001, RQ-002, RQ-003, RQ-004, RQ-006, RQ-008
- Referenced plan sections: T-011; boundaries BD-013, BD-014, BD-017
- Owned boundary: no product change of its own. It records evidence over the completed solution and may only correct a defect the inventories expose, inside the boundaries the earlier tasks already own.
- Permitted files or interfaces: the Task file's own evidence sections; any file whose defect the inventories expose, inside the earlier tasks' declared boundaries.
- Dependencies: TASK-10
- Required dependency outputs: the completed solution and revised conventions record.
- Acceptance conditions: the rule-input inventory has zero unresolved entries; no rule service declares a repository, registry, directory, store, or any other world-state reader or writer; the suite is green at the baseline count; the behavioural-suite diff changes no assertion, expected value or test case.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: none beyond enumeration and the suite.
- Required checks: CK-022 non-command rule-input inventory with unresolved entries counted; CK-023 non-command dependency enumeration over every rule service; CK-024 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-025 `git diff --stat main -- Server/tests`.
- Required evidence: the four check results, with CK-022 stating the unresolved count as zero and CK-025 accompanied by a reading of the behavioural diff.

## Contract Amendment 1 — two items the RQ-004 inventory must resolve explicitly

- Raised by: TASK-02.
- Contract revision: 2

1. **Three readings of the monster-or-ally boundary.** `MonsterExperienceRules.IsMonster` answers
   `<= MonsterRank.Boss`; `DungeonRunState.LastHostileActorType = 3` skips anything above it when
   testing whether a room is cleared; `DungeonRoomRuleService.FirstAiCharacterActorType = 5` decides
   the packet index. Actor type 4 is consequently an ally for clearing, not a monster for experience,
   and a monster for numbering. This is present behaviour and this task preserves it. The inventory
   must record all three homes and say which rule service owns each, rather than counting the subject
   once; unifying them would be a behaviour change and is out of scope.
2. **`DungeonRunState.FindGroundDrop` is reached only from a test** since TASK-02 gave the rule
   service `RemainingGroundDropsAsync`. Record it as production code with no production caller. It
   was not removed because the test that uses it asserts the renumbering semantics, and changing that
   test's subject is what this Spec's behaviour-preservation reading forbids.

## Contract Amendment 2 — two classifications the inventory must state, not skip

- Raised by: TASK-04.
- Contract revision: 3

1. **`FixedRewardSlotCount = 23`** (`DungeonClearSlice.cs`) was listed in the Plan as an experience
   rule constant and reclassified out during TASK-04: it counts the four-byte zero slots the
   clear-reward frame writes, sits beside `ClearRewardBodyLength = 274`, and is tied to it by the
   method's own length assertion. Packet layout, not archive or game semantics. The inventory must
   record it as classified out with this reason, not omit it.
2. **`ExperienceDiagnostics`** is configuration read from `Experience:LabelProbe` and consumed by
   `MonsterDeathSlice`. Classified as instrumentation rather than a game-rule value — it makes a kill
   pay distinct amounts into each named line so the client labels them, which is a measurement switch
   of the same family as the deployment configuration the user excluded. The inventory must record
   the classification and its reason.

## Contract Amendment 3 — one archive fact, three rule-service homes

- Raised by: TASK-06.
- Contract revision: 4

`private const byte EquipmentKind = 1` — "what the archive's equipment index says every worn item
is" — now lives in three rule services:

| Home | Why it is there |
|---|---|
| `ItemPlacementRuleService` | TASK-01's owner for the read direction: is this item equipment |
| `ItemArrivalRuleService` | pre-existing; decides which bands an arriving item is offered. One of the six rule services the Spec's non-goals shield from acquiring inputs they do not need |
| `CharacterCreationRuleService` | TASK-06's owner for the write direction: what kind a new character's worn piece is stamped with |

TASK-06 tried making the first of these the single owner and withdrew it: creating a character would
then have required `IItemPlacementDefinitionSource` to be registered, which broke eighteen tests
whose compositions do not wire it. The reason is recorded in `CharacterCreationRuleService`.

The inventory must record all three homes and the reason there are three, rather than counting the
subject once and showing a clean result that is not the whole truth. Unifying them is a design
change with a real coupling cost, not a relocation, and is therefore outside this Spec.

## Execution Result — round 1

### CK-022 — the rule-input inventory. Unresolved entries: **zero**

**Archive content sources — 29, each consumed by exactly one rule service.**

| Source | Owning rule service |
|---|---|
| `IChannelCatalog`, `IChannelScriptSource` | `ChannelContentRuleService` |
| `ICharacterStatDefinitionSource`, `IInitialSkillDefinitionSource`, `IInitialEquipmentDefinitionSource` | `CharacterCreationRuleService` |
| `ISkillPointDefinitionSource`, `IExpertSkillContractDefinitionSource`, `ICreatureEquipmentLifetimeSource` | `CharacterEntryPreparationRuleService` |
| `IDropRateTableSource`, `IGoldDropTableSource`, `IDropItemPoolSource` | `DropRuleService` |
| `IDungeonDefinitionSource` | `DungeonLayoutRuleService` |
| `IDungeonRoomActorSource`, `IDungeonRoomDropSource` | `DungeonRoomRuleService` |
| `IExperienceParameterSource`, `IDungeonRankGradeSource`, `IDungeonExperienceProgressionPolicySource`, `IChannelExperienceSource` | `ExperienceRuleService` |
| `IEntryLifecycleContentSource` | `InventoryLifecycleRuleService` |
| `IItemPlacementDefinitionSource`, `IEquipmentSlotDefinitionSource` | `ItemPlacementRuleService` |
| `IItemUseRuleDefinitionSource` | `ItemUseJudgementService` |
| `IQuestDefinitionSource`, `IQuestParameterSource` | `QuestContentRuleService` |
| `ITownDefinitionSource` | `EntryPlacementRuleService` |
| `ITutorialRewardDefinitionSource` | `TutorialRewardRuleService` |
| `IDailyActivityDefinitionSource`, `ICollectionBoxDefinitionSource`, `ICreatureDefinitionSource` | `WorldContentEntryRuleService` |

At the baseline 42 of these injection points sat outside a rule service. Now none does.
`ItemUseJudgementService` is a rule service that shares a file with `PlayingCharacter`; it is the
only entry whose home is not obvious from the file name.

**Game-rule configuration values — 4, each consumed by exactly one rule service.**

| Value | Owning rule service |
|---|---|
| `DropRateOptions` | `DropRuleService` |
| `ChannelEntryOptions` | `ChannelEntryPolicy`, `EntryPlacementRuleService` — both rule services |
| `ExperienceTuning` | `ExperienceRuleService` |
| `ExperienceEntitlementPolicy` | `ExperienceRuleService`. Its second appearance is in `ExperienceBonusRules.cs`, which declares the type and takes it as a *parameter* of a pure rule function — the rules being handed the configuration, which is the intended direction |

**Two values classified out, with their reasons, rather than omitted.**

| Value | Consumer | Classification |
|---|---|---|
| `ExperienceDiagnostics` | `MonsterDeathSlice` | A measurement switch. It makes a kill pay distinct amounts into each named line so the client labels them; it moves nothing a player keeps. Same family as the host deployment configuration the user excluded |
| `SelectCharacterDeliveryOptions` | `SelectCharacterHandler` | Protocol delivery behaviour: whether frames follow a completion marker. Client compatibility, not a rule the game plays by |

**Host deployment configuration, excluded by the user's own decision**, is unchanged: five
`IServerConfigService` references, identical to `8af7990c` line for line.

### CK-022 — pure rule functions. Call sites outside a rule service: **zero**

| Static rule class | Called from |
|---|---|
| `DropRules` | `DropRuleService` only. The one other grep hit is a comment in `ApplicationModule` |
| `MonsterExperienceRules` | `ExperienceRuleService` only |
| `DungeonClearExperienceRules` | `ExperienceRuleService` only |
| `ExperienceBonusRules` | `ExperienceRuleService` only |
| `CharacterNameRules` | `CharacterCreationRuleService` only |
| `DungeonSelectionRules` | Gone. It was a static class shared across two slices, which the slice rules forbid; it is now `DungeonSelectionRuleService` |

At the baseline none of the six was reached through a rule service and one was called from a
protocol handler.

### CK-022 — relocated constants. Values and justifications preserved

| Constant | Baseline | Now | Home |
|---|---|---|---|
| equipment kind | 1 | 1 | `ItemPlacementRuleService`, `CharacterCreationRuleService`, `ItemArrivalRuleService` |
| `FirstAiCharacterActorType` | 5 | 5 | `DungeonRoomRuleService` |
| `ActorSequenceRange` | 60000 | 60000 | `DungeonRoomRuleService` |
| `TutorialLevel` | 1 | 1 | `DungeonSelectionRuleService` |
| `FirstPassiveSlot` | 102 | 102 | `CharacterCreationRuleService` |
| `MaxJobId` | 12 | 12 | `CharacterCreationRuleService` |
| name bounds | 2 / 30 / 2 / 18 | 2 / 30 / 2 / 18 | `CharacterCreationRuleService` |
| account slot limit | 32 | 32 | `CharacterCreationRuleService` |
| `FixedRewardSlotCount` | 23 | 23 | `DungeonClearSlice` — classified out, see below |
| `SecretShopNpcId` | 1000 | 1000 | `GameProtocol` — classified out, see the CK-022 amendment at the end of this file |

Two were renamed while moving, values unchanged: `EquipmentItemKind` → `EquipmentKind`, and
`DefaultSlotLimit = 32` → `AccountSlotLimit => 32`. Both carry their original justification comment.

### The four things this inventory is required to state rather than tidy away

1. **One archive fact, three rule-service homes.** `EquipmentKind = 1` lives in
   `ItemPlacementRuleService` (is this equipment), `CharacterCreationRuleService` (what a new
   character's worn piece is stamped with) and `ItemArrivalRuleService` (which bands an arriving
   item is offered). TASK-06 tried making the first the single owner and withdrew it: creating a
   character would then have required `IItemPlacementDefinitionSource` to be registered, which broke
   eighteen tests whose compositions do not wire it. `ItemArrivalRuleService` is additionally one of
   the six rule services the Spec's non-goals shield from acquiring inputs they do not need.
   Unifying them is a design change with a real coupling cost, not a relocation, and is outside this
   Spec.

2. **Three readings of the monster-or-ally boundary, all preserved.**
   `MonsterExperienceRules.IsMonster` answers `<= MonsterRank.Boss`;
   `DungeonRunState.LastHostileActorType = 3` skips anything above it when testing whether a room is
   cleared; `DungeonRoomRuleService.FirstAiCharacterActorType = 5` decides the packet index. Actor
   type 4 is therefore an ally for clearing, not a monster for experience, and a monster for
   numbering. That is present behaviour. Unifying them would change what the server does and is out
   of scope; `LastHostileActorType` also sits on a domain record's pure method rather than in a rule
   service, which is a residual RQ-004 gap recorded here rather than closed.

3. **`FixedRewardSlotCount = 23` is classified out.** The Plan listed it as an experience rule
   constant. Read in place it counts the four-byte zero slots the clear-reward frame writes, sits
   beside `ClearRewardBodyLength = 274`, and the method that uses both ends with a length assertion
   tying them together. Packet layout, not archive or game semantics.

4. **`DungeonRunState.FindGroundDrop` has no production caller.** TASK-02 gave the rule service
   `RemainingGroundDropsAsync`, which composes the same predicate. It was not removed because the
   test that uses it asserts the renumbering semantics, and changing that test's subject is what
   this Spec's behaviour-preservation reading forbids.

### CK-023 — rule-service dependencies. World-state readers or writers: **zero**

Eighteen rule services enumerated. Every declared dependency is a content source, a game-rule
configuration value, or another rule service. **No repository, registry, directory, store,
persistence, table or domain service appears in any of them.**

Five hold nothing at all — `GoldLimitRuleService`, `ItemArrivalRuleService`, `ItemSlotRuleService`,
`DungeonSelectionRuleService`, `ExpertSkillContractRuleService`, `DungeonEffectRuleService`,
`DailyRefillRuleService` — which is the six the Spec's non-goals named, plus the one this task
created from a static class.

`Docs/领域结构重构设计.md` §6 therefore still holds, and now holds visibly: it can be checked by
reading constructors.

### CK-024 — the suite, with the archive reachable

1690 passed, 2 skipped, 0 failed. Identical to the baseline measured at `8af7990c`.

The two skips are `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
and `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
— they skip *because* the archive is present, which is what proves the run was made with it.

### CK-025 — the behavioural diff

`git diff --stat main -- Server/tests`: 43 files, +555 / −109.

| Measure | Count |
|---|---|
| `[Fact]` / `[Theory]` / `[SkippableFact]` added | **0** |
| `[Fact]` / `[Theory]` / `[SkippableFact]` removed | **0** |
| `Assert.` lines added | 1 |
| `Assert.` lines removed | 1 |

The one changed assertion is
`Assert.Equal(first, DungeonEntryPayloadCodec.FirstActorSequence(AtStart))` becoming
`Assert.Equal(first, Rooms.FirstActorSequenceFor(AtStart))` — the symbol being called changed, the
expected value did not.

The 555 insertions are constructor argument lists wrapped across more lines, and new stub classes
that answer nothing: `NoWearables`, `NoQuestParameters`, `NoQuestDefinitions`, `NoRoomActors`,
`NoChannelScript`, `NoChannelCatalog`, `UnpricedKills`, `UngradedRanks`, `NoProgression`,
`NoChannelExperience`, `NoQuestContent`, `UnusedExpertContracts`, `UnusedCreatureLifetimes`,
`KindOnlyPlacements`. Most of them throw rather than returning a value, so a path that starts
needing one fails loudly instead of quietly using an invented number.

### The flake, disclosed rather than smoothed over

`QuestLogTests.ProgressRecordedAgainstAValueTheQuestNoLongerHoldsFails` failed once during TASK-09's
suite run and once, unidentified, during TASK-05's. It passed in isolation (33/33) and on every
re-run since, including this task's CK-024. It is SQLite-backed, which matches the load-dependent
read-then-write contention `AGENTS.md` documents as a false red. Not diagnosed: the exception text
was never captured. Recorded so that a future run seeing it knows it has been seen twice before.

## Executor Self-check — round 1

- Acceptance "the rule-input inventory has zero unresolved entries": met. 29 content sources, 4
  configuration values, 5 static rule classes, all resolved to one owning rule service each; 2
  values classified out with reasons; 1 exclusion unchanged by the user's decision.
- Acceptance "no rule service declares a world-state reader or writer": met, CK-023.
- Acceptance "the suite is green at the baseline count": met, CK-024, 1690/2/0.
- Acceptance "the behavioural diff changes no assertion, expected value or test case": met, CK-025.
- Four residual items are stated in the inventory rather than resolved, each with the reason and
  each out of this Spec's scope. None of them is a rule input consumed outside a rule service.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **The inventory was built by sweeping, not by reading back the earlier tasks' claims.** Every
   number here comes from a fresh `grep` over the finished tree. That matters because the routing
   decision named "a partial migration that looks finished" as the first failure mode, and the
   defence against it is a count taken at the end rather than a sum of the counts taken along the
   way.
2. **Zero test cases added or removed is the strongest single line of evidence** that the behaviour
   is the behaviour. Combined with a green suite at the identical count, the only way a value could
   have changed is in a path no test covers — which is why the four residual items above are stated
   rather than assumed harmless.
3. **The three-homes and three-boundaries items are the honest cost of this refactor.** Both were
   found by this work and neither is closed by it. Recording them here is what stops the next reader
   concluding the subject is settled.
4. **`ItemUseJudgementService` living in `PlayingCharacter.cs`** is the one place the inventory
   needed prose to be readable. Worth a file move some day; not this task's business.

## Amendment to CK-022, from the independent change review

The independent reviewer found one constant this change **moved** that the inventory above did not
list: `GameProtocol.SecretShopNpcId = 1000`, lifted out of `DungeonClearPayloadCodec` in TASK-09 so
that two slices could stop reaching across for it. Its value is unchanged (1000 at `8af7990c`, 1000
now) and its justification comment travelled with it, so this is an evidence gap rather than a
behaviour defect — but CK-022 claimed zero unresolved entries while one relocated constant was
unaccounted for.

Classified, with the same test the other constants were judged by: it names **an NPC the result
screen puts on the wire**, in the same family as `ClearRewardBodyLength` and `FixedRewardSlotCount`
— frame content, not a rule the game plays by. It stays at the `Game` endpoint layer, which is where
AGENTS.md says a thing two slices share belongs.

With that entry recorded, CK-022 stands: **zero unresolved entries.**
