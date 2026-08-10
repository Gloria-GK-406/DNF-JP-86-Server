---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Migrate the Game login handshake

## Context

### Source Request

“对，然后从登陆协议先开始进行吧。这个更自然一点，然后能让我们构建出那个查询和修改用户登陆信息的服务以及这排他的执行节点的设计。”

### Current Situation

The refactor branch can frame Game TCP traffic and dispatch the EXIT message, but it has
no Game login message, online-user identity model, account persistence boundary, or
per-user identity-transition barrier in the Command pipeline. The legacy server creates
an anonymous connection, accepts Game CMD `0x0001`, resolves or creates an account, binds
it to the session, and returns a fixed-format success acknowledgement.

## Goal

Provide a tested Game login mainline that establishes and authenticates one indexed
online-user aggregate and protects identity-changing Commands with an atomic exclusive
execution boundary while allowing ordinary user Commands to execute concurrently.

## Scope

### In

- Define the shared login wire messages and lightweight online-user/account identifiers.
- Register Game connections as anonymous online users and remove the matching generation
  on disconnect.
- Provide explicit services for querying and changing online login state by index.
- Add per-online-user shared/exclusive execution metadata and an asynchronous Command
  pipeline barrier with writer preference.
- Treat login as an exclusive identity-changing Command.
- Parse the observed legacy login request, resolve or create its account through an
  account persistence abstraction, bind the account to the online user, and return the
  observed base login success acknowledgement.
- Preserve the observed Game framing and cover the mainline and concurrency boundary with
  automated tests.
- Document reverse-engineered login behavior and remaining capture uncertainty.

### Out

- Character selection and character-domain initialization.
- Account settings, auction, honor, growth-capsule, and other post-login notifications.
- Password-policy redesign or changes to the legacy account auto-creation behavior.
- Migration of Game CMD `0x0002` or any protocol after the base login acknowledgement.
- Push, merge, pull request, release, or remote mutation.

## Constraints

- Commands must not dispatch nested Commands; shared execution logic remains in services.
- Once an exclusive identity transition is waiting, new shared executions for that online
  user must not enter ahead of it.
- A stale disconnect or execution generation must not remove or mutate a replacement
  online user.
- The socket layer must convert complete frames to shared business message types before
  Application handling.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | A Game LOGIN frame is parsed to a typed request and returns the observed success frame through TouchSocket. | TCP integration test asserting exact request/response bytes. |
| SC-2 | Login resolves or creates an account and the online-user query service reports the authenticated account for the same connection index. | Application and infrastructure tests covering creation, lookup, binding, and query. |
| SC-3 | Ordinary Commands for one online user may overlap, while an exclusive login/identity transition blocks new shared Commands and begins only after existing shared Commands finish. | Deterministic concurrency tests for overlap, draining, writer preference, cancellation, and release after failure. |
| SC-4 | Game connect/disconnect maintains the indexed anonymous/authenticated online-user lifecycle without stale removal. | Connection observer and TCP lifecycle tests. |
| SC-5 | Existing Channel and Game EXIT behavior remains intact. | Full solution regression suite. |
| SC-6 | The protocol document distinguishes code-derived behavior from packet-capture-confirmed behavior and records excluded post-login notifications. | Documentation inspection. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “对，然后从登陆协议先开始进行吧。这个更自然一点，然后能让我们构建出那个查询和修改用户登陆信息的服务以及这排他的执行节点的设计。” | Implement and locally commit the bounded Game login mainline, online-user services, exclusive Command pipeline node, tests, and documentation. |

## Requires Confirmation

- Push, merge, pull request creation, release, or any remote mutation.

## Assumptions

- The base login stage includes anonymous Game connection registration, login request and
  success acknowledgement; the legacy post-login notification fan-out remains staged for
  later protocol migrations.
- Until packet capture provides stronger evidence, the migration preserves the legacy
  behavior of looking up accounts by `m_id` and creating a missing account without adding
  new password validation semantics.
