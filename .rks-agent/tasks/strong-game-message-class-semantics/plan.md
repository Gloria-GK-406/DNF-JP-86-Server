---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Strong Game CMD and NOTI Semantics

## Implementation Approach

Start with the earliest core slice: a Protocol-owned message-category contract, category-fixed
encoder bases and a Game sender facade that delegates to the generic sender. At the same time,
neutralize the raw first-byte terminology below Protocol and make the one C→S registration helper
fix CMD. Once that single authority compiles, migrate ordinary Game protocols, then the larger state
and supplemental families. The state step preserves the exact observed inventory rather than
inferring category from workflow names, and splits the one mixed rejection result into adjacent CMD
and NOTI messages. Finish with durable architecture coverage, guidance and complete verification.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs` and new Game category/encoding/sending support under `Server/src/Dfo.Server.Protocol/Game/` | Define Protocol-owned CMD/NOTI message contracts, category-fixed encoder bases, category-compatible sender operations, Game delivery types and registration helpers. | RQ-001..RQ-005, existing generic sender/encoder seams | One category authority with no caller wire-byte override |
| BD-002 | `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`, `Server/src/Dfo.Server.Abstractions/Networking/ClientMessageDecoding.cs`, `Server/src/Dfo.Server.Networking/Framing/GameClientFrameRequestInfo.cs`, `FrameEncoders.cs`, `Server/src/Dfo.Server.Networking/Runtime/ClientMessageDecoderRegistries.cs`, `NetworkPlugins.cs` | Rename the raw first byte to an opaque selector and preserve selector-plus-type routing without CMD/NOTI interpretation. | Confirmed framing layout, BD-001 category boundary | Protocol-neutral raw frame and decoder seams |
| BD-003 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` inbound decoder block | Register all client-originated Game protocols through the CMD-only Protocol helper. | BD-001, BD-002, current ten decoders | Explicit CMD C→S routing table |
| BD-004 | Game `Login/`, `Exit/`, `InitialConnection/`, `AccountRoster/` messages/encoders/handlers and `Game/OnlineUserConnectionObserver.cs`; corresponding outbound registration rows | Classify ordinary messages, migrate encoders to category-fixed payload encoding and use the Game sender facade. | BD-001, existing payload behavior | Strong CMD login/exit and NOTI initial/roster paths |
| BD-005 | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/` and `Game/SelectCharacter/SelectCharacterHandler.cs` | Split CMD/NOTI state hierarchies and registrations, remove raw selectors from codecs/messages, preserve PremiumService and CloneTitle as CMD, split rejection into ordered CMD+NOTI messages, and use category-safe direct/explicit delivery. | BD-001, exact current state/supplemental frames and order | Fully classified state synchronization with no mixed-category message |
| BD-006 | Game handler/observer tests under `Server/tests/Dfo.Server.Application.Tests/`, protocol tests under `Server/tests/Dfo.Server.Protocol.Tests/Game/`, and raw-frame/registry tests under `Server/tests/Dfo.Server.Networking.Tests/` | Preserve behavioral coverage, add durable category/registration architecture coverage, and adapt test senders to the Protocol facade. | BD-001..BD-005 and existing golden vectors | Exact-frame, routing, category and workflow regression evidence |
| BD-007 | `AGENTS.md`, `Docs/NetworkingFrameProtocol.md`, and implementation mapping in affected `protocol/` records | Describe CMD/NOTI ownership, sender/encoder rules, opaque Networking selector and exact implemented classifications without changing reverse conclusions. | `f29123b`, completed code inventory | Maintained guidance consistent with implementation and evidence |

## Ordered Tasks

### T-001: Establish the single category authority and neutral raw boundary

- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005
- Boundaries: BD-001, BD-002, BD-003
- Inputs: Confirmed CMD/NOTI evidence, generic sender/encoder seams, current selector-plus-type registry
- Outputs: Compiling category contracts, sender facade, fixed encoders/registrations, opaque raw selector and CMD-only inbound registrations

#### Steps

1. Add Protocol-owned base Game message and distinct CMD/NOTI marker contracts, fixed category encoder/payload support and category-safe outbound registration helpers.
2. Add a Game sender facade with distinct direct CMD/NOTI operations and typed explicit delivery that maps to the generic transport without altering category or delivery semantics.
3. Rename raw first-byte `Command` members/parameters below Protocol to `Selector`, including metadata, parser, registry key, plugin lookup and frame primitive.
4. Add a Protocol CMD decoder-registration helper and migrate every current client Game decoder row to it while retaining selector-plus-type lookup.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/src/Dfo.Server.Protocol/Dfo.Server.Protocol.csproj --no-restore` | Protocol and dependencies compile with the new single authority. |
| CK-002 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~FrameRequestInfoTests|FullyQualifiedName~ClientMessageDecoderRegistryTests|FullyQualifiedName~UnknownInboundFrameIntegrationTests"` | Opaque selector parsing and selector-plus-type routing retain all behavior. |
| CK-003 | `rg -n "\bCommand\b|command byte" Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs Server/src/Dfo.Server.Abstractions/Networking/ClientMessageDecoding.cs Server/src/Dfo.Server.Networking/Framing/GameClientFrameRequestInfo.cs Server/src/Dfo.Server.Networking/Framing/FrameEncoders.cs Server/src/Dfo.Server.Networking/Runtime/ClientMessageDecoderRegistries.cs Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` | No Game CMD/NOTI semantic name remains below Protocol; only unrelated domain uses, if any, are explainable. |

### T-002: Migrate ordinary Game messages and direct send paths

- Depends on: T-001
- Covers: RQ-001, RQ-002, RQ-003, RQ-007
- Boundaries: BD-004, ordinary-message portion of BD-006
- Inputs: T-001 category contracts, current ordinary encoders and golden frames
- Outputs: CMD Login/Exit and NOTI InitialConnection/Roster messages, registrations, handlers and observer using strong semantics

#### Steps

1. Mark ordinary outbound message records with their intrinsic category and move their encoders onto the matching fixed-category base.
2. Convert the initial-connection codec from complete-frame ownership to category-neutral payload ownership so its NOTI encoder fixes the selector.
3. Replace generic direct Game send calls with the matching protocol sender operations and update DI registration and test doubles.
4. Convert ordinary outbound registration rows to category-specific Game helpers.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~GameLoginCodecTests|FullyQualifiedName~GameMessageCategory"` | Shared type `0x0001` remains exact CMD login and NOTI initial notice; category contracts are complete. |
| CK-005 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~GameLoginIntegrationTests|FullyQualifiedName~GameExitIntegrationTests|FullyQualifiedName~OutboundSendFailureTests"` | Ordinary Game wire behavior and endpoint rejection remain unchanged. |
| CK-006 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~GameLoginTests|FullyQualifiedName~GameExitMessageHandlerTests|FullyQualifiedName~OnlineUserServiceTests"` | Handler and connection-observer behavior uses the strong facade without changing outcomes. |

### T-003: Classify state synchronization and split mixed rejection

- Depends on: T-002
- Covers: RQ-001, RQ-002, RQ-003, RQ-006, RQ-007
- Boundaries: BD-005, state/supplemental portion of BD-006
- Inputs: T-001/T-002 patterns, current StateSynchronization and SupplementalSynchronization exact frames
- Outputs: Separate CMD/NOTI state families, category-fixed encoders, category-safe delivery plans and adjacent split rejection messages

#### Steps

1. Replace the raw-command state base with CMD and NOTI state message families; retain CharacterEntryAccepted and PremiumService as CMD and every other existing main-state category exactly.
2. Convert supplemental codecs to category-neutral payload descriptions or bodies consumed by fixed-category encoders; retain CloneTitle as CMD and existing notification frames as NOTI.
3. Replace `EntryAdmissionRejectedMessage` with one CMD rejection message and one NOTI notice message carried in the same required ordered delivery.
4. Migrate state encoder registrations, factory outputs, SelectCharacter sends, cleanup and explicit recipient plans to Game-typed delivery without changing recipient order or failure isolation.
5. Update state/supplemental tests and application sender fixtures, retaining all existing byte/order assertions and adding the category inventory invariant.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterAcceptedTranscriptTests|FullyQualifiedName~SelectCharacterPayloadCodecTests|FullyQualifiedName~SupplementalSynchronizationTests|FullyQualifiedName~GameMessageCategory"` | Accepted/rejected transcripts and all state/supplemental frames retain exact class, bytes and order. |
| CK-008 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterLifecycleTests|FullyQualifiedName~OnlineUserServiceTests"` | Workflow ordering, compensation, explicit recipients and failure behavior remain unchanged. |
| CK-009 | `rg -n "GameServerFrameEncoder\.Encode|Command =>|Frame\([01],|Frame\(byte command" Server/src/Dfo.Server.Protocol/Game` | The only raw frame composition is the category authority; no message or codec selects `0/1`. |

### T-004: Reconcile guidance and prove the complete result

- Depends on: T-003
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007, RQ-008, RQ-009
- Boundaries: remaining BD-006, BD-007
- Inputs: Completed implementation, `f29123b` evidence and all earlier check results
- Outputs: Accurate maintained guidance and complete review/verification-ready change

#### Steps

1. Update architecture and frame-protocol guidance so CMD/NOTI live in Protocol, Networking carries an opaque selector, and strong send/encoder rules are explicit.
2. Update only affected protocol implementation mapping, preserving the reverse-evidence conclusions from `f29123b`.
3. Add or finalize architecture checks that prove every concrete outbound Game message has exactly one category and every Game encoder/registration matches it.
4. Run focused and full verification, inspect the complete diff for GameOption and unrelated changes, then hand the uncommitted result to complete-change review.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `dotnet build Server/86JP.Server.slnx --no-restore` | Complete solution builds with zero warnings and errors. |
| CK-011 | `dotnet test Server/86JP.Server.slnx --no-build --no-restore` | All enabled tests pass; only existing environment-dependent skips remain. |
| CK-012 | `rg -n "GameProtocol\.Command|\.Command\b|command:" Server/src/Dfo.Server.Protocol/Game Server/src/Dfo.Server.Networking Server/src/Dfo.Server.Abstractions/Networking Server/src/Dfo.Server.Data/Networking` | No obsolete raw selector ownership remains; any domain-command matches are outside the wire-category check and explicitly classified. |
| CK-013 | `git diff --check && test -z "$(git diff --name-only -- Server/src/Dfo.Server.Networking/Configuration/GameLoginOptions.cs)"` | Diff is clean and GameOption remains untouched. |
| CK-014 | Non-command: compare `AGENTS.md`, frame guidance and affected protocol implementation mappings with `f29123b` evidence and the final source inventory. | Guidance states one consistent CMD/NOTI model without overstating capture evidence or leaking semantics into Networking. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-002, T-003, T-004 | BD-001, BD-004, BD-005, BD-006 | CK-001, CK-004, CK-007, CK-009, CK-012 |
| RQ-002 | T-001, T-002, T-003, T-004 | BD-001, BD-004, BD-005, BD-006 | CK-001, CK-006, CK-008, CK-012 |
| RQ-003 | T-001, T-002, T-003, T-004 | BD-001, BD-004, BD-005, BD-006 | CK-001, CK-004, CK-007, CK-009, CK-012 |
| RQ-004 | T-001, T-004 | BD-002, BD-003, BD-006 | CK-002, CK-003, CK-012 |
| RQ-005 | T-001, T-004 | BD-001, BD-002, BD-006 | CK-002, CK-003, CK-012 |
| RQ-006 | T-003, T-004 | BD-005, BD-006 | CK-007, CK-008, CK-009, CK-012 |
| RQ-007 | T-002, T-003, T-004 | BD-004, BD-005, BD-006 | CK-004, CK-005, CK-006, CK-007, CK-008, CK-010, CK-011 |
| RQ-008 | T-004 | BD-007 | CK-014 |
| RQ-009 | T-004 | BD-006, BD-007 | CK-011, CK-013, CK-014 |

## Risks and Rollback

- Risk: The name “state synchronization” hides CMD exceptions. Mitigation: classify from current
  selector bytes and exact-frame tests, not names or workflow position.
- Risk: A protocol facade could accidentally change explicit-recipient failure isolation. Mitigation:
  adapt into the existing generic delivery contract and retain its focused tests unchanged.
- Risk: Mixed rejection splitting could alter adjacency. Mitigation: keep both messages in one
  required direct ordered delivery and assert the two-frame transcript.
- Rollback: Before the accepted result commit, restore only this task's classified paths and process
  artifacts to baseline `f29123b`; never touch `Diagnostics/` or unrelated concurrent work.

## Assumptions

- Current golden frames and transcript tests are authoritative for the implemented category of each
  message, including PremiumService, CloneTitle and rejection.
