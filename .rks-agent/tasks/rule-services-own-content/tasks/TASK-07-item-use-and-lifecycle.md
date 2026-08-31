# TASK-07 — Item use and inventory lifecycle answers come from rule services

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: The existing item-use judgement rule service gains `IItemUseRuleDefinitionSource` and becomes its only consumer. One new rule service owns `IEntryLifecycleContentSource`.
- Referenced spec requirements: RQ-001, RQ-005, RQ-008
- Referenced plan sections: T-007; boundaries BD-008, BD-009, BD-013, BD-015, BD-017
- Owned boundary: BD-008 `IItemUseJudgementService` and its implementation; BD-009 inventory-lifecycle rule seam in `Server/src/Dfo.Server.Abstractions/Inventory/` and `Server/src/Dfo.Server.Application/Inventory/`; the corresponding portions of BD-013, BD-015 and BD-017.
- Permitted files or interfaces: `IItemUseJudgementService` and its implementation; the new lifecycle rule service and its interface; `PlayingCharacter`, `ItemUseEffectDomainService`, `SelectCharacterEntryInventoryDomainService`; `ApplicationModule.cs`; affected test construction sites.
- Dependencies: TASK-06
- Required dependency outputs: a tree green at TASK-06's accepted state, and the placement rule service from TASK-01, which `SelectCharacterEntryInventoryDomainService` already consults by then.
- Acceptance conditions: the only consumers of `IItemUseRuleDefinitionSource` and `IEntryLifecycleContentSource` in `Server/src` are the judgement rule service and the lifecycle rule service, plus the infrastructure implementations; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-014 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-015 non-command enumeration of both sources' consumers.
- Required evidence: suite result against 1690/2/0, the CK-015 enumeration, and the owned diff.

## Execution Result — round 1

### What was built

- `IItemUseJudgementService` gained `RuleForAsync(itemId, ct)` and its implementation gained
  `IItemUseRuleDefinitionSource`. The existing judgement rule service is now the only reader of what
  the archive declares about using an item — which is what it was already judging against.
- `IInventoryLifecycleRuleService` and `InventoryLifecycleRuleService`, owning
  `IEntryLifecycleContentSource`. One answer: `DailyRefillsAsync(preparedAt, ct)`.

### Consumers redirected

| Consumer | Source it held |
|---|---|
| `PlayingCharacter` | `IItemUseRuleDefinitionSource` — now asks the judgement service it already held |
| `ItemUseEffectDomainService` | same, same |
| `SelectCharacterEntryInventoryDomainService` | `IEntryLifecycleContentSource` |

The item-use half is the shape the plan named as an *extend* rather than an *add*, and it turned out
to be the right call for a reason worth writing down: `PlayingCharacter` looked the declaration up
and then handed it to the judgement, and `ItemUseEffectDomainService` looked the same declaration up
again and handed it to the same judgement for the cooldown key. Two lookups of one declaration,
feeding one judge. Now there is one.

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-014 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-015 | `grep -rn` over both sources in `Server/src` | `IItemUseRuleDefinitionSource` only in `ItemUseJudgementService`; `IEntryLifecycleContentSource` only in `InventoryLifecycleRuleService`; plus their declarations and PVF implementations |

## Executor Self-check — round 1

- Acceptance "both sources consumed only by the two rule services": met, CK-015.
- Acceptance "suite green at the baseline count": met, 1690/2/0.
- Behaviour preservation: `RuleForAsync` forwards to the same `GetAsync` with the same argument, and
  both former call sites read the declaration at the same point in their flow. Nothing is cached, so
  the archive read count per call is unchanged.
- The judgement service's own remark — "nothing here can find a character, and that is the point" —
  still holds: it gained an archive reader, not a way to look a character up. `Judge` still compares
  only values it is handed.
- Test doubles: `ItemUseJudgementTests` hands the judge declarations directly and never looks one
  up, so its new `NoItemUseRules` throws rather than answering. The other four sites wrap their
  existing rule stubs in the real judgement service.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **`PlayingCharacter` lost a constructor parameter without losing a capability**: it held both the
   source and the judge, and the source's only use was the lookup the judge now performs. Checked
   that `CanUseAsync` still reads the declaration before the unreadable-declaration guard, which is
   the guard the comment says exists to stop an unparsed restriction being read as no restriction.
2. **`ItemUseEffectDomainService` likewise.** Its `rule is null` early return is unchanged, which
   matters: an item the archive declares nothing usable for plans no effects rather than throwing.
3. **The lifecycle rule service is a pass-through and is meant to be.** Its value is the ownership
   boundary; the moment is still handed in, so it reads no clock.
4. **Scope check.** No file outside the permitted list changed.
