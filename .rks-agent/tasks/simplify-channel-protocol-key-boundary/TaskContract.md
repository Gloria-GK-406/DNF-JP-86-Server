---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Simplify Channel Protocol Key Boundary

## Context

### Source Request

The user decided that the Channel connection path does not represent a domain operation and asked:
“对，你先完成Channel的小型重构吧，不需要开新的工作区，直接在分支上干就行。最小型sdd应该就够了，修改点很有限。”

### Current Situation

`EstablishChannelConnectionCommandHandler` only obtains the current legacy Channel encryption key
and returns it to `ChannelConnectHandler`. The key rule is implemented in Application and exposed
through an Abstractions interface, although every production consumer is a concrete Channel protocol
handler or encoder. This creates a command with no domain behavior and moves a wire-compatibility
rule into the application layer.

## Goal

Make the Channel protocol own its current-key rule and let `ChannelConnectHandler` send the response
without dispatching a domain command, while preserving every Channel response byte and encryption,
compression and framing behavior.

## Scope

### In

- Move the legacy date-derived Channel key capability out of Application/Abstractions and into the
  Channel protocol boundary.
- Remove the connection command/result/handler and its command registration.
- Let `ChannelConnectHandler` obtain the protocol key and send the existing response directly.
- Keep ScriptVersion, Script and Info encoders on the same shared protocol key capability.
- Update focused tests and maintained architecture guidance affected by the removed command.

### Out

- Changing the key formula, time basis, AES mode, padding, compression or Channel frame layout.
- Changing Channel script/catalog behavior or introducing connection-domain state.
- Modifying Game Login/Game bootstrap options or their inferred semantics.
- Push, pull request, release, history rewriting or unrelated workspace cleanup.

## Constraints

- For a fixed time and message input, all Channel response frames must remain byte-identical.
- Application and Abstractions must no longer own the concrete Channel key derivation capability.
- The direct handler path must not introduce domain mutation or transport implementation knowledge.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Channel Connect sends the existing response without command-bus dispatch. | Handler test proving direct response construction and exact encoded key bytes. |
| SC-2 | ScriptVersion, Script and Info responses use the same date-derived key and preserve current frames. | Focused encoder/codec tests with a fixed `TimeProvider`. |
| SC-3 | The obsolete command/result/handler, its registration, and the Application/Abstractions key provider are absent. | Source/dependency scans and registration tests. |
| SC-4 | The affected solution builds and enabled tests pass without warning or validation weakening. | Fresh build, focused tests, full solution tests and diff hygiene. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “你先完成Channel的小型重构吧，不需要开新的工作区，直接在分支上干就行。最小型sdd应该就够了” | Implement and verify the bounded Channel protocol-key ownership refactor directly on the current branch, including minimal SDD artifacts and one local result commit. |

## Requires Confirmation

- Push, pull request creation, release, history rewriting, branch deletion or cleanup outside this
  task's own temporary artifacts.

## Assumptions

- `TimeProvider` remains available through the existing composition root and the current local-time
  key formula is the compatibility baseline.
