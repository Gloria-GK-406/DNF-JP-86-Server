---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-3
depends_on: []
---

# TaskContract: Load Character Entry State

## Context

### Source Request

The authorized root migration requires building the cohesive domain services needed by legacy `SELECT_CHARACTER`, keeping SQLite behind repositories and preventing unrelated data from accumulating inside `ActiveCharacterState`.

### Current Situation

Accepted G1/G2 checkpoints provide consistent active-character publication and complete ownership-separated inventory entry state. The migration matrix still assigns a large G3 set of character-owned state to legacy snapshot fields and packet builders, including skills, quests, settings, appearance, pets, collections, progression, dungeon/PvP/mail/daily state and class-specific features.

## Goal

Provide a complete, typed, repository-backed composition of the character-owned non-inventory state required by the mapped select-character initialization flow, using cohesive subdomain services and explicit applicable-empty versus inapplicable semantics.

## Scope

### In

- Character public/detail state, fatigue, position, appearance, settings and progression.
- Skill pages/points and Dark Knight optional combo state.
- Quest active/available/clear/visibility state and quest notifications as character progress, without static definitions.
- Pet/creature runtime state that is not already inventory item state.
- Collections, achievements, title books, expert job, dungeon permissions/progress, PvP missions, mailbox summary and daily/activity progress.
- Optional Knight Shield and other class-applicable character features identified by the accepted matrix.
- Cohesive service boundaries, repository abstractions, SQLite adapters and composition into `SelectCharacterResult` outside `ActiveCharacterState` except for the minimal current-state fields already accepted there.

### Out

- Account settings, wallet, premiums, honor/adventure progression and other G4 data.
- Static quest/activity/collection/pet definitions, channel policy, social/PvP lobby and other G5 data.
- Daily refill or other entry-time mutations, session lifecycle and Networking packet projection.
- Inventing semantics for fields the matrix marks unresolved; they remain neutral typed compatibility state.

## Constraints

- Each cohesive service owns one business slice; packet identity does not determine service boundaries.
- Account/world/session/static-content data cannot be copied into character state for convenience.
- `null` means a feature is inapplicable to this character; applicable loaded-empty collections are non-null empty; missing tables/data required for an applicable feature are errors unless a documented domain default exists.
- Repository and SQLite types remain outside Application control flow and Networking.
- Exact AccountId/CharacterId ownership is validated before returning any composite result.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-4.1 | The selected character produces typed cohesive views covering every G3 matrix row and conditional character portion, with a traceable coverage map. | Coverage table review and representative ordinary/conditional construction tests. |
| SC-4.2 | Applicable-empty and inapplicable optional features are distinguishable, while malformed, wrong-owner or unavailable applicable data fails explicitly. | Focused service and repository tests. |
| SC-4.3 | SQLite adapters are driven through cohesive services and no account/world/session data is placed in character views or ActiveCharacterState. | Dependency and complete diff review. |
| SC-4.4 | CharacterEntryService composes the complete character-owned entry view before active-character publication, preserving prior state on load failure. | Workflow tests. |
| SC-4.5 | Existing behavior remains buildable and all tests pass. | Fresh complete solution tests and diff checks. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root authorization: “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果”. | Implement, review, verify and locally commit this contained G3 character-state checkpoint. |

## Requires Confirmation

- Push, merge, PR, cleanup, external packet capture and excluded domains remain outside this child.

## Assumptions

- Legacy tables and typed snapshot builders are compatibility evidence, but unclear names remain neutral rather than being promoted to domain facts.
