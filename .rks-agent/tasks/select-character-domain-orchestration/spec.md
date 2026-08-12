---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Select Character domain orchestration

## Why and Outcome

Selecting a character is an application use case that combines facts owned by several domains. Treating that use case as a character-domain service obscures ownership, invites account and world concerns into character models, and makes future equipment rules depend on more character behavior than they need. After this refactor, the application command boundary visibly coordinates the participating domains, every returned value retains one clear owner, and successful and failed selections behave exactly as before.

## Task Perspective

- Core outcome: Select Character is an explicit application-level orchestration over cohesive domain capabilities rather than a cross-domain character-service facade.
- Necessary support: Domain-owned result boundaries, narrow read-only equipment-facing character facts, early channel admission, late active-character publication, stable account identity, and unchanged protocol projection make that orchestration safe and correctly separated.
- Peripheral assurance: Focused orchestration tests, dependency inspection, complete-result review, and full-suite verification prove that the structural change preserved behavior.

## Scope and Non-Goals

### In Scope

- The complete Select Character application flow from authenticated account and selected slot through final composite result and active-character publication.
- Ownership separation among character facts, character inventory/equipment, account state, account inventory/cargo, channel policy, world/content derivation, placement, session-adjacent state, and online active-character state.
- The read-only character facts needed by present or future equipment eligibility decisions.
- Compatibility of the existing networking response with the refactored application result.

### Non-Goals

- New equipment movement, wearing, eligibility, stat calculation, or persistence behavior.
- New PVF parsing, catalog import, G5 source completion, or production session registry population.
- Protocol, packet framing, packet ordering, or response-content changes.
- Persistence schema redesign or unrelated repository migration.
- Nested command execution, direct repository use from command handling, merge, push, pull request, or worktree cleanup.

## Constraints

- Cross-domain sequencing belongs to the Select Character application boundary; reusable business decisions belong to their owning domain capabilities.
- Business flow after authentication uses the account identifier and must not substitute an online-session identifier.
- Channel refusal occurs after resolving only the minimum character facts and before the remaining entry state is loaded.
- Active-character publication occurs only after every required result is available; failures before that point preserve the previously active character.
- Domain-facing code remains independent of concrete persistence and transport adapters.
- Separating public domain contracts does not prohibit an internal adapter from sharing a transaction or query when ownership does not leak into the public result.

## Definitions

| Term | Meaning |
|---|---|
| Character facts | Persisted facts intrinsic to the selected character, including identity, progression, position, skills, and accepted or completed quest state. |
| Character peripheral state | State owned for a character but managed by another domain, including inventory items, equipment instances, equipped slots, and mutable item metadata. |
| World-derived capability | An answer derived from content definitions and current progress, such as quests the character may accept; it is not persisted character state. |
| Application result | The complete Select Character response envelope that references domain-owned results without transferring their ownership to the character domain. |
| Publication | The online-lifecycle transition that makes the newly selected character current for the account. |

## External Behavior and Data Rules

- Accepted, active, cleared, and completed quest progress remains character-owned; the set of quests currently available to accept remains world/content-owned.
- Account settings, entitlements, progression, relations, wallet values, account cargo, and account virtual resources remain account-owned.
- Item instances, equipment slots, item values, equipment locks, rentals, and character inventory resources remain inventory/equipment-owned.
- Channel admission and placement are derived from the destination channel and the minimum relevant character facts; they are not character state.
- Town presence, expert-store, PvP, and dungeon-rejoin views remain session-adjacent runtime state.
- The application result may combine all required domain results, but the active character contains only character-owned active facts.
- Missing content or session sources remain unavailable errors rather than being converted to empty or fabricated values.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | A Select Character request is coordinated at the application command-use-case boundary, and no character-domain capability owns the complete account, channel, world, placement, session, inventory, and publication flow. |
| RQ-002 | The successful result exposes character, character inventory/equipment, account, account inventory/cargo, world/placement, and session-adjacent values through their owning domain boundaries and combines them only in an application-owned response envelope. |
| RQ-003 | Equipment-facing behavior can read the minimum character identity and progression facts it needs through a narrow read-only capability, without receiving broad character mutation or unrelated character-entry behavior. |
| RQ-004 | Channel admission is evaluated immediately after the selected character's minimum facts are known; refusal prevents all later entry loading and publication. |
| RQ-005 | Active-character publication happens exactly once after all required results have loaded successfully, and every failure before publication leaves any previously active character unchanged. |
| RQ-006 | The use case continues to execute under the account's exclusive command boundary, flows downstream by account identifier, performs no nested command dispatch, and gives the command layer no direct repository access. |
| RQ-007 | Existing Select Character network projection remains behaviorally compatible, including response categories, packet order, and packet content. |
| RQ-008 | Unavailable G5/PVF content or session-adjacent production sources continue to fail honestly before publication and are not replaced with default or empty data by this refactor. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | An authenticated account selects an owned character and every required domain source is available | The Select Character use case completes | The application boundary obtains each domain-owned result, returns one complete application response, and no character-domain capability acts as the cross-domain facade. |
| AC-002 | RQ-002 | Character inventory and account cargo both contain data | The successful response is composed | Character inventory/equipment data and account inventory/cargo data remain separately identifiable and neither is embedded in the other's domain result or in active character facts. |
| AC-003 | RQ-003 | Equipment eligibility requires character level, job, or growth facts | Equipment-facing behavior requests those facts | It receives a read-only purpose-limited view and cannot access unrelated character entry composition or mutation behavior. |
| AC-004 | RQ-004, RQ-005 | A previously active character exists and the newly selected character does not meet destination-channel admission | Selection is attempted | Admission is refused before later entry sources are consulted, publication does not occur, and the previous active character remains current. |
| AC-005 | RQ-005, RQ-008 | A previously active character exists and any required inventory, character, account, world, placement, or session source fails | Selection is attempted | The failure propagates without publication and the previous active character remains current. |
| AC-006 | RQ-005 | Every required domain source succeeds | Selection completes | The selected character is published exactly once after the last required result is available and the returned response contains those same resolved results. |
| AC-007 | RQ-006 | An authenticated connection dispatches Select Character | The request crosses the command boundary | The account identifier and exclusive execution semantics are retained, no nested command is dispatched, and no command-layer component reaches a repository adapter. |
| AC-008 | RQ-007 | A representative complete Select Character result is produced before and after the refactor | Networking projects the response | The same response packet categories, ordering, and encoded content are observed. |
| AC-009 | RQ-008 | A required production content or session source reports unavailable | Selection is attempted | The unavailability remains explicit, no fabricated empty/default result is emitted, and publication does not occur. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002 | AC-001, AC-002 | Complete production call-graph and model-ownership review demonstrating application orchestration and separately owned domain results. |
| EV-002 | RQ-003 | AC-003 | Dependency inspection and focused contract tests demonstrating the purpose-limited, read-only equipment-facing character view. |
| EV-003 | RQ-004, RQ-005 | AC-004, AC-005, AC-006 | Focused orchestration tests demonstrating early refusal, failure preservation, successful composition, and exactly-once final publication. |
| EV-004 | RQ-006 | AC-007 | Command registration and static dependency inspection demonstrating exclusive account execution, account-identifier flow, no nested dispatch, and no repository access. |
| EV-005 | RQ-007 | AC-008 | Existing and focused networking projection regression tests demonstrating unchanged response behavior. |
| EV-006 | RQ-008 | AC-005, AC-009 | Unavailable-source regression tests demonstrating explicit failure before publication without fabricated data. |
| EV-007 | RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007, RQ-008 | AC-001, AC-002, AC-003, AC-004, AC-005, AC-006, AC-007, AC-008, AC-009 | Approved complete-result code review, clean architecture checks, and fresh passing focused and full-solution verification. |

## Assumptions

- The current behavior at task baseline is the compatibility oracle when the contract does not explicitly change ownership presentation.
- Exact type names and adapter query organization may change without changing these observable boundaries.
