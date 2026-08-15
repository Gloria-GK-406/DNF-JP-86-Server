# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The user accepted the proposed strong Game CMD/NOTI design; the
  pulled confirm-grade protocol evidence fixes `0x01` as CMD, `0x00` as NOTI and client outbound as
  CMD-only. The outcome is behavior-preserving semantic enforcement, not protocol discovery.
- Requirement stability: The TaskContract fixes category ownership, strong sending/encoding,
  protocol-neutral Networking, exact byte preservation, state-sync classification and explicit
  exclusions for GameOption and behavior changes.
- Execution direction: One Protocol-owned category model must become the single authority, followed
  by typed encoders/registrations and Game send operations; raw Networking bytes remain opaque.
- Remaining uncertainty: Local naming and the smallest reusable generic shapes may be chosen during
  Spec/Plan without changing the task meaning.

## Marginal Control Value

- User precision signal: The user explicitly requested a TaskContract after accepting the design,
  and the protocol silently drops frames assigned to the wrong client registry.
- Direct failure mode: A direct migration could miss one of the many state-synchronization or
  supplemental paths, or temporarily leave category selection duplicated between messages, send
  methods and encoders. Compilation could still succeed while a frame is silently routed wrong.
- Useful control: One durable Spec inventories the required semantic invariants; an explicit Plan
  orders the single-authority model before consumer migration and binds exact byte/order checks;
  complete-change review checks every outbound and inbound Game path.
- Time-for-quality rationale: Planning and complete review are cheaper than diagnosing a wrong
  `0x00/0x01` whose client behavior is silent discard, and the user explicitly chose SDD precision.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Sender semantics, encoder categories, decoder registration, opaque framing and
  state-sync cleanup jointly produce one indivisible outcome: every Game frame has one correct,
  statically constrained class. None is independently acceptable without the others.
- Why Direct is insufficient: Direct lacks the durable inventory and dependency sequence needed to
  prove that no raw category selector or unclassified message path remains.
- Why Multi is unnecessary: There is one acceptance and rollback boundary with one wire-compatibility
  gate; separating components would accept transient dual-authority states.
- Reroute conditions: Return to intent if current source contradicts CMD/NOTI evidence or a required
  behavior change appears; return to Multi only if an independently releasable outcome emerges.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard adds the explicit dependency plan and complete ordinary code
  review needed to prevent partial classification while retaining fresh behavior verification.
- Why lighter is insufficient: Lite has no durable Plan, so the coupled choice of single authority,
  typed encoding, sender constraints and state-sync migration could be resolved inconsistently.
- Why heavier is unnecessary: No safety, authorization, protected-data, durable-data or irreversible
  external outcome requires independent challenge or drift governance beyond review and verification.

## Workspace Exception

The user explicitly required development directly on the current branch. The normal standalone
worktree preparation step is therefore intentionally replaced by the verified current checkout at
baseline `f29123b`; `Diagnostics/` remains unrelated and excluded.
