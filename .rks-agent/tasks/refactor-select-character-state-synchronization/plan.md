---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Extract SelectCharacter State Synchronization

## Implementation Approach

Preserve the accepted client transcript and lifecycle behavior while replacing the SelectCharacter-specific multi-frame response with an explicit protocol workflow. The protocol handler will own the ordered workflow: acquire the selection transition boundary, dispatch cohesive commands sequentially, and send concrete outbound messages sequentially. Command handlers will own business mutation and return domain facts, but will neither send frames nor dispatch another command. Concrete state-synchronization messages will carry only the data required by their encoder; SelectCharacter will reuse those messages in the legacy order without a raw `type` switch or `occurrence` selector.

The earliest implementation task locks the existing transcript and the corrected dispatch rule because every later extraction can otherwise compile while silently reordering, omitting, or broadening the 58-frame behavior. That narrow safety work is the only prerequisite before moving orchestration and codecs.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `AGENTS.md`; `Docs/ProtocolSliceArchitectureNotes.md`; `Docs/Refactor/SelectCharacterMigrationMatrix.md`; `Docs/Refactor/SelectCharacterG6Coverage.md`; `Docs/Refactor/SelectCharacterG7Coverage.md` | Correct the dispatch rule and document entry acceptance plus lifecycle-wide state synchronization | Existing architecture rules, migration evidence, TaskContract | Maintained guidance matching executable behavior |
| BD-002 | `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/**`; `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs`; `Server/tests/Dfo.Server.Application.Tests/Commands/CommandBusTests.cs` | Establish transcript, order, absence, lifecycle and dispatch regression evidence | Current behavior and Spec acceptance criteria | Tests that fail on semantic or wire drift |
| BD-003 | `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs`; `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterCommand.cs`; new SelectCharacter phase command/result files | Move ordered orchestration to the protocol handler and split business phases without nested command dispatch | Authenticated connection, slot, channel, current lifecycle services | Sequential command results, sends and compensation decisions |
| BD-004 | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/**`; existing SelectCharacter static and supplemental codec files | Introduce concrete semantic outbound messages and one-frame encoders with narrow data inputs; retain explicit multi-frame encoders only where one semantic event is inherently a fixed adjacent batch | Domain-owned state records and explicit protocol constants | Reusable state messages and deterministic frames |
| BD-005 | `Server/src/Dfo.Server.Data/Game/SelectCharacterModels.cs`; `Server/src/Dfo.Server.Data/Game/SelectCharacterSupplementalModels.cs`; related domain data model files when required | Remove packet-ready SelectCharacter projection identities and retain only domain/workflow state | Existing domain state records | Domain results without sender, socket, raw type or occurrence semantics |
| BD-006 | `Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs`; `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`; `Server/tests/Dfo.Server.Networking.Tests/**SelectCharacter**` | Remove or narrow the SelectCharacter-specific delivery exception after concrete messages own encoding | Explicit recipients and concrete outbound messages | Protocol-neutral transport behavior |
| BD-007 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`; `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`; protocol registry tests | Register new command handlers and concrete outbound encoders by CLR message type | New phase commands and messages | Complete DI/encoder wiring with duplicate protection |
| BD-008 | `Server/src/Dfo.Server.Application/Characters/**SelectCharacter**`; `Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryService.cs`; `Server/src/Dfo.Server.Application/Sessions/**SelectCharacter**` | Expose cohesive lifecycle operations and domain results while keeping mutations out of the protocol handler | Selected session identity and prepared domain state | Reusable application services with exact cleanup/publication behavior |

## Ordered Tasks

### T-001: Lock the real dispatch boundary and accepted transcript

- Covers: RQ-001, RQ-002, RQ-005, RQ-006, RQ-009
- Boundaries: BD-001, BD-002
- Inputs: Current command-bus behavior, current 58-descriptor transcript and conditional fixtures
- Outputs: Corrected architecture wording and regression tests for handler-level sequential dispatch, command-handler nested-dispatch rejection, ACK-first ordering, optional rows and failure behavior

#### Steps

1. Replace the fabricated “handler cannot send a second command” sentence with the exact rule: protocol handlers may dispatch sequential commands; command handlers may not recursively or indirectly dispatch through `ICommandBus`.
2. Keep the existing nested-dispatch rejection test and add a handler-oriented sequential-dispatch test so documentation cannot re-expand the prohibition.
3. Convert the existing SelectCharacter composer coverage into a golden semantic transcript assertion that records ordered command/type identities, duplicate positions, row-11 Dark Knight adjacency and conditional row omission before the implementation is moved.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~CommandBusTests"` | Sequential top-level dispatch succeeds and nested command-handler dispatch remains rejected. |
| CK-002 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacter"` | Existing ACK-first transcript and conditional behavior are captured and passing. |
| CK-003 | `Non-command: review AGENTS.md handler and command rules against RQ-005` | No wording prohibits a protocol handler from dispatching multiple commands. |

### T-002: Separate lifecycle mutation phases from protocol delivery

- Depends on: T-001
- Covers: RQ-003, RQ-005, RQ-006, RQ-007
- Boundaries: BD-003, BD-008
- Inputs: Current `SelectCharacterCommandHandler`, lifecycle gate, preparation, refill, publication, quest, pet, PvP, rejoin and cleanup services
- Outputs: Cohesive phase commands/results with no sender or command bus inside command handlers and an explicit failure-compensation phase

#### Steps

1. Split admission/preparation/session replacement, committed daily refill, runtime publication/PvP planning, quest synchronization, pet revival and rejoin planning into the minimum cohesive command phases supported by current service ownership.
2. Return domain facts, current-session identity and explicit recipient facts from those phases; do not return encoded bodies, raw command/type identities or transport services.
3. Keep the selected-session transition lease around the whole handler workflow so concurrent selections preserve the current serialization boundary while individual commands retain their normal command-bus gates.
4. Preserve stale-generation checks before mutations and between required sends, and preserve exact cleanup plus best-effort close behavior after failure.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterLifecycle"` | Replacement, rollback, generation, refill, publication and cleanup scenarios retain their current outcomes. |
| CK-005 | `rg -n "ICommandBus|INetworkMessageSender|SendAsync|SendProjectionAsync" Server/src/Dfo.Server.Protocol/Game/SelectCharacter/*Command*.cs` | No command handler dispatches commands or owns outbound transport. |

### T-003: Extract reusable concrete state-synchronization messages

- Depends on: T-001
- Covers: RQ-002, RQ-003, RQ-004, RQ-006, RQ-008, RQ-010
- Boundaries: BD-004, BD-005, BD-007
- Inputs: Existing static and supplemental body codecs, domain state records and the golden transcript
- Outputs: Semantic CLR messages and registered encoders whose inputs are limited to the state required for that protocol meaning

#### Steps

1. Introduce domain-grouped state-synchronization slices for entry ACK, inventory full snapshot/update, user/public state, skills, quests, account settings/wallet/progression, character progression/features, world/session constants and optional pet/PvP/rejoin events.
2. Replace the raw `EncodeNotification(result, type, occurrence)` dispatch with concrete message encoders. Use domain identifiers such as container, title-book category or collection-box identity only where they are part of the message meaning; do not use wire type or occurrence as routing parameters.
3. Narrow every encoder input so no state-synchronization encoder consumes `SelectCharacterResult`; keep explicit validation of required, loaded-empty, optional-absent and invalid states.
4. Represent `00/000D` and `00/000E` as reusable full-snapshot and committed-delta messages sharing inventory item encoding without SelectCharacter naming.
5. Register every concrete outbound message by CLR type and assert registry completeness and duplicate rejection.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `rg -n "EncodeNotification|occurrence|SelectCharacterResult" Server/src/Dfo.Server.Protocol/Game/StateSynchronization Server/src/Dfo.Server.Protocol/Game/SelectCharacter/*Encoder*.cs` | No raw type/occurrence dispatcher remains and concrete sync encoders do not consume the aggregate result. |
| CK-007 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~StateSynchronization|FullyQualifiedName~SelectCharacter"` | Concrete encoders preserve required bytes, variants and null/empty behavior. |
| CK-008 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~ServerMessageEncoderRegistry"` | All message registrations resolve once with the correct game endpoint. |

### T-004: Rebuild SelectCharacter as explicit handler coordination

- Depends on: T-002, T-003
- Covers: RQ-001, RQ-002, RQ-005, RQ-006, RQ-007
- Boundaries: BD-003, BD-004, BD-007
- Inputs: Phase commands/results, concrete state messages and transcript evidence
- Outputs: A handler that sends entry acceptance first and then sequentially synchronizes domain state without a SelectCharacter multi-frame response

#### Steps

1. Make `SelectCharacterHandler` acquire the lifecycle transition, dispatch the preparation phase, pre-validate required message representability and send the concrete `01/0004` acceptance.
2. Send concrete domain synchronization messages in the legacy order, including deliberate duplicates and conditional omissions, with Dark Knight state adjacent to the skill snapshot.
3. Sequentially dispatch refill, publication/PvP, quest, pet and rejoin phases and send only non-empty returned facts at their existing lifecycle positions.
4. On admission refusal, required send failure, stale generation or later workflow failure, dispatch/execute exact compensation and close the affected connections without masking the original failure.
5. Remove `SelectCharacterResponse`, its multi-frame encoder/composer and the descriptor sequence after parity tests pass against the new handler-driven transcript.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacter"` | The new handler emits the same ordered transcript, ACK first, with identical conditional behavior. |
| CK-010 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterLifecycle"` | Required-send failures and concurrent selections retain cleanup and generation safety. |
| CK-011 | `rg -n "SelectCharacterResponseComposer|SelectCharacterStaticSequence|EncodeNotification" Server/src Server/tests` | No obsolete multi-frame response, descriptor table or raw type switch remains. |

### T-005: Remove SelectCharacter transport/projection semantic leakage

- Depends on: T-004
- Covers: RQ-003, RQ-004, RQ-008, RQ-010
- Boundaries: BD-004, BD-005, BD-006, BD-008
- Inputs: Concrete state messages, explicit recipient plans and current cleanup/PvP delivery rules
- Outputs: Protocol-neutral transport plus domain/protocol-owned recipient and message semantics

#### Steps

1. Replace SelectCharacter supplemental projection interfaces/composer switches with concrete outbound messages and an explicit handler/application recipient plan.
2. Preserve required-direct ordering and best-effort recipient isolation without teaching Networking about SelectCharacter or domain packet families.
3. Remove or narrow `SendProjectionAsync`, `ISelectCharacterProjectionComposer` and packet-ready `SelectCharacter*Projection` data types to the smallest remaining generic recipient capability.
4. Keep inventory deltas, PvP publication, cleanup notices, pet revival and rejoin batches independently usable outside character selection.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `rg -n "SelectCharacter|Projection" Server/src/Dfo.Server.Networking Server/src/Dfo.Server.Abstractions/Networking` | Networking and its abstraction expose no SelectCharacter-specific protocol composer or delivery contract. |
| CK-013 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore` | Required-direct order, best-effort isolation and endpoint validation remain passing through protocol-neutral messages. |
| CK-014 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterProjectionContract|FullyQualifiedName~SelectCharacterLifecycle"` | Domain and application boundaries no longer require packet-ready SelectCharacter projection types. |

### T-006: Reconcile documentation and verify the complete result

- Depends on: T-005
- Covers: RQ-001, RQ-006, RQ-007, RQ-008, RQ-009, RQ-010
- Boundaries: BD-001, BD-002, BD-003, BD-004, BD-005, BD-006, BD-007, BD-008
- Inputs: Implemented handler workflow, concrete messages, final source graph and maintained reverse-engineering evidence
- Outputs: Updated architecture/migration documents and fresh full-solution evidence

#### Steps

1. Update maintained protocol documents from “one SelectCharacter response/projection stream” to “entry acceptance followed by reusable lifecycle state messages,” preserving confidence labels and unconfirmed-client caveats.
2. Record the corrected dispatch boundary and remove the Networking SelectCharacter exception when the source no longer needs it.
3. Run focused tests, full build/tests, formatting/diff checks and dependency scans; reconcile any plan drift before review.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | `dotnet build Server/86JP.Server.slnx --no-restore` | The complete solution builds without warnings introduced by the task. |
| CK-016 | `dotnet test Server/86JP.Server.slnx --no-restore` | All solution tests pass. |
| CK-017 | `git diff --check` | No whitespace or patch-format defects. |
| CK-018 | `rg -n "handler 里不能再发第二条命令|SelectCharacter projection-delivery exception|EncodeNotification\(.*type.*occurrence" AGENTS.md Docs Server/src Server/tests` | Fabricated rule, obsolete exception and raw routing API are absent. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-004, T-006 | BD-001, BD-002, BD-003, BD-004 | CK-002, CK-009, CK-015, CK-016 |
| RQ-002 | T-001, T-003, T-004 | BD-002, BD-004, BD-007 | CK-002, CK-007, CK-009 |
| RQ-003 | T-002, T-003, T-005 | BD-003, BD-004, BD-005, BD-006, BD-008 | CK-005, CK-006, CK-012, CK-014 |
| RQ-004 | T-003, T-005 | BD-004, BD-005, BD-006 | CK-006, CK-007, CK-012 |
| RQ-005 | T-001, T-002, T-004 | BD-001, BD-002, BD-003 | CK-001, CK-003, CK-005, CK-009 |
| RQ-006 | T-001, T-003, T-004, T-006 | BD-002, BD-003, BD-004 | CK-002, CK-007, CK-009, CK-016 |
| RQ-007 | T-002, T-004, T-006 | BD-002, BD-003, BD-008 | CK-004, CK-010, CK-016 |
| RQ-008 | T-003, T-005, T-006 | BD-004, BD-005, BD-006, BD-007 | CK-007, CK-013, CK-016 |
| RQ-009 | T-001, T-006 | BD-001, BD-002 | CK-001, CK-003, CK-018 |
| RQ-010 | T-003, T-005, T-006 | BD-004, BD-005, BD-006, BD-007, BD-008 | CK-006, CK-012, CK-015, CK-018 |

## Risks and Rollback

- Risk: Moving the lifecycle lease to the handler could allow another selection command to interleave if the lease is released early. Mitigation: handler tests hold one lease across all sequential dispatch/send/compensation phases and assert stale-generation behavior.
- Risk: Narrow message inputs can accidentally reinterpret loaded-empty or optional-absent state. Mitigation: migrate one semantic family at a time behind byte-parity fixtures and keep explicit validation at encoder boundaries.
- Risk: Replacing projection delivery can change failure isolation for secondary recipients. Mitigation: retain required-direct failure propagation and per-recipient best-effort isolation as generic delivery tests.
- Rollback: Before the result is accepted, restore only task-owned implementation changes to the accepted worktree baseline `40f701b1f27702d5cca6e5ee3e361378865f3cc2`, preserving all durable task artifacts and unrelated work.

## Assumptions

- The legacy frame order and bytes remain the compatibility baseline; Windows/client findings may refine semantic names but do not authorize wire changes in this task.
- The selected-session transition gate is application workflow coordination and may be held by the protocol handler without moving business mutation into that handler.
- A fixed adjacent multi-frame batch is acceptable only when it represents one protocol semantic event (for example rejection plus notice or pet revival pair), not as a container for unrelated domain state.
