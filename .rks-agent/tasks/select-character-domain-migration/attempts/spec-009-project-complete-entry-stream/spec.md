---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Complete Select Character Entry Stream Projection

## Why and Outcome

Character selection currently acknowledges success but does not reproduce the legacy initialization stream the client needs. After this outcome, a completed selection result can be projected into the complete code-reversed static entry stream, with the same packet identities, occurrence order, conditional omissions and supplemental body shapes, while all state mutation and recipient discovery remain outside the projection boundary.

## Task Perspective

- Core outcome: A selected character's completed character, account, inventory, world and session views become the exact ordered protocol stream required to enter the game.
- Necessary support: Each value comes from the owning typed result limb, conditional packets remain conditional, missing required state fails visibly, and supplemental projections accept explicit data and recipients without discovering them.
- Peripheral assurance: Mechanical coverage, golden body checks, dependency inspection and protocol documentation prove that no packet occurrence or architectural boundary was lost.

## Scope and Non-Goals

### In Scope

- The complete mixed-domain selection acknowledgement and every static packet occurrence numbered 1 through 58 in the reviewed migration matrix.
- Conditional item, class, pet and feature packets whose applicability is already known in the completed result.
- Typed projection inputs and encoded body results for supplemental identities X-01 through X-18.
- Exact packet identity, order, multiplicity and condition behavior for the result-only stream.
- Honest documentation of code-reversed fields and implemented coverage.

### Non-Goals

- Registering sessions or players, resolving recipients, mutating inventories or quests, persisting placement or appearance, or compensating failed lifecycle work.
- Loading any missing data from repositories, PVF, SQLite or domain services during protocol projection.
- External client execution, packet-capture confirmation, unrelated post-entry game messages, push, merge or cleanup.

## Constraints

- Required unavailable state is an error and is never projected as a fabricated zero, null or empty business value.
- An inapplicable conditional packet is omitted; a loaded-empty applicable collection retains its defined empty packet behavior.
- Body encoding cannot choose recipients or perform business or lifecycle mutation.
- Protocol assertions remain code-reversed until confirmed by capture or client execution.

## Definitions

| Term | Meaning |
|---|---|
| Static occurrence | One matrix row from 1 through 58, including repeated command/type identities that appear at distinct ordered positions. |
| Supplemental projection | A typed X-01 through X-18 packet body whose data and recipients are supplied by the later lifecycle workflow. |
| Projection stream | The ordered list of outbound protocol messages produced solely from completed typed input. |

## External Behavior and Data Rules

- The static projection stream preserves the matrix's command/type/occurrence sequence; identity equality does not collapse duplicate occurrences.
- Character-owned, account-owned, inventory-owned, world-owned and session-owned values are read only from their corresponding completed result views.
- Conditional projections are determined by typed applicability or loaded collection state, not by protocol-layer database or content lookups.
- Supplemental bodies do not embed socket/session discovery; recipient targeting is supplied separately by G7.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The projected static stream contains every matrix occurrence 1 through 58 in exact order and preserves repeated identities as separate occurrences. |
| RQ-002 | The selection acknowledgement and all static bodies project populated character, inventory, account, world and session values from the correct typed ownership boundary without convenience placeholders. |
| RQ-003 | Applicable empty collections, populated collections, class-specific state, pet state and other conditional state produce the matrix-defined packet count and order, while genuinely inapplicable state omits only the corresponding packets. |
| RQ-004 | Each supplemental identity X-01 through X-18 has a typed, deterministic body projection that consumes explicit completed data and remains independent of recipient discovery or mutation. |
| RQ-005 | Missing or invalid required typed state fails projection before a partially fabricated success stream is returned. |
| RQ-006 | Protocol projection remains independent of repositories, Infrastructure, PVF, SQLite and business/domain services. |
| RQ-007 | Maintained protocol documentation maps all implemented static and supplemental identities and distinguishes confirmed code structure from unresolved wire meaning. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | A fully populated completed selection result spanning all owning views | The result is projected | The stream begins with the complete acknowledgement and contains every static occurrence 1 through 58 in the matrix order with populated owner-correct bodies. |
| AC-002 | RQ-001, RQ-003 | Applicable loaded-empty collections plus class and feature state that is inapplicable | The result is projected | Required empty packets remain present where defined, inapplicable packets are absent, and neighboring occurrence order is unchanged. |
| AC-003 | RQ-001, RQ-003 | Populated item lists, conditional class data and pet/feature data | The result is projected | Multiplicity and conditional packets exactly match the matrix without deduplicating repeated command/type identities. |
| AC-004 | RQ-004, RQ-006 | Explicit data for each supplemental identity and separately supplied recipient information | Supplemental bodies are projected | Each X-01 through X-18 body is deterministic and no encoder discovers recipients, loads state or performs mutation. |
| AC-005 | RQ-005 | A required completed projection limb or required protocol value is unavailable or invalid | Projection is attempted | Projection fails and does not return a plausible partial static stream. |
| AC-006 | RQ-006 | The complete protocol projection feature | Its dependency boundary is inspected | No repository, Infrastructure, PVF, SQLite or business/domain service dependency is present. |
| AC-007 | RQ-007 | The implemented projection coverage | Documentation is reviewed against the matrix | All static and supplemental identities resolve to an implemented projection or an explicitly documented G7 targeting responsibility, with code-reversed confidence stated. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001 | AC-001, AC-002, AC-003 | Mechanical ordered identity comparison and complete populated/conditional stream behavior. |
| EV-002 | RQ-002, RQ-003, RQ-005 | AC-001, AC-002, AC-003, AC-005 | Focused body, ownership, multiplicity, applicability and failure behavior. |
| EV-003 | RQ-004 | AC-004 | Supplemental contract and deterministic body projection coverage for every X identity. |
| EV-004 | RQ-006 | AC-004, AC-006 | Dependency and source-boundary inspection plus complete solution verification. |
| EV-005 | RQ-007 | AC-007 | Matrix-aligned protocol coverage documentation review. |

## Assumptions

- The reviewed migration matrix and legacy builders are the current protocol authority.
- Accepted G1 through G5 results are authoritative for static business state; G7 will supply runtime-only supplemental inputs and recipients rather than asking Networking to discover them.
