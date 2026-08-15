---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: Strong Game CMD and NOTI Semantics

## Why and Outcome

The Game client owns two independent receive registries selected by the first frame byte. A category
mistake therefore does not merely mislabel a frame: it directs the frame to the wrong registry and
is normally discarded silently. The completed result makes CMD and NOTI explicit, non-overridable
Game protocol properties while leaving the generic network transport unaware of their meaning and
preserving all established wire behavior.

## Task Perspective

- Core outcome: Every Game message follows exactly one statically constrained CMD or NOTI path from
  protocol send intent through encoding, so the client always selects the intended registry.
- Necessary support: Client-originated messages remain CMD-only, Networking treats the selector as
  opaque framing data, and all current complete frames, recipients and ordering remain unchanged.
- Peripheral assurance: Architecture inspection, exact frame vectors, integration behavior, full
  build/test results and updated maintained guidance prove that no unclassified or raw-choice path
  remains.

## Scope and Non-Goals

### In Scope

- Distinct Protocol-owned CMD and NOTI identities for outbound Game messages.
- Category-compatible Game sending operations that expose intent without choosing a different wire
  category from the message.
- Category-constrained Game encoding and registration.
- Explicit CMD identity for every client-to-server Game decoder registration.
- Protocol-neutral naming and behavior for the first Game frame byte in Networking.
- Exact classification of accepted-entry, premium-service, clone-title, rejection and reusable
  lifecycle state messages, including separation of the current mixed-category rejection result.
- Maintained server architecture and protocol implementation guidance for the two families.

### Non-Goals

- Any Game payload, type number, recipient, delivery order, encryption, checksum, sequence or domain
  workflow change.
- Game Login/GameOption field correction or timestamp implementation.
- Channel protocol changes or a cross-endpoint CMD/NOTI abstraction.
- New client reverse engineering or changes to the conclusions recorded at baseline `f29123b`.

## Constraints

- Existing CMD frames remain byte-identical with selector `0x01`; existing NOTI frames remain
  byte-identical with selector `0x00`.
- A sending call can state and constrain category intent but cannot override the category intrinsic
  to its message and encoder.
- Game semantic category types and operations are owned by Protocol. Networking may carry only the
  opaque selector byte and cannot depend on Protocol.
- Development occurs directly on the current branch and excludes unrelated `Diagnostics/` content.

## Definitions

| Term | Meaning |
|---|---|
| CMD | Game protocol family selected by `0x01`; the client sends only this family, and the server uses it for corresponding replies. |
| NOTI | Game protocol family selected by `0x00`; used only for server-to-client asynchronous notifications. |
| Category-compatible send | A Game protocol send operation whose accepted message types belong to the category named by that operation. |
| Opaque selector | The first Game frame byte as handled by Networking without CMD/NOTI interpretation. |

## External Behavior and Data Rules

- The shared type `0x0001` remains two different messages: the CMD login response and the NOTI
  initial-connection notice.
- Selected-character frames retain their existing per-frame categories: accepted-entry,
  premium-service and clone-title are CMD; the remaining established state pushes retain NOTI.
- Admission rejection remains an adjacent CMD rejection followed by a NOTI notice, represented as
  two category-intrinsic messages rather than one message spanning both categories.
- Every client-to-server Game frame is routed as CMD plus its type. No client-originated NOTI path is
  introduced.
- Complete frame bytes and externally visible send/failure behavior remain unchanged.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Every outbound Game message has exactly one intrinsic category, CMD or NOTI. |
| RQ-002 | Game protocol send call sites explicitly select CMD or NOTI and cannot accept a message from the other category. |
| RQ-003 | Each Game encoder and its registration are category-constrained, and the encoded selector is fixed by that category rather than freely chosen per frame. |
| RQ-004 | Every client-to-server Game decoder registration is explicitly CMD, with routing identity remaining selector plus type. |
| RQ-005 | Networking treats the first Game frame byte as opaque and exposes no CMD/NOTI enum, named sender operation or Protocol dependency. |
| RQ-006 | Every state-synchronization and supplemental frame retains its existing category without raw selector choice in message data, and no one outbound message spans both CMD and NOTI. |
| RQ-007 | All affected complete frame bytes, payloads, recipients, ordering and failure behavior remain unchanged. |
| RQ-008 | Maintained architecture and protocol implementation guidance accurately describes CMD, NOTI and their ownership boundary. |
| RQ-009 | GameOption/Login field behavior and unrelated workspace content remain unchanged. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002, RQ-003 | Representative Game CMD and NOTI messages | Their protocol send and encoding contracts are evaluated | Each message is accepted only by its own category path and emits that category's fixed selector. |
| AC-002 | RQ-003, RQ-007 | The CMD login response and NOTI initial-connection notice sharing type `0x0001` | Both are encoded for the same established inputs | Their complete frames remain byte-identical and differ in selector exactly as before. |
| AC-003 | RQ-004 | Any registered client-to-server Game message | Its decoder route is inspected and exercised | The route is CMD plus type and no client NOTI decoder route exists. |
| AC-004 | RQ-005 | The completed transport boundary | Networking source and project dependencies are inspected | The selector is opaque and no Game category semantics or Protocol dependency appears. |
| AC-005 | RQ-001, RQ-002, RQ-003, RQ-006, RQ-007 | Successful and rejected SelectCharacter workflows | Acceptance, state synchronization and rejection messages are produced | Accepted-entry, premium-service and clone-title use CMD; established NOTI frames remain NOTI; rejection remains adjacent CMD then NOTI; exact frames and order match existing behavior. |
| AC-006 | RQ-007, RQ-009 | The complete change and established integration inputs | Channel, Game Login, roster, exit and state synchronization paths execute | Existing observable behavior remains unchanged and GameOption/Login fields are untouched. |
| AC-007 | RQ-008 | A maintainer reads current architecture/protocol guidance | They trace the first-byte meaning and implementation ownership | Guidance consistently identifies Protocol-owned CMD/NOTI and protocol-neutral Networking. |
| AC-008 | RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007, RQ-008, RQ-009 | The completed repository state | Build, tests, structural checks and diff review are performed | Every requirement has current passing evidence with no validation weakening or unrelated change. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-003 | AC-001 | Type-contract and registration evidence plus representative category compatibility checks. |
| EV-002 | RQ-003, RQ-007 | AC-002 | Exact complete-frame vectors for the shared-type CMD and NOTI pair. |
| EV-003 | RQ-004 | AC-003 | Decoder registry behavior and complete registration inspection. |
| EV-004 | RQ-005 | AC-004 | Dependency graph, public seam and residual-symbol inspection. |
| EV-005 | RQ-001, RQ-002, RQ-003, RQ-006, RQ-007 | AC-005 | Complete accepted/rejected SelectCharacter frame-class/order regressions and state/supplemental message inventory. |
| EV-006 | RQ-007, RQ-009 | AC-006 | Existing focused integration and exact-byte regression results plus GameOption diff exclusion. |
| EV-007 | RQ-008 | AC-007 | Focused maintained-guidance review against the confirmed protocol record. |
| EV-008 | RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007, RQ-008, RQ-009 | AC-008 | Fresh complete build/test, independent change review, static boundary checks and diff hygiene. |

## Assumptions

- The CMD/NOTI conclusions committed at `f29123b` define the two families; current exact-frame
  baselines define each implemented message's category where workflow-oriented names are misleading.
