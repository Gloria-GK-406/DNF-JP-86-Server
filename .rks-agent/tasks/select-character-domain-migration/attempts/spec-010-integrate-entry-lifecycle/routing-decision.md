# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-010-integrate-entry-lifecycle/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: G7 is the reviewed matrix's single integrated entry lifecycle and consumes accepted G1-G6 boundaries.
- Requirement stability: X-07, X-09-X-18 and L-01-L-10 enumerate the required triggers, ordering, recipients, mutation and rollback behavior.
- Execution direction: Introduce generation-aware session ownership and narrow lifecycle ports, then make the account-exclusive command coordinate phased preparation, typed delivery, publication and compensation.
- Remaining uncertainty: Protocol meaning remains code-reversed, but the observable order and mutation rules are present in legacy code and real PVF inputs are available.

## Marginal Control Value

- User precision signal: The operator asked for uninterrupted completion through G7 and intends to validate the finished flow with a real client.
- Direct failure mode: A plausible implementation can publish too early, delete a replacement during stale rollback, emit mutation packets before commit, or broadcast before a required PvP handshake succeeds.
- Useful control: A durable stage plan, behavior-first fault/order tests, independent complete review and fresh verification protect the coupled state machine and rollback boundary.
- Time-for-quality rationale: These failures can leave persistent or in-memory state inconsistent while broad success tests still pass, making later client failures difficult to localize.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Preparation, registration, delivery, publication and compensation form one atomic user-visible entry outcome; accepting only one mutation slice would not produce a usable lifecycle.
- Why Direct is insufficient: Direct execution cannot cheaply preserve the cross-service order, exact-generation invariants and failure compensation across the complete matrix.
- Why Multi is unnecessary: The root controller already isolates G7 from G1-G6; splitting G7 itself would create intermediate accepted states that cannot safely run entry.
- Reroute conditions: Return to the root controller only if a required mutation has no available normalized/PVF source or the legacy evidence contradicts the accepted lifecycle boundary.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-feature`
- Why this is sufficient: Explicit dependency/order planning, behavior-first tests and independent complete review protect the lifecycle state machine.
- Why lighter is insufficient: Lite lacks a durable dependency and compensation plan for several coupled mutation and delivery phases.
- Why heavier is unnecessary: The task has no credential, privacy, safety, regulatory or irreversible external-system boundary; SQLite mutations are locally testable and compensable.
