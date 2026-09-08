---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: The stored appearance decoder is pinned

## Delivered

- `SelectCharacterPreparationService.DecodeAppearances` now has checks:
  `Server/tests/Dfo.Server.Application.Tests/Characters/TheStoredAppearanceBlobIsReadExactlyAsWrittenTests.cs`,
  six cases covering a non-empty blob member by member, two entries in order, both refusals by their exact
  message, the shortfall found after an entry has already been read, and the empty and null blobs.
- A record separating the two coverage gaps: `coverage-record.md`. The two refusals never had a check;
  the non-empty decoding was covered indirectly until `35aaedf3` replaced the case that covered it. The
  record also states exactly how much that deleted case covered -- three members of eight -- rather than
  letting "the decoding was covered" stand.
- Mutation evidence that the checks are load-bearing, including the negative result that signedness is not
  observable through this method at all.
- **No production code changed.**

## Verification

- `verification/completion-evidence.md` -- 2398 passed / 2 skipped / 0 failed against a 2392 / 2 / 0
  baseline, condition-by-condition outcomes for C-1..C-6, and the boundary proofs.

## Repository Result

- Changed paths: two untracked additions --
  `Server/tests/Dfo.Server.Application.Tests/Characters/TheStoredAppearanceBlobIsReadExactlyAsWrittenTests.cs`
  and `.rks-agent/tasks/every-frame-draws-the-current-character/`. `git diff --stat` empty.
- Baseline or accepted result: built on `addbeb52bffdd329a85f292f6784200d1146bb28`.
- Parent rollback required: no.

## Successor Guidance

- **The decoder's behaviour is now written down, and the next attempt is the reason it was.** The entry
  roster frame is its only production reader (`SelectCharacterPreparationService.cs:226`). When that frame
  starts computing appearance from what is worn, this decoder loses that reader. **Deleting it together
  with these checks is a legitimate outcome there** -- it is not a regression, and these checks are not a
  reason to keep dead code. What they are for is telling that attempt whether its replacement changed
  anything it did not mean to change.
- **Signedness is not observable through `DecodeAppearances` and no future check should claim otherwise.**
  `CharacterRosterAppearanceState.EnchantValue` is a `uint`, so a signed read must be converted back and
  the conversion is bit-preserving. This was measured, not argued: replacing `reader.ReadUInt32()` with
  `(uint)reader.ReadInt32()` leaves all six cases green. This Spec asserted the opposite before the review
  and was corrected.
- **A mutation set that only alters control flow proves less than its red count suggests.** Three of the
  five mutations here reddened cases by raising or suppressing an exception, never reaching the member
  assertions; two of those reddened cases named the member-assertion case while failing before any
  assertion ran. Whether a mutation reddens a case is not the question -- *what reddened it* is. AC-011 was
  added mid-attempt to make that an acceptance condition rather than a habit, and it is worth carrying into
  the remaining attempts.
- **Reading only a run's summary line can turn a run that never happened into a pass.** One verification
  invocation here was launched from the wrong directory; MSBuild answered `MSB1009` and no test executed,
  while the surrounding shell line still exited 0. The root contract already requires keeping run output;
  this attempt is a live instance of why.
- The deleted case's blob asserted `Slot`, `DisplayItemId` and `ExpansionLength` only. Any later claim
  about what coverage `35aaedf3` removed should use that, not "the decoding".

## Artifacts

- `spec.md` -- includes **Revisions after this Spec was marked ready**, recording the three changes the
  review caused and why none of them moved the Spec below the TaskContract, which was not edited.
- `coverage-record.md`
- `reviews/change-review.md` -- `approved` on re-review, after `changes_required` on the first round.
