# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The discussion has converged on one behavior-preserving outcome: `01/0004` is the entry acceptance message, the following frames are reusable domain state synchronization, and the fabricated handler multi-dispatch restriction must be removed.
- Requirement stability: Wire compatibility, domain/protocol ownership, handler-versus-command dispatch rules, lifecycle safety, full/delta synchronization evidence and verification expectations are explicit in the ready TaskContract.
- Execution direction: The central direction is known: preserve the wire transcript while replacing the SelectCharacter-owned multi-frame codec with domain-cohesive command/results and concrete protocol messages/encoders coordinated by the handler.
- Remaining uncertainty: Pending Windows analysis may identify local client dependencies, but the contract retains the existing order and bytes, so it does not change the refactor's acceptance boundary.

## Marginal Control Value

- User precision signal: The user explicitly requested a TaskContract before refactoring and corrected an architectural rule that must not be silently reintroduced.
- Direct failure mode: A direct refactor could easily omit one of 58 occurrences, move a wire type into the wrong module, break lifecycle compensation, or preserve the old semantic leak under a new abstraction. An early mistaken boundary would propagate across Application, Protocol, Networking and tests.
- Useful control: One durable Spec fixes behavior and ownership; an explicit Plan maps domain extraction, command sequencing, encoder migration and lifecycle preservation before implementation; complete-code review and fresh verification protect the final transcript and dependency direction.
- Time-for-quality rationale: The plan and review cost is smaller than recovering from a partially migrated entry stream whose compiler dependencies pass while client behavior or semantic ownership regresses.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Acceptance is coherent and atomic: SelectCharacter must cease owning lifecycle-wide synchronization while preserving the complete accepted entry behavior. Separate partial results would not be independently acceptable because the central switch and multi-frame response cannot be retired until all static occurrences have successors.
- Why Direct is insufficient: Direct lacks durable dependency mapping for the coupled command, message, encoder, lifecycle and documentation changes, making omission and cross-layer relapse too easy to detect only after broad implementation.
- Why Multi is unnecessary: The domain slices are implementation units contributing to one inseparable wire-compatible outcome; they do not need separate user acceptance, integration or rollback boundaries.
- Reroute conditions: Reroute to Multi only if an extracted domain requires an independently acceptable migration with its own compatibility or rollback boundary. Reroute to Governed only if implementation reaches credentials, authorization, irreversible external mutation or credible durable-data loss. Return to intent alignment if client evidence requires changing the wire transcript rather than preserving it.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: The Profile adds a behavior-focused Spec, an explicit dependency-ordered Plan, test-driven implementation, complete code review and fresh verification—the controls needed to preserve the transcript while moving ownership.
- Why lighter is insufficient: Lite lacks the durable Plan needed to resolve the order of command-boundary correction, domain result extraction, concrete encoder creation, handler migration and removal of the old stream without consequential rework.
- Why heavier is unnecessary: Ordinary complete-code review and fresh compatibility verification adequately protect this reversible local refactor; no safety, access-control, protected-data, financial, legal or irreversible-data boundary requires independent challenge or approval.
