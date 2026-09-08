# Routing Decision

- TaskContract: `.rks-agent/tasks/every-frame-draws-the-current-character/attempts/spec-001-the-stored-appearance-decoder-is-pinned/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The behaviour to describe already exists and was read out of this repository at the baseline, field by
field, including both refusal messages. There is nothing to decide about what the result should be: the
requirement is that the existing behaviour be written down as checks that fail when it changes.

Scope is closed on both sides. No production code changes, and the question of whether this decoder should
survive belongs to the next outcome, not this one. The only judgement left is how to shape three checks and
one record, which is local implementation.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: One acceptance boundary -- a decoder's behaviour is either pinned or it is not -- and one
  rollback boundary, since nothing here depends on anything else in the root task. The concrete Direct
  failure here is narrow but real and this repository has produced it before: a check that passes whatever
  the code does. Checks written from the method's own text can assert that something threw without
  asserting which refusal, or fill a blob with zeros so that a field read at the wrong offset or width
  still matches. The control that catches it is a mutation of the decoder that must redden the checks,
  which C-4 makes an acceptance condition rather than a habit. That control costs one run, so its time is
  earned even on an outcome this small. A complete-result review by someone who did not write the checks
  is the second control, against the standing repository failure of a check whose name does not match what
  it proves.
- Reroute conditions: If pinning the current behaviour turns out to require changing production code --
  visibility, a seam, or a signature -- stop and return to the root map rather than widening here, because
  a production change belongs to an outcome that was scoped to allow one. If the decoder is found to have
  no production reader at this baseline after all, the premise of doing this before the next outcome is
  gone and the order needs to be reconsidered with the owner.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-refactor`
- Rationale: The failure this outcome has to survive -- a check that passes whatever the decoder does -- is
  caught by reviewing the finished checks against the contract and by the mutation the contract requires,
  both of which Lite already configures. Standard's distinctive control is a durable Plan resolving coupled
  decisions before implementation; here there is one file to add, no ordering, and no decision whose wrong
  early answer would propagate, so a Plan would document sequence that has no sequence and buy nothing.
  Governed's controls -- independent challenge, artifact approval, drift control -- protect against escaped
  high-consequence failure, and the worst outcome available here is a weak check that the next outcome's
  own review would surface; none of the hard-floor categories is touched, since this changes no production
  code, no data, and nothing external. `code-refactor` is the closest Profile because its acceptance
  language is preserved behaviour, which is exactly this boundary: nothing about the decoder may change,
  and the deliverable is the evidence of that.
