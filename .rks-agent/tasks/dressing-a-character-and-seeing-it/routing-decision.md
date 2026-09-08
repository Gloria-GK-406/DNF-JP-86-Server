# Routing Decision

- TaskContract: `.rks-agent/tasks/dressing-a-character-and-seeing-it/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The intent is stable and the scope is closed. Each of the three defects was traced to a named line
before the contract was written -- the single-ended read in the three cross-list move paths, the
handler that sends only its acknowledgement, and the roster's stored appearance blob -- and each was
cross-checked against the upstream reference and against a live observation, so nothing here rests
on a hypothesis that execution could overturn. The success conditions are observable without
inventing new instruments: automated checks for the behaviour, the byte anchor for the frames, the
suite for the baseline, and the owner's own re-test for the three live symptoms that started this.

The one thing execution has to decide rather than discover is where the "one end holds something"
rule belongs -- once at dispatch, or three times inside the paths that lack it. That is an
implementation choice with a stable acceptance boundary either way, not an unresolved requirement.

The direction of risk is known and named in the contract: defect A converts requests that are
refused today into requests that succeed and write to the store, and this repository has repeatedly
under-reported exactly that direction. SC-3 is written to be failable by an independent reader.

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Rationale: The owner asked for all three defects to be repaired and left the division of work to
  the executor, and the three do not share an acceptance boundary. A is judged by which moves are
  carried out and what the store then holds; B by which frames leave the server after a move that
  already succeeded; C by what one unrelated frame contains at character selection, in a code path
  the other two never touch. Each can be accepted or rolled back on its own without leaving the
  other two in a half state, which is what makes them separate Specs rather than three stages of
  one. Working directly is the wrong route for A in particular: it widens a refusal into a durable
  write across every served pair of lists, and the reverse enumeration that has to accompany it is
  the step this repository has skipped before -- that is the control worth its time here, and it is
  a per-Spec control, so it argues for Multi rather than for one large Single.
- Reroute conditions: If B turns out to need the appearance frame rebuilt rather than re-sent, its
  boundary grows into A's and the two should be reconsidered as one Spec. If C cannot derive the
  roster appearance without changing what is stored on the character row, it acquires a storage
  boundary of its own and its Spec should stop and return here rather than widen. If A's rule can
  only be placed correctly by changing the acknowledgement's shape, the byte anchor is in play and
  the contract's frame constraint has to be revisited before implementation continues.

## Controller Selection

Left to each child Spec, per Multi Spec rules.
