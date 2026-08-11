# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-001-map-legacy-select-character-initialization/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The child has one documentation outcome: a complete, traceable legacy initialization migration matrix.
- Requirement stability: Coverage, ordering, source ownership, side-effect, confidence, and successor-grouping fields are explicit.
- Execution direction: Read-only legacy code inspection and matrix construction are sufficient; no product-code decisions are required.
- Remaining uncertainty: Protocol meanings may remain explicitly uncertain without blocking matrix completeness.

## Marginal Control Value

- User precision signal: The user requested SDD checkpoints before the large service migration.
- Direct failure mode: A free-form analysis could silently omit duplicate occurrences, conditional packets, or lifecycle sends and give later Specs an incomplete source list.
- Useful control: A durable complete-result specification and independent deliverable review protect matrix coverage and traceability.
- Time-for-quality rationale: The documentation checkpoint is cheap compared with implementing a service slice against an incomplete legacy inventory.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: One coherent migration-matrix deliverable has one acceptance and rollback boundary.
- Why Direct is insufficient: The root controller needs reviewed durable coverage evidence before accepting later implementation successors.
- Why Multi is unnecessary: This child produces one documentation artifact and no independently acceptable sub-outcomes.
- Reroute conditions: Return to root modeling if inspection reveals a separately authorized external evidence source is required to define matrix completeness.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `documentation`
- Why this is sufficient: A stable documentation Spec, complete-result review, and fresh mechanical verification protect coverage without a separate implementation Plan.
- Why lighter is insufficient: Direct execution would not provide the root controller with an approved durable acceptance boundary and complete-result review for the matrix checkpoint.
- Why heavier is unnecessary: Matrix construction has no coupled product-code decisions, independent high-consequence approval need, or durable sequencing choice that earns Standard or Governed controls.
