---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Legacy Select Character Initialization Map

## Why and Outcome

The service migration needs a complete and auditable account of the legacy character-entry behavior before domain ownership is changed. The outcome is one migration matrix that lets a reviewer identify every locally discoverable initialization packet or lifecycle projection, trace its evidence and data ownership, see uncertainty without guessed semantics, and derive bounded successor implementation groups.

## Task Perspective

- Core outcome: A complete ordered map of legacy select-character initialization behavior with traceable sources and proposed domain ownership.
- Necessary support: Duplicate occurrences, conditional sends, trailing sends, lifecycle side effects, and uncertain meanings remain distinguishable so later service work does not silently omit or misclassify them.
- Peripheral assurance: Mechanical coverage counts, source cross-checks, and an independent complete-result review prove that the map accounts for the local legacy surface.

## Scope and Non-Goals

### In Scope

- All 58 ordered entries in the legacy static initialization sequence.
- Conditional and trailing packets associated with initial character selection.
- Entry-related lifecycle projections that prepare or immediately synchronize the selected character session.
- Traceable legacy sources, observable send conditions, data ownership, side effects, evidence confidence, and proposed successor grouping.

### Non-Goals

- Product-code implementation or modification.
- Packet-capture confirmation.
- Ordinary gameplay behavior after the initial town-entry synchronization boundary.
- Treating legacy structure or terminology as authoritative when the observable meaning remains uncertain.

## Constraints

- Account, character, world/content, and session ownership remain distinct even when the wire packet mixes their fields.
- An applicable empty data set, an inapplicable feature, an unavailable source, and an unknown meaning must remain distinguishable.
- Read-only projection data, state mutation, and lifecycle side effects must be classified separately.

## Definitions

| Term | Meaning |
|---|---|
| Static entry | One ordered item produced by the legacy initialization-sequence definition, including its occurrence index when command and type repeat. |
| Conditional packet | An initialization packet whose presence or multiplicity depends on character, account, server, or session state. |
| Trailing packet | A packet sent as part of character entry but outside the 58-entry static sequence. |
| Lifecycle projection | Immediate post-selection behavior that publishes, restores, or synchronizes entry state beyond pure packet encoding. |
| Confidence | Whether a claimed meaning is directly established by code structure, inferred from legacy naming or use, or unresolved without capture or further evidence. |

## External Behavior and Data Rules

- Static rows preserve the exact observed order and occurrence identity.
- Every mapped behavior exposes its command/type when available, send condition, source reference, domain owner, proposed service or result view, side-effect classification, and confidence.
- Missing local evidence remains explicit and does not become a default value or confident business meaning.
- Successor groups cover the mapped surface without pre-authoring future child contracts.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The matrix accounts for exactly 58 ordered static entries and preserves distinguishable duplicate command/type occurrences. |
| RQ-002 | The matrix separately accounts for every locally discoverable conditional packet, trailing packet, and entry-related lifecycle projection. |
| RQ-003 | Every row identifies its protocol identity or explicit absence, observable condition, legacy source, domain owner, proposed service or projection, side-effect class, and confidence. |
| RQ-004 | The matrix never hides uncertain, conflicting, unavailable, inapplicable, or applicable-empty semantics behind one undifferentiated empty value. |
| RQ-005 | A dependency-ordered successor grouping covers every matrix row while keeping character, account, world/content, session, and Networking responsibilities separate. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001 | The legacy static sequence contains repeated command/type combinations and occurrence indices | A reviewer compares it with the matrix in order | Every one of the 58 positions has one corresponding row with the same position and occurrence identity. |
| AC-002 | RQ-002 | The legacy selection builders and lifecycle coordinator emit behavior outside the static list | A reviewer compares those paths with the supplemental matrix sections | Every locally discoverable initialization-related conditional, trailing, or lifecycle behavior is represented or explicitly excluded with a reason. |
| AC-003 | RQ-003 | A mapped packet combines fields or derives data from several legacy sources | A reviewer follows the row references | The row exposes each relevant source and separates domain ownership and side effects rather than naming only the packet builder. |
| AC-004 | RQ-004 | Legacy names or zero-filled bodies do not establish business semantics | A reviewer reads the confidence and data-state classification | The matrix marks the uncertainty and does not claim that zero means loaded-empty, inapplicable, or confirmed protocol behavior. |
| AC-005 | RQ-005 | All static and supplemental rows are present | A reviewer compares row ownership with the successor grouping | Every row belongs to a bounded implementation group with visible prerequisites and no cross-domain convenience ownership. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001 | AC-001 | Mechanical ordered-entry count and occurrence comparison against the legacy sequence. |
| EV-002 | RQ-002 | AC-002 | Source cross-check against the legacy packet builder, character-select handler, and lifecycle coordinator. |
| EV-003 | RQ-003 | AC-003 | Complete-row review of source, owner, target, side-effect, and confidence fields. |
| EV-004 | RQ-004 | AC-004 | Review of every low-confidence or zero/default row for explicit uncertainty and data-state semantics. |
| EV-005 | RQ-005 | AC-005 | Coverage review from every matrix row to one dependency-ordered successor group. |

## Assumptions

- The locally discoverable initialization boundary ends after the immediate selection packet sequence and entry synchronization invoked directly by the select-character lifecycle path; normal later gameplay handlers are excluded.
