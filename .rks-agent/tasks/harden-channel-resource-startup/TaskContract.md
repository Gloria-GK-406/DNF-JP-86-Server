---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Harden channel resource startup

## Context

### Source Request

After the real client twice reached a late runtime failure caused by omitted connection or
resource initialization, the user accepted the proposed permanent fix and instructed:
“好，完成任务吧”.

### Current Situation

The client completes Channel CONNECT and then requests the channel script, but the server
can start successfully with a stale default resource path. It later throws
`FileNotFoundException` while handling `0x09`, emits no `0x0A`, and the client reports that
channel information could not be received. The packaged resource exists under
`Resources/Channel/channel_info.etc`; runtime configuration and fallback resolution do not
reliably select it when the Host is launched from a different working directory.

## Goal

Make Host configuration and packaged channel-resource resolution independent of the process
working directory, and make an unavailable or invalid required channel script prevent the
server from advertising ready listeners rather than failing on the first client request.

## Scope

### In

- Host content/configuration-root behavior for normal run and publish layouts.
- Channel script configuration, path resolution, startup validation, and immutable serving.
- Startup diagnostics that identify the resolved content root, resource, advertised address,
  and Channel/Game listener configuration without exposing secrets.
- Production-shaped Host/resource and TCP Channel handshake regression coverage.
- Correction of the functional-verification run instructions.

### Out

- Changes to Channel encryption, packet identities, channel-list wire format, Game protocol,
  client Patch, PVF semantics, database behavior, or post-channel business flow.
- Committing, merging, pushing, or deleting the existing uncommitted packet-diagnostics work.
- Claiming protocol semantics beyond the existing code-reversed behavior.

## Constraints

- The packaged channel script remains under `Resources/Channel/channel_info.etc`; no root-level
  duplicate may mask a configuration error.
- Explicit absolute channel-script paths remain supported for tests and operator overrides.
- Required-resource validation must finish before Channel or Game listeners are considered
  started.
- Existing packet-diagnostics changes in the main checkout are user-owned and must remain
  untouched by the isolated task.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Starting the built Host from a working directory other than the project/output directory loads its packaged appsettings and channel script. | Production-shaped process or Host integration evidence using an unrelated working directory. |
| SC-2 | A default-configured client can complete `0x0B→0x0C→0x09→0x0A→0x11→0x12` without an injected script path. | Live TCP integration asserting the ordered response frames and nonempty script payload. |
| SC-3 | Missing, empty, or unreadable required channel script prevents successful Host startup before listeners are advertised ready. | Negative startup behavior tests and explicit actionable error evidence. |
| SC-4 | Relative configured paths resolve against one documented stable Host root, while explicit absolute overrides continue to work. | Focused configuration/source tests from distinct working directories. |
| SC-5 | Startup logs expose the effective content root, resolved channel script, advertised IP, and listener ports. | Host startup log assertion or reviewed structured startup-summary implementation. |
| SC-6 | Existing Channel, Game, resource, and full-solution behavior remains compatible. | Fresh affected suites, full solution tests with the real PVF, Host build, and clean diff checks. |
| SC-7 | The runbook no longer depends on an accidental working directory or an undocumented path environment override. | Reviewed documentation diff matching the verified startup behavior. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | User accepted the proposed permanent six-layer fix and instructed “好，完成任务吧”. | Implement, test, independently review, verify, and create one accepted local result commit for the bounded Host/channel-resource startup fix in one isolated worktree. |

## Requires Confirmation

- Merging the accepted task branch, pushing it, restarting the shared live server, or cleaning
  the accepted worktree/branch requires a separate user choice after completion.

## Assumptions

- `Resources/Channel/channel_info.etc` is the authoritative packaged default selected by the
  existing Host project and should remain so.
- The current Channel cryptography and response codecs are already compatible when the script
  resource is actually available; this task protects resource delivery rather than changing
  the wire protocol.
