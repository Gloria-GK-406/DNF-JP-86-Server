---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Split Select Character orchestration by domain

## Context

### Source Request

The user approved the previously aligned Select Character domain split with: “好，可以了。完成这个任务契约吧”. The approved direction is to stop treating `CharacterEntryService` as a whole-use-case facade, let the command handler coordinate cohesive domain services, keep character facts separate from character-adjacent, account, channel, world/content, session, and online-lifecycle state, and let equipment rules consume only a narrow character-facts interface.

### Current Situation

The Select Character path currently delegates from `SelectCharacterCommandHandler` to a broad `CharacterEntryService`. That service loads and coordinates character state, character and account inventory, account state, channel admission, world content, placement, session-adjacent state, and active-character publication. Although its final active-character model does not contain all of those values, the service boundary and the `SelectCharacterResult` location make the character domain appear to own a cross-domain use case.

The existing flow already protects important behavior: commands flow by `AccountId`, channel admission happens before expensive entry loading, and the active character is published only after required state loads successfully. Those protections must survive the structural change. G5/PVF production data availability remains a separate, known incomplete concern and must not be disguised or completed as part of this task.

## Goal

Make Select Character an explicit application-level orchestration whose handler coordinates cohesive domain services, while each model and service contract expresses one correct ownership boundary and all existing externally observable selection behavior and failure atomicity remain intact.

## Scope

### In

- Move cross-domain Select Character coordination out of the current character-domain facade and into the Select Character command handling use case.
- Establish clear service and result boundaries for character state, character inventory/equipment, account state, account inventory/cargo, channel admission, world/content derivation, entry placement, session-adjacent state, and active-character publication.
- Keep accepted/completed quest state and other persisted character facts in the character boundary; keep available quests and other world-derived capabilities outside it.
- Keep equipment instances, equipped slots, and mutable item state in the inventory/equipment boundary.
- Provide any character facts needed by equipment eligibility through a narrow, read-only equipment-facing contract rather than a broad character service.
- Treat the complete Select Character response as an application/use-case result rather than a character-domain model.
- Update dependency injection, callers, tests, and directly affected documentation to match the resulting boundaries.
- Preserve the current command execution and network response contract.

### Out

- Implementing or changing equipment equip, unequip, move, stat-calculation, or eligibility behavior beyond what is necessary to preserve current Select Character behavior.
- Completing missing PVF parsing, PVF catalog imports, G5 content sources, or unavailable production world/session data.
- Changing packet framing, protocol message IDs, packet projection semantics, or the number/order/content of Select Character response packets.
- Redesigning the persistence schema or migrating unrelated repositories.
- Adding nested command dispatch or allowing a command handler to access repositories directly.
- Merging, pushing, opening a pull request, or cleaning a worktree.

## Constraints

- The command handler may coordinate multiple cohesive services, but reusable business decisions must remain inside the service that owns them.
- CommandBus execution must not create or dispatch another command. Shared execution logic must be expressed as a service.
- Domain services and handlers must not depend on concrete SQLite repositories or other infrastructure adapters.
- Account-scoped execution continues to flow by `AccountId`; `OnlineUserId` must not become a downstream business identifier.
- Channel admission must be decided from the minimum required character facts before expensive entry-state composition.
- Active-character publication must be the last state-changing step after every required domain result has been obtained successfully. A failure before publication must preserve the previously active character.
- Separating domain contracts must not prevent an infrastructure adapter from internally sharing a read transaction or query when that optimization does not leak across domain contracts.
- Missing G5/PVF or session-adjacent production sources must continue to fail honestly according to their current contract; this refactor must not replace unavailable data with fabricated empty/default state.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Select Character orchestration is visible at the application command-use-case boundary, and no character-domain service coordinates account, channel, world/content, placement, session, and publication as one facade. | Current production call graph and dependency inspection show the handler/use-case coordinator calling the separated service contracts and the former cross-domain facade removed or reduced to a character-only responsibility. |
| SC-2 | Character, character inventory/equipment, account, account inventory/cargo, channel, world/content, session, and online-lifecycle data have unambiguous owning contracts and result types. | Contract/model inspection demonstrates that each cross-domain value is exposed from its owning boundary and that the final composite response is application-owned. Architecture tests or dependency scans protect the relevant project-layer boundaries. |
| SC-3 | Equipment-related code can obtain only the character facts it needs without depending on a broad, mutable character service or creating a circular domain dependency. | A narrow read-only character-facts contract is present where needed, contains only justified eligibility facts, and its dependency graph and focused tests/build demonstrate valid composition. |
| SC-4 | Channel rejection and any later loading/composition failure cannot replace or partially publish the active character. | Focused tests cover early admission rejection and representative downstream failures, asserting that publication does not occur and any prior active-character state remains unchanged. |
| SC-5 | A successful Select Character operation composes the same categories of character, inventory, account, world, and session data and publishes exactly once after all required loads. | Focused orchestration tests assert the successful call order at behavior-significant boundaries, the complete composite result, and final publication. Existing networking projection tests remain green without protocol expectation changes. |
| SC-6 | The refactor introduces no direct repository/infrastructure access from Application handlers or domain-facing projects and no nested command dispatch. | Static dependency/reference inspection and code review find no forbidden dependencies or nested dispatch; the relevant projects and full solution build successfully. |
| SC-7 | Existing verified behavior remains healthy after the structural change. | All focused Select Character/domain tests and the full solution test suite pass from a clean task-worktree state, and the final diff passes repository consistency checks. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | After the Agent stated it would create an independent work branch and complete the aligned Select Character domain split once formally authorized, the user replied: “好，可以了。完成这个任务契约吧”. | Persist and execute this bounded local refactor from the current `codex/architecture-refactor` baseline, including one task worktree/branch, task-local SDD artifacts, implementation, tests, review/remediation, verification, and one verified local result commit. No merge, push, PR, or cleanup. |

## Requires Confirmation

- Merging the completed task branch into another branch.
- Pushing the task branch or opening a pull request.
- Removing the task worktree or deleting its branch.

## Assumptions

- The task starts from the current `codex/architecture-refactor` state, including the accepted PVF name-index memory optimization already present at its HEAD.
- Current G5/PVF and session-adjacent unavailable-source behavior is the compatibility baseline for this structural refactor.
- Exact interface, class, namespace, and result names are implementation decisions as long as the ownership and dependency boundaries above remain observable.
