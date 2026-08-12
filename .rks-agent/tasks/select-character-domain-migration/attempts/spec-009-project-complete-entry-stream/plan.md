---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Complete Select Character Entry Stream Projection

## Implementation Approach

The earliest core slice is a faithful body projection, but it cannot begin honestly while several accepted entry views discard protocol-relevant facts. First preserve those facts through their owning Data, repository and Application boundaries without adding wire types to domain models. Then implement cohesive body codecs, drive the 58 static occurrences from one explicit descriptor sequence, add packet-ready supplemental projections for G7, and integrate transport sending. This ordering prevents zero-filled bodies, hidden reordering and lifecycle leakage from becoming the foundation of later work.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/{Characters,Inventory,Accounts,Game,World}/*.cs` | Preserve owner-correct entry facts and packet-ready supplemental projection values without raw frames or service dependencies. | Normalized character, inventory, account, world and session facts. | Immutable typed inputs sufficient for every static and X projection. |
| BD-002 | `Server/src/Dfo.Server.Abstractions/{Characters,Inventory,Accounts,World}/*.cs` | Extend only the narrow owner ports required to load the preserved facts. | Owner identities and repository storage rows. | Domain-neutral typed storage/service contracts. |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/{Characters,Inventory,Accounts}/*.cs` | Read and validate currently discarded normalized legacy fields. | SQLite normalized entry tables. | Complete owner storage records with unavailable/malformed data rejected. |
| BD-004 | `Server/src/Dfo.Server.Application/{Characters,Accounts,Commands/Game}/*.cs` | Compose complete owner state, roster and projection envelope before active publication. | G1-G5 services and complete typed storage. | Fully populated `SelectCharacterResult` with publication still last. |
| BD-005 | `Server/src/Dfo.Server.Networking/Framing/SelectCharacter*.cs` and `GameProtocol.cs` | Encode cohesive body families and the exact descriptor-driven static/supplemental sequence. | Immutable completed selection and supplemental projection values. | Framed protocol messages with exact identities, order and multiplicity. |
| BD-006 | `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`, `Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs`, `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs` | Carry and send explicit static/direct/targeted projection batches without target discovery. | Completed projection messages and explicit connection recipients. | Required direct sends and best-effort targeted sends with ordered batch semantics. |
| BD-007 | `Server/tests/Dfo.Server.{Infrastructure,Application,Networking}.Tests/**` | Protect source preservation, body fidelity, static identity sequence, conditions, supplemental bodies and sender behavior. | Representative normalized rows and typed results. | Behavior-first regression evidence. |
| BD-008 | `Docs/Refactor/SelectCharacterMigrationMatrix.md` and `Docs/Refactor/SelectCharacterG6Coverage.md` | Record implemented identities, ownership, conditions and remaining G7 triggers honestly. | Reviewed matrix and complete implementation. | Auditable static/X coverage with code-reversed confidence. |

## Ordered Tasks

### T-001: Preserve complete typed projection input

- Covers: RQ-002, RQ-005, RQ-006
- Boundaries: BD-001, BD-002, BD-003, BD-004, BD-007
- Inputs: Accepted G1-G5 entry views, normalized legacy tables and current selection orchestration.
- Outputs: Owner-correct typed values for avatar detail, public subtypes, skills, options, collection status, expert state, premium usage, mailbox and roster, with unavailable required data rejected before publication.

#### Steps

1. Add behavior-first source-to-result cases that distinguish populated, loaded-empty, inapplicable and unavailable values for every identified gap.
2. Extend owner models and narrow storage ports without adding command/type numbers, encoded bodies or transport concerns.
3. Load and validate the corresponding normalized legacy values, then compose them into the completed selection result before active publication.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterEntry\|FullyQualifiedName~InventoryEntry\|FullyQualifiedName~AccountEntry"` | All preserved source facts and malformed/unavailable cases pass. |
| CK-002 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacter\|FullyQualifiedName~CharacterEntry"` | Complete composition passes and failure occurs before active publication. |

### T-002: Encode all static body families

- Depends on: T-001
- Covers: RQ-002, RQ-003, RQ-005, RQ-006
- Boundaries: BD-001, BD-005, BD-007
- Inputs: Complete typed selection result and code-reversed legacy body builders.
- Outputs: Deterministic ACK, inventory, user-info, character, account and constant body codecs for rows 1 through 58.

#### Steps

1. Establish representative body vectors for each cohesive wire family, including boundary, empty and conditional states.
2. Encode each static row from immutable typed inputs and reject unrepresentable required values.
3. Verify high-risk distinctions: avatar detail, subtype occurrences, option unavailable versus empty, collection status, mailbox entries and mixed premium usage.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterPayload"` | All body-family vectors and failure boundaries pass. |

### T-003: Compose the exact static occurrence sequence

- Depends on: T-002
- Covers: RQ-001, RQ-003, RQ-005
- Boundaries: BD-005, BD-007
- Inputs: Static body codecs and the authoritative 58-row matrix.
- Outputs: One descriptor-driven static stream preserving every identity, occurrence, condition and X-01 injection point.

#### Steps

1. Add an exact mechanical identity/occurrence comparison against the reviewed 58-row sequence.
2. Drive composition from explicit descriptors so duplicate identities remain separate and conditional omission cannot reorder neighbors.
3. Protect Knight, Dark Knight, pet, empty relation and repeated user-info variants with complete stream scenarios.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterResponseComposer\|FullyQualifiedName~SelectCharacterStaticSequence"` | Exactly 58 static descriptors are represented; emitted order and conditional multiplicity match the matrix. |

### T-004: Define and encode every supplemental projection

- Depends on: T-003
- Covers: RQ-003, RQ-004, RQ-005, RQ-006
- Boundaries: BD-001, BD-005, BD-007
- Inputs: Static stream, matrix X-01 through X-18 and explicit packet-ready runtime facts.
- Outputs: Deterministic supplemental body/batch projections whose triggers, recipients and mutations remain G7 responsibilities.

#### Steps

1. Define neutral packet-ready values for wire identities, ordered participants and bodies without sockets, registries or service handles.
2. Encode X-01 through X-18 positive, negative, empty and multi-occurrence forms, including X-08 rejection pair and X-13/X-14 adjacency.
3. Protect the distinction between static row 36 and trailing X-11 and preserve explicit cleanup/rollback projections X-17/X-18.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterSupplemental\|FullyQualifiedName~SelectCharacterRejection"` | Every X identity has deterministic covered encoding, order and multiplicity with no lifecycle decision inside Networking. |

### T-005: Integrate explicit projection delivery

- Depends on: T-004
- Covers: RQ-001, RQ-004, RQ-006
- Boundaries: BD-006, BD-007
- Inputs: Static and supplemental framed projection batches plus explicit connection recipients.
- Outputs: Ordered required-direct and best-effort-target delivery that does not discover business state.

#### Steps

1. Extend server message and sender contracts only as needed to carry explicit ordered projection batches and targets.
2. Preserve required direct-send failure while isolating disconnected best-effort recipients.
3. Verify sender order, owner exclusion and absence of reverse calls into application/domain services.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacter\|FullyQualifiedName~TouchSocketNetworkMessageSender"` | Static and targeted batches retain order and the sender performs no target discovery. |
| CK-007 | `rg -n "Microsoft.Data.Sqlite|Dfo.Pvf|I[A-Za-z]+Repository" Server/src/Dfo.Server.Networking Server/src/Dfo.Server.Application` | Networking has no SQLite, PVF or repository dependency; Application has no SQLite or PVF dependency. |

### T-006: Verify and document the complete G6 handoff

- Depends on: T-005
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007
- Boundaries: BD-007, BD-008
- Inputs: Complete static and supplemental projection implementation.
- Outputs: Fresh full verification and honest row/X coverage that hands lifecycle triggering and mutation to G7.

#### Steps

1. Mechanically compare all static identities and document every static and supplemental source/condition.
2. Record that G6 proves encoding from supplied typed input, while G7 owns X-07 and X-09 through X-18 triggers, recipients, mutation and rollback.
3. Run the complete solution with the real PVF archive and inspect the final dependency and whitespace boundaries.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore` | The complete solution passes; archive-present negative tests are only the documented expected skips. |
| CK-009 | `git diff --check` | No whitespace error exists. |
| CK-010 | `Non-command: compare Docs/Refactor/SelectCharacterG6Coverage.md identities and conditions to the reviewed migration matrix` | Rows 1-58 and X-01-X-18 have exact coverage and G7 responsibilities are explicit. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-003, T-005, T-006 | BD-005, BD-006, BD-007, BD-008 | CK-004, CK-006, CK-008, CK-010 |
| RQ-002 | T-001, T-002, T-006 | BD-001, BD-002, BD-003, BD-004, BD-005, BD-007, BD-008 | CK-001, CK-002, CK-003, CK-008, CK-010 |
| RQ-003 | T-002, T-003, T-004, T-006 | BD-001, BD-005, BD-007, BD-008 | CK-003, CK-004, CK-005, CK-008, CK-010 |
| RQ-004 | T-004, T-005, T-006 | BD-001, BD-005, BD-006, BD-007, BD-008 | CK-005, CK-006, CK-008, CK-010 |
| RQ-005 | T-001, T-002, T-003, T-004, T-006 | BD-001, BD-002, BD-003, BD-004, BD-005, BD-007 | CK-001, CK-002, CK-003, CK-004, CK-005, CK-008 |
| RQ-006 | T-001, T-002, T-004, T-005, T-006 | BD-001, BD-002, BD-003, BD-004, BD-005, BD-006, BD-007 | CK-001, CK-002, CK-003, CK-005, CK-006, CK-007, CK-008 |
| RQ-007 | T-006 | BD-008 | CK-010 |

## Risks and Rollback

- Risk: The legacy body surface contains unresolved names and duplicate identities; mitigate with raw-shape compatibility names, explicit occurrences and behavior vectors rather than invented domain semantics.
- Risk: Extending entry storage can accidentally fabricate defaults for existing databases; required normalized state must fail explicitly and optionality must preserve inapplicable versus loaded-empty distinctions.
- Rollback: This is a parent-managed attempt; if its acceptance boundary becomes invalid, the parent controller preserves durable evidence and requests exact rollback to baseline `501c2ca330fc6f506261dc9016e82327dccc73fe`.

## Assumptions

- Legacy normalized tables and builders contain every static byte source; real-client confirmation remains a later verification activity.
- G7 will consume the packet-ready supplemental seams and will not move recipient or mutation decisions back into Networking.
