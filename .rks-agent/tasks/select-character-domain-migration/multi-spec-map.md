---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-001
last_transition: child-created
transition_evidence: "spec-001 has a ready contained child contract, valid empty dependency list, child Single Spec routing decision, unique deterministic identity, and no prior active row."
workspace:
  repository_root: /Users/puxianguo/project/86JP-main
  task_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration/.rks-agent/tasks/select-character-domain-migration
  code_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration
  branch: codex/select-character-domain-migration
  baseline: b0a33128501ed03f5aca81f706275c30372b6382
  baseline_evidence: "Source codex/architecture-refactor and task branch both resolved to b0a33128501ed03f5aca81f706275c30372b6382; task product state was clean before durable artifacts; dotnet test Server/86JP.Server.slnx restored successfully and passed 70/70 tests."
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
| SC-1 | Complete legacy initialization migration matrix | not_started | none | Map all static, conditional, and trailing packets | aligned |
| SC-2 | Complete separated entry-result composition | not_started | none | Implement reviewed character/account/world/session views and optionality | aligned |
| SC-3 | Service-driven peripheral data access | not_started | none | Implement cohesive services and repository adapters | aligned |
| SC-4 | Exact and consistent account-exclusive selection | not_started | Baseline `b0a3312` supplies strict slot command and account gate | Complete preparation/publication consistency with full services | aligned |
| SC-5 | Result-only ordered Networking projection | not_started | Baseline `b0a3312` supplies one-to-many sender seam and ACK composer | Project all required packets without loaders or placeholders | aligned |
| SC-6 | Integrated verification and honest documentation | not_started | Baseline solution has 70 passing tests | Verify completed migration, Host startup, review, and documentation | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | `attempts/spec-001-map-legacy-select-character-initialization/TaskContract.md` | Map legacy select-character initialization | SC-1 | running | pending | active |
