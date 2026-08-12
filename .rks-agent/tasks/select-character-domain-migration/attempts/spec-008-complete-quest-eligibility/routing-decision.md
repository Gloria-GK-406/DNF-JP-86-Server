# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-008-complete-quest-eligibility/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The accepted G5 coverage names the exact known defect: quest eligibility is over-broad because legacy predicates were omitted. The requested outcome is to close that fidelity gap before G6 exposes it on the wire.
- Requirement stability: The legacy quest index, current neutral source contract, real archive and accepted G5 result provide enumerable rule, boundary and failure requirements.
- Execution direction: Extend the existing Infrastructure source and neutral contracts, then evaluate the complete subject in the existing Application world-content service.
- Remaining uncertainty: Exact PVF labels and the classification of standalone-only predicates are local evidence questions resolved by legacy/PVF inspection; they do not change the task meaning.

## Marginal Control Value

- User precision signal: The operator prefers completing through G7 in one sustained run and will validate behavior with a real client, while accepting that legacy code is imperfect.
- Direct failure mode: A plausible green implementation can silently keep accepting quests when one newly parsed predicate is absent or inverted; the current accepted implementation already demonstrates this escaped-fidelity mode.
- Useful control: An explicit rule matrix, implementation plan, populated negative cases, drift check and independent code review protect both completeness and the no-fabricated-state boundary.
- Time-for-quality rationale: G6 will make the result wire-visible across the full initialization stream, so correcting eligibility after packet work would create larger diagnostic and rework cost.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Complete quest eligibility is one cohesive business answer with one rollback and review boundary; it can be accepted before any Networking or lifecycle work.
- Why Direct is insufficient: Direct execution does not cheaply prove that every legacy predicate is classified and that missing values narrow or fail rather than silently widen the answer.
- Why Multi is unnecessary: Static source expansion and eligibility projection are inseparable parts of the same `AvailableQuests` outcome; no sibling result is independently useful.
- Reroute conditions: Block if the supplied archive lacks mandatory referenced quest data or if completing a rule requires an unavailable external runtime authority rather than an in-repository source.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-feature`
- Why this is sufficient: The explicit plan can order legacy-rule inventory, neutral contract expansion, PVF parsing and business projection before any implementation choice propagates; TDD and complete code review then protect observable eligibility behavior.
- Why lighter is insufficient: Lite would defer the cross-layer predicate/source dependency mapping until implementation, making it easy to parse a field without supplying its character subject or to add a subject without proving its source.
- Why heavier is unnecessary: No safety, credential, privacy, irreversible mutation or durable data-loss boundary is involved, and ordinary independent code review plus real-archive verification protects the identified fidelity risk.
