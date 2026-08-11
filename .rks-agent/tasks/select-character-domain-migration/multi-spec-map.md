---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: root_evaluation
phase: child_disposition
phase_state: pending
active_attempt: spec-004
last_transition: child-sealed
transition_evidence: "spec-004 has a completed result, final approved independent review after substantive remediation, fresh 102/102 solution tests, focused 15/15 checks, exact 51/51 G3 coverage, and an observed uncommitted state ready for acceptance."
workspace:
  repository_root: /Users/puxianguo/project/86JP-main
  task_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration/.rks-agent/tasks/select-character-domain-migration
  code_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration
  branch: codex/select-character-domain-migration
  baseline: fa51169421e14c02cdf3462460a1df14088a4f04
  baseline_evidence: "Accepted spec-003 commit fa51169421e14c02cdf3462460a1df14088a4f04 after approved independent re-review, fresh 91/91 solution tests, focused 19/19 inventory checks, dependency checks, clean task worktree, and unchanged source branch."
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
| SC-2 | Complete separated entry-result composition | in_progress | `fa51169`: character inventory and account inventory/cargo are separate typed result fields; rentals/item values/locks remain character-owned and account cube resources account-owned | Implement remaining character/account/world/session views and optionality | aligned |
| SC-3 | Service-driven peripheral data access | in_progress | `3e70094`: active-character service; `fa51169`: inventory entry repository/service with SQLite confined to Infrastructure and explicit legacy migration errors | Implement remaining cohesive entry services and repository adapters | aligned |
| SC-4 | Exact and consistent account-exclusive selection | satisfied | `3e70094`: exact-slot load precedes one locked AccountId-indexed publication; stale disconnect and load-failure preservation verified; baseline command registration remains account-exclusive | none | aligned |
| SC-5 | Result-only ordered Networking projection | not_started | Baseline `b0a3312` supplies one-to-many sender seam and ACK composer | Project all required packets without loaders or placeholders | aligned |
| SC-6 | Integrated verification and honest documentation | in_progress | `fd39009`: reverse-engineering confidence documented; fresh solution tests pass 70/70 | Verify completed product migration, Host startup, and root review | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | `attempts/spec-001-map-legacy-select-character-initialization/TaskContract.md` | Map legacy select-character initialization | SC-1 | completed | `attempts/spec-001-map-legacy-select-character-initialization/spec-result.md` | `fd39009` accepted |
| 2 | spec-002 | `attempts/spec-002-publish-active-character-consistently/TaskContract.md` | Publish active character consistently | SC-3, SC-4 | completed | `attempts/spec-002-publish-active-character-consistently/spec-result.md` | `3e70094` accepted |
| 3 | spec-003 | `attempts/spec-003-load-inventory-entry-state/TaskContract.md` | Load inventory entry state | SC-2, SC-3 | completed | `attempts/spec-003-load-inventory-entry-state/spec-result.md` | `fa51169` accepted |
| 4 | spec-004 | `attempts/spec-004-load-character-entry-state/TaskContract.md` | Load character entry state | SC-2, SC-3 | running | pending | active |
