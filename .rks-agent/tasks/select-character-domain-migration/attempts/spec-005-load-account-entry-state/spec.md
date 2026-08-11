---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Account Entry State

## Why and Outcome

Selection needs one complete AccountId-owned business result assembled from cohesive settings, wallet/entitlement, progression and relation views, without selected-character or static/world/session state leaking into it.

## Task Perspective

- Core outcome: AccountId yields complete typed G4 account entry views.
- Necessary support: exact ownership, normalized reads, applicable-empty semantics and neutral unresolved records.
- Peripheral assurance: exact coverage, populated/failure tests, dependency review and full regression.

## Scope and Non-Goals

### In Scope

- Settings/input/quick-chat, wallet, entitlements/premiums, honor/adventure/growth, lottery/event progress, relations and neutral purchase restriction entries.
- Cohesive repository/service loading and selection composition.

### Non-Goals

- Account cargo already in G2, character usage already in G3, static/world/session data, mutations and packet projection.

## Constraints

- No CharacterId or selected-character fields appear in account-owned views except account-wide relation entries identifying roster characters.
- Applicable lists are non-null empty when loaded; required missing/malformed state is an error.
- Neutral uncertain fields retain width/order without invented business policy.

## G4 Coverage

| Slice | Matrix coverage | Outcome |
|---|---|---|
| Account mixed portions | rows 1, 6, 9, 16, 35, 41 | wallet/entitlement/settings/progression portions; cargo remains G2. |
| Settings/wallet | rows 15, 43, 55 | game options, input/quick-chat and balances. |
| Progress/events | rows 30, 38, 42, 52 | neutral purchase entries, relation, lottery and unresolved scalar. |
| Trailing account views | X-04-X-06 account portions | roster reference and honor/adventure/growth without character/static composition. |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Account settings and wallet views faithfully expose normalized account values and documented loaded-empty/default state. |
| RQ-002 | Entitlement, premium, honor/adventure/growth and lottery/event views preserve ordered complete account-owned state. |
| RQ-003 | Cross-character relations and unresolved purchase entries remain typed ordered account records without selected-character or policy fabrication. |
| RQ-004 | Wrong-owner, missing mandatory, malformed or duplicate data fails before active publication; selection composes AccountEntryState only after all slices load. |
| RQ-005 | Repository/service dependency direction and existing solution behavior remain compatible. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001-RQ-003 | A populated account has normalized G4 slices | Entry state loads | Every value and ordered entry appears in the correct account view with no foreign-domain data. |
| AC-002 | RQ-001-RQ-003 | Applicable account lists have no rows | Entry state loads | Lists are explicit empty collections and scalar defaults are only documented defaults. |
| AC-003 | RQ-004 | Ownership, mandatory state, ranges or uniqueness is invalid | Selection runs | Loading fails before publication and no partial result is returned. |
| AC-004 | RQ-005 | G4 is integrated | Complete checks run | Coverage, dependencies and regression pass. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001-RQ-003 | AC-001, AC-002 | Exact coverage plus populated/empty normalized repository and service tests. |
| EV-002 | RQ-004 | AC-003 | Failure and workflow-preservation tests. |
| EV-003 | RQ-005 | AC-004 | Dependency review and fresh full solution tests. |

## Assumptions

- PC-room-like scalar and Unknown730 records remain neutral compatibility values until later evidence resolves meaning.
