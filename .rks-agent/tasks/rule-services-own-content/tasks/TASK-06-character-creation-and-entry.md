# TASK-06 — Character creation and entry preparation answers come from rule services

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Two rule services. One owns what a new character starts with and what a name may be, taking the three creation sources and owning `CharacterNameRules`. The other owns what entry must reconcile, taking the three preparation sources.
- Referenced spec requirements: RQ-001, RQ-003, RQ-005, RQ-008
- Referenced plan sections: T-006; boundaries BD-006, BD-007, BD-013, BD-015, BD-017
- Owned boundary: BD-006 and BD-007 rule seams in `Server/src/Dfo.Server.Abstractions/Characters/` and `Server/src/Dfo.Server.Application/Characters/`; the character portions of BD-013; those registrations in BD-015; affected construction sites in BD-017.
- Permitted files or interfaces: the two new rule services and their interfaces; the consumers of `ICharacterStatDefinitionSource`, `IInitialEquipmentDefinitionSource`, `IInitialSkillDefinitionSource`, `ICreatureEquipmentLifetimeSource`, `IExpertSkillContractDefinitionSource` and `ISkillPointDefinitionSource`; `CharacterNameRules`; `CharacterDomainService`; `SelectCharacterPreparationService`; `ApplicationModule.cs`; affected test construction sites.
- Dependencies: TASK-05
- Required dependency outputs: a tree green at TASK-05's accepted state.
- Acceptance conditions: the only consumers of the six sources in `Server/src` are the two new rule services and the infrastructure implementations; `CharacterNameRules` is called only from the creation rule service; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-012 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-013 non-command enumeration of the six sources' consumers and of `CharacterNameRules` call sites.
- Required evidence: suite result against 1690/2/0, the CK-013 enumeration, and the owned diff.

## Contract Amendment 1 — the third equipment-kind constant

- Raised by: TASK-01, from its CK-003 enumeration.
- Contract revision: 2

`CharacterRosterService.cs` holds `private const byte EquipmentItemKind = 1` with the justification
"What the archive's equipment index says every worn item is." — the same value and the same sentence
that TASK-01 consolidated out of `FloorPieces.cs` and `GroundPickupSlice.cs`. The baseline survey did
not list it, so neither the Plan's T-006 inputs nor this Task's original contract named it.

It belongs here rather than to TASK-01 because what it stamps is the item core of a *new character's
starting equipment*, built from `IInitialEquipmentDefinitionSource` — BD-006's subject. TASK-01 left
it deliberately: its consumer is an `ICharacterRepository`, and injecting a rule service into a
repository to read one number would be a worse shape than the duplication it removes.

Added to this Task's inputs: `CharacterRosterService.EquipmentCore` and its `EquipmentItemKind`
constant. Added to its permitted files: `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs`.
Added to its acceptance conditions: `EquipmentItemKind` exists in exactly one place in `Server/src`,
carrying its present value and justification, and the character-creation rule service is what answers
with it — either by holding it or by asking `IItemPlacementRuleService`, which already owns the same
number for the read direction.

The approved Spec boundary is unchanged: this is RQ-004, which was always whole-repository.

## Contract Amendment 2 — Amendment 1's acceptance clause was too strong

- Raised by: this Task, during execution, from its own CK-013 enumeration.
- Contract revision: 3

Amendment 1 required that `EquipmentItemKind` "exists in exactly one place in `Server/src`". That
was written before the whole repository had been enumerated for it, and it is not achievable without
breaking something the Spec protects. There are three homes, not two:

| Home | Kind of thing | Status |
|---|---|---|
| `ItemPlacementRuleService` | rule service (TASK-01) | owner for the read direction |
| `ItemArrivalRuleService` | rule service, pre-existing | one of the six the Spec's non-goals shield: it holds its rules without reading content and is "not required to acquire inputs it does not need" |
| `CharacterRosterService` | **repository** | the one that had no business holding a rule — this Task's actual target |

The clause is restated to what this Task can and should achieve: **the equipment kind leaves the
repository and lives in a rule service.** Three rule services carrying the same archive fact is a
separate question and is recorded in TASK-11's inventory beside the actor-type triple, not answered
here.

## Execution Result — round 1

### What was built

Two rule services in `Server/src/Dfo.Server.Abstractions/Characters/` and
`Server/src/Dfo.Server.Application/Characters/`:

- `ICharacterCreationRuleService` — the three creation sources plus `CharacterNameRules`, the two
  name-bound pairs, the job ceiling, the account slot limit, the passive-skill band and the
  equipment kind. Five answers: `AccountSlotLimit`, `IsKnownJob`, `InspectNameForCheck`,
  `InspectNameForCreation`, `StartingStateAsync`.
- `ICharacterEntryPreparationRuleService` — the three preparation sources. Three answers:
  `SkillPointsAsync`, `OverSkillPremiumTypes`, `CreatureLifetimeAsync`.

`CharacterNameShape` moved from an internal enum in `Application/Characters` to a public one in
`Abstractions/Characters`, because it is now a rule answer that crosses the seam.
`CharacterNameRules` itself stays internal in Application and is called only from the creation rule
service.

`NewCharacterEquipment` gained `byte ItemKind`. That is what let the third copy of the equipment
kind leave `CharacterRosterService`: the repository now stamps the core with the kind it was handed,
which is the same thing its sibling comment already says about stats — "the repository's job is to
store what it is handed".

### What the two consumers are now

`CharacterDomainService` lost three content sources and five constants; `CreateCharacterAsync` asks
one question — what is a character of this job born with — instead of assembling stats, banding
skills and mapping equipment itself. `SelectCharacterPreparationService` lost three constructor
parameters and stopped threading them down through four helper signatures.

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-012 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-013 | `grep -rn` over the six sources and `CharacterNameRules` in `Server/src` | Each of the six appears only in its rule service (plus its declaration and PVF implementation); `CharacterNameRules.Inspect` has exactly two call sites, both in `CharacterCreationRuleService` |

## Executor Self-check — round 1

- Acceptance "the six sources consumed only by the two rule services": met, CK-013.
- Acceptance "`CharacterNameRules` called only from the creation rule service": met.
- Acceptance, as restated by Amendment 2, "the equipment kind leaves the repository": met.
- Acceptance "suite green at the baseline count": met, 1690/2/0.
- Behaviour preservation: the skill banding, the name bounds, the job ceiling, the slot limit and
  the passive-slot base all moved with their justifications verbatim and their values unchanged.
  `StartingStateAsync` performs the same three archive reads in the same order and builds the same
  three collections; `CreateCharacterAsync`'s guard order — job, name, slot count, name taken — is
  unchanged, which matters because each returns a different status.

### An experiment that was tried and withdrawn

I first made `IItemPlacementRuleService` expose `EquipmentItemKind` and had the creation rule service
ask it, so that one rule service owned the number. It built, and then eighteen tests in
`CharacterLifecycleServiceTests` failed with "Unable to resolve service for type
`IItemPlacementDefinitionSource` while attempting to activate `ItemPlacementRuleService`".

That is not a test defect. It is the design telling me the price: creating a character would have
required the item-definition archive source to be registered, which it never has and which several
compositions do not wire. A large coupling to read one number. Withdrawn, and the constant is held
in the creation rule service with a remark saying why. Recorded because "it built" was not enough
information to judge it by, and the suite is what supplied the rest.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **The withdrawn experiment is the finding worth keeping.** It cost one build-and-test cycle and
   it settled a question that would otherwise have been settled by opinion. The remark in
   `CharacterCreationRuleService` carries the reason forward so the next reader does not retry it.
2. **`NewCharacterEquipment` gaining a field was checked against its other reader.**
   `CharacterRosterService.Appearance(equipment)` also walks that list and was unaffected; only
   `EquipmentCore` needed the new field, which is where the constant used to be.
3. **The guard order in `CreateCharacterAsync` was compared line by line** before and after, because
   each guard returns a different `CharacterCreationStatus` and reordering them would change which
   refusal a client sees for a request that fails two.
4. **`SelectCharacterPreparationService`'s helpers were checked for the threading change**: the
   three sources were passed down into four different helper signatures, and every one now takes the
   single rule service. `grep` for the old parameter names returns nothing.
5. **Scope check.** `CharacterRosterService.cs` was touched, which Amendment 1 authorised. No other
   file outside the permitted list changed.
