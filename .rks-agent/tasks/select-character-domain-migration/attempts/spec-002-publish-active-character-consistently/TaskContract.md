---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-3
  - SC-4
depends_on: []
---

# TaskContract: Publish Active Character Consistently

## Context

### Source Request

The authorized root task requires a service-backed select-character flow in which AccountId is the business identity, current-user binding and active-character lookup are consistent, and later domain services can query the selected state without passing an online context.

### Current Situation

The accepted baseline strictly loads `AccountId + SlotIndex` under an account-exclusive command. `CharacterEntryService` then mutates two independent services in sequence: `UserService.BindCurrentCharacter` updates the online aggregate and `CharacterService.Activate` updates a separate active-state dictionary. A failure between those operations can expose inconsistent current-character state, and CharacterService currently mixes persistence loading with online-session ownership.

## Goal

Provide one account-indexed active-character service boundary whose implementation publishes the authenticated user's current CharacterId and complete active-character state as one consistent transition, while CharacterService remains responsible only for character data loading/query behavior.

## Scope

### In

- A shared abstraction for publishing and querying active character state by AccountId.
- One implementation that owns both the authenticated online user's current CharacterId and the corresponding active state.
- Character-entry orchestration that loads exact slot data before publishing and performs no state mutation when loading fails.
- Removal of active-session storage responsibility from CharacterService.
- Clearing active state when the owning authenticated connection is successfully disconnected.
- Focused stable-behavior verification for publication, lookup, failure preservation, and disconnect cleanup.

### Out

- Loading inventory, skills, quests, account entry views, or other later domain slices.
- Multi-session replacement policy beyond the existing requirement that exactly one online aggregate owns an AccountId for publication.
- Network packet changes, channel admission, or legacy lifecycle broadcasts.

## Constraints

- Public business APIs use AccountId and CharacterId, not OnlineUserId, after authentication resolution.
- The active-character service does not expose network connection objects or SQLite/repository implementations.
- Invalid exact-slot selection leaves both the current CharacterId and active-state lookup unchanged.
- The same service implementation must own both pieces of online selection state so callers cannot observe a half-published transition.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-2.1 | A caller can publish and query `ActiveCharacterState` using only AccountId, and the authenticated user snapshot exposes the same CharacterId after publication. | Focused service test and interface/dependency review. |
| SC-2.2 | Exact-slot load failure performs no publication and preserves any previously active character state. | Focused workflow test using a missing slot or failing loader. |
| SC-2.3 | Successful disconnect of the owning online user removes its active-character state, while rejected stale disconnect does not mutate it. | Focused service tests covering accepted and rejected disconnect paths. |
| SC-2.4 | CharacterService no longer stores online active-character mappings and remains a repository-driven character query/load service. | Complete diff review and dependency inspection. |
| SC-2.5 | Existing solution behavior remains buildable and all tests pass. | Fresh complete solution test run and clean-diff verification. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root authorization: “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果”. | Refactor the local active-character/user services and character-entry workflow, add proportionate stable-behavior tests, review, verify, and locally commit this checkpoint in the root worktree. |

## Requires Confirmation

- Push, merge, PR, cleanup, and any broader duplicate-login/session replacement policy remain outside this child.

## Assumptions

- Account-exclusive command execution remains the concurrency boundary around publication; this child does not add a second per-account lock.
