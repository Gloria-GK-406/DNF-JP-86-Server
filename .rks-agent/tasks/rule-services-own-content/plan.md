---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Rule Services Own Every Rule Input

## Implementation Approach

The Spec's core outcome is that a rule input reaches the server only through a rule service. The
necessary support is that behaviour is preserved value for value and that §6 keeps holding. The
peripheral assurance is the inventories and the suite run, which land at the end.

The minimal approach is to give each rule input an owning rule service grouped by rule subject, then
change its consumers to ask that owner. Ownership is settled here, in Owned Boundaries, before any
consumer is touched — that is the ordering constraint this controller was chosen for. Sources shared
by many consumers come first, because their allocation constrains the most call sites; the two most
shared, item placement and room composition, are therefore T-001 and T-002.

No task precedes the first core task. Each task is vertical: it creates one owner, moves that
owner's inputs, redirects that owner's consumers, and runs the suite before the next task starts.
The suite is the behaviour oracle throughout, so every task ends green rather than accumulating a
red middle.

Where a protocol handler currently reads a rule input, the handler is redirected to the domain
service that already owns its use case, and that domain service consults the rule service. No new
domain service is introduced where an existing one owns the flow.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Inventory/` and `Server/src/Dfo.Server.Application/Inventory/` item-placement rule seam | Add a rule service owning where an item may go and what kind it is | `IItemPlacementDefinitionSource`, `IEquipmentSlotDefinitionSource`, the equipment-kind constant | Placement and slot answers |
| BD-002 | `Server/src/Dfo.Server.Abstractions/World/` and `Server/src/Dfo.Server.Application/World/` room-composition rule seam | Add a rule service owning what a room declares | `IDungeonRoomActorSource`, `IDungeonRoomDropSource`, the AI-actor-type and actor-sequence-range constants | Room actor and floor-piece answers |
| BD-003 | Quest-content rule seam in `Server/src/Dfo.Server.Abstractions/Quests/` and `Server/src/Dfo.Server.Application/Quests/` | Add a rule service owning quest definitions and parameters | `IQuestDefinitionSource`, `IQuestParameterSource` | Quest definition and parameter answers |
| BD-004 | Experience rule seam in `Server/src/Dfo.Server.Abstractions/World/` and `Server/src/Dfo.Server.Application/World/` | Add a rule service owning what a kill, a clear and a channel are worth | `IExperienceParameterSource`, `IDungeonExperienceProgressionPolicySource`, `IDungeonRankGradeSource`, `IChannelExperienceSource`, `MonsterExperienceRules`, `DungeonClearExperienceRules`, `ExperienceBonusRules`, the fixed-reward-slot constant | Experience and bonus answers |
| BD-005 | Drop rule seam in `Server/src/Dfo.Server.Abstractions/World/` and `Server/src/Dfo.Server.Application/World/` | Add a rule service owning what an occasion drops | `IDropRateTableSource`, `IGoldDropTableSource`, `IDropItemPoolSource`, `DropRateOptions`, `DropRules` | Rolled-drop answers |
| BD-006 | Character-creation rule seam in `Server/src/Dfo.Server.Abstractions/Characters/` and `Server/src/Dfo.Server.Application/Characters/` | Add a rule service owning what a new character starts with and what a name may be | `ICharacterStatDefinitionSource`, `IInitialEquipmentDefinitionSource`, `IInitialSkillDefinitionSource`, `CharacterNameRules` | Starting-state and name answers |
| BD-007 | Character-entry preparation rule seam in `Server/src/Dfo.Server.Abstractions/Characters/` and `Server/src/Dfo.Server.Application/Characters/` | Add a rule service owning what entry must reconcile | `ICreatureEquipmentLifetimeSource`, `IExpertSkillContractDefinitionSource`, `ISkillPointDefinitionSource` | Entry reconciliation answers |
| BD-008 | `IItemUseJudgementService` and its implementation | Extend the existing judgement rule service to own item-use content | `IItemUseRuleDefinitionSource` | Item-use answers |
| BD-009 | Inventory-lifecycle rule seam in `Server/src/Dfo.Server.Abstractions/Inventory/` and `Server/src/Dfo.Server.Application/Inventory/` | Add a rule service owning entry-lifecycle content | `IEntryLifecycleContentSource` | Lifecycle grant answers |
| BD-010 | Tutorial-reward rule seam in `Server/src/Dfo.Server.Abstractions/` and `Server/src/Dfo.Server.Application/` | Add a rule service owning what a tutorial step grants | `ITutorialRewardDefinitionSource` | Tutorial grant answers |
| BD-011 | Channel-content rule seam in `Server/src/Dfo.Server.Abstractions/Channel/` and `Server/src/Dfo.Server.Application/Channel/` | Add a rule service owning channel catalogue and script content | `IChannelCatalog`, `IChannelScriptSource` | Channel catalogue and script answers |
| BD-012 | Dungeon-selection rule seam in `Server/src/Dfo.Server.Abstractions/World/` and `Server/src/Dfo.Server.Application/World/` | Add a rule service owning the selection rules two slices share today | `DungeonSelectionRules` | Dungeon selection answers |
| BD-013 | Consuming domain services under `Server/src/Dfo.Server.Application/` | Replace direct rule-input reads with rule-service calls | Rule services | Unchanged domain behaviour |
| BD-014 | Consuming slices and command handlers under `Server/src/Dfo.Server.Protocol/Game/` and `Server/src/Dfo.Server.Protocol/Channel/` | Replace direct rule-input reads and direct rule-service calls with domain-service calls | Domain services | Unchanged frames |
| BD-015 | `Server/src/Dfo.Server.Application/ApplicationModule.cs`, `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`, `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | Register each new rule service at its layer's single wiring point | Rule service types | Resolvable graph |
| BD-016 | `AGENTS.md` | Restate the layering as successive layers rather than alternatives | Current taxonomy table | Convention record matching the code |
| BD-017 | `Server/tests/` | Update construction sites for changed constructors | Changed signatures | Suite green at baseline count |

## Ordered Tasks

### T-001: Item placement answers come from one rule service

- Covers: RQ-001, RQ-004, RQ-005, RQ-008
- Boundaries: BD-001, BD-013, BD-014, BD-015, BD-017
- Inputs: the six consumers of `IItemPlacementDefinitionSource`, the one consumer of `IEquipmentSlotDefinitionSource`, the equipment-kind constant in the ground-pickup command handler
- Outputs: one placement rule service; those consumers asking it or their domain service

#### Steps

1. Create the placement rule service taking `IItemPlacementDefinitionSource` and
   `IEquipmentSlotDefinitionSource`, exposing the placement and slot answers its current callers
   derive today, and carrying the equipment-kind constant with its existing justification comment.
2. Redirect the Application consumers — the persistent item container and the entry inventory
   domain service — to the rule service.
3. Redirect the Protocol consumers — dungeon entry, floor pieces, ground pickup, move item space —
   through the domain service that owns each use case, so no handler injects the source.
4. Register the rule service in the Application wiring point and remove the now-unused source
   registrations from consumers.
5. Update test construction sites for the changed constructors.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | Builds with no new warning |
| CK-002 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set to the A21 archive | 1690 passing, 2 skipped, 0 failing |
| CK-003 | Non-command: enumerate the consumers of `IItemPlacementDefinitionSource` and `IEquipmentSlotDefinitionSource` | Only the placement rule service |

### T-002: Room composition answers come from one rule service

- Depends on: T-001
- Covers: RQ-001, RQ-004, RQ-005, RQ-008
- Boundaries: BD-002, BD-013, BD-014, BD-015, BD-017
- Inputs: the consumers of `IDungeonRoomActorSource` and `IDungeonRoomDropSource`, the AI-actor-type and actor-sequence-range constants
- Outputs: one room-composition rule service; the drop service and the dungeon-run flow asking it

#### Steps

1. Create the room-composition rule service taking both room sources and carrying the two constants
   with their existing justifications.
2. Redirect the monster drop service to it.
3. Redirect the dungeon-entry and monster-death handlers through the domain service that owns the
   run, so neither handler injects a room source.
4. Redirect the ground-pickup command handler's declared-drop read to the same owner.
5. Register the rule service and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-005 | Non-command: enumerate the consumers of both room sources | Only the room-composition rule service |

### T-003: Quest content answers come from one rule service

- Depends on: T-002
- Covers: RQ-001, RQ-005, RQ-008
- Boundaries: BD-003, BD-013, BD-014, BD-015, BD-017
- Inputs: the five remaining consumers of `IQuestDefinitionSource`, the two of `IQuestParameterSource`, and the existing world-content rule service that already holds one
- Outputs: one quest-content rule service; quest commands reaching it through their domain service

#### Steps

1. Create the quest-content rule service taking both quest sources.
2. Point the existing world-content rule service at it rather than at the raw source, so one owner
   remains.
3. Redirect the character entry-state and quest entry domain services to it.
4. Redirect the accept, finish and trigger command handlers through the quest log domain service.
5. Register and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-007 | Non-command: enumerate the consumers of both quest sources | Only the quest-content rule service |

### T-004: Experience answers come from one rule service

- Depends on: T-003
- Covers: RQ-001, RQ-003, RQ-004, RQ-005, RQ-008
- Boundaries: BD-004, BD-013, BD-014, BD-015, BD-017
- Inputs: the four experience-related sources, the three experience static rule classes, the fixed-reward-slot constant
- Outputs: one experience rule service; the kill and clear award flows asking it

#### Steps

1. Create the experience rule service taking the four sources, and move the three static rule
   classes behind it so they are called from it alone.
2. Move the fixed-reward-slot constant into it with its existing justification.
3. Redirect the experience grant and dungeon-effect recovery domain services to it.
4. Redirect the kill-experience, clear-experience and tutorial-level command handlers, and the
   monster-death handler's hostile-actor question, through their domain services.
5. Register and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-009 | Non-command: enumerate call sites of the three experience static rule classes | Only the experience rule service |

### T-005: Drop answers come from one rule service

- Depends on: T-004
- Covers: RQ-001, RQ-002, RQ-003, RQ-005, RQ-008
- Boundaries: BD-005, BD-013, BD-015, BD-017
- Inputs: the three drop sources, `DropRateOptions`, `DropRules`
- Outputs: one drop rule service; the monster drop service reduced to flow

#### Steps

1. Create the drop rule service taking the three drop sources and `DropRateOptions`, and move the
   rate, gate, rarity, pick and gold-amount evaluation into it so `DropRules` is called from it
   alone.
2. Reduce the monster drop service to asking for rates, asking for a draw, allocating numbers and
   logging.
3. Confirm the rate basis is still read from configuration and still passed in rather than
   defaulted inside the rules.
4. Register and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-011 | Non-command: enumerate call sites of `DropRules` and consumers of the three drop sources and `DropRateOptions` | Only the drop rule service |

### T-006: Character creation and entry preparation answers come from rule services

- Depends on: T-005
- Covers: RQ-001, RQ-003, RQ-005, RQ-008
- Boundaries: BD-006, BD-007, BD-013, BD-015, BD-017
- Inputs: the three creation sources, the three preparation sources, `CharacterNameRules`
- Outputs: two rule services; the character domain service and preparation service asking them

#### Steps

1. Create the character-creation rule service taking the three creation sources, and move
   `CharacterNameRules` behind it.
2. Create the entry-preparation rule service taking the three preparation sources.
3. Redirect the character domain service and the select-character preparation service to them.
4. Register and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-013 | Non-command: enumerate consumers of the six sources and call sites of `CharacterNameRules` | Only the two new rule services |

### T-007: Item use and inventory lifecycle answers come from rule services

- Depends on: T-006
- Covers: RQ-001, RQ-005, RQ-008
- Boundaries: BD-008, BD-009, BD-013, BD-015, BD-017
- Inputs: the two consumers of `IItemUseRuleDefinitionSource`, the one of `IEntryLifecycleContentSource`
- Outputs: item-use content owned by the existing judgement rule service; one lifecycle rule service

#### Steps

1. Give the existing item-use judgement rule service the item-use content source, and redirect the
   playing-character and item-use-effect domain services to it.
2. Create the inventory-lifecycle rule service taking the entry-lifecycle content source, and
   redirect the entry inventory domain service to it.
3. Register and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-014 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-015 | Non-command: enumerate consumers of both sources | Only the two rule services |

### T-008: Tutorial reward and channel content answers come from rule services

- Depends on: T-007
- Covers: RQ-001, RQ-005, RQ-008
- Boundaries: BD-010, BD-011, BD-013, BD-014, BD-015, BD-017
- Inputs: the tutorial reward source, the channel catalogue and script sources, the existing channel entry policy
- Outputs: two rule services; the tutorial handler and channel commands reaching them through domain services

#### Steps

1. Create the tutorial-reward rule service and redirect the tagged-id report handler through the
   domain service that owns the tutorial grant.
2. Create the channel-content rule service taking the catalogue and script sources, and point the
   existing channel entry policy at it so one owner remains.
3. Redirect the three channel commands to it.
4. Register and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-016 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-017 | Non-command: enumerate consumers of the tutorial, catalogue and script sources | Only the two rule services and the channel entry policy |

### T-009: The Protocol layer stops reaching rules directly

- Depends on: T-008
- Covers: RQ-003, RQ-005, RQ-008
- Boundaries: BD-012, BD-014, BD-015, BD-017
- Inputs: the four protocol-layer calls into rule services, `DungeonSelectionRules` shared by two slices
- Outputs: handlers reaching rules only through domain services

#### Steps

1. Create the dungeon-selection rule service owning `DungeonSelectionRules`, which two slices share
   today in violation of the slice rule that forbids cross-slice reference.
2. Redirect the dungeon-entry and dungeon-selection slices to it through their domain service.
3. Redirect the move-item-space slice, the quest offer refresher and the town projection slice to
   reach their rule services through the domain services that own those flows.
4. Leave endpoint resolution through the channel entry policy as the sanctioned handler capability
   it already is.
5. Register and update test construction sites.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-018 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-019 | Non-command: enumerate rule-service and rule-input dependencies declared by protocol and command handlers | Only sanctioned handler capabilities remain, each identifiable as such |

### T-010: The conventions record describes the layering as a stack

- Depends on: T-009
- Covers: RQ-007, RQ-009
- Boundaries: BD-016
- Inputs: the current taxonomy table and handler rules in `AGENTS.md`
- Outputs: a record a reader can apply without reproducing the drift

#### Steps

1. Replace the reading in which rule services and pure rule functions are alternatives with one in
   which a business service consults a rule service and a rule service calls a pure rule function.
2. State that a rule input is read by a rule service and by nothing else, and that host deployment
   configuration is not a rule input.
3. Leave the existing record of the deployment-configuration exclusion and the four sanctioned
   handler capabilities as they are.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-020 | Non-command: read the revised record against the completed structure | Every statement matches the code, and no statement permits a business service or handler to read a rule input |
| CK-021 | Non-command: compare host deployment configuration consumers against the baseline | Unchanged, and the exclusion note unchanged |

### T-011: The result is demonstrated complete

- Depends on: T-010
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-006, RQ-008
- Boundaries: BD-013, BD-014, BD-017
- Inputs: the completed solution
- Outputs: the inventories and suite evidence the Spec's evidence classes require

#### Steps

1. Produce the rule-input inventory over every content source and game-rule configuration value,
   resolving each entry to its owning rule service.
2. Produce the call-site enumeration for every pure rule function and the per-constant record of
   home, value and justification.
3. Produce the dependency enumeration over rule services.
4. Run the full suite with the archive reachable and diff the behavioural suite against the
   baseline.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-022 | Non-command: rule-input inventory with unresolved entries counted | Zero unresolved entries |
| CK-023 | Non-command: dependency enumeration over every rule service | No repository, registry, directory or store, and no other world-state reader or writer |
| CK-024 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passing, 2 skipped, 0 failing |
| CK-025 | `git diff --stat main -- Server/tests` | Only identifier and construction changes; no changed assertion, expected value or test case |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-002, T-003, T-004, T-005, T-006, T-007, T-008, T-011 | BD-001, BD-002, BD-003, BD-004, BD-005, BD-006, BD-007, BD-008, BD-009, BD-010, BD-011 | CK-003, CK-005, CK-007, CK-011, CK-013, CK-015, CK-017, CK-022 |
| RQ-002 | T-005, T-011 | BD-005 | CK-011, CK-022 |
| RQ-003 | T-004, T-005, T-006, T-009, T-011 | BD-004, BD-005, BD-006, BD-012 | CK-009, CK-011, CK-013, CK-019, CK-022 |
| RQ-004 | T-001, T-002, T-004, T-011 | BD-001, BD-002, BD-004 | CK-003, CK-005, CK-022 |
| RQ-005 | T-001, T-002, T-003, T-004, T-005, T-006, T-007, T-008, T-009 | BD-013, BD-014, BD-015 | CK-003, CK-005, CK-007, CK-013, CK-015, CK-017, CK-019 |
| RQ-006 | T-011 | BD-013 | CK-023 |
| RQ-007 | T-010 | BD-016 | CK-021 |
| RQ-008 | T-001, T-002, T-003, T-004, T-005, T-006, T-007, T-008, T-009, T-011 | BD-013, BD-014, BD-017 | CK-001, CK-002, CK-004, CK-006, CK-008, CK-010, CK-012, CK-014, CK-016, CK-018, CK-024, CK-025 |
| RQ-009 | T-010 | BD-016 | CK-020 |

## Risks and Rollback

- Risk: a rule input shared by consumers in different subject areas is allocated to one owner and
  the other consumer then reaches across subjects. Mitigation: ownership is fixed in Owned
  Boundaries before any consumer changes, and each task's inventory check names the single owner.
- Risk: relocating a constant changes its effective value. Mitigation: each task ends with the full
  suite, and CK-025 requires the behavioural diff to change no expected value. This is the failure
  that has escaped review here twice before, so it is checked per task rather than only at the end.
- Risk: a handler's redirect through a domain service changes ordering and therefore frames.
  Mitigation: redirects reuse the domain service that already owns the use case rather than
  introducing a new one, and the suite covers the affected frames.
- Risk: a suite run made without the archive reports green while archive-dependent checks skip.
  Mitigation: every suite check requires the archive path to be set, and the expected result names
  the skip count, so an unexpected skip count fails the check.
- Rollback: the work is uncommitted until accepted, so discarding the working tree returns to
  `8af7990c`. A single task's work can be discarded on its own because each task ends green.

## Assumptions

- `SelectCharacterDeliveryOptions` is treated as protocol delivery behaviour rather than a
  game-rule configuration value, so it stays with its handler and is outside RQ-002. It switches
  whether frames follow a completion marker, which is client compatibility of the same family as
  the endpoint configuration the user already excluded. If it is instead a rule input, T-005 gains
  it and the Spec's RQ-002 clause about declaration placement applies to it.
- Existing domain services own every use case whose handler currently reads a rule input, so no new
  domain service is required. If a use case turns out to have no owning domain service, that task
  gains one rather than leaving the read in the handler.
- The six rule services that hold their rules as constants without reading content stay as they
  are, per the Spec's non-goals.
- The baseline suite result of 1690 passing, 2 skipped, 0 failing is the expected result of every
  suite check. A changed passing count is a finding, not a new baseline.
