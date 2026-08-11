---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Migrate Select Character Domain Services

## Context

### Source Request

The user authorized starting a separate work branch with SDD and checkpoints to implement the domain services required by the legacy `SELECT_CHARACTER` flow: “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果”. This follows the accepted direction to compare the legacy mainline implementation, build a migration matrix, preserve domain boundaries, and then migrate the complete flow.

### Current Situation

Branch `codex/architecture-refactor` established TouchSocket framing, typed network messages, an account-keyed CommandBus execution gate, login and roster flows, and a first `SELECT_CHARACTER` vertical slice. That slice strictly resolves `AccountId + SlotIndex`, binds the current character, and emits only the initial selection acknowledgement. The legacy implementation still owns a 58-entry static initialization sequence plus conditional and trailing packets, with data access and business behavior mixed across large snapshots, packet builders, SQLite access, global state, and session handlers.

## Goal

Replace the legacy select-character initialization dependency with a service-backed flow that loads complete, honestly modeled character, account, world, and session entry data; composes a typed business result; and lets Networking project the required ordered and conditional game packets without querying repositories or executing business logic.

## Scope

### In

- Produce a traceable migration matrix for every static select-character initialization entry and known conditional or trailing packet, including legacy data source, domain owner, new service or projection, side effects, and confidence.
- Define and implement the character, account, world/content, and session service boundaries required by the select-character flow.
- Keep SQLite and other peripheral data sources behind repository abstractions driven by cohesive services.
- Expand `SelectCharacterResult` into a complete composition without placing account, world, static-content, or session data inside `ActiveCharacterState`.
- Preserve AccountId as the business identity after Networking resolves an authenticated connection.
- Execute select-character under the existing account-exclusive command boundary and keep user binding and active-character publication consistent.
- Decode strict slot selection and project the migrated ordered and conditional packet sequence through TouchSocket Networking.
- Record protocol claims as code-reversed unless packet capture evidence exists.
- Add or update only verification that materially protects stable behavior; do not add tests solely to preserve temporary refactoring shapes.

### Out

- Migrating unrelated game commands after character entry.
- Reimplementing legacy bugs, seed-character fallbacks, or invalid-slot fallback behavior merely for compatibility.
- Treating packet capture confirmation as available when it has not been performed.
- Pushing, merging, opening a pull request, deleting branches, or cleaning worktrees.

## Constraints

- Networking may encode, order, split, and conditionally emit packets from a completed business result, but may not query databases, call domain services to fill missing data, or mutate business state.
- Command handlers may coordinate one cohesive application workflow but may not execute nested commands; shared business behavior belongs in services.
- Missing or unimplemented data must not be represented as fabricated empty domain state. `null` means a feature is genuinely inapplicable; empty collections mean the applicable data set was loaded and is empty; load failures remain errors.
- Account, world/content, and session information must remain outside `ActiveCharacterState`, even when the wire protocol combines those fields in one packet.
- Repository implementation types and SQLite details must remain outside the Application and Networking control flow.
- Existing accepted refactor behavior remains the baseline unless the migration matrix demonstrates that a correction is required for the stated goal.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Every static entry in the legacy initialization sequence and every known conditional or trailing select-character packet is represented in a migration matrix with command/type, send condition, source, domain owner, target service/projection, side effects, and confidence. | Reviewed matrix cross-checked against the legacy sequence, builders, select data source, and lifecycle coordinator with no unexplained entries. |
| SC-2 | The business result cleanly composes complete character, account, world/content, and session entry views, with explicit applicable-empty versus inapplicable semantics and no cross-domain convenience fields in character state. | Code review of shared data contracts and service outputs, plus representative construction evidence for ordinary and conditional character features. |
| SC-3 | Cohesive services load and coordinate all migrated entry data through abstractions, and concrete SQLite or other peripheral implementations are only driven behind those boundaries. | Dependency inspection and integration evidence showing Handler → Command → entry workflow/services → repositories, with no Infrastructure dependency in Application or Networking. |
| SC-4 | Character selection uses exact `AccountId + SlotIndex`, executes under account-exclusive access, and publishes the user binding and active-character state only for a successfully prepared selection. | Command registration and workflow review plus verification of invalid-slot rejection and consistent successful publication. |
| SC-5 | Networking projects the required static-order and conditional initialization frames solely from the completed result, without domain/repository calls, and does not emit placeholder packets for unavailable business data. | Projection review, packet-order verification against the matrix, and dependency search showing the Networking projection has no business data loaders. |
| SC-6 | The integrated refactor builds, all existing tests pass, stable migrated behavior has proportionate regression coverage, Host registration starts successfully, and remaining protocol uncertainty is explicitly documented. | Fresh solution test output, Host startup evidence, focused verification results, clean diff review, and updated reverse-engineering documentation. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果” following the proposed separate SDD worktree, migration matrix, service implementation, packet projection, and checkpoint flow. | Create one isolated task branch/worktree from `codex/architecture-refactor`; create and execute the SDD task and in-scope child Specs; inspect and refactor legacy select-character code; implement, review, verify, and locally commit accepted checkpoints and the completed result. |

## Requires Confirmation

- Any push, pull request, merge, source-branch integration, branch/worktree cleanup, packet capture against an external server, or external-system mutation requires separate authorization.

## Assumptions

- The legacy implementation is evidence of intended or observed behavior, not an infallible specification; inconsistencies may be corrected when the matrix and code evidence support the correction.
- Current local SQLite schemas and static resources are sufficient to implement a service-backed migration, while packet-capture confirmation may remain a documented follow-up.
