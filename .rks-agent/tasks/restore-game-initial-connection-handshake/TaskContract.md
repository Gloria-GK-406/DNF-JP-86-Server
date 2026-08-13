---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Restore the Game initial connection handshake

## Context

### Source Request

“确实是缺失了一些逻辑是吧。直接完成这个开发吧，走sdd。”

### Current Situation

The real client completes the Channel exchange and establishes TCP to the configured Game listener, but then remains on “正在连接服务器”. Runtime evidence shows an established Game socket with no complete client Game frame. The accepted legacy server immediately emitted a server-first `cmd=0x00/type=0x0001` initial connection notice from its Game-connected callback; the successor `OnlineUserConnectionObserver.ConnectedAsync` currently registers the connection and sends nothing, so the client waits for a notice while the server waits for LOGIN.

## Goal

Restore the server-first Game initial connection handshake so that a newly accepted Game connection receives the typed `00/0001` initial notice before the server waits for the client's LOGIN request, allowing the client to advance into the existing LOGIN flow.

## Scope

### In

- Define the neutral server message needed to represent the Game initial connection notice.
- Trigger that message exactly for newly connected Game endpoints after their connection identity is registered.
- Encode the notice in Networking with the code-reversed legacy body fields, configured advertised server address, current listener/channel identity, and one captured preparation timestamp.
- Preserve the layer boundary: Application coordinates the connection event through typed contracts; Networking owns bytes and framing.
- Add focused codec and live transport/integration regression coverage proving server-first ordering and endpoint scoping.
- Update the protocol documentation to record the restored behavior and retain its code-reversed/not-capture-confirmed confidence boundary.

### Out

- Additional post-LOGIN account settings, auction, honor, growth-capsule, or other legacy notifications.
- Changes to client Patch, encryption bypasses, Channel exchange, LOGIN request parsing, account authentication policy, character roster, or Select Character.
- Reverse engineering or assigning new semantics to the legacy constants beyond preserving the observed compatibility layout.
- Merge, push, worktree cleanup, release, or production deployment.

## Constraints

- A Channel connection must never receive the Game initial notice.
- The notice must be sent without waiting for any client Game payload; the existing client LOGIN request remains the next client-driven message.
- No raw frame or encoded body may be constructed in Application or Data.
- Listener-to-channel resolution and advertised addresses must fail explicitly when a trustworthy configured value cannot be produced; do not silently advertise loopback or fabricate another channel.
- Existing unrelated and uncommitted packet-diagnostics work in the main worktree must remain untouched.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Connecting to a Game listener and sending no client bytes causes the server to send exactly one `cmd=0x00/type=0x0001` initial frame before any client LOGIN is required. | A focused live transport/integration regression that connects, reads and decodes the first server frame without sending a client frame. |
| SC-2 | The initial body preserves the accepted legacy field order, widths, configured server address, listener-derived channel identity, compatibility constants, and a single preparation timestamp. | Exact codec/body assertions covering the normal configured listener and at least one distinct channel mapping or explicit unresolved-listener failure. |
| SC-3 | The connection is registered before the message is triggered, Game connections receive one notice, and Channel connections receive none. | Application connection-observer behavior tests with an order ledger and endpoint-scoping assertions. |
| SC-4 | Application and Data remain independent of Game framing and Networking implementation details. | Dependency scan plus review of the complete diff showing only typed message contracts cross the boundary. |
| SC-5 | Existing Channel, Game LOGIN, connection cleanup, and Networking behavior do not regress. | Focused affected suites and the full server solution test suite pass. |
| SC-6 | Protocol documentation states the server-first handshake and honestly retains the code-reversed confidence boundary. | Reviewed documentation diff linked to the implemented typed source and encoder. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | User: “确实是缺失了一些逻辑是吧。直接完成这个开发吧，走sdd。” | Implement and verify the bounded server-first Game initial connection handshake through SDD, including task artifacts, one isolated worktree, tests, review, and one accepted local result commit. |

## Requires Confirmation

- Merge, push, release, worktree cleanup, or any change to the Windows client/Patch requires separate user authorization.

## Assumptions

- The accepted legacy `BuildInitialLoginNotice` is the compatibility source for this missing handshake until real-client evidence disproves a field or constant.
- The current configured Game listener set and channel catalog provide the authoritative listener-to-channel mapping for the notice.
