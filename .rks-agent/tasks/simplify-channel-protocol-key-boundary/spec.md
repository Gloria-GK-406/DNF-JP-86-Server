---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Channel Protocol Key Ownership

## Why and Outcome

The Channel connection response currently passes a wire-compatibility key through a domain-command
surface even though no domain state or decision exists. The completed result makes this a direct
Channel protocol interaction, places the shared key rule outside Application and preserves every
observable Channel frame.

## Task Perspective

- Core outcome: A Channel Connect request returns its existing response without creating a domain
  command execution, and Channel protocol code owns the key meaning.
- Necessary support: All Channel response families continue using the same local-time key and retain
  their current encryption, compression and frame bytes.
- Peripheral assurance: Boundary inspection, focused regressions, a clean build and complete enabled
  test results prove that the small ownership change is complete.

## Scope and Non-Goals

### In Scope

- Direct Channel Connect response behavior with no application-command execution.
- One shared Channel-protocol current-key capability for Connect, ScriptVersion, Script and Info.
- Removal of the Channel wire-key capability from Application and cross-layer domain abstractions.
- Maintained architecture guidance that accurately describes the direct protocol interaction.

### Non-Goals

- Any change to Game Login or Game bootstrap configuration.
- Any change to Channel business data, scripts, catalogs or connection-domain state.
- Any reinterpretation of the legacy key or frame fields.

## Constraints

- For the same local time and message input, all affected response frames remain byte-identical.
- The key remains the invariant-culture local-time value formatted as `yyyyMMdd000006`.
- AES mode, padding, compression selection, message numbers and frame layout remain unchanged.

## Definitions

| Term | Meaning |
|---|---|
| Channel protocol key | The date-derived legacy string used by Channel response payload encryption and returned by the Channel Connect response. |
| Direct protocol interaction | A request/response path with no domain state or decision and therefore no application-command execution. |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | A valid Channel Connect request produces the existing response without executing a domain command. |
| RQ-002 | Connect, ScriptVersion, Script and Info use one Channel-protocol key rule with the preserved local-time formula. |
| RQ-003 | Application and domain abstractions expose no concrete Channel wire-key derivation capability or command used solely to return that key. |
| RQ-004 | Affected Channel response identities, plaintext content, encryption, compression and complete frame bytes remain unchanged. |
| RQ-005 | Game Login and Game bootstrap behavior and configuration remain outside the change. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002, RQ-004 | A fixed local time and a valid Channel Connect request | The request is handled | No domain command is observed and the response contains the preserved key and frame bytes. |
| AC-002 | RQ-002, RQ-004 | A fixed local time and representative ScriptVersion, Script and Info responses | Each response is encoded | Each frame matches its current encryption/compression behavior and uses the same preserved key rule. |
| AC-003 | RQ-003 | The completed project boundaries and registrations | Channel key ownership is inspected | No Application or domain-abstraction key provider and no key-only connection command remain. |
| AC-004 | RQ-001, RQ-002, RQ-003, RQ-004, RQ-005 | The complete change | The affected solution and tests are evaluated | All enabled checks pass and no Game configuration or behavior changed. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-004 | AC-001 | Handler and exact-frame regression evidence at a fixed time. |
| EV-002 | RQ-002, RQ-004 | AC-002 | Focused response-encoder byte and encryption/compression regressions. |
| EV-003 | RQ-003 | AC-003 | Dependency, source and registration inspection with absence checks. |
| EV-004 | RQ-001, RQ-002, RQ-003, RQ-004, RQ-005 | AC-004 | Fresh complete build, enabled test and diff-hygiene evidence. |

## Assumptions

- The current local-time formula and existing golden behavior are the compatibility authority for
  this refactor; client reverse engineering is not required because no semantic value changes.
