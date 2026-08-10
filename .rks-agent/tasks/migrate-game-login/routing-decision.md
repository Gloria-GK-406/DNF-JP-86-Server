# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The user selected Game LOGIN as the next protocol and
  explicitly tied it to indexed online-user state plus an exclusive identity-transition
  Command boundary.
- Requirement stability: The main login request/acknowledgement, online-user lifecycle,
  account binding, shared/exclusive barrier semantics, and exclusions are observable and
  reviewable.
- Execution direction: The existing typed Message/Command/TouchSocket architecture and
  legacy login parser/builder establish the central direction; local types and persistence
  details can be resolved in the Spec and Plan.
- Remaining uncertainty: Packet-capture truth and later post-login notifications remain
  explicitly outside this staged result.

## Marginal Control Value

- User precision signal: The user has repeatedly corrected architectural coupling and
  concurrency semantics and expects the foundation to support later protocol migrations.
- Direct failure mode: A direct edit could implement login while missing atomic writer
  preference, allowing new shared commands to enter during an identity transition, or
  could let disconnect bypass the same boundary.
- Useful control: One complete Spec, an ordered dependency Plan, complete-code review, and
  fresh verification protect the coupled lifecycle and protocol requirements.
- Time-for-quality rationale: These controls prevent an early concurrency or ownership
  choice from propagating into every later Game command, which outweighs their local cost.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Protocol handling, online-user state, account binding, and the
  execution barrier jointly form one login-mainline acceptance boundary.
- Why Direct is insufficient: Direct checks cannot as cheaply preserve the dependency
  order and complete cross-layer acceptance mapping during this foundational change.
- Why Multi is unnecessary: None of the deliverables is independently useful or
  acceptable without the complete login mainline.
- Reroute conditions: Discovering independently deployable outcomes, unavoidable password
  policy redesign, or a credential/security hard floor would reopen routing.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-feature`
- Why this is sufficient: A durable Plan, TDD implementation, complete-code review, and
  fresh verification protect the identified concurrency and cross-layer risks.
- Why lighter is insufficient: Lite lacks the explicit dependency sequencing needed to
  settle connection registration, exclusive execution, persistence, response, and
  disconnect ordering before implementation.
- Why heavier is unnecessary: This migration preserves legacy local authentication
  behavior and does not handle secrets, authorization policy, external mutation, or
  high-consequence protected data requiring independent governed review.
