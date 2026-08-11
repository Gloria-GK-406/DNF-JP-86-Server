# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-005-load-account-entry-state/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- The G4 matrix provides one stable AccountId-owned acceptance boundary with explicit exclusions and normalized legacy evidence.
- A durable plan is valuable because settings, wallet, entitlement, progression and relation tables must remain distinct while producing one account composition.

## Structural Selection

- Selected workflow: `single-spec-workflow`.
- Direct is insufficient for exact G4 coverage and cross-project persistence/service composition.
- Multi is unnecessary because the account slices have no independently useful downstream acceptance until the account entry view is complete.
- Reroute if a field proves character/world-owned or requires mutation.

## Controller Selection

- Controller: `sdd-standard` using `code-feature`.
- Standard adds the coverage/dependency plan needed to prevent account/character leakage; Governed adds no material protection for this local reversible feature.
