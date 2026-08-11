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

# TaskContract: Load Inventory Entry State

## Context

### Source Request

The authorized root migration requires the domain services needed by legacy `SELECT_CHARACTER`, with SQLite hidden behind repositories and non-character state kept outside `ActiveCharacterState`.

### Current Situation

Accepted Spec 002 provides consistent AccountId-indexed active-character ownership. The migration matrix identifies inventory as the next prerequisite: character containers and account cargo are currently absent from the typed entry result, while the legacy source stores item instances in character/account tables and mixes loading with packet construction and mutation.

## Goal

Provide complete, typed, service-backed read views for the selected character's entry inventory and the account's cargo, preserving container identity, slot identity, item-core data, applicable-empty semantics, rental expiry data, equipment locks, runtime item values, and gold-limit state without exposing SQLite or packet builders.

## Scope

### In

- Character inventory entry view for main, avatar, personal cargo, equipment, and pet containers.
- Account cargo entry view as a separate account-owned result.
- Typed item-core data sufficient for later Networking projection without re-querying storage.
- Rental, equipment-lock, cooldown/effect runtime values, container metadata, and character gold-limit data when present.
- Repository abstraction and SQLite adapter driven through cohesive Application services.
- Composition of the two inventory views into the select-character business result.

### Out

- Daily refill grants, rental deletion, or other entry-time mutations.
- Pet runtime/creature behavior beyond pet-container items.
- Skills, quests, appearance, account wallet/settings, world content, session lifecycle, and Networking packet projection.

## Constraints

- Character inventory and account cargo remain separate domain views even when one wire command represents both.
- Empty collections mean the applicable container or runtime list was loaded and empty; load failure is not converted to empty state.
- Application and Networking cannot depend on SQLite implementation types or issue persistence queries.
- Item ordering is deterministic by container and slot, and duplicate occupied slots are rejected rather than silently overwritten.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-3.1 | A selected AccountId and CharacterId produce separate complete character-inventory and account-cargo views with deterministic container/slot ordering. | Representative service and SQLite integration tests for populated and loaded-empty data. |
| SC-3.2 | Item-core, container metadata, rental/lock/runtime values, and gold limits required by the mapped G2 read packets survive repository-to-service conversion without packet encoding concerns. | Data-contract review and representative round-trip assertions. |
| SC-3.3 | SelectCharacterResult contains the two inventory views without adding account cargo or unrelated data to ActiveCharacterState. | Composition test and complete diff review. |
| SC-3.4 | Concrete SQLite access remains behind repository abstractions driven by Application services. | Project dependency and source inspection. |
| SC-3.5 | Existing behavior remains buildable and all tests pass. | Fresh complete solution test and diff checks. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root authorization: “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果”. | Implement, review, verify, and locally commit this contained inventory-loading checkpoint in the existing task worktree. |

## Requires Confirmation

- Push, merge, PR, cleanup, legacy database mutation policy, and the excluded entry-time mutations remain outside this child.

## Assumptions

- The accepted legacy `character_new_items` 82-byte item-core schema is the most complete local storage representation and can be decoded into typed state while compatibility fallback from older item tables remains an Infrastructure concern.
