# Routing Decision

- TaskContract: `.rks-agent/tasks/restore-game-initial-connection-handshake/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The live client and server establish Game TCP but deadlock before LOGIN because the successor omits the legacy server-first initial notice; the desired observable result is one precise first frame followed by the existing LOGIN flow.
- Requirement stability: Endpoint scoping, send-before-client-payload ordering, typed layer separation, compatibility body layout, failure honesty, and regression coverage are explicit and reviewable.
- Execution direction: Add one typed server message, trigger it from the existing connection lifecycle after registration, encode it in Networking, and prove it through codec and live transport integration tests.
- Remaining uncertainty: Implementation-local naming and the exact reuse of existing configuration services; neither changes the task meaning.

## Marginal Control Value

- User precision signal: The user explicitly requested SDD after a real-client failure exposed omitted migration behavior.
- Direct failure mode: A direct patch could send at the wrong lifecycle point, leak framing into Application, omit listener/channel-dependent fields, or validate only a hand-built codec while the real connection callback still sends nothing.
- Useful control: A durable Spec plus complete-result code review and fresh verification protects timing, exact body compatibility, endpoint scope, and architectural boundaries together.
- Time-for-quality rationale: The change is small, but another escaped omission immediately blocks all real-client testing; a Lite review cycle is cheaper than another Windows test round with ambiguous evidence.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: The typed contract, connection trigger, encoder, and transport proof form one inseparable handshake outcome with one acceptance and rollback boundary.
- Why Direct is insufficient: Direct lacks the durable complete-result gate that cross-checks lifecycle ordering and layer ownership against the exact compatibility outcome.
- Why Multi is unnecessary: There are no independently useful or independently acceptable outcomes; codec-only or trigger-only work would remain unusable.
- Reroute conditions: Reroute only if evidence shows the client requires a materially larger multi-message pre-LOGIN negotiation or a separate external/client change with its own acceptance boundary.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-bugfix`
- Why this is sufficient: The root cause and central fix direction are demonstrated; Lite provides an approved behavior Spec, TDD implementation, complete-result code review, and fresh verification.
- Why lighter is insufficient: Direct would not retain the explicit protocol-order and layer-boundary requirements through a complete-result review.
- Why heavier is unnecessary: No consequential design choice requires a durable Plan, and no safety, data-loss, authorization, or irreversible external-mutation risk requires Governed controls.
