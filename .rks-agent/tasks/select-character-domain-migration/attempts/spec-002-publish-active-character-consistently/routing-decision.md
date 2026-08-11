# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-002-publish-active-character-consistently/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The child has one bounded structural outcome: consistent account-indexed publication of user binding and active-character state.
- Requirement stability: The identity boundary, ownership, failure preservation, disconnect cleanup, and CharacterService responsibility are explicit.
- Execution direction: The existing split dictionaries and workflow call sequence identify the refactor seam; implementation-local naming remains delegated.
- Remaining uncertainty: Broader duplicate-login policy is explicitly excluded.

## Marginal Control Value

- User precision signal: The user requested SDD checkpoints for the large migration and approved corrections to the legacy shape.
- Direct failure mode: Moving only one dictionary or changing the workflow without failure/disconnect coverage could preserve the same split-brain state under a different name.
- Useful control: A behavior-preserving Spec, focused regression checks, complete code review, and fresh verification protect the ownership transition.
- Time-for-quality rationale: This is the state-publication foundation every later domain slice will query, so catching an inconsistent boundary now avoids repeated downstream rework.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: One service-ownership refactor has one acceptance and rollback boundary.
- Why Direct is insufficient: The root needs reviewed evidence that the new account-indexed state source is reliable before later services depend on it.
- Why Multi is unnecessary: Tests, interface changes, and implementation changes are inseparable parts of one publication outcome.
- Reroute conditions: Return to root modeling if implementation requires a new multi-session replacement policy or external distributed state.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-refactor`
- Why this is sufficient: The central refactor path is known; a stable Spec, focused behavior-preservation tests, complete review, and fresh verification protect it without a separate Plan.
- Why lighter is insufficient: Direct execution would not give the root an approved durable contract and complete-result review for this foundational state boundary.
- Why heavier is unnecessary: No unresolved dependency sequence, high-consequence external mutation, or independent governance control earns Standard or Governed overhead.
