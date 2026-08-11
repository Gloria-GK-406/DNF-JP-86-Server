---
kind: rks-plan
spec: ./spec.md
state: ready
---

# Plan: Character Entry State

## Approach

Model one immutable `CharacterEntryState` composed from cohesive profile, skill/quest, progression/activity, and feature/social views. Define service interfaces per cohesive slice while allowing one Infrastructure repository adapter to read an exact ownership-validated snapshot transaction. Preserve unresolved legacy structures as named neutral compatibility records or bounded blobs; never copy account/static/session overlays. Compose all required services after inventory loading and before active publication.

## Boundaries

| ID | Owned boundary | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/Characters/CharacterEntryModels.cs` and selection result | Define separated complete character entry views and optionality. | CharacterId-owned stored state | Typed `CharacterEntryState`. |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryStateServices.cs` | Define cohesive profile, skills/quests, progression/activity, features and composite service/repository seams. | AccountId, CharacterId | Slice views and complete composition. |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterEntryStateRepository.cs` and registration | Load and validate G3 storage without account/world overlays. | SQLite database and exact owner | Complete storage snapshot or explicit error. |
| BD-004 | `Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs`, registration and entry workflow | Produce cohesive slice views and compose before publication. | Repository snapshot and exact selected character | Complete selection result. |
| BD-005 | Character entry coverage document/tests | Trace every G3 row and protect ordinary, conditional, empty and failure behavior. | Matrix and implemented views | Reviewable coverage/evidence. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Checks |
|---|---|---|---|
| RQ-001 | T-001, T-002, T-003 | BD-001-BD-004 | CK-001, CK-002 |
| RQ-002 | T-001, T-002, T-003 | BD-001-BD-004 | CK-002, CK-003 |
| RQ-003 | T-001, T-002, T-003 | BD-001-BD-004 | CK-002, CK-003 |
| RQ-004 | T-002, T-003 | BD-003-BD-005 | CK-003, CK-004 |
| RQ-005 | T-004 | BD-005 | CK-005, CK-006 |

## Tasks

### T-001: Define complete G3 contracts and coverage

- Boundaries: BD-001, BD-002, BD-005
- Requirements: RQ-001, RQ-002, RQ-003
- Inputs: accepted G3 matrix rows and legacy typed snapshots
- Outputs: immutable view contracts, cohesive service seams and a row-to-field/service coverage document
- Steps: inventory every G3 row; group by business slice; represent conditional class features with nullable containing state; represent applicable collections as non-null lists; retain bounded neutral fields for unresolved meanings.
- Checks:
  - CK-001: `dotnet build Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj --no-restore` exits 0 and the coverage document has no unexplained declared G3 row.

### T-002: Implement exact character-owned persistence loading

- Depends on: T-001
- Boundaries: BD-003, BD-005
- Requirements: RQ-001, RQ-002, RQ-003, RQ-004
- Inputs: typed contracts, accepted local legacy schemas, AccountId and CharacterId
- Outputs: one ownership-validated storage snapshot transaction supporting populated and explicit empty data
- Steps: ensure/read only character-owned tables; validate sizes/ranges/duplicates and exact owner; detect legacy-only incompatible sources as explicit migration errors; avoid querying account/world/static definitions.
- Checks:
  - CK-002: `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --filter FullyQualifiedName~CharacterEntryState --no-restore` passes representative populated/empty/conditional coverage.
  - CK-003: the same tests pass wrong-owner, malformed, missing-applicable and duplicate rejection cases.

### T-003: Implement cohesive services and selection composition

- Depends on: T-002
- Boundaries: BD-004, BD-005
- Requirements: RQ-001-RQ-004
- Inputs: validated storage snapshot and selected character
- Outputs: cohesive slice services, complete CharacterEntryState and expanded SelectCharacterResult
- Steps: implement slice projections and composite service; register all seams; load after accepted inventory and before publication; keep AccountId only for ownership validation and do not add account data.
- Checks:
  - CK-004: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~CharacterEntry --no-restore` proves composition, optionality and failure preservation.

### T-004: Verify coverage, dependencies and regression

- Depends on: T-003
- Boundaries: BD-005
- Requirements: RQ-005
- Inputs: complete G3 implementation
- Outputs: review-ready diff and current evidence
- Steps: cross-check coverage against matrix; inspect dependencies; run focused and complete suites.
- Checks:
  - CK-005: `dotnet test Server/86JP.Server.slnx --no-restore` exits 0.
  - CK-006: `git diff --check` exits 0; Application/Networking contain no SQLite implementation dependency; coverage cross-check reports no missing G3 row.
