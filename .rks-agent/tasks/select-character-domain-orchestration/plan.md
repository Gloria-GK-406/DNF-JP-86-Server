---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Select Character domain orchestration

## Implementation Approach

The earliest core slice is to make the intended ownership compile-time visible: place the composite selection result in an application/use-case data namespace, replace the combined inventory entry contract with character- and account-owned contracts, remove the cross-domain character-entry facade contract, and introduce the narrow equipment-facing character facts view. Existing behavior tests are migrated with those contracts, while structural ownership is verified by compilation, static inspection, and review rather than by manufacturing an interface-shaped RED.

The implementation then adapts storage and domain services behind those contracts before moving orchestration into the command handler. This order keeps repository decoding, domain projection, and final flow failures separable. Networking changes last and are limited to the result namespace, followed by complete architecture and behavior verification. The handler may have many explicit dependencies because it is the honest composition boundary; it must not hide them in another facade or dependency bundle.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs`, `Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs`, and new `Server/src/Dfo.Server.Data/Game/` models | Remove application response and cross-owner inventory bundles from character/inventory domain models; add the application-owned selection envelope and narrow equipment facts value. | Existing character, inventory, account, world, and session state types. | Domain-owned values plus one application/use-case composite result. |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterServices.cs` and `Server/src/Dfo.Server.Abstractions/Inventory/IInventoryEntryServices.cs` | Remove the cross-domain character-entry facade; expose separate character inventory, account inventory, equipment snapshot query, and equipment-facing character facts contracts. | Approved domain ownership and existing storage needs. | Purpose-limited public service and repository ports with no combined application result. |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteInventoryEntryRepository.cs` and `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs` | Serve character inventory and account cargo through separated storage operations while sharing decoding/validation internally and retaining ownership and migration checks. | Existing normalized SQLite tables and item-core decoder. | Independently loadable character and account inventory storage results with unchanged validation semantics. |
| BD-004 | `Server/src/Dfo.Server.Application/Characters/CharacterService.cs`, `Server/src/Dfo.Server.Application/Inventory/InventoryEntryService.cs`, and `Server/src/Dfo.Server.Application/ApplicationModule.cs` | Implement and register the separated inventory projections, equipped-creature query, and narrow read-only character equipment facts capability. | BD-001 and BD-002 contracts, BD-003 repository ports. | Cohesive application domain services available to Select Character and future equipment rules. |
| BD-005 | `Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs`, `Server/src/Dfo.Server.Application/Characters/CharacterEntryService.cs`, and directly related application support | Make the command handler explicitly coordinate selection, keep admission first and publication last, and retire the broad facade implementation. | Separated services from BD-004 plus existing character, account, channel, world, placement, session, and publication services. | Complete application selection result with preserved success and failure behavior. |
| BD-006 | `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs`, `Server/tests/Dfo.Server.Application.Tests/OnlineUsers/OnlineUserServiceTests.cs`, `Server/tests/Dfo.Server.Application.Tests/Support/EntryServices.cs`, and new focused command/domain tests as needed | Migrate facade-centered tests to domain-service and command-orchestration tests; add ordering, exactly-once publication, failure preservation, ownership, and narrow-contract coverage. | Approved acceptance scenarios and existing fixtures. | Focused executable evidence for RQ-001 through RQ-006 and RQ-008. |
| BD-007 | `Server/tests/Dfo.Server.Infrastructure.Tests/Inventory/SqliteInventoryEntryRepositoryTests.cs` and directly related inventory fixtures | Prove separated SQLite reads preserve populated, empty, malformed, ownership, and legacy-migration behavior. | BD-003 storage split and existing normalized test databases. | Infrastructure regression evidence for both inventory owners. |
| BD-008 | `Server/src/Dfo.Server.Networking/Framing/SelectCharacterResponseComposer.cs`, `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`, and focused networking tests | Consume the application-owned result namespace without changing projection behavior. | BD-001 selection result and existing packet composer. | Compile-time integration and byte-level regression evidence for unchanged response projection. |

## Ordered Tasks

### T-001: Establish the separated model and service contracts

- Covers: RQ-001, RQ-002, RQ-003, RQ-006
- Boundaries: BD-001, BD-002, BD-006
- Inputs: Ready Spec, existing domain models and service ports, current facade-centered tests.
- Outputs: Application-owned selection envelope, separate inventory ports, narrow equipment-facing character facts, removed facade contract, and preserved behavior tests expressed at the new boundary.

#### Steps

1. Add focused tests that require separate character/account inventory results, an application-owned response, and a purpose-limited read-only character equipment facts view.
2. Move only the composite selection envelope and its entry wrappers out of character-domain ownership; retain intrinsic character state in the character model.
3. Replace the combined inventory service/storage result contracts with separate character and account operations and records, retaining shared low-level item record types where ownership is explicit.
4. Remove the cross-domain character entry facade contract and ensure no replacement interface hides the same complete flow.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | Non-command: compare migrated focused tests with the baseline behavior cases before changing their assertions | Tests continue to observe successful composition, source failure, refusal, publication, and inventory projection behavior rather than constructor shape or collaborator identity. |
| CK-002 | `dotnet build Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj --no-restore` after contract changes | The domain-facing contract project builds with only Data and approved abstraction dependencies. |

### T-002: Adapt inventory storage and cohesive domain services

- Depends on: T-001
- Covers: RQ-002, RQ-003, RQ-005, RQ-008
- Boundaries: BD-003, BD-004, BD-006, BD-007
- Inputs: Separated contracts and preserved focused behavior tests from T-001, existing SQLite decoder and normalized schema.
- Outputs: Green character inventory, account inventory, equipped-creature query, and character equipment facts services with preserved validation.

#### Steps

1. Split the SQLite entry read into character-owned and account-owned results while reusing item-core decoding, range validation, ownership checks, and legacy-migration detection.
2. Split the application inventory projection into the two public domain services and keep account cargo locks and virtual resources out of the character result.
3. Implement the equipped-creature snapshot query inside the inventory/equipment boundary so selection orchestration does not encode inventory slot or item-kind rules.
4. Implement the read-only equipment-facing character facts service from account-owned character data and expose only character identifier, level, job, and growth type unless current code demonstrates another necessary fact.
5. Register each public contract explicitly, preserving a shared concrete service only where it does not reintroduce a combined public boundary.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~SqliteInventoryEntryRepositoryTests` | Character and account storage tests pass for populated, empty, invalid-owner, malformed-core, duplicate, and legacy-migration cases. |
| CK-004 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterEquipmentFacts|FullyQualifiedName~InventoryEntry"` | Separate projection and narrow-facts tests pass; no test consumes a combined inventory result. |

### T-003: Move Select Character orchestration to the command boundary

- Depends on: T-002
- Covers: RQ-001, RQ-002, RQ-004, RQ-005, RQ-006, RQ-008
- Boundaries: BD-004, BD-005, BD-006
- Inputs: Green separated services, existing channel/world/placement/session/publication behavior, current facade orchestration as compatibility reference.
- Outputs: Explicit handler-level orchestration, no facade implementation, early refusal, late exactly-once publication, and complete application response.

#### Steps

1. Make the command handler load the selected character, evaluate channel admission, load the required character/account/inventory/world/placement/session results, and publish only after every read succeeds.
2. Build the world-content question from character progress, character quest/collection state, and the inventory-owned equipped-creature query without moving those source values into character state.
3. Compose the application result from the independently owned results and current timestamp, preserving the existing channel destination used for session-adjacent lookup.
4. Remove the old facade implementation, registration, factory helpers, and facade-centered test support.
5. Retain workflow/exclusive account command registration and ensure no handler dependency is a repository, transport adapter, command bus, or newly introduced all-purpose facade.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter FullyQualifiedName~SelectCharacterCommandHandlerTests` | Tests pass for success composition, admission-before-load, each representative downstream failure, unchanged previous active state, unavailable-source propagation, and exactly-once final publication. |
| CK-006 | `rg -n "ICharacterEntryService|CharacterEntryService|ICommandBus|I.*Repository" Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs Server/src/Dfo.Server.Application/Characters Server/src/Dfo.Server.Abstractions/Characters` | The former facade has no production reference; the Select Character command handler contains neither nested command dispatch nor repository dependencies, and character abstractions do not regain a cross-domain entry facade. |

### T-004: Reconnect networking without changing the protocol

- Depends on: T-003
- Covers: RQ-002, RQ-007
- Boundaries: BD-001, BD-008
- Inputs: Application-owned selection result and current response composer behavior.
- Outputs: Networking consumes the relocated result and byte-level behavior remains compatible.

#### Steps

1. Update command, message response, sender, and composer imports to the application-use-case result namespace.
2. Add or adapt focused response-composer regression coverage with a representative complete result.
3. Confirm no protocol constant, frame, response ordering, or encoded field changes are introduced.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter FullyQualifiedName~SelectCharacter` | Select Character codec/composer tests pass with unchanged packet ordering and byte content. |
| CK-008 | `git diff -- Server/src/Dfo.Server.Networking/Framing/GameProtocol.cs` | No protocol constant or message identifier changes are present. |

### T-005: Prove the complete refactor boundary

- Depends on: T-004
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007, RQ-008
- Boundaries: BD-001, BD-002, BD-003, BD-004, BD-005, BD-006, BD-007, BD-008
- Inputs: Complete product and test diff from T-001 through T-004.
- Outputs: Review-ready implementation with full behavior, architecture, and repository-consistency evidence.

#### Steps

1. Inspect the complete call graph and type locations against every ownership boundary and remove obsolete combined types, registrations, usings, and test fixtures.
2. Scan Application, Networking, Data, and Abstractions for concrete SQLite leakage, repository use from command handling, nested command dispatch, `OnlineUserId` flow, and stale facade references.
3. Run focused and full verification from the task worktree and check the complete diff for formatting and accidental scope expansion.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `rg -n "Microsoft\.Data\.Sqlite" Server/src/Dfo.Server.Application Server/src/Dfo.Server.Networking Server/src/Dfo.Server.Data Server/src/Dfo.Server.Abstractions` | No concrete SQLite dependency leaks into domain-facing or transport projects. |
| CK-010 | `rg -n "OnlineUserId" Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs Server/src/Dfo.Server.Application/Characters Server/src/Dfo.Server.Application/Inventory` | No online-session identifier is introduced into downstream Select Character business flow. |
| CK-011 | `dotnet test Server/86JP.Server.slnx --no-restore` | The full solution passes at or above the baseline of 296 passing tests, with only environment-dependent PVF tests skipped for absent archive data. |
| CK-012 | `git diff --check` | The complete task diff has no whitespace errors. |
| CK-013 | Non-command: inspect the complete diff against RQ-001 through RQ-008 and all non-goals | Every approved ownership, sequencing, failure, dependency, and compatibility condition is represented with no PVF/G5, equipment behavior, protocol, or schema scope expansion. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-003, T-005 | BD-001, BD-002, BD-005, BD-006 | CK-001, CK-005, CK-006, CK-013 |
| RQ-002 | T-001, T-002, T-003, T-004, T-005 | BD-001, BD-002, BD-003, BD-004, BD-005, BD-006, BD-007, BD-008 | CK-002, CK-003, CK-004, CK-005, CK-007, CK-013 |
| RQ-003 | T-001, T-002, T-005 | BD-001, BD-002, BD-004, BD-006 | CK-001, CK-002, CK-004, CK-013 |
| RQ-004 | T-003, T-005 | BD-005, BD-006 | CK-005, CK-013 |
| RQ-005 | T-002, T-003, T-005 | BD-003, BD-004, BD-005, BD-006, BD-007 | CK-003, CK-004, CK-005, CK-013 |
| RQ-006 | T-001, T-003, T-005 | BD-002, BD-004, BD-005, BD-006 | CK-002, CK-005, CK-006, CK-009, CK-010, CK-013 |
| RQ-007 | T-004, T-005 | BD-001, BD-008 | CK-007, CK-008, CK-011, CK-013 |
| RQ-008 | T-002, T-003, T-005 | BD-003, BD-004, BD-005, BD-006, BD-007 | CK-003, CK-005, CK-011, CK-013 |

## Risks and Rollback

- Risk: Splitting inventory reads may accidentally weaken ownership, legacy-migration, or duplicate-slot validation. Mitigation: preserve common validation helpers and require owner-specific repository regression coverage before orchestration migration.
- Risk: Moving the composite result may silently alter Networking projection imports or construction order. Mitigation: limit Networking edits to type consumption and require representative byte-level projection evidence.
- Risk: The application handler's explicit dependency list is long. This is accepted as an honest use-case boundary; introducing a dependency bundle or new all-domain facade to shorten the constructor is prohibited.
- Risk: Environment-dependent PVF tests are skipped when the archive is absent. Mitigation: preserve current unavailable-source tests and compare the full-suite skip reason with the clean baseline rather than treating fabricated content as verification.
- Rollback: Before acceptance, the isolated task worktree can be restored exactly to baseline `3b32ca494c3f384c9d2f29dd63bcd1bbe3b2d0ce` while preserving only declared SDD evidence, subject to the workspace controller's ownership proof.

## Assumptions

- The combined SQLite inventory repository is an implementation detail that may be split into owner-specific operations without a schema change.
- The selected character's current account-cargo lock rows remain resolved with the selected character identifier where the legacy storage requires it, while the projected result remains account-owned.
