# Routing Decision

- TaskContract: `.rks-agent/tasks/every-frame-draws-the-current-character/attempts/spec-004-the-session-copy-keeps-up/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The outcome is observable -- a screen that reads the session's copy shows what the character has after a
move -- and the scope is closed on both sides. What is not decided is *how*, and that is not vagueness: the
three candidate treatments have costs that differ by an order of magnitude in blast radius, and which one
is right depends on a set of readers nobody has enumerated.

The contract makes the enumeration a condition in its own right rather than a step, and forbids the choice
from preceding it. So the uncertainty is bounded and named, with its input identified.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: One acceptance boundary -- the readers are known and a screen that reads the copy is current
  -- and one rollback boundary, with nothing after it in the root task. The concrete Direct failure path is
  the one this repository has now produced twice in this task alone: enumerating from the thing the report
  names instead of from the code, and then justifying a decision with a survey that was never taken. The
  previous outcome missed one of four dispatch paths exactly that way. Here the report names a single
  screen, and the cheapest-looking treatment -- repair that screen -- is the one that would be chosen by
  anyone who took the report's word for the reader set. The controls that catch it are a written condition
  that the enumeration precede the choice, a complete-result review by someone who did not write it, and
  fresh verification.
- Reroute conditions: If the enumeration shows that none of the three treatments is acceptable, or that
  refreshing the copy grows its own storage boundary -- a second source of truth to keep consistent, or a
  write path where there was only a read -- stop and return to the root map rather than widening in place.
  That condition was written into the root routing decision before any of this work began and it still
  stands. If making a screen current turns out to require changing what the copy *is* rather than when it
  is replaced, that is a different outcome.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-bugfix`
- Rationale: There is one decision here whose wrong answer propagates through everything after it, and
  unlike the previous outcome's it is genuinely open: which treatment. Reloading and replacing the copy
  after every move changes what **every** reader of it draws, and the readers are exactly what is not yet
  known; repairing one screen leaves the others as they are, which is right or wrong depending on the same
  unknown. Lite would let implementation begin against whichever treatment looked reasonable and catch a
  bad choice at review, with the change already built around it. A Plan is where the enumeration's result
  becomes a chosen treatment, in writing, before anything is built on it -- which is precisely the control
  the contract's first condition asks for. Governed adds independent challenge and artifact approval
  against escaped high-consequence failure; this touches no credential, no external system, no irreversible
  mutation and no data-loss path -- the worst outcome is a screen showing stale equipment, which is the
  defect being repaired -- so no hard floor applies. `code-bugfix` because this is a defect with a
  demonstrated mechanism and a live symptom, and its plan stage is scoped to cause, fix, regression and
  verification.
