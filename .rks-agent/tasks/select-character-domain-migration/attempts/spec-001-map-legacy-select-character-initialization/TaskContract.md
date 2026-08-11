---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-1
depends_on: []
---

# TaskContract: Map Legacy Select Character Initialization

## Context

### Source Request

The authorized root task requires comparing the legacy mainline implementation before creating the complete domain services and packet projections, with checkpoints that tolerate documented corrections to the old code.

### Current Situation

The legacy server defines a 58-entry static initialization sequence and adds conditional or trailing behavior across packet builders, `CharacterSelectHandler`, `CharacterSessionLifecycleCoordinator`, and a large SQLite select-character data source. Later service boundaries cannot be reviewed for completeness until every entry has a traceable owner and source.

## Goal

Create a reviewed migration matrix that accounts for every known legacy select-character initialization packet or lifecycle projection and identifies its protocol identity, condition, data source, domain owner, target service/projection, side effects, and evidence confidence.

## Scope

### In

- The 58 entries returned by `NewCharacterInitSequence.Build()` including occurrence-index distinctions.
- Conditional packet construction inside `SelectCharacterPacketBuilder` and trailing select-character sends coordinated outside the static list.
- Entry-related post-selection lifecycle notifications when they affect the initial client state.
- Legacy builder, snapshot/data-source, repository/table/static-content, and side-effect references needed to route later work.
- A proposed domain/service and result-view owner for every mapped behavior, clearly marked as a proposal when semantics are uncertain.

### Out

- Implementing or modifying product code.
- Claiming packet-capture confirmation.
- Resolving unrelated game commands or every behavior reachable after normal town entry.

## Constraints

- Unknown or conflicting semantics remain visible; they must not be normalized into a confident business meaning.
- Account, world/content, and session data may not be assigned to character state merely because a legacy packet combines them.
- The matrix must distinguish read-only projection data from state mutations and lifecycle side effects.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1.1 | The matrix contains exactly one row for each of the 58 ordered static entries, preserving duplicate command/type occurrences. | Mechanical count and order comparison with `NewCharacterInitSequence.Build()`. |
| SC-1.2 | Every known conditional or trailing select-character packet and entry-related lifecycle projection is listed separately with its send condition. | Cross-check against `SelectCharacterPacketBuilder`, `CharacterSelectHandler`, and `CharacterSessionLifecycleCoordinator`. |
| SC-1.3 | Every row records the legacy builder/source, domain owner, proposed service or projection, side-effect classification, and confidence. | Complete matrix review with no blank ownership or unexplained source cells; uncertain meanings are explicitly marked. |
| SC-1.4 | The matrix derives a bounded dependency-ordered implementation grouping for successor Specs without generating implementation contracts in advance. | Reviewed summary grouping that references matrix rows and identifies prerequisite relationships. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root authorization: “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果”. | Inspect the legacy and refactored local code; create, review, verify, and locally commit the migration-matrix checkpoint within the root worktree. |

## Requires Confirmation

- External packet capture, push, merge, PR, or workspace cleanup remains outside this child.

## Assumptions

- The legacy static sequence and directly invoked select-character lifecycle code represent the complete locally discoverable initialization surface, while deeper ordinary post-entry gameplay remains out of scope.
