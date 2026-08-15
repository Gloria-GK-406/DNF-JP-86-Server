# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

- Intent and outcome stability: The user explicitly selected a behavior-preserving Channel-only
  refactor and excluded Game options pending client reverse engineering.
- Requirement stability: Direct handler response, Protocol-owned key derivation, removal of the
  obsolete command and byte-identical Channel frames are observable and reviewable.
- Execution direction: Existing consumers and tests expose a single narrow dependency chain; local
  naming and exact Protocol placement can be selected during implementation.
- Remaining uncertainty: None that changes task semantics. The user explicitly requires reuse of the
  current branch rather than creation of another worktree.

## Marginal Control Value

- User precision signal: The user explicitly requested the minimum SDD appropriate for a small change.
- Direct failure mode: A direct edit could remove the Connect command but leave the key interface or
  another encoder registered through Application, or preserve compilation while changing fixed-time
  encrypted frame bytes.
- Useful control: One durable Spec, complete-result review and fresh verification protect the full
  ownership cleanup and byte-parity boundary without a separate Plan.
- Time-for-quality rationale: These controls are small compared with the implementation and prevent
  an incomplete cross-project cleanup or unnoticed protocol regression.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Command removal, provider relocation and all four Channel response consumers
  form one indivisible acceptance boundary with one rollback outcome.
- Why Direct is insufficient: It lacks the requested durable acceptance boundary and complete-result
  review across Abstractions, Application and Protocol.
- Why Multi is unnecessary: No independently acceptable or independently reversible sub-outcome exists.
- Reroute conditions: Discovery of actual connection-domain state, a key-formula change, or a required
  Game/bootstrap modification would reopen intent and scope.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-refactor`
- Why this is sufficient: A Spec, complete-change review and fresh verification cover the credible
  omissions; implementation sequencing is direct.
- Why lighter is insufficient: Direct execution would omit the user-requested minimal SDD artifact
  and its complete-result gate.
- Why heavier is unnecessary: No durable Plan, independent challenge set, high-consequence mutation
  or multi-session sequencing adds material protection.
