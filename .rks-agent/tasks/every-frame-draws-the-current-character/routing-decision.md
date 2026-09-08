# Routing Decision

- TaskContract: `.rks-agent/tasks/every-frame-draws-the-current-character/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

Intent and scope were fixed by the owner in two explicit choices: which debts to clear, and which four of
them in which order. All four already exist as written issues whose citations were made against the
baseline this work starts from, so the requirements are enumerable rather than discovered.

The desired result is observable in each case: a decoder's behaviour proven by checks, two frames drawn from
one source, a restated move that does not undo itself, a screen that shows what the character has after a
move.

Two execution directions are deliberately left open inside the contract rather than left unclear. The
disposition of `appearance_blob` depends on whether a reader survives the change, and the treatment for the
session's inventory copy depends on an enumeration that has not been done. Both are recorded as decisions
the work must reach and justify, with the inputs that decide them named. That is delegated direction, not
missing task semantics.

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Rationale: The owner picks direction by looking at a working game and has repeatedly chosen precision over
  speed for exactly this kind of change, so the added workflow time is one they have already accepted. The
  Direct failure path here is not "break something" but the one this repository has hit four times running:
  a change that turns previously refused requests into ones that succeed and write, whose consequences are
  enumerated only along the direction the change started from, so the newly reachable paths go unreported
  and a green suite is read as proof rather than as evidence that nothing exercises them. Two of these four
  outcomes have that exact shape -- the entry frame changes what a byte-anchored frame contains, and a
  reverse echo guard adds a refusal path -- and the control that reduces it is a complete-result review by a
  reviewer who did not write the change, against a written acceptance boundary, plus fresh verification.
  The boundary is Multi rather than Single because the four outcomes are accepted by different evidence and
  can be rolled back without each other: checks over a decoder; a byte-anchor movement with a frame-by-frame
  account; a refusal path with a forward-and-reverse change list; and an enumeration whose result then
  selects among three treatments with different costs. Sealing them together would mean one rollback
  discards three accepted results, and would let the last one's undecided direction hold the first one's
  finished work.
- Reroute conditions: If the enumeration for the session's inventory copy shows the three recorded
  treatments are all unacceptable, or that refreshing the copy grows its own storage boundary, stop and
  return here rather than widening in place. If retiring `appearance_blob` turns out to require a schema
  migration of existing saves, that is a storage outcome that was not authorized and needs its own
  boundary. If the restated take-off proves indistinguishable from a genuine reverse move, that outcome
  ends as a recorded finding rather than a code change, and the remaining outcomes continue unaffected.
