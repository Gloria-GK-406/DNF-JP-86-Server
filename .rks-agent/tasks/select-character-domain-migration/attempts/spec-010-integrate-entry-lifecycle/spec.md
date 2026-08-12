---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Generation-Safe Select Character Entry Lifecycle

## Why and Outcome

The complete entry payload is not useful until the server owns a coherent runtime
lifecycle around it. A successful Select Character request must now produce one usable,
current game session whose persistent and in-memory state agree with the packets delivered
to the owner and explicit peers. A failed request must either remain side-effect free or
remove only the exact registered attempt and close that connection.

## Task Perspective

- Core outcome: An authenticated connection enters the selected character through one
  ordered, generation-safe workflow and is left in a coherent current session.
- Necessary support: Honest mutation results, exact recipient resolution, generation-safe
  registries, failure compensation and the accepted G6 typed delivery boundary.
- Peripheral assurance: Event-ledger, fault-injection, dependency, Host-start and
  code-reversed documentation evidence.

## Scope and Non-Goals

### In Scope

- Admission, wire identity, previous/displaced session cleanup, selected-session
  registration, preparation, entry delivery, publication, post-entry synchronization and
  rollback for Select Character.
- Entry-specific inventory refill/lease, quest seeking sync, pet revival, PvP handshake,
  expert-store/town leave, dungeon-rejoin and persistence behavior.
- Explicit owner and peer delivery through accepted typed G6 projections.

### Non-Goals

- General inventory, dungeon, quest, pet or PvP gameplay after entry.
- Real-client execution, packet capture, remote traffic, push, merge or workspace cleanup.

## Constraints

- Business services receive account/owner identifiers; connection generation remains a
  session concern and `OnlineUserId` does not spread into character, inventory or quest
  domains.
- Required owner delivery participates in success/failure; peer delivery is explicitly
  addressed and best effort.
- No frame construction, recipient discovery, repository query or lifecycle mutation is
  performed by Networking.
- Empty means an applicable operation completed with no result; missing required state or
  unavailable mutation capability is an error.
- Cleanup and rollback compare the exact registered session before removing active or
  presence state.

## Definitions

| Term | Meaning |
|---|---|
| Selected generation | One connection-bound Select Character registration identified by account, character, connection and expected connection generation. |
| Required delivery | An ordered owner-directed message or projection whose failure aborts the entry attempt. |
| Best-effort target | An explicitly resolved peer connection whose delivery failure does not invalidate the owner's otherwise successful entry. |
| Publication | Making the selected character/session visible as the current account state and town/PvP presence after required preparation and delivery. |

## External Behavior and Data Rules

- Admission refusal delivers X-08's selection rejection and server notice in order and
  performs no previous-session cleanup, registration, mutation or publication.
- Wire user IDs accept only positive character IDs representable without truncation;
  invalid IDs fail before state change.
- Successful order is previous/displaced cleanup, selected registration, preparation,
  static and X-01-X-06 delivery, committed X-07 updates, publication/presence, PvP
  handshake/publication, X-11, conditional X-12, adjacent X-13/X-14, then ordered
  X-15/X-16.
- Required delivery failure after registration triggers compensation and connection close;
  peer broadcast failure is isolated.
- A stale disconnect or rollback never removes a newer selected generation or its active
  character.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The workflow validates authenticated connection generation, admission and lossless wire identity before performing lifecycle side effects. |
| RQ-002 | Previous, displaced and newly registered selections are owned and removed by exact session identity, with correct expert-store, town and runtime cleanup behavior. |
| RQ-003 | A successful selection performs preparation, required static/supplemental delivery, publication and post-entry synchronization in the documented order. |
| RQ-004 | Daily inventory refill is driven by authoritative content rules, commits atomically, emits only committed grouped updates and restores a reloadable lease after failure. |
| RQ-005 | PvP entry resolves current identities and recipients, completes owner-directed identity/room delivery before marking ready and publishing the newcomer, and isolates peer failures. |
| RQ-006 | Quest seeking sync, pet revival and dungeon rejoin emit only truthful changed/transition/available results with required adjacency and registry order. |
| RQ-007 | Any failure after selected registration performs exact-generation compensation, attempts independent cleanup despite individual cleanup failures, and closes only the failed connection. |
| RQ-008 | Domain and transport boundaries remain separated, the integrated Host is runnable with the supplied data, and documentation retains code-reversed confidence. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001 | A selected character is below the listener minimum | Selection is requested | X-08's two required messages are delivered in order and no lifecycle side effect occurs. |
| AC-002 | RQ-001 | A character ID cannot be represented as the wire user ID or the connection generation is stale | Selection is requested | The request fails before cleanup, registration, mutation, delivery or publication. |
| AC-003 | RQ-002 | The connection already owns a character | Another character is selected | The old store/town/runtime state is cleaned in order and only the old exact session is removed. |
| AC-004 | RQ-002,RQ-007 | A selected registration replaces another connection and the displaced connection later disconnects or rolls back | Both cleanup paths run | The replacement remains current and its active/presence state is preserved. |
| AC-005 | RQ-003 | All required sources and mutations succeed | Selection completes | The event ledger matches the documented order and the current session is published exactly once. |
| AC-006 | RQ-004 | Refill rules match changed inventory entries | Entry refill runs | State commits once and only committed list-group updates are delivered in stable group order. |
| AC-007 | RQ-004,RQ-007 | Refill mutation or required update delivery fails | Entry refill runs | No false update is emitted, the lease is reloadable, and registered-attempt compensation executes. |
| AC-008 | RQ-005 | The listener is free-duel with existing ready participants and rooms | PvP entry runs | Owner identities precede owner rooms; only after both succeed is the newcomer current/ready and published to explicit peers. |
| AC-009 | RQ-005 | A required owner PvP delivery fails or a peer disconnects | PvP entry runs | Required failure prevents ready/publication and rolls back; peer failure is isolated without owner rollback. |
| AC-010 | RQ-006 | Item-seeking progress changes, a dead equipped pet revives, and a valid rejoin exists | Post-entry synchronization runs | X-12 is changed-only, X-13 is immediately followed by X-14, and X-15 precedes participants in registry order. |
| AC-011 | RQ-006 | Those operations are no-op, invalid, expired or unavailable | Post-entry synchronization runs | No fabricated change/revival/offer is emitted and invalid rejoin state is released. |
| AC-012 | RQ-007 | A required preparation or delivery fails and one compensation action also throws | Rollback runs | Remaining independent cleanup is attempted, only the exact attempt is removed, and its connection is closed. |
| AC-013 | RQ-008 | The integrated change is built with the supplied PVF and server resources | Verification runs | The solution passes, Host starts, prohibited dependency scans are empty, and G7 docs remain code-reversed. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001,RQ-002 | AC-001,AC-002,AC-003,AC-004 | Generation, admission, replacement and stale-cleanup behavior evidence. |
| EV-002 | RQ-003 | AC-005 | Integrated owner event-ledger evidence over the complete success path. |
| EV-003 | RQ-004 | AC-006,AC-007 | Real-content refill source plus transactional mutation and failure behavior evidence. |
| EV-004 | RQ-005 | AC-008,AC-009 | PvP direct/target ordering and failure-isolation behavior evidence. |
| EV-005 | RQ-006 | AC-010,AC-011 | Quest, pet and rejoin positive/negative behavior evidence. |
| EV-006 | RQ-007 | AC-004,AC-007,AC-009,AC-012 | Fault-injection and exact-generation compensation evidence. |
| EV-007 | RQ-008 | AC-013 | Independent complete review, full solution, Host smoke, dependency and documentation evidence. |

## Assumptions

- The legacy lifecycle and accepted matrix define ordering until real-client validation.
- Entry-specific content and normalized persistence needed by refill, seeking quests and pet
  revival are present or can fail explicitly without fabricating success.
