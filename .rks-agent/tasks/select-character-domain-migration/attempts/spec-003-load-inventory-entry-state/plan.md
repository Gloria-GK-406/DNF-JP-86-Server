---
kind: rks-plan
spec: ./spec.md
state: ready
---

# Plan: Inventory Entry State

## Approach

Introduce neutral inventory entry contracts in Data, one repository boundary in Abstractions, and one Application service that validates/normalizes repository output into separate character and account views. Decode the accepted legacy 82-byte item-core representation in Infrastructure, compose the views after exact character loading, and prove the vertical path with focused behavior and SQLite integration checks.

## Boundaries

| ID | Owned boundary | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs` and `Characters/CharacterModels.cs` | Define neutral typed item/container/runtime/gold views and extend selection composition. | AccountId, CharacterId, stored item state | Separate character and account inventory entry views. |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Inventory/IInventoryEntryServices.cs` | Define persistence and cohesive service boundaries without SQLite types. | AccountId, CharacterId | Repository record and validated entry result. |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteInventoryEntryRepository.cs` and module registration | Load accepted inventory tables, validate 82-byte cores, and return owned storage records. | Configured SQLite database | Complete repository record or explicit error. |
| BD-004 | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryService.cs`, module registration, and `Characters/CharacterEntryService.cs` | Validate ownership/order and compose inventory into selection. | Repository record plus exact loaded character | Deterministic business views in SelectCharacterResult. |
| BD-005 | Application and Infrastructure test projects | Protect populated, empty, malformed, replacement-compatible composition and dependency behavior. | Public/internal service seams and temporary SQLite database | Focused regression and integration evidence. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Checks |
|---|---|---|---|
| RQ-001 | T-001, T-002, T-003 | BD-001, BD-002, BD-003, BD-004 | CK-001, CK-002 |
| RQ-002 | T-001, T-002 | BD-001, BD-003 | CK-001, CK-003 |
| RQ-003 | T-002, T-003 | BD-003, BD-004, BD-005 | CK-002, CK-003 |
| RQ-004 | T-003 | BD-001, BD-002, BD-004 | CK-004 |
| RQ-005 | T-004 | BD-005 | CK-005, CK-006 |

## Tasks

### T-001: Define complete ownership-separated inventory contracts

- Boundaries: BD-001, BD-002
- Requirements: RQ-001, RQ-002
- Inputs: accepted legacy list identities and item-core offsets
- Outputs: immutable item/container/runtime/gold models, repository record, and service result contracts
- Steps: model every accepted item-core field with neutral names; make character and account views distinct; retain explicit container records for loaded-empty state.
- Checks:
  - CK-001: `dotnet build Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj --no-restore` exits 0 and contract review confirms no Infrastructure/Networking types.

### T-002: Load inventory storage faithfully

- Depends on: T-001
- Boundaries: BD-003, BD-005
- Requirements: RQ-001, RQ-002, RQ-003
- Inputs: typed contracts and configured SQLite database
- Outputs: deterministic repository records for current and compatibility storage with explicit malformed-data errors
- Steps: ensure required read schema; query character/account rows by exact owner; decode little-endian cores; load container metadata, runtime values and gold limits; reject invalid length and duplicate slots.
- Checks:
  - CK-002: `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqliteInventoryEntryRepository --no-restore` passes populated and loaded-empty cases.
  - CK-003: the same focused test set passes malformed-core and duplicate/owner validation cases.

### T-003: Normalize and compose the entry views

- Depends on: T-002
- Boundaries: BD-004, BD-005
- Requirements: RQ-001, RQ-003, RQ-004
- Inputs: exact selected character and repository record
- Outputs: validated inventory result and expanded SelectCharacterResult
- Steps: implement service normalization/order; register repository/service; load inventory after exact character resolution and before active-state publication; preserve failure-before-publication behavior.
- Checks:
  - CK-004: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~CharacterEntry --no-restore` proves separated composition and failure preservation.

### T-004: Complete regression and dependency verification

- Depends on: T-003
- Boundaries: BD-005
- Requirements: RQ-005
- Inputs: complete inventory slice
- Outputs: review-ready diff and current regression evidence
- Steps: inspect project references and old constructor sites; run focused and complete checks; ensure no SQLite use outside Infrastructure.
- Checks:
  - CK-005: `dotnet test Server/86JP.Server.slnx --no-restore` exits 0.
  - CK-006: `git diff --check` exits 0 and `rg "Microsoft.Data.Sqlite" Server/src/Dfo.Server.Application Server/src/Dfo.Server.Networking` returns no matches.
