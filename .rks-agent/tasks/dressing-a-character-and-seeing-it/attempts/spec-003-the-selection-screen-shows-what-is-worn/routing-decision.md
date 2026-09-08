# Routing Decision

- TaskContract: `.rks-agent/tasks/dressing-a-character-and-seeing-it/attempts/spec-003-the-selection-screen-shows-what-is-worn/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The defect is located and the shape of the answer is settled: derive the selection screen's
appearance from what each character wears, using the rule the in-world frame already uses, and read
what a whole account wears through a query this repository does not yet have. A read-only survey of
both this repository and the upstream reference was done before the contract was written, so the
pieces and their layers are known rather than guessed.

The boundary was drawn on a checkable fact rather than on convenience: the selection screen's frame
is outside the byte anchor and the entry burst's roster frame is inside it, so this work can reach
the screen the owner reported without moving a single anchored byte. The cost is that two screens
will disagree until someone decides about the second one, and the contract says so rather than
leaving it to be found.

## Marginal Control Value

This one has more coupled decisions than its two siblings: a new persistence read, a projection
lifted out of a byte-writing loop into a value, two callers of that projection that must not drift,
and a stored blob that stops being read on one path while staying read on another. Each is small;
together they are the kind of set where an early choice made for the first constrains the last.

That is what a Plan buys here, and it is the first time in this task it has been worth buying: the
order in which the projection is lifted, what shape it returns, and which side calls it first decide
whether C-4 is structurally true or merely asserted. Getting that wrong is not caught by a review of
the finished change — it is caught by a review of the finished change *and then rewritten*.

The independent complete-change review stays worth its time for the reason it was in both siblings,
and it earned it in spec-002 by finding a blocking defect the executor did not see.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: One acceptance boundary — what the character-selection screen draws. The new read, the
  lifted projection and the rewired caller have no value apart from each other and could not be
  accepted or rolled back separately.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-bugfix`
- Rationale: Lite was enough for both siblings and is not enough here, for one nameable reason: this
  is the first of the three with several coupled implementation decisions whose order matters —
  lift the projection, give it a shape, hand it to two callers, add a read beneath it — and a Plan
  is what stops the last of those from being shaped by an expedient choice made in the first. Lite
  would reach the same acceptance boundary and reach it having to redo work that a review can only
  point at. Governed adds independent challenge and audit-strength evidence for a change that
  touches no safety, credential, privacy, financial or irreversible external boundary, writes no
  data at all, and is read-only against a store whose rollback boundary is proven; ordinary review
  plus fresh verification is adequate, as it was for the two accepted results before it.
