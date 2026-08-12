---
kind: rks-verification
decision: pass
---

# Completion Evidence: Select Character domain orchestration

## Claim

The current task-worktree change makes Select Character an application command use case that explicitly coordinates cohesive character, inventory/equipment, account, channel, world, placement, session, and online-lifecycle capabilities; preserves existing selection and network behavior; and retains failure atomicity by publishing only after the complete result is constructed.

## Condition Mapping

| Condition | Fresh evidence |
|---|---|
| SC-1 | Complete source inspection shows `SelectCharacterCommandHandler` owns the full orchestration and the former `ICharacterEntryService`/`CharacterEntryService` has no source or test reference. |
| SC-2 | `SelectCharacterResult` and its world/session wrappers are under `Dfo.Server.Data.Game`; character and account inventory have separate models, services, repository ports, projections, and focused tests. |
| SC-3 | `IEquipmentEligibilityCharacterReader` exposes only `CharacterId`, level, job, and growth type; `CharacterService` provides the implementation and focused tests pass. Inventory-owned equipped-creature lookup is separately exposed through `IEquippedCreatureQuery`. |
| SC-4 | Focused Application verification passed 13/13, including refusal-before-inventory, inventory/character/account/world/placement/session failures, and a final-result clock failure that all preserve the previous active character. |
| SC-5 | Focused success coverage proves the complete composite result and exactly one final publication; the full Application project passed 191/191. |
| SC-6 | Static scans found no SQLite dependency in Application, Networking, Data, or Abstractions; no `OnlineUserId`, nested `ICommandBus`, or repository dependency appears in the Select Character use case; the command remains registered as account-exclusive workflow execution. |
| SC-7 | Fresh full solution verification passed 355 tests with 2 intentionally skipped missing-archive negative scenarios and 0 failures. Abstractions built with 0 warnings/errors, and `git diff --check` passed. |

## Verification Detail

- Focused Application domain/orchestration tests: 13 passed, 0 failed, 0 skipped.
- Focused SQLite inventory tests: 18 passed, 0 failed, 0 skipped.
- Focused Networking Select Character response test: 1 passed, 0 failed, 0 skipped.
- Full solution: 355 passed, 2 skipped, 0 failed.
- The supplied `/Users/puxianguo/Downloads/Script.pvf` was copied to the task root for verification. It is covered by `*.pvf` ignore rules, was present during the full run, and does not appear in Git status.
- With the real archive available, all positive PVF/content integration tests executed. The two skipped Infrastructure tests are intentionally conditioned on archive absence and therefore do not apply in this environment.
- No production protocol constant or message identifier changed.
- Independent review verdict: approved after remediation; the reviewer independently proved the final-result failure test turns red under the former publish-before-composition ordering.
