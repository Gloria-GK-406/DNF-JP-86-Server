# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The user explicitly accepted a behavior-preserving Select Character domain split after the ownership boundaries, orchestration location, equipment dependency direction, and non-goals were discussed and confirmed.
- Requirement stability: The ready contract constrains domain ownership, handler-level orchestration, narrow equipment-facing character facts, failure atomicity, dependency direction, and protocol compatibility with observable evidence.
- Execution direction: The central direction is known: replace the character-owned cross-domain facade with application-use-case orchestration over cohesive services, migrate the composite result to the application boundary, then update DI, callers, and preservation tests.
- Remaining uncertainty: Exact names, namespaces, adapter query sharing, and local code organization are non-semantic implementation choices delegated to the Spec and Plan.

## Marginal Control Value

- User precision signal: The user explicitly asked to complete the task contract after iteratively correcting the domain model and requested a separate working branch for this refactor, showing preference for a controlled structural change over a quick local edit.
- Direct failure mode: A direct refactor can easily move the orchestration while leaving account/world/session types under the character boundary, broaden the equipment-to-character dependency, or accidentally publish active state before a later load failure. These defects can compile and escape shallow tests.
- Useful control: One behavior-focused Spec plus a dependency-ordered Plan, complete-result code review, and fresh verification protects the ownership boundary and publication ordering across all coupled callers.
- Time-for-quality rationale: The interfaces, composite result, DI registrations, application handler, and networking projection are coupled; resolving their migration order before edits avoids repeated type movement and makes the added workflow time materially cheaper than structural rework.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: The domain split has one coherent acceptance boundary: Select Character is not complete until every service boundary, caller, result, failure guarantee, and projection works together. None of the pieces is independently releasable or meaningfully rollbackable.
- Why Direct is insufficient: Direct work lacks a durable mapping from every ownership and ordering requirement to the affected contracts, call graph, tests, and final review evidence.
- Why Multi is unnecessary: Character, inventory, account, world, session, and publication changes are coupled parts of one application use case and cannot be accepted independently without leaving an invalid intermediate architecture.
- Reroute conditions: Reroute only if implementation evidence reveals a separately acceptable persistence migration, protocol change, or PVF/G5 source implementation is required; those outcomes are outside this contract and would need a distinct task boundary.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: A behavior-preserving Spec, explicit dependency-ordered Plan, ordinary complete-result code review, and fresh verification protect the credible ownership, migration-order, and failure-atomicity risks.
- Why lighter is insufficient: Lite would defer coupled interface/result/DI/caller sequencing to implementation, making consequential type-placement or publication-order rework more likely before complete-result review.
- Why heavier is unnecessary: The task has no safety, security, protected-data, irreversible external mutation, or durable-data-loss boundary requiring independent challenge, approval, or audit-strength drift controls.
