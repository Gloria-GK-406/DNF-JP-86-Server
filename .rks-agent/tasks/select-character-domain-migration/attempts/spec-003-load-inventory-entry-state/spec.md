---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Inventory Entry State

## Why and Outcome

Character entry needs reusable inventory state rather than packet builders querying storage. The outcome is a complete character-owned inventory view and a separate account-owned cargo view, both loaded through services and included in the selection result for later pure projection.

## Task Perspective

- Core outcome: Consumers receive deterministic, typed inventory and cargo entry views for the selected account and character.
- Necessary support: Item fidelity, ownership validation, applicable-empty behavior, and repository/service isolation make those views trustworthy.
- Peripheral assurance: Integration tests, dependency inspection, complete review, and solution regression prove the slice.

## Scope and Non-Goals

### In Scope

- Character containers: main, avatar, personal cargo, equipment, and pet.
- Account cargo as a distinct view.
- Container parameters, item slots and complete typed item-core fields.
- Rental expiry attributes, equipment lock identity, item runtime values, and gold limits.
- Selection-result composition.

### Non-Goals

- Expiring/deleting rentals, granting daily items, live inventory leasing, or other mutation.
- Pet runtime, skill, quest, account wallet/settings, world/session, or packet behavior.

## Constraints

- Account cargo cannot appear inside the character inventory or active-character state.
- A loaded container is present even when it contains no items.
- Invalid ownership, malformed item-core data, or duplicate occupied slots fails the load.
- Unknown protocol semantics remain neutral typed storage fields rather than invented business meanings.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Loading entry inventory returns all applicable character containers and a separately owned account cargo view in deterministic container/slot order. |
| RQ-002 | Each loaded item preserves its slot, item identity, typed 82-byte core fields, expiration and lock data; container metadata, runtime values, and gold limits are preserved when stored. |
| RQ-003 | Applicable loaded-empty containers/lists are represented as empty, while malformed, duplicate, wrong-owner, or failed loads surface as errors. |
| RQ-004 | Selection composition carries both views outside ActiveCharacterState and all persistence access remains behind services and repositories. |
| RQ-005 | Existing login, roster, selection, Networking, and storage behavior remains compatible. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | A selected character and account have items across character containers and account cargo plus stored metadata | Entry inventory is loaded | Both ownership views contain the exact ordered items and metadata assigned to them. |
| AC-002 | RQ-003 | Applicable containers and runtime lists contain no rows | Entry inventory is loaded | The views contain explicit empty collections without being null or fabricated from a failed load. |
| AC-003 | RQ-002, RQ-003 | Stored item data has an invalid core length, owner, or duplicate occupied slot | Entry inventory is loaded | Loading fails explicitly and no partial business result is returned. |
| AC-004 | RQ-004 | Exact character loading and inventory services succeed | Selection entry is composed | Character and account inventory views appear as separate result fields and ActiveCharacterState remains character-state-only. |
| AC-005 | RQ-005 | The inventory slice is integrated | The complete solution is exercised | Existing and new stable behavior remains passing. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002 | AC-001 | Populated SQLite integration and service assertions. |
| EV-002 | RQ-003 | AC-002, AC-003 | Loaded-empty and malformed/duplicate rejection tests. |
| EV-003 | RQ-004 | AC-004 | Selection composition test plus dependency review. |
| EV-004 | RQ-005 | AC-005 | Fresh complete solution test and diff checks. |

## Assumptions

- Legacy 82-byte item cores are little-endian and the accepted field offsets in the legacy `ItemCore` model remain the local compatibility authority until packet capture validation.
