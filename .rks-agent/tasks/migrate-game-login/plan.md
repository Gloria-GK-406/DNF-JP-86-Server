---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Game login and indexed online-user execution

## Implementation Approach

Build the smallest complete vertical login mainline around a server-generated
`OnlineUserId`. The unavoidable first slice defines shared identity/account data and
service boundaries. Then implement and test the online aggregate plus its writer-preferred
shared/exclusive gate, integrate that gate into Command registration, add SQLite account
persistence, and finally connect the typed LOGIN Message/Command to TouchSocket framing.
Complete protocol documentation and full regression evidence after the core path works.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/Accounts/`, `Server/src/Dfo.Server.Data/OnlineUsers/`, `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` | Define pure account, online-user, and LOGIN data contracts. | Approved Spec data rules. | Stable identifiers, snapshots, and typed LOGIN messages. |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Accounts/`, `Server/src/Dfo.Server.Abstractions/OnlineUsers/` | Define persistence and online-user query/mutation boundaries. | BD-001 models. | Dependency-direction-safe service interfaces. |
| BD-003 | `Server/src/Dfo.Server.Application/OnlineUsers/` | Implement aggregate behavior, indexed registry, connection observer, and asynchronous execution gate. | BD-001 and BD-002. | Anonymous/authenticated lifecycle and writer-preferred leases. |
| BD-004 | `Server/src/Dfo.Server.Application/Commands/` | Add user access metadata and the online-user execution Pipeline behavior. | BD-003 execution gate. | Unscoped/shared/exclusive Command execution policy. |
| BD-005 | `Server/src/Dfo.Server.Infrastructure/Accounts/`, Infrastructure project/configuration files | Persist legacy-compatible account lookup or creation in SQLite. | BD-001 and BD-002 account contract. | Production account repository and isolated persistence tests. |
| BD-006 | `Server/src/Dfo.Server.Application/Game/`, `Server/src/Dfo.Server.Application/Commands/Game/`, `ApplicationModule.cs` | Implement exclusive LOGIN workflow and response projection. | BD-002, BD-003, BD-004, BD-005. | Account-bound online user and typed success response. |
| BD-007 | `Server/src/Dfo.Server.Networking/Configuration/`, `Framing/`, `Runtime/`, `NetworkingModule.cs` | Decode LOGIN DStrings and encode the configured legacy success body through TouchSocket. | BD-001 and BD-006 messages. | Exact Game LOGIN TCP request/response behavior. |
| BD-008 | `Server/tests/`, `Docs/NetworkingFrameProtocol.md`, `.rks-agent/tasks/migrate-game-login/` | Prove concurrency, lifecycle, persistence, protocol, and regressions; record reverse-engineering limits. | All implementation boundaries. | Acceptance evidence and maintained documentation. |

## Ordered Tasks

### T-001: Establish online-user contracts and aggregate lifecycle

- Covers: RQ-001,RQ-005,RQ-009
- Boundaries: BD-001,BD-002,BD-003,BD-008
- Inputs: Ready Spec and existing network connection observer abstraction.
- Outputs: Lightweight identity models, online-user query/mutation services, aggregate state transitions, and matching connection lifecycle behavior.

#### Steps

1. Add pure account and online-user identifiers, states, snapshots, and LOGIN message shapes.
2. Add account persistence and online-user query/mutation abstractions.
3. Implement the online-user aggregate and indexed singleton registry.
4. Register Game connections anonymously and perform generation-safe disconnect cleanup.
5. Drive lifecycle behavior with focused application tests.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~OnlineUser` | Anonymous registration, lookup, authentication, and stale cleanup tests pass. |

### T-002: Add the shared/exclusive Command execution barrier

- Depends on: T-001
- Covers: RQ-006,RQ-007,RQ-008,RQ-009
- Boundaries: BD-003,BD-004,BD-008
- Inputs: OnlineUserId and online-user registry from T-001; existing Command pipeline registry.
- Outputs: Writer-preferred per-user gate, access-mode registration metadata, and Pipeline behavior.

#### Steps

1. Add unscoped/shared/exclusive access metadata to Command registration and execution context.
2. Implement an asynchronous per-user shared/exclusive gate with FIFO writer preference,
   cancellation-safe waiting, and idempotent lease release.
3. Add a Pipeline behavior that resolves user-scoped Commands and holds the appropriate lease around the complete handler execution.
4. Add deterministic concurrency tests for shared overlap, exclusive draining, writer preference, cancellation, and exception release.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-002 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~OnlineUserExecution` | All barrier ordering and release scenarios pass without timing-only assertions. |
| CK-003 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~CommandBusTests` | Existing Command dispatch, logging, policy, and no-nesting behavior remains passing. |

### T-003: Add account persistence for the login path

- Depends on: T-001
- Covers: RQ-003
- Boundaries: BD-005,BD-008
- Inputs: Account models and repository abstraction.
- Outputs: Configured SQLite repository that reuses existing accounts and atomically creates missing accounts.

#### Steps

1. Add the centrally versioned SQLite dependency and database path configuration.
2. Implement compatible accounts-table initialization and get-or-create behavior.
3. Test account creation, existing-account reuse, stored password hash, and persistence across repository instances using an isolated database.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --filter FullyQualifiedName~AccountRepository` | SQLite creation and reuse scenarios pass against a temporary database. |

### T-004: Implement the exclusive typed LOGIN application flow

- Depends on: T-001,T-002,T-003
- Covers: RQ-002,RQ-003,RQ-004,RQ-009
- Boundaries: BD-001,BD-004,BD-006,BD-008
- Inputs: Typed LOGIN request, account repository, online-user services, and exclusive Pipeline mode.
- Outputs: LOGIN Message handler, exclusive login Command handler, account binding, and typed success response.

#### Steps

1. Add the LOGIN application Command and result using `OnlineUserId` as its only online identity input.
2. Resolve or create the account and authenticate the aggregate inside the Command handler without nested Command dispatch.
3. Map connection to online-user index in the Message handler and send the success response only after successful execution.
4. Register the LOGIN Message and exclusive Command and add focused application tests.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~GameLogin` | LOGIN mapping, account binding, exclusive metadata, and response targeting tests pass. |

### T-005: Complete TouchSocket LOGIN decode and response encoding

- Depends on: T-004
- Covers: RQ-001,RQ-002,RQ-004,RQ-009
- Boundaries: BD-007,BD-008
- Inputs: Game framing, typed LOGIN messages, and configured legacy response fields.
- Outputs: Valid DString decoding, legacy success-body encoding, and real TCP login mainline.

#### Steps

1. Add LOGIN protocol constants and bounded ASCII DString decoding from the Game frame Body.
2. Extend the Game plugin to produce typed LOGIN requests while preserving EXIT dispatch.
3. Encode the configured legacy success body and Game server header for LOGIN responses.
4. Add codec tests and a TCP integration test that connects, logs in, asserts exact bytes, and observes the authenticated online-user state.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --filter FullyQualifiedName~GameLogin` | LOGIN parsing, encoding, TouchSocket dispatch, and authenticated-state assertions pass. |

### T-006: Document and verify the complete staged result

- Depends on: T-001,T-002,T-003,T-004,T-005
- Covers: RQ-010,RQ-011
- Boundaries: BD-008
- Inputs: Completed implementation and existing reverse-engineered protocol document.
- Outputs: Honest LOGIN documentation, clean complete diff, and fresh regression evidence.

#### Steps

1. Document the code-derived LOGIN body, response body, online-state effect, excluded post-login notifications, and remaining capture questions.
2. Inspect the complete diff for dependency direction, nested Command dispatch, stale cleanup, and unintended protocol expansion.
3. Run all solution tests and patch hygiene checks.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `dotnet test Server/86JP.Server.slnx` | All prior and new tests pass. |
| CK-008 | `git diff --check` | No whitespace errors are present. |
| CK-009 | `Non-command: inspect Docs/NetworkingFrameProtocol.md against RQ-011` | Documentation distinguishes reverse-engineered facts, staged exclusions, and capture uncertainty. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001,T-005 | BD-001,BD-002,BD-003,BD-007,BD-008 | CK-001,CK-006 |
| RQ-002 | T-004,T-005 | BD-001,BD-006,BD-007,BD-008 | CK-005,CK-006 |
| RQ-003 | T-003,T-004 | BD-002,BD-005,BD-006,BD-008 | CK-004,CK-005 |
| RQ-004 | T-004,T-005 | BD-001,BD-006,BD-007,BD-008 | CK-005,CK-006 |
| RQ-005 | T-001 | BD-001,BD-002,BD-003,BD-008 | CK-001 |
| RQ-006 | T-002 | BD-003,BD-004,BD-008 | CK-002 |
| RQ-007 | T-002 | BD-003,BD-004,BD-008 | CK-002 |
| RQ-008 | T-002 | BD-003,BD-004,BD-008 | CK-002 |
| RQ-009 | T-001,T-002,T-004,T-005 | BD-002,BD-003,BD-004,BD-006,BD-007,BD-008 | CK-001,CK-002,CK-005,CK-006 |
| RQ-010 | T-006 | BD-008 | CK-007,CK-008 |
| RQ-011 | T-006 | BD-008 | CK-009 |

## Risks and Rollback

- Risk: A non-atomic waiter list can violate writer preference or leak execution rights;
  mitigate with deterministic state-machine tests before protocol integration.
- Risk: SQLite schema assumptions can diverge from the legacy database; limit the table
  contract to columns already present with compatible defaults and verify reuse behavior.
- Risk: Login can authenticate an aggregate that disconnects concurrently; acquire the
  same exclusive per-user boundary and revalidate aggregate ownership before mutation.
- Rollback: If the reviewed result cannot meet the approved Spec, restore only this
  unaccepted attempt to baseline `61fef82` while preserving the declared task artifacts.

## Assumptions

- `Microsoft.Data.Sqlite` is available as a centrally versioned dependency compatible with
  the solution target framework.
- The base LOGIN response uses configuration defaults matching the observed legacy values;
  listener-specific policy beyond those fields remains outside this stage.
