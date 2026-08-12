---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Select Character domain orchestration

## Delivered

- Select Character is now explicitly orchestrated by its application command handler rather than a cross-domain character-entry facade.
- Character state, character inventory/equipment, account state, account inventory/cargo, world/placement, session-adjacent state, and online publication remain separate domain results and contracts.
- Character and account inventory repository/service contracts are independently consumable while the SQLite adapter shares decoding and validation internally.
- Equipment eligibility can consume a narrow read-only character facts view; equipped-creature lookup remains inventory-owned.
- The application result is owned by `Dfo.Server.Data.Game`, and Networking consumes it without protocol changes.
- Active-character publication occurs exactly once after complete result construction; every earlier failure preserves the previously active character.

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md` call-graph and stale-facade scan; independent approved change review. |
| SC-2 | Separated models, contracts, projections, DI registrations, and focused owner-specific tests recorded in completion evidence. |
| SC-3 | Narrow `IEquipmentEligibilityCharacterReader` inspection and passing focused facts test. |
| SC-4 | Passing refusal, source-failure, and final-result-failure preservation tests, including independently validated RED behavior for the old publication order. |
| SC-5 | Passing success composition and exactly-once publication tests plus 191/191 Application verification. |
| SC-6 | Passing static dependency, identifier, nested-command, repository, and protocol scans. |
| SC-7 | Fresh full solution 355 passed, 2 expected skips, 0 failed; Abstractions 0 warnings/errors; diff check clean. |

## Repository Result

- Changed paths: complete product, test, documentation, and SDD evidence diff in the isolated task worktree; temporary ignored `Script.pvf` excluded.
- Baseline or accepted result: baseline `3b32ca494c3f384c9d2f29dd63bcd1bbe3b2d0ce`; accepted-result commit to be created by the workspace controller after this result is sealed.
- Parent rollback required: no

## Artifacts

- `TaskContract.md`
- `routing-decision.md`
- `spec.md`
- `plan.md`
- `reviews/change-review.md`
- `verification/completion-evidence.md`
