# TASK-01 — Item placement answers come from one rule service

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: One rule service owns where an item may go and what kind it is. Its six current `IItemPlacementDefinitionSource` consumers and one `IEquipmentSlotDefinitionSource` consumer ask it, or ask the domain service that owns their use case, instead of reading the sources themselves. The equipment-kind constant moves into it with its existing justification.
- Referenced spec requirements: RQ-001, RQ-004, RQ-005, RQ-008
- Referenced plan sections: T-001; boundaries BD-001, BD-013, BD-014, BD-015, BD-017
- Owned boundary: BD-001 item-placement rule seam in `Server/src/Dfo.Server.Abstractions/Inventory/` and `Server/src/Dfo.Server.Application/Inventory/`; the placement- and slot-reading portions of BD-013 and BD-014; the placement registration in BD-015; placement construction sites in BD-017.
- Permitted files or interfaces: the new placement rule service and its interface; `ItemContainers.cs`, `SelectCharacterEntryInventoryDomainService.cs`, `DungeonEntrySlice.cs`, `FloorPieces.cs`, `GroundPickupSlice.cs`, `MoveItemSpaceSlice.cs` and the domain services those slices call; `ApplicationModule.cs`; test construction sites naming the changed constructors.
- Dependencies: none
- Required dependency outputs: none
- Acceptance conditions: the only consumers of `IItemPlacementDefinitionSource` and `IEquipmentSlotDefinitionSource` in `Server/src` are the placement rule service and the infrastructure implementations that supply them; no protocol or command handler injects either source; the equipment-kind constant lives in the rule service with an unchanged value and justification; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only; no commit, merge, push or cleanup.
- Required methods: `test-driven-development` in its behaviour-preserving form — the existing suite is the oracle, and no assertion or expected value may change.
- Required checks: CK-001 `dotnet build Server/86JP.Server.slnx --nologo`; CK-002 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set to the A21 archive; CK-003 non-command enumeration of both sources' consumers.
- Required evidence: build result, suite result stated as passing/skipped/failing against 1690/2/0, the CK-003 enumeration, and the owned diff.

## Execution Result — round 1

### What was built

`IItemPlacementRuleService` (`Server/src/Dfo.Server.Abstractions/Inventory/`) and
`ItemPlacementRuleService` (`Server/src/Dfo.Server.Application/Inventory/`). The rule service takes
`IItemPlacementDefinitionSource` and `IEquipmentSlotDefinitionSource` and answers three questions:
`PlacementForAsync`, `WornSlotForAsync`, `IsEquipmentAsync`. It carries `EquipmentItemKind = 1` with
the justification its two former homes carried, verbatim: "What the archive's equipment index says
every worn item is."

### Consumers redirected

| Consumer | Layer | Before | After |
|---|---|---|---|
| `StoreBackedItemContainer` | Application | `IItemPlacementDefinitionSource` | `IItemPlacementRuleService` |
| `PersistentItemContainer` | Application | same | same |
| `SelectCharacterEntryInventoryDomainService` | Application | same | same |
| `FloorPieceValues.DescribeAsync` | Protocol slice helper | source + own `EquipmentItemKind` | `IsEquipmentAsync` |
| `SelectDungeonHandler` | Protocol handler | source | rule service |
| `MoveMapHandler` | Protocol handler | source | rule service |
| `PickUpGroundDropCommandHandler` | Command handler | source + own `EquipmentItemKind` | `IsEquipmentAsync` |
| `MoveItemSpaceCommandHandler` | Command handler | both sources | rule service |

Two private copies of `EquipmentItemKind = 1` — one in `FloorPieces.cs`, one in
`GroundPickupSlice.cs` — collapsed into the rule service's single copy. They had to agree for a
picked-up piece to be recognisably the piece that was described on the floor, and nothing made them.

Registered in `ApplicationModule` beside `IItemSlotRuleService`.

### Checks

| Check | Command or method | Result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-002 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed — the baseline exactly |
| CK-003 | `grep -rn` over both source interfaces in `Server/src` | Only `ItemPlacementRuleService`, plus the interface declaration, the PVF implementation and its `InfrastructureModule` registration |

The two skips are `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
and `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
— they skip because the archive is present, which is what proves the run was made with it.

### Owned diff

15 tracked files changed (+98/−49 before the encoding repair), plus the two new source files.
`Server/tests` accounts for 8 of them, +66/−17, containing only construction wiring and five new
`NoWearables` stubs that answer null. No assertion, expected value or test case differs.

### Cross-task finding — a third `EquipmentItemKind`

`CharacterRosterService.cs:251` holds a third copy of `private const byte EquipmentItemKind = 1`
carrying the identical justification. The baseline survey did not list it. It is not folded in here:
its consumer is `ICharacterRepository`, and injecting a rule service into a repository would be a
worse shape than the duplication. What it stamps is a *new character's starting equipment*, which is
BD-006's subject, so it is recorded as an input of TASK-06 by contract amendment there rather than
left to be rediscovered.

### Recovered exception — spurious encoding change

The script that rewired five test files and `MoveItemSpaceSlice.cs` wrote them back with a UTF-8 BOM
they did not have, and re-sorted `DungeonEntryCodecTests.cs`'s usings so `System.Buffers.Binary` fell
below the `Dfo.*` ones. Both were caught by reading the diff rather than by a check, were reverted,
and the suite was re-run green afterwards. Neither reached the result.

## Executor Self-check — round 1

- Acceptance condition "sources consumed only by the rule service": met, evidenced by CK-003.
- Acceptance condition "no protocol or command handler injects either source": met. Four handlers
  now take `IItemPlacementRuleService` instead. That is still a rule service inside a handler, which
  RQ-005 forbids at completion; TASK-09 owns that sweep and its acceptance is written as a universal
  rather than as the four call sites the survey found, so this is visible to it rather than hidden.
- Acceptance condition "the equipment-kind constant lives in the rule service with an unchanged value
  and justification": met for the two copies in scope; the third is handed to TASK-06 above.
- Acceptance condition "the suite is green at the baseline count": met, 1690/2/0.
- Behaviour preservation: every redirect is a call-for-call substitution. `IsEquipmentAsync` reproduces
  `placement?.ItemKind == EquipmentItemKind` including the null reading, which both former call sites
  took as "not equipment".
- Not done, deliberately: nothing else. No file outside the declared boundary was changed except
  `CharacterRosterService.cs`, which was *not* changed.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: **inline, not an independent context.** `executing-tasks` calls for a fresh
  reviewer via `reviewing-task-units-quick`; this session is under a standing environment instruction
  forbidding subagent dispatch unless the user asks for it. The review below was performed as a
  separate read-only pass over the Task file, the owned diff and T-001, and its independence is
  therefore weaker than the configured control. Recorded here rather than left implicit.
- Reviewed: this Task file, `git diff -- Server/src` and `git diff -- Server/tests` in full, and
  plan section T-001 with boundaries BD-001, BD-013, BD-014, BD-015, BD-017.

### Findings

1. **Behaviour preservation, checked expression by expression.** `IsEquipmentAsync` evaluates
   `placement?.ItemKind == EquipmentItemKind`. `FloorPieces` previously wrote that expression exactly;
   `GroundPickupSlice` wrote its negation and returned early. Both readings of a null placement — an
   item the archive does not declare is *not* equipment — are preserved. `PlacementForAsync` and
   `WornSlotForAsync` are pass-throughs to the identical source calls. No expression changed meaning.
2. **`MoveItemSpaceCommandHandler` lost a constructor parameter** rather than exchanging one.
   Two sources became one rule service. Checked that both former uses of `equipmentSlots` — the worn
   slot of the piece being put on, and the worn slot of the displaced piece — now call
   `WornSlotForAsync`, and that the one use of `placements` calls `PlacementForAsync`. Three uses,
   three redirects, none dropped.
3. **Remediated during review:** the parameter in `ItemContainers.cs` and
   `SelectCharacterEntryInventoryDomainService.cs` was still named `placements` while holding a rule
   service — the exact mislabelling this task exists to remove — and one line had grown to 103 columns
   against a file that wraps near 100. Renamed to `placementRules` and wrapped. Build and suite re-run
   after: 0 warnings, 1690/2/0.
4. **Not a finding, recorded so it is not mistaken for one later:** four handlers now declare
   `IItemPlacementRuleService`. RQ-005 forbids that at completion, and TASK-09 owns it. This is
   within T-001's own acceptance, which asks only that no handler inject the *source*.
5. **Test diff re-read against CK-025's condition.** 8 files, +66/−17. Every hunk is either a
   constructor argument list or a new `NoWearables` stub returning null. No assertion, no expected
   byte sequence, no test case added or removed. `ItemPathLiveTests` now resolves
   `IItemPlacementRuleService` from the real container, so the new registration is exercised rather
   than assumed.

### Round identity

Result round 1 plus its self-check, this review, and the working-tree diff at the time of the
1690/2/0 run all describe the same state: the two new source files plus 15 changed tracked files.
