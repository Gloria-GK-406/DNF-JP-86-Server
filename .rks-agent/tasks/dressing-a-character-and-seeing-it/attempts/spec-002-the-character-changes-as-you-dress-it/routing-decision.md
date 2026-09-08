# Routing Decision

- TaskContract: `.rks-agent/tasks/dressing-a-character-and-seeing-it/attempts/spec-002-the-character-changes-as-you-dress-it/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The outcome is settled and the ground was surveyed before the contract was written: the frame
already exists and is already sent from one place, the projection it needs is already inside its
encoder, and three of its four arguments are already carried by the runtime session directory. What
is missing is one of them -- the equipment, whose only copy in the session is the snapshot taken at
character selection, which no move refreshes.

One thing execution decides rather than discovers: how to obtain the post-move equipment. Two routes
are visible and both satisfy every condition, so this is an implementation choice with a stable
acceptance boundary either way. It is named in the contract's assumptions with the cost of each,
including the restriction one of them has to answer rather than ignore.

## Marginal Control Value

The risk here is not getting it wrong, it is getting it *nearly* right and not noticing: a frame
that is sent but built from the stale snapshot looks correct in any check that seeds the snapshot
and the store alike. C-4 exists to be failable by exactly that mistake, and it is the condition a
Spec has to pin in words before implementation, because a check written afterwards will be written
to pass.

The second control worth its time is the independent complete-change review, for the reason it was
worth it in spec-001: this repository has repeatedly under-reported what a change made reachable,
and this one adds a send on a path that had none.

A plan is not worth it: one handler, one decision, no ordering.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: One acceptance boundary -- what leaves the server after a move that already succeeded.
  Nothing inside it can be accepted or rolled back apart from the rest.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-bugfix`
- Rationale: A demonstrated failure with a located cause and a settled direction, so the controls
  that earn their time are a durable Spec pinning what "changed what the character wears" and "the
  store, not the snapshot" mean, and an independent review of the whole result. Standard would add a
  Plan with nothing to sequence -- one handler, one decision, no ordered dependencies, and no early
  choice that could propagate past a review that would catch it cheaply. Governed would add
  independent challenge and audit-strength evidence for a change that touches no safety,
  credential, privacy, financial or irreversible external boundary and writes no data of its own.
  `code-bugfix` fits a located defect, and its `test-driven-development` binding is what makes C-4
  a check written before the implementation that could fool it.
