# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-003-load-inventory-entry-state/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: One bounded read-model outcome covers selected-character inventory plus separately owned account cargo.
- Requirement stability: Containers, item fidelity, optionality, dependency direction, composition, and mutation exclusions are explicit.
- Execution direction: The accepted legacy SQLite schema and current repository/service layering provide a concrete migration seam.
- Remaining uncertainty: Packet byte projection and entry-time mutation policy are explicitly excluded.

## Marginal Control Value

- User precision signal: The user requested SDD checkpoints for this large migration.
- Direct failure mode: An incomplete item-core model or wrong account/character ownership would propagate into every later packet projection and force schema/API rework.
- Useful control: A durable dependency plan maps shared data contracts, repository decoding, services, composition, and integration verification before implementation.
- Time-for-quality rationale: G3 pet/appearance/collection work consumes these inventory outputs, so ordering mistakes have consequential downstream cost.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Character and account inventory reads form one coupled entry-data acceptance boundary and one rollback unit.
- Why Direct is insufficient: The cross-project data/repository/service/composition path needs traceable complete-result review.
- Why Multi is unnecessary: The two ownership views share one storage decoding and are only useful together for entry composition.
- Reroute conditions: Return to the root if mutation behavior or an independent live-inventory subsystem becomes necessary.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-feature`
- Why this is sufficient: Explicit planning materially prevents an early item contract or schema choice from forcing repository, service, result, and later-domain rework.
- Why lighter is insufficient: Lite lacks the durable boundary/dependency mapping needed across Data, Abstractions, Infrastructure, Application, and composition.
- Why heavier is unnecessary: The work has no security, irreversible external mutation, or other governed hard floor; independent complete-code review is sufficient.
