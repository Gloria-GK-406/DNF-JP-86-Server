# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-004-load-character-entry-state/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The accepted matrix fixes the complete character-owned G3 coverage boundary and excludes account/world/network concerns.
- Requirement stability: Ownership, optionality, failure behavior, persistence isolation and workflow ordering are explicit.
- Execution direction: Existing legacy repositories/tables and accepted service layering provide migration evidence.
- Remaining uncertainty: Unresolved protocol meanings remain neutral fields and do not block loading.

## Marginal Control Value

- User precision signal: The user requested SDD checkpoints for the large migration.
- Direct failure mode: Omitting one of many conditional character slices or leaking account/static content into a convenience snapshot would surface only during later network projection.
- Useful control: Explicit coverage planning, ordered implementation boundaries, independent complete review and fresh verification prevent those omissions.
- Time-for-quality rationale: G4/G5 composition and G6 projection depend on this full character result.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: All included views share one CharacterId-owned entry-result acceptance boundary and must be complete before downstream projection.
- Why Direct is insufficient: Matrix traceability and several cohesive repository/service paths need durable dependency planning.
- Why Multi is unnecessary: Splitting individual character subdomains would allow no downstream acceptance until the G3 composition is complete and would add rollback boundaries without independent product value.
- Reroute conditions: Return to root if an alleged character field proves account/world-owned or entry mutation becomes required.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-feature`
- Why this is sufficient: A durable plan materially prevents coverage and dependency drift across many cohesive slices.
- Why lighter is insufficient: Lite cannot record the G3 row-to-view-to-repository implementation map needed before coding.
- Why heavier is unnecessary: No governed hard floor or irreversible external mutation exists; independent complete-code review is sufficient.
