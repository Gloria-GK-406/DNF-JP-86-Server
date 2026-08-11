---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: blocked
phase: blocked
phase_state: completed
active_attempt: none
last_transition: no-safe-successor
transition_evidence: "Spec-006 was sealed blocked and its classified product changes were rolled back exactly: HEAD remained accepted baseline b8e817c482795e805fb001bd5d0627d77848b987 and the Server product diff was empty. This controller update, durable spec-006 evidence and the user-requested incomplete-status document record the paused result. Root evaluation still has unmet G5 and downstream G6/G7 gaps. No safe successor exists because complete quest/activity/collection/pet definitions require the absent Script.pvf or an equivalent validated export, while live town/PvP/rejoin/expert-store state also has no normalized source in the workspace."
workspace:
  repository_root: /Users/puxianguo/project/86JP-main
  task_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration/.rks-agent/tasks/select-character-domain-migration
  code_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration
  branch: codex/select-character-domain-migration
  baseline: b8e817c482795e805fb001bd5d0627d77848b987
  baseline_evidence: "Accepted spec-005 commit b8e817c482795e805fb001bd5d0627d77848b987 after approved re-review, fresh 118/118 solution tests, focused 15/15 account-entry checks, exact G4 coverage, clean task worktree, and unchanged source branch."
---

# Multi-Spec Map

## Multi-Spec Boundary

- The legacy coverage matrix is an independently reviewable documentation outcome that controls all later implementation scope.
- Character-owned, account-owned, world/session-owned service results can be accepted separately while preserving earlier service boundaries if a later slice needs rollback.
- Networking projection depends on accepted business results but has its own packet-order and dependency evidence, so it must not share one unreviewed attempt with all data loading work.

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | Complete legacy initialization migration matrix | satisfied | `fd39009` and `Docs/Refactor/SelectCharacterMigrationMatrix.md`: 58 static, 18 supplemental, 10 lifecycle rows; approved review and fresh verification | none | aligned |
| SC-2 | Complete separated entry-result composition | in_progress | `fa51169`: inventory; `927e6ed`: G3 character state; `b8e817c`: complete G4 AccountId-owned state with inventory reuse | G5 is blocked by the absent PVF catalog and live session-adjacent source | at_risk |
| SC-3 | Service-driven peripheral data access | in_progress | Accepted active-character, inventory, G3 and G4 normalized repository/service slices; SQLite remains confined to Infrastructure | G5 adapters cannot provide complete production data from current inputs | at_risk |
| SC-4 | Exact and consistent account-exclusive selection | satisfied | `3e70094`: exact-slot load precedes one locked AccountId-indexed publication; stale disconnect and load-failure preservation verified; baseline command registration remains account-exclusive | none | aligned |
| SC-5 | Result-only ordered Networking projection | not_started | Baseline `b0a3312` supplies one-to-many sender seam and ACK composer | Requires an accepted complete G5 result before G6/G7 projection | at_risk |
| SC-6 | Integrated verification and honest documentation | in_progress | `fd39009`: reverse-engineering confidence documented; accepted checkpoint `b8e817c` remains intact | Root completion cannot be verified while G5 and downstream projection remain incomplete | at_risk |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | `attempts/spec-001-map-legacy-select-character-initialization/TaskContract.md` | Map legacy select-character initialization | SC-1 | completed | `attempts/spec-001-map-legacy-select-character-initialization/spec-result.md` | `fd39009` accepted |
| 2 | spec-002 | `attempts/spec-002-publish-active-character-consistently/TaskContract.md` | Publish active character consistently | SC-3, SC-4 | completed | `attempts/spec-002-publish-active-character-consistently/spec-result.md` | `3e70094` accepted |
| 3 | spec-003 | `attempts/spec-003-load-inventory-entry-state/TaskContract.md` | Load inventory entry state | SC-2, SC-3 | completed | `attempts/spec-003-load-inventory-entry-state/spec-result.md` | `fa51169` accepted |
| 4 | spec-004 | `attempts/spec-004-load-character-entry-state/TaskContract.md` | Load character entry state | SC-2, SC-3 | completed | `attempts/spec-004-load-character-entry-state/spec-result.md` | `927e6ed` accepted |
| 5 | spec-005 | `attempts/spec-005-load-account-entry-state/TaskContract.md` | Load account entry state | SC-2, SC-3 | completed | `attempts/spec-005-load-account-entry-state/spec-result.md` | `b8e817c` accepted |
| 6 | spec-006 | `attempts/spec-006-load-world-entry-state/TaskContract.md` | Load world/content entry state | SC-2, SC-3 | blocked | `attempts/spec-006-load-world-entry-state/spec-result.md` | rolled back to `b8e817c` |
