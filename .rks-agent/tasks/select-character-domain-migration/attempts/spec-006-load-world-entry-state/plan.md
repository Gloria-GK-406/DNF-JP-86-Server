---
kind: rks-plan
spec: ./spec.md
state: ready
---

# Plan: World and Content Entry State

## Approach

Define content, world/admission and session-adjacent views; add abstractions for static/config repositories and cohesive services; resolve listener to ChannelId in the network handler; load G5 in the account-exclusive workflow before publication; preserve pure read behavior.

## Boundaries

| ID | Boundary | Output |
|---|---|---|
| BD-001 | Data world/content/channel models | Complete G5 result. |
| BD-002 | Abstractions repositories/services | Content/world/admission/query seams. |
| BD-003 | Infrastructure config/file/SQLite adapters | Loaded raw sources. |
| BD-004 | Application services, command/handler/workflow | Resolved ChannelId and composed state. |
| BD-005 | Coverage/tests | Exact G5 evidence. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Checks |
|---|---|---|---|
| RQ-001-RQ-004 | T-001-T-003 | BD-001-BD-005 | CK-001-CK-004 |
| RQ-005 | T-004 | BD-005 | CK-005-CK-006 |

## Tasks

### T-001: Contracts and exact G5 coverage
- Boundaries: BD-001, BD-002, BD-005
- Requirements: RQ-001-RQ-004
- Checks: CK-001 Abstractions build and exact coverage pass.

### T-002: Content/world adapters and services
- Depends on: T-001
- Boundaries: BD-003, BD-004, BD-005
- Requirements: RQ-001, RQ-002, RQ-004
- Checks: CK-002 populated/empty focused tests; CK-003 missing/malformed tests.

### T-003: Channel resolution/admission and workflow composition
- Depends on: T-002
- Boundaries: BD-004, BD-005
- Requirements: RQ-003, RQ-005
- Checks: CK-004 handler/admission/publication tests pass.

### T-004: Complete verification
- Depends on: T-003
- Boundaries: BD-005
- Requirements: RQ-005
- Checks: CK-005 full solution tests; CK-006 coverage/dependency/diff checks.
