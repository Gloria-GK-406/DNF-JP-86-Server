---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Restore the Game initial connection handshake

## Why and Outcome

The real client reaches the configured Game listener but cannot begin LOGIN because it expects a server-first connection notice that the successor server omitted. A newly accepted Game connection must receive the compatibility `00/0001` notice immediately, without first sending a client Game frame, so the client can continue into the existing LOGIN exchange.

## Task Perspective

- Core outcome: A real Game client receives the required first server frame and can advance from transport connection into the LOGIN flow.
- Necessary support: The frame must identify the listener's configured channel, preserve the accepted legacy field layout and constants, use the configured advertised address, and be emitted only after the connection is registered.
- Peripheral assurance: Deterministic codec evidence, live-transport ordering evidence, endpoint-scope checks, architecture scans, full regression tests, and an honest protocol-documentation update protect the result.

## Scope and Non-Goals

### In Scope

- The server-first Game connection notice and its listener-specific values.
- Game connection lifecycle ordering through the first server message.
- The typed boundary between connection coordination and wire encoding.
- Compatibility, transport, endpoint-scope, and regression evidence for this handshake.
- Protocol documentation for the restored code-reversed behavior.

### Non-Goals

- Any message sent after a client LOGIN request other than preserving existing behavior.
- Account settings, auction, honor, growth-capsule, roster, Select Character, or other legacy notification migration.
- Client Patch, encryption, Channel exchange, authentication-policy, or database changes.
- Assigning newly inferred business meaning to compatibility constants.

## Constraints

- A Game connection must not wait for a client payload before producing the notice.
- A Channel connection must never receive this Game notice.
- Encoded frames and bodies remain owned by Networking; upstream layers may exchange only typed protocol-neutral messages.
- Missing listener/channel mapping or an unusable advertised address must fail explicitly without silently falling back to a fabricated loopback notice.
- The change must not incorporate or modify unrelated packet-diagnostics work from the source worktree.

## Definitions

| Term | Meaning |
|---|---|
| Initial connection notice | The server-first Game frame with command `0x00` and type `0x0001`, sent once for a newly accepted Game connection before client LOGIN. |
| Advertised address | The configured server IPv4 text carried in the notice for the client to use in later protocol activity. |
| Listener channel | The configured channel whose Game port equals the accepted connection's local listener port. |

## External Behavior and Data Rules

- The server frame uses the existing 15-byte server-to-client Game header, with command `0x00`, type `0x0001`, and total length covering header plus body.
- The body is, in order: success byte `1`; ASCII DString `ch.<channelId>`; two little-endian signed 32-bit zero values; server-index byte `1`; channel-id byte; zero byte; one little-endian signed 32-bit Unix timestamp captured for this notice; little-endian signed 32-bit value `1`; ASCII DString advertised address; little-endian signed 32-bit UDP ports `12311` and `12312`; two zero bytes; little-endian signed 32-bit command count `1086`; and little-endian signed 32-bit notification count `1036`.
- Each DString is a little-endian signed 32-bit byte length followed by that many ASCII bytes, without an implicit terminator.
- The channel ID comes from the listener channel rather than from connection order or a hard-coded normal-channel assumption.
- The compatibility field layout remains code-reversed and is not claimed as packet-capture-confirmed semantics.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Every newly accepted Game connection receives exactly one initial connection notice before the server requires or receives a client Game frame. |
| RQ-002 | The notice frame identity, field ordering, widths, byte order, DString representation, and compatibility constants match the declared external data rules. |
| RQ-003 | The notice carries the configured advertised address, the channel ID resolved from the exact Game listener, the corresponding `ch.<channelId>` login name, and one internally consistent preparation timestamp. |
| RQ-004 | Connection identity registration completes before the notice is triggered, and failure to produce or send the notice does not leave an untracked connection identity. |
| RQ-005 | Channel connections receive no Game initial connection notice. |
| RQ-006 | Missing or invalid listener/channel/address input produces an explicit handshake failure and no misleading notice. |
| RQ-007 | Application and Data coordinate through a typed message and contain no Game frame/header/body construction or dependency on Networking implementation types. |
| RQ-008 | Existing Channel exchange, Game LOGIN, disconnect cleanup, and other server behavior remain compatible. |
| RQ-009 | Maintained protocol documentation records the server-first behavior, exact compatibility layout, and code-reversed confidence boundary. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-004 | A Game listener is configured and no client bytes have been sent | A client opens a TCP connection to that listener | The connection is registered and the client can read exactly one complete `00/0001` server frame as the first Game payload. |
| AC-002 | RQ-002, RQ-003 | A deterministic timestamp, advertised IPv4 address, and configured channel are available | The initial notice is encoded | Every declared field decodes at the specified width and order, and the frame uses that exact timestamp, address, channel ID, and login name. |
| AC-003 | RQ-003 | Two configured Game listeners map to different channel IDs | A connection is accepted on either listener | Each notice carries the channel ID and login name belonging to its own listener. |
| AC-004 | RQ-005 | A Channel endpoint is accepted | Its connected lifecycle runs | No Game initial connection notice is sent. |
| AC-005 | RQ-006 | The accepted Game listener has no exact configured channel mapping, or its advertised address is unusable | Initial handshake preparation is attempted | The handshake fails explicitly and emits no fabricated fallback notice. |
| AC-006 | RQ-007 | The completed implementation is inspected | Layer dependencies and message flow are evaluated | Upstream layers expose only typed message intent while Networking alone owns Game bytes and framing. |
| AC-007 | RQ-008 | Existing server tests and affected transport integrations are available | The completed result is tested | Existing Channel, LOGIN, cleanup, and full-solution behavior pass without regression. |
| AC-008 | RQ-009 | The protocol documentation is reviewed | The change is complete | It describes the server-first notice and its layout without upgrading code-reversed facts to capture-confirmed facts. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-004 | AC-001 | Live transport integration evidence that reads the first server frame before writing client bytes, plus an observer order ledger. |
| EV-002 | RQ-002, RQ-003 | AC-002, AC-003 | Deterministic byte-vector and decoded-field assertions for distinct listener/channel mappings. |
| EV-003 | RQ-005 | AC-004 | Endpoint-scoping behavior evidence showing no send for Channel connection lifecycle. |
| EV-004 | RQ-006 | AC-005 | Negative behavior evidence for unresolved listener and invalid advertised address with zero emitted frames. |
| EV-005 | RQ-007 | AC-006 | Complete-diff architecture review and dependency scan showing no upstream frame construction or Networking implementation dependency. |
| EV-006 | RQ-008 | AC-007 | Passing focused affected suites and fresh full server solution tests. |
| EV-007 | RQ-009 | AC-008 | Reviewed protocol-documentation diff linked to the notice behavior and compatibility layout. |

## Assumptions

- The accepted legacy initial-notice builder remains the compatibility authority until real-client evidence disproves a field or constant.
- Configured Game listeners and channel entries provide a one-to-one listener-to-channel mapping in supported deployments.
