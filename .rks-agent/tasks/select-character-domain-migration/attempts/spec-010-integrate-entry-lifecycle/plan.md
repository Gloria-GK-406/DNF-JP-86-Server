---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Integrated Select Character Lifecycle

## Implementation Approach

Make the existing account-exclusive command the explicit application workflow while
extracting its current cross-domain read composition into a preparation service. Introduce
one immutable selected-session identity carrying account, character, connection, expected
connection generation and lossless wire user ID. Session registries compare that full
identity; character, inventory, quest and account services continue to receive owner IDs
only. Add entry-specific peripheral adapters and cohesive mutation services, then drive the
accepted typed G6 sender in lifecycle phases. Required owner sends participate in rollback;
explicit peer sends remain best effort. Behavior tests protect observable order and exact
side effects rather than constructor or collaborator shape.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/{Game,Networking,World}` and `Server/src/Dfo.Server.Abstractions/{Sessions,Networking,OnlineUsers,Characters}` | Define selected-session identity, exact lifecycle/publication results, typed mutation projections and connection-close capability. | Accepted G6 contracts and current online-user snapshot. | Neutral generation-safe contracts with no frame or repository type. |
| BD-002 | `Server/src/Dfo.Server.Application/{Sessions,Users}` and corresponding Application tests | Implement exact register-replacing/remove/current resolution and generation-aware presence, PvP, expert-store, rejoin and active-character behavior. | BD-001 identity and existing runtime registries. | Current-session directory and recipient queries that cannot remove a replacement. |
| BD-003 | `Server/src/Dfo.Server.Abstractions/{Inventory,Characters,Content}` and `Server/src/Dfo.Server.Infrastructure/{Inventory,Characters,Content}` | Add entry-specific normalized persistence and PVF sources for leases/refill, position/dungeon preparation, seeking quest operands/progress and pet revival. | Existing normalized SQLite tables, real `Script.pvf`, `PreparedAt`. | Owner-validated atomic mutation results and fail-closed content rules. |
| BD-004 | `Server/src/Dfo.Server.Application/{Inventory,Characters,World,Sessions}` and focused tests | Implement cohesive entry mutation services and packet-ready results without connection discovery or framing. | BD-002 current identity, BD-003 ports, accepted G1-G5 state. | Committed refill groups, changed quests, revival, cleanup and rejoin results. |
| BD-005 | `Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs`, a bounded preparation helper, `Server/src/Dfo.Server.Application/Game/SelectCharacterHandler.cs`, and `ApplicationModule.cs` | Coordinate preflight, cleanup, register, preparation, required delivery, publication, post-entry stages and compensation. | BD-001-BD-004 and accepted G6 sender. | One accepted/rejected lifecycle result with exact event order and rollback. |
| BD-006 | `Server/src/Dfo.Server.Networking/Runtime`, `NetworkingModule.cs`, and Networking tests | Implement connection close and preserve typed required/best-effort delivery semantics without target discovery. | Explicit connection ID and typed messages/projections. | Required sends, isolated target sends and exact failed-connection close. |
| BD-007 | `Docs/Refactor/{SelectCharacterMigrationMatrix.md,SelectCharacterG6Coverage.md,SelectCharacterG7Coverage.md}` plus integrated tests and Host configuration | Record implemented G7 ownership/order and provide complete verification. | Reviewed implementation and accepted matrix. | Honest code-reversed coverage, full test and Host-start evidence. |

## Ordered Tasks

### T-001: Establish exact selected-session ownership

- Covers: RQ-001, RQ-002, RQ-007
- Boundaries: BD-001, BD-002
- Inputs: Authenticated connection snapshots, existing active-character and session registries.
- Outputs: Lossless wire identity policy, register-replacing directory, exact remove/current
  checks and generation-safe active/presence/PvP cleanup.

#### Steps

1. Add behavior tests for invalid wire IDs, same-account replacement, stale disconnect,
   exact removal and recipient resolution by current character/wire identity.
2. Define a session-only identity carrying connection generation without exposing
   `OnlineUserId` to downstream business services.
3. Extend or replace runtime registries so every mutable session-owned entry is guarded by
   the exact selected identity and deterministic recipient ordering.
4. Make active-character publication/clear compare the same identity while retaining
   AccountId-indexed business queries.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~Session\|FullyQualifiedName~OnlineUser\|FullyQualifiedName~WireUser"` | Replacement and stale cleanup behavior passes without clearing the new generation. |
| CK-002 | `rg -n "OnlineUserId" Server/src/Dfo.Server.Application/{Characters,Inventory,World} Server/src/Dfo.Server.Abstractions/{Characters,Inventory,Content}` | No downstream domain propagation is introduced. |

### T-002: Add truthful entry mutation sources and adapters

- Depends on: T-001
- Covers: RQ-004, RQ-006, RQ-008
- Boundaries: BD-003
- Inputs: Normalized inventory/character tables, real PVF refill rules, item stack limits and
  quest seeking operands.
- Outputs: Atomic owner-validated persistence ports plus fail-closed typed content sources.

#### Steps

1. Reverse-map the legacy daily refill tuple, stack-limit lookup and seeking objective
   operands into narrow neutral content records; prove real-archive populated behavior.
2. Add entry-specific repository transactions for inventory refill/lease reload, position
   and dungeon preparation, active-quest trigger update and equipped-pet state transition.
3. Validate owner, slot, template, quantity, trigger and generation-independent persistence
   fields; do not invent missing tables or treat malformed rules as loaded-empty.
4. Cover success, no-op, rollback and unavailable/malformed data at adapter boundaries.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~EntryLifecycle\|FullyQualifiedName~DailyRefill\|FullyQualifiedName~SeekingQuest\|FullyQualifiedName~PetRevival"` | Real content and normalized persistence positive/negative cases pass. |
| CK-004 | `rg -n "Dfo.Pvf|PvfArchive|PvfToken|Microsoft.Data.Sqlite" Server/src/Dfo.Server.Application Server/src/Dfo.Server.Networking` | No concrete peripheral dependency leaks into Application or Networking. |

### T-003: Implement cohesive preparation and post-entry services

- Depends on: T-002
- Covers: RQ-003, RQ-004, RQ-005, RQ-006
- Boundaries: BD-004
- Inputs: Exact current session, peripheral ports, accepted entry state and G6 projection contracts.
- Outputs: Lease/preparation outcomes and typed X-07/X-09-X-18 projection plans.

#### Steps

1. Implement inventory lease save/load/register/reload and daily refill transaction service;
   only committed changes become grouped X-07 projections.
2. Implement position/dungeon-effect/skill preparation behind narrow entry services and
   ensure reloaded state is used when a preparation mutation commits.
3. Implement PvP snapshot/recipient planning with owner-direct handshake separated from
   newcomer publication and ready-state mutation.
4. Implement changed-only seeking quest sync, pet revival transition, trailing locks and
   valid/expired rejoin handling with typed results and stable ordering.
5. Implement expert-store and town cleanup plans for normal leave, displaced cleanup and
   rollback include-owner semantics.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~EntryLifecycleService\|FullyQualifiedName~DailyRefill\|FullyQualifiedName~PvpEntry\|FullyQualifiedName~QuestEntry\|FullyQualifiedName~PetEntry\|FullyQualifiedName~DungeonRejoin"` | Positive, no-op and failure service behavior passes with no framing dependency. |

### T-004: Coordinate the complete generation-safe workflow

- Depends on: T-003
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007
- Boundaries: BD-005, BD-006
- Inputs: Exact session services, preparation/mutation results, typed G6 response and projection sender.
- Outputs: Admission rejection or one fully published selected session, with compensation on failure.

#### Steps

1. Pass connection ID and expected generation from the network handler; preflight current
   authentication, wire identity and admission before any side effect.
2. Extract the current cross-domain read composition as a preparation operation while the
   command remains the explicit phase coordinator.
3. Implement the success event order: clear/release old state, clean previous/displaced,
   register, prepare, send static and X-01-X-06, commit/send X-07, publish/admit, perform
   PvP direct then target publication, X-11, X-12, X-13/X-14 and X-15/X-16.
4. Implement compensation after registration with exact removal, independent best-effort
   cleanup, reload/detach, active clear and failed-connection close. Do not attempt to
   fabricate restoration of a previous runtime already left successfully.
5. Preserve peer failure isolation and ensure required owner failure stops later stages.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterLifecycle"` | Integrated success, rejection, replacement, fault and compensation ledgers pass. |
| CK-007 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacter\|FullyQualifiedName~ConnectionClose"` | Required and target delivery plus exact close behavior passes without recipient discovery. |
| CK-008 | `rg -n "SendAsync\(|SendProjectionAsync\(" Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs` | The command is visibly the phase coordinator for required typed delivery; no nested command path is introduced. |

### T-005: Verify the integrated G7 result and hand off client validation

- Depends on: T-004
- Covers: RQ-008
- Boundaries: BD-007
- Inputs: Completed integrated implementation and reviewed legacy matrix.
- Outputs: Exact G7 coverage, full regression evidence and runnable Host smoke evidence.

#### Steps

1. Add a G7 coverage ledger mapping X-07, X-09-X-18 and L-01-L-10 to trigger owner,
   mutation, recipient, failure and rollback behavior.
2. Update G6/matrix status without promoting code-reversed claims to client-confirmed.
3. Run the real-PVF full solution, focused lifecycle suites, dependency/diff checks and a
   bounded Host startup smoke using non-production local configuration.
4. Confirm all accepted G1-G6 projection tests remain green and no stale single-send or
   early-publication path survives.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore` | Complete solution passes with only documented archive-present skips. |
| CK-010 | `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore` | Host builds with zero errors and the bounded startup smoke reaches listening state without DI failure. |
| CK-011 | `git diff --check` | No whitespace error exists. |
| CK-012 | `Non-command: compare G7 coverage to matrix X-07, X-09-X-18 and L-01-L-10` | Every identity/lifecycle row has an implemented owner, order, failure and rollback disposition; confidence remains code-reversed. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001,T-004 | BD-001,BD-002,BD-005 | CK-001,CK-006,CK-008 |
| RQ-002 | T-001,T-004 | BD-001,BD-002,BD-005 | CK-001,CK-006 |
| RQ-003 | T-003,T-004 | BD-004,BD-005,BD-006 | CK-005,CK-006,CK-007 |
| RQ-004 | T-002,T-003,T-004 | BD-003,BD-004,BD-005 | CK-003,CK-005,CK-006 |
| RQ-005 | T-003,T-004 | BD-002,BD-004,BD-005,BD-006 | CK-005,CK-006,CK-007 |
| RQ-006 | T-002,T-003,T-004 | BD-003,BD-004,BD-005 | CK-003,CK-005,CK-006 |
| RQ-007 | T-001,T-004 | BD-001,BD-002,BD-005,BD-006 | CK-001,CK-006,CK-007 |
| RQ-008 | T-002,T-005 | BD-003,BD-007 | CK-004,CK-009,CK-010,CK-011,CK-012 |

## Risks and Rollback

- Risk: Extending session identity across existing registries can accidentally leave one
  character-keyed removal path; mitigate with stale-generation fault tests and a complete
  removal-call review.
- Risk: Legacy refill and seeking rules are content-shaped; malformed/unresolved rules must
  fail closed rather than grant items or advance quests.
- Risk: Required send failure occurs after previous-runtime cleanup; compensation closes the
  failed connection and removes only its new attempt, but does not invent restoration.
- Rollback: The parent controller can restore the entire unaccepted Spec-010 diff to accepted
  baseline `1ab5f0d`; no remote or external system is mutated.

## Assumptions

- `Script.pvf` remains available at the task worktree root for real-content tests.
- Host smoke may use temporary local ports and an ephemeral database without contacting an
  external client.
