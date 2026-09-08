# Routing Decision

- TaskContract: `.rks-agent/tasks/every-frame-draws-the-current-character/attempts/spec-002-both-rosters-draw-what-is-worn/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The outcome is observable and the direction is known: one frame's appearance source is replaced by the
projection the other frame already uses, and both the projection (`DrawnAppearance`) and the read it needs
(`ReadWornByAccountAsync`) exist at the baseline. Nothing has to be invented to do it.

Two things are deliberately left to be decided by evidence rather than settled here, and both have their
inputs named: whether the anchored bytes move at all, and whether `characters.appearance_blob` and its
decoder keep a reader. Neither changes what the outcome is; each is a measurement the work must take and
record. That is delegated direction, not missing task semantics.

One boundary is a stop rather than a decision: if retiring the column needs a migration of existing saves,
the outcome has grown a storage boundary that was not authorized, and the attempt returns to the root map.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: One acceptance boundary -- either both roster frames draw from what is worn, with the anchor
  accounted for, or they do not -- and one rollback boundary independent of the two outcomes that follow.
  The concrete Direct failure path is the one this repository has produced four times: a change that alters
  what a frame contains and what a request can reach, whose consequences are enumerated only along the
  direction the change started from. Here that has a specific shape. Replacing the appearance source moves
  the entry burst from reading a column to reading every worn row on the account, which is a read that can
  fail for its own reasons, on the request that lets a player into the world -- and `CharacterRosterService`
  was split in two precisely so that read would stay off the cheap paths. A Direct change would very
  plausibly satisfy the visible symptom and quietly undo that split. The controls that catch it are a
  written acceptance boundary that names it (C-5), a complete-result review by someone who did not write
  the change, and fresh verification. The owner has chosen precision over speed for exactly this kind of
  change throughout, so the added time is already accepted.
- Reroute conditions: If retiring `characters.appearance_blob` requires migrating existing saves, stop and
  return to the root map -- that is a storage outcome with its own boundary. If making the entry frame draw
  from what is worn cannot be done without changing a frame's field layout rather than its values, stop:
  the contract forbids the shape changing and the root task forbids a new frame, so the outcome would have
  moved. If the measurement shows the anchored frames do not move at all, that is a legitimate result and
  the attempt continues; it does not on its own indicate the change failed to take effect, but C-3 requires
  saying why the transcript does not exercise the difference rather than reporting silence.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-bugfix`
- Rationale: Lite would review the finished change against the Spec, which catches a wrong result but only
  after it is built. There is one decision here whose wrong answer propagates through everything after it:
  **how the entry path gets what each character wears.** The obvious move is to switch that path to the
  listing that already carries appearances -- and that listing is the expensive one, split off deliberately
  so that a failure reading equipment could not stop a player entering the world. Taking it wholesale would
  satisfy the visible symptom, quietly undo that split, and be discovered at review with the change already
  written around it. Everything downstream depends on that answer: what the anchor measurement is
  measuring, what C-5's failure-surface account says, and which paths the reverse enumeration has to walk.
  A Plan that fixes the seam, the order, and the checks before implementation is what stops that rework,
  and that is the control Lite does not have. Governed adds independent challenge and artifact approval
  against escaped high-consequence failure; this change touches no credential, no external system, no
  irreversible mutation and no data-loss path -- the column it may retire is one nothing has written since
  creation and nothing but the frame under change reads -- so no hard floor applies and ordinary review
  plus fresh verification is adequate. `code-bugfix` because this is a demonstrated defect with an
  observed live symptom, and its plan stage is scoped to cause, fix, regression and verification, which is
  the shape of the work.
