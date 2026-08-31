---
kind: rks-review
verdict: approved
---

# Change Review: Rule Services Own Every Rule Input

## How this review was obtained

The gate is `auto`, which `reviewing-code` defines as requiring an independent, fresh-context
reviewer and forbids satisfying with same-context self-review.

This artifact first recorded `verdict: blocked` — not because of the work, but because the session
carried a standing instruction not to dispatch subagents, leaving no independent path. The user was
asked and granted standing authorization for subagent dispatch in this project. Two independent
reviewers then ran, each given the artifact paths, the change scope and the places to look hardest,
and neither given the executor's conclusions.

| Round | Scope | Verdict |
|---|---|---|
| 1 | the complete change at baseline `8af7990c` | `approved`, nine non-blocking observations |
| 2 | the three remediations made in response to round 1, plus re-confirmation of the invariants | `approved`, no blocking findings |

The eleven per-Task quick reviews in `tasks/TASK-*.md` were performed inline and are supporting
evidence only. They are not the basis of this verdict.

## What the reviewers checked themselves

Both ran the suite rather than quoting it: 1690 passed, 2 skipped, 0 failed, and both confirmed the
two skips are the `A_missing_archive_fails*` tests — which skip *because* the archive is present, so
the count is evidence the run was made with it.

Round 1 independently swept all 29 content-source interfaces, the four configuration values, the
five static rule classes, all 18 rule-service constructors, and the whole Protocol layer; compared
`IServerConfigService` consumers line for line against `8af7990c`; and verified digit by digit that
`GameDropIdentity` and `FirstActorSequenceFor` reproduce the derivations they were lifted from. It
confirmed the five-frame dungeon-entry delivery keeps its order, its conditional frame and every log
field; that `WorthOfClear` reads `parameters.Load()` once; that `MoveItemSpace`'s three refusals keep
their conditions and order; and that `Server/tests` gained and lost zero test cases with one changed
assertion that changed only the symbol it calls.

Round 2 re-derived the `firstEntry` equivalence for the changed `MoveAsync` signature, established
from `DungeonRunState` being a `sealed record` with an `ImmutableHashSet` and `TryMove` being a
`with`-plus-CAS that never mutates the caller's object, and re-counted `[Fact]`/`[Theory]` at 1173
before and after.

## Remediation applied between the rounds

Three of round 1's observations were acted on; all three were the executor's own doing.

| Observation | Fix |
|---|---|
| `checked((byte)Progression.Level)` is a no-op cast that misleads a reader into thinking the field is wider | Removed in `DungeonEntrySlice` and `DungeonSelectionSlice`. Round 2 confirmed `Level` is `byte` at baseline and now, so the removal cannot change behaviour or throw |
| `MoveMapHandler` looked the run up, then `MoveAsync` looked it up again — a second snapshot the baseline did not take | `MoveAsync` now takes the run the caller already holds. Round 2 confirmed `firstEntry` is computed from the same object, in the same position relative to `TryMove`, and every log field is unchanged |
| `GameProtocol.SecretShopNpcId` was a constant this change *moved* and the CK-022 inventory did not list it, so "zero unresolved entries" was not strictly true | Classified and recorded in `tasks/TASK-11-completeness-evidence.md`, both in the constants table and in an amendment. Value verified unchanged: 1000 at `8af7990c`, 1000 now |

The suite was re-run green after the remediation, before round 2.

## Non-blocking Observations carried forward

Recorded rather than fixed. None affects the acceptance boundary; each is a judgement a later reader
may want to revisit.

1. **`MoveAsync(CharacterId, DungeonRunState, …)` opens a hole the baseline could not have.** A
   caller could pass one character's identifier with another's run. The single caller is correct and
   the parameter is documented, but the signature permits the mistake. Closing it means either
   `DungeonRunState` carrying its own `CharacterId` or an assertion in the method.

2. **`MoveMapHandler` has no direct test.** Round 2 states this plainly: the green suite proves
   almost nothing about remediation 2, whose correctness rests on source-level equivalence plus the
   immutability of `DungeonRunState`. This is the baseline's coverage, not a regression, but it is
   the weakest evidence in the whole change.

3. **The `SecretShopNpcId` classification argument is weaker than the others.**
   `ClearRewardBodyLength` and `FixedRewardSlotCount` are packet-layout numbers; 1000 is a game
   entity identifier. Round 2 accepts the conclusion — the constant never read the archive at
   baseline and does not now, and making it a rule input would introduce an archive read, which is a
   design change rather than a relocation — but calls the analogy rhetorical. Recorded as debt.

4. **Five of the new domain services are one-line pass-throughs** —
   `ExperienceValuationDomainService`, `QuestDefinitionDomainService`,
   `ChannelDirectoryDomainService`, `TownMovementDomainService`, `TutorialRewardDomainService`.
   Round 1 judged them the mechanical consequence of RQ-005 rather than unrequested structure, and
   the Plan's Assumptions authorise a use case gaining an owner. The cost is real and was put to the
   user before it was paid. `QuestOfferRefreshDomainService`, `DungeonVisitDomainService`,
   `MonsterKillDomainService`, `GroundPickupDomainService` and `ItemMovementDomainService` carry
   genuine orchestration.

5. **`DropRules`, `MonsterExperienceRules` and `DungeonClearExperienceRules` are `public` in
   `Abstractions`.** RQ-003 is satisfied by enumeration rather than by visibility; making them
   `internal` or moving them into `Application` would make "called only from a rule service" hold at
   compile time.

6. **`DungeonRunState.LastHostileActorType = 3`** remains a game-semantic constant outside a rule
   service, on a domain record's pure method. Its only production path reaches it through
   `DungeonRoomRuleService`. Disclosed by the executor in TASK-11 and confirmed here.

7. **`DropRuleService` loads the rate table twice per kill** where the baseline loaded once. Round 1
   verified `PvfDropRateTableSource.Load` is a `ConcurrentDictionary.GetOrAdd`, so both calls return
   the same instance and no drawn value can differ.

8. **`SelectDungeonHandler` now reads the room before its information log** rather than while
   building frames. Timing of logs and I/O only; no value changes.

## What neither reviewer could check

- **Two dungeon frames and the breakable-object drop counts have no live confirmation.** Both the
  TaskContract and the Spec declare this: behaviour preservation here is established against the
  suite and the archive, not against a running client. Not introduced by this change, but it bounds
  what "value for value" can mean.
- **Round 2 had no pre-remediation working tree to diff against** — nothing was committed and the
  reflog holds only `8af7990c` — so it verified the remediations against the baseline and the current
  tree rather than against the intermediate state.
