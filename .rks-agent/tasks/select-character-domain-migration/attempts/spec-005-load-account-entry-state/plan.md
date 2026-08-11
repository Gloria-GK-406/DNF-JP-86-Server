---
kind: rks-plan
spec: ./spec.md
state: ready
---

# Plan: Account Entry State

## Approach

Define separate settings, wallet/entitlement, progression/event and relation/compatibility views. Read normalized legacy account tables transactionally into raw storage rows, map through four cohesive Application services, and replace the placeholder AccountEntryState before active publication.

## Boundaries

| ID | Owned boundary | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | Data account entry models/result | Complete ownership-safe G4 contracts. | Account storage | AccountEntryState. |
| BD-002 | Abstractions account repository/slice services | Raw seams and cohesive services. | AccountId | Storage slices/domain views. |
| BD-003 | Infrastructure normalized SQLite adapter/DI | Exact transactional reads and validation. | SQLite tables | Raw record/error. |
| BD-004 | Application account services/DI/workflow | Map/order/compose before publication. | Raw storage | Complete account state. |
| BD-005 | G4 coverage/tests | Trace and protect all slices. | Matrix/legacy | Review evidence. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Checks |
|---|---|---|---|
| RQ-001-RQ-003 | T-001-T-003 | BD-001-BD-005 | CK-001-CK-003 |
| RQ-004 | T-002-T-003 | BD-003-BD-005 | CK-003-CK-004 |
| RQ-005 | T-004 | BD-005 | CK-005-CK-006 |

## Tasks

### T-001: Define G4 contracts and exact coverage

- Boundaries: BD-001, BD-002, BD-005
- Requirements: RQ-001-RQ-003
- Inputs: matrix and legacy account builders/tables
- Outputs: typed views, raw seams and row-to-field coverage
- Checks:
  - CK-001: Abstractions build succeeds and exact G4 set has no unexplained ID.

### T-002: Load normalized account storage

- Depends on: T-001
- Boundaries: BD-003, BD-005
- Requirements: RQ-001-RQ-004
- Inputs: AccountId and normalized tables
- Outputs: validated raw account slices
- Checks:
  - CK-002: Infrastructure populated/empty focused tests pass.
  - CK-003: missing/wrong/range/duplicate tests pass.

### T-003: Map cohesive services and compose selection

- Depends on: T-002
- Boundaries: BD-004, BD-005
- Requirements: RQ-001-RQ-004
- Inputs: raw slices
- Outputs: complete AccountEntryState before publication
- Checks:
  - CK-004: Application account/character-entry focused tests pass.

### T-004: Verify coverage and regression

- Depends on: T-003
- Boundaries: BD-005
- Requirements: RQ-005
- Inputs: complete G4 slice
- Outputs: review-ready change
- Checks:
  - CK-005: full solution tests pass.
  - CK-006: exact coverage, dependency scan and diff check pass.
