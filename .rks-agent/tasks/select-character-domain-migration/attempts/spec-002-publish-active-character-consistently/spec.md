---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Consistent Active Character Publication

## Why and Outcome

Later character-domain services need one reliable AccountId-indexed source for the current active character. The outcome is a single publication boundary that makes the authenticated user's current CharacterId and the corresponding `ActiveCharacterState` observable together, preserves the prior state when exact-slot loading fails, and removes that state when its owning online user disconnects successfully.

## Task Perspective

- Core outcome: AccountId plus the active-character service reliably identifies and returns the currently selected character state.
- Necessary support: Publication shares one state owner with authenticated-user binding, rejected selections do not mutate state, accepted disconnect removes state, and CharacterService owns no online-session cache.
- Peripheral assurance: Focused regression tests, complete code review, dependency inspection, and the full solution test run prove the ownership refactor.

## Scope and Non-Goals

### In Scope

- AccountId-indexed publish and required-query behavior for active character state.
- Consistent user snapshot CharacterId and active-state publication.
- Selection failure preservation and disconnect cleanup.
- CharacterService responsibility limited to repository-backed roster and exact-slot loading.

### Non-Goals

- Duplicate-login replacement policy.
- Additional character/account state loading.
- Network response, channel admission, or packet changes.

## Constraints

- Business consumers require AccountId, not OnlineUserId or a network connection.
- One implementation owns both current-character binding and active-state publication.
- A missing exact slot is an error and cannot clear or replace accepted active state.
- Rejected stale disconnect cannot clear accepted active state.

## External Behavior and Data Rules

- Before publication, required active-state lookup fails explicitly.
- After publication, required lookup returns the same state whose CharacterId appears in the authenticated user snapshot.
- Re-publishing another successfully loaded character atomically replaces both observable values.
- Successful owner disconnect removes the AccountId-indexed active state; rejected disconnect preserves it.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Active character state can be published and queried using AccountId, and successful publication exposes the same CharacterId in the authenticated user snapshot. |
| RQ-002 | Exact-slot loading failure performs no publication and preserves any previously published state. |
| RQ-003 | Successful owning-user disconnect removes active state, while a rejected stale disconnect leaves it unchanged. |
| RQ-004 | CharacterService provides repository-backed roster and exact-slot loading without storing online active state. |
| RQ-005 | Existing login, roster, command, and Networking behavior remains compatible with the refactor. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001 | One authenticated online user owns an AccountId and a character state was loaded | The selection workflow publishes that state | AccountId lookup returns that exact state and the user snapshot exposes its CharacterId. |
| AC-002 | RQ-002 | An account already has a published active state | A later exact-slot load fails | The workflow reports failure and both previously observable values remain unchanged. |
| AC-003 | RQ-003 | An account has a published active state | Disconnect is attempted with stale generation evidence and then with current owner evidence | The stale attempt preserves state, while the accepted attempt removes it. |
| AC-004 | RQ-004 | Character data is requested for roster or selection | CharacterService serves the request | It delegates to its repository and exposes no active-state publication/query responsibility. |
| AC-005 | RQ-005 | The ownership refactor is complete | The complete solution is exercised | Existing tests and registrations remain successful. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001 | AC-001 | Focused publication/query test and interface ownership review. |
| EV-002 | RQ-002 | AC-002 | Focused workflow failure-preservation test. |
| EV-003 | RQ-003 | AC-003 | Focused stale and accepted disconnect cleanup test. |
| EV-004 | RQ-004 | AC-004 | Complete diff and dependency inspection. |
| EV-005 | RQ-005 | AC-005 | Fresh complete solution test and Host registration evidence where relevant. |

## Assumptions

- Existing account-exclusive command execution serializes selection publication with other account-scoped commands.
