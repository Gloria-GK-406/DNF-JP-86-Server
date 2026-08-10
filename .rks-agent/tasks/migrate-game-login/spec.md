---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Game login and indexed online-user execution

## Why and Outcome

The Game server needs an authenticated online-user identity before character selection
and later domain commands can be migrated safely. A Game connection must obtain one
lightweight server-side index, LOGIN must bind an account to that indexed aggregate, and
identity-changing work must have a reliable exclusive boundary without serializing all
ordinary work for the same user.

## Task Perspective

- Core outcome: A client completes the base Game LOGIN exchange and the server can query
  the authenticated online user by its lightweight index or connection.
- Necessary support: Online-user lifecycle ownership, account lookup or creation, and an
  atomic shared/exclusive identity-transition boundary preserve a coherent login state.
- Peripheral assurance: Exact frame compatibility, concurrency stress scenarios,
  regression tests, and reverse-engineering documentation prove and qualify the result.

## Scope and Non-Goals

### In Scope

- Anonymous online-user registration for each Game connection.
- Typed Game LOGIN request and base success response.
- Account lookup or creation and authenticated online-user binding.
- Indexed online-user query and mutation behavior.
- Shared and exclusive user-scoped Command execution behavior.
- Matching disconnect cleanup and protocol documentation.

### Non-Goals

- Character selection or character initialization.
- Account settings, auction, honor, growth-capsule, or other post-login notifications.
- Password-policy changes or credential-token issuance.
- CMD `0x0002` and later Game protocol migrations.
- Remote Git or release operations.

## Constraints

- A Command handler must not dispatch another Command.
- A waiting exclusive identity transition prevents later shared executions for that user
  from overtaking it.
- Cleanup from a stale connection or generation must not remove replacement online state.
- Complete Game frames become typed shared data before Application handling.

## Definitions

| Term | Meaning |
|---|---|
| Online user | The runtime aggregate representing one Game connection and its optional authenticated account and selected character. |
| Online-user index | An opaque, lightweight server-generated value used to locate one online-user aggregate. |
| Shared execution | User-scoped work allowed to overlap other shared work while the online identity remains stable. |
| Exclusive execution | Identity-changing work that starts after earlier shared work drains and blocks later shared work until completion. |
| Generation | A monotonically changing aggregate version used to reject stale lifecycle operations. |

## External Behavior and Data Rules

- A valid LOGIN request uses Game `command=0x01`, `type=0x0001` and carries two
  length-prefixed ASCII strings: `m_id` followed by password hash.
- A successful base LOGIN response uses Game `command=0x01`, `type=0x0001` and preserves
  the legacy fixed-format success body for configured environment, ports, and server IP.
- Looking up an existing `m_id` reuses that account; a missing `m_id` creates one using the
  supplied password hash, preserving current legacy behavior.
- The online-user index and generation are internal server data and are not emitted in the
  Game wire response.
- A Game disconnect removes only the aggregate still owned by that connection generation.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Every accepted Game connection has exactly one queryable anonymous online-user index before LOGIN handling. |
| RQ-002 | A valid Game LOGIN frame is represented as a typed request containing its wire metadata, `m_id`, and password hash. |
| RQ-003 | LOGIN resolves an existing account or creates a missing account and binds its stable account identifier to the same online-user aggregate. |
| RQ-004 | Successful LOGIN emits the legacy-compatible base success frame without exposing internal online-user identifiers. |
| RQ-005 | Online-user state is queryable by online-user index and resolvable from its current network connection; mutations preserve aggregate state-transition and generation invariants. |
| RQ-006 | Shared executions for one online user may overlap when no exclusive execution is pending or active. |
| RQ-007 | An exclusive execution blocks later shared executions, waits for earlier shared executions to finish, then runs without overlapping user-scoped work. |
| RQ-008 | Cancellation or failure releases acquired execution rights, and a waiting exclusive execution cannot be starved by later shared requests. |
| RQ-009 | LOGIN uses exclusive user-scoped execution and disconnect cleanup cannot remove a replacement generation. |
| RQ-010 | Existing Channel and Game EXIT behavior remains unchanged. |
| RQ-011 | Documentation identifies LOGIN facts derived from code, the staged post-login exclusions, and packet-capture uncertainty. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001,RQ-005 | A new Game TCP connection | Connection handling completes | One anonymous online-user index resolves from that connection and has no account binding. |
| AC-002 | RQ-002,RQ-003,RQ-004,RQ-009 | A connected anonymous user and a valid LOGIN body for a missing `m_id` | The LOGIN frame is handled | One account is created and bound under exclusive execution, and the exact base success frame is returned. |
| AC-003 | RQ-003,RQ-004 | A persisted account and a newly connected anonymous user | That `m_id` logs in | The existing account identifier is bound, no duplicate is created, and login succeeds. |
| AC-004 | RQ-006 | Two shared Commands for one online-user index | Both handlers are released only after observing each other active | Both executions overlap successfully. |
| AC-005 | RQ-007,RQ-008 | Shared work is active, an exclusive Command is waiting, and another shared Command arrives | Earlier shared work finishes | The later shared work does not overtake the exclusive Command; exclusive work completes before it enters. |
| AC-006 | RQ-008 | A shared or exclusive handler fails or its wait is cancelled | Later work requests execution | The later work can acquire the appropriate right and no execution count remains leaked. |
| AC-007 | RQ-005,RQ-009 | An online user is replaced or its generation advances | A stale disconnect cleanup runs | The current online-user state remains registered and unchanged. |
| AC-008 | RQ-010 | The complete refactor solution | Regression verification runs | All existing Channel and Game EXIT tests still pass. |
| AC-009 | RQ-011 | The completed login migration | Protocol documentation is inspected | Code-derived behavior, exclusions, and capture uncertainty are explicit. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001,RQ-002,RQ-003,RQ-004,RQ-005,RQ-009 | AC-001,AC-002,AC-003,AC-007 | Application, persistence, lifecycle, and real TCP integration tests. |
| EV-002 | RQ-006,RQ-007,RQ-008 | AC-004,AC-005,AC-006 | Deterministic asynchronous concurrency tests observing entry and release order. |
| EV-003 | RQ-010 | AC-008 | Fresh full-solution regression results. |
| EV-004 | RQ-011 | AC-009 | Review of the updated reverse-engineered protocol document. |

## Assumptions

- The base login success frame is sufficient for this staged migration; post-login
  notification protocols will be migrated separately.
- The observed legacy account behavior remains authoritative until packet capture or a
  separate authentication-policy decision replaces it.
