---
kind: rks-review
verdict: approved
---

# Change Review: A restated move does not undo itself

Scope `change`. Reviewed against `TaskContract.md` revision 1 (C-1..C-8), `spec.md` (RQ-001..RQ-006,
AC-001..AC-011), `routing-decision.md`, the parent contract's SC-6/SC-7/SC-10/SC-11, and the complete
uncommitted change between `19fa03d6` and the working tree. No adversarial challenge set was supplied, so
there is no `Challenge Dispositions` section.

Three passes. The first raised two blocking findings and five observations; the second confirmed those
resolved, raised two new blocking findings from the remediation itself, and four observations; this one
confirms every finding closed and adds an independent mutation of its own. Nothing self-reported was taken
on trust in any pass. Every count below was re-derived from `git show 19fa03d6:<path>`, from the working
tree, or from a run this review started.

## Verdict

Approved. The production change is correct and complete across all four move paths, the checks are
mutation-sensitive rather than decorative, and the records now say what the evidence supports and no more.
Two cosmetic items are left below as observations; neither blocks, and one of them this review recommends
leaving alone.

## Independently re-run checks

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test <abs>/Server/86JP.Server.slnx --nologo`,
started by this review with no `Dfo.Server.Host` process running, absolute solution path, output retained:

- **2409 passed / 2 skipped / 0 failed**, exit 0.
- Pvf 60, Networking 52, **Protocol 623**, Host 6, Persistence 333, Infrastructure 259 (+2 skipped),
  Application 1076 — the seven sum to 2409.
- Output body searched, not the summary line: `[FAIL]` **0**, `error CS` **0**, `MSB<digit>` **0**,
  **7** `的测试运行` banners.
- Against the 2401 baseline that is **+8**, all of it in Protocol (615 → 623), and the file's `[Fact]`
  count goes 14 at `19fa03d6` → 22 now. The reported figures are correct.

**The guard count still checks out.** `grep -c "AlreadyCarriedOut(command"` gives **8**, matching 8
`MoveAlreadyMade` production sites, against exactly **one** at `19fa03d6` — four paths, two directions
each, **seven new**.

**The delivered hash is the recorded hash.** `sha256sum` on the working tree gives
`3f50a544c9b8e512dfd8a509096cd3fcec50a827c70a441d93f65820ef6c125d`, the value the mutation section pins.
The three mutation logs were regenerated after the prose was final (`scratchpad/efdcc/N1.txt`, `N2.txt`,
`N3.txt`, all 19:24) and show 22 cases with 3 / 2 / 1 red, every one a value mismatch.

## An independent mutation this review added

The delivered mutation set exercises the reverse half (N1), the end the forward half looks at (N2) and the
identity equality (N3). Nothing in it exercises the **occupancy clause** — `atTheEndItNames is null` — which
is the clause the whole no-misfire argument turns on and the one the rewritten swap case is supposed to
hold down. So this review wrote a fourth mutation and ran it: drop `&& atTheEndItNames is null` from
`AlreadyCarriedOut`, leaving the identity comparison and the other end's occupancy.

```
Dropping_a_piece_back_where_it_already_is_changes_nothing [FAIL]
  Expected: Not MoveAlreadyMade
  Actual:       MoveAlreadyMade
A_swap_onto_an_occupied_worn_slot_is_carried_out_whatever_either_end_names [FAIL]
  Expected: Moved
  Actual:   MoveAlreadyMade
失败!  - 失败: 2，通过: 621，总计: 623
```

Two red, both value mismatches, and both of them the cases added in this round's remediation. That settles
three things at once that the delivered evidence left resting on argument:

- The rewritten swap case is genuinely load-bearing. It is not decorative and it is not passing for an
  incidental reason, which is what its predecessor was doing.
- Occupancy really is asked before identity. The same-template pair at both ends is safe **because** of
  clause ordering, exactly as the case's remarks claim — and that is the near-counterexample this review
  was asked to hunt in the first pass, now held down by a check rather than by reading.
- `Dropping_a_piece_back_where_it_already_is_changes_nothing` is load-bearing too, which retires the
  reservation this review would otherwise have had about its `Assert.NotEqual` form.

The file was restored and re-hashed to `3f50a544…` and `git diff --stat` returned to 407 / 73.

## Re-verification of the second pass

**B-3 (the swap case carried out no swap) — resolved, and resolved better than asked.** The fixture is now
`Main:BagSlot` and `Equipment:WornCostumeSlot` each holding `Costume`, both instance values naming it, and
the assertion is positive: `Assert.Equal(Moved, ...)` plus both ends read back. Walked against the code, it
is a real swap — bag slot 12 is in the equipment band so a costume may sit there, `WornSlotForAsync(Costume)`
answers 3 which is the destination, so `PutOnAsync` reaches its two writes and returns `Moved`. The
`Moved` status is reachable from nowhere but the write path, so AC-005 and C-3's swap half are now met by
a check. The mutation above confirms it reddens.

The same-template choice is the right one and the remarks say why: it is the closest thing the rule has to
a counter-example, and it fails to be one only because occupancy is asked first. Making that the fixture
rather than a footnote is better than what the finding asked for.

**On the weakness self-declared — answered directly: no further case is needed.** It is true that two
identical templates make the content assertions unable to tell a completed swap from a no-op, and true
that `Moved` is what carries the case. But `Moved` carries it completely: neither a refusal nor a no-op can
produce it, and no path returns it without having written both ends. Distinguishable items would prove
that the two cells *exchanged* contents, which is not what AC-005 or C-3 asks and is not behaviour this
round changes. What matters more than the extra assertion is that the limitation is written into the case
itself instead of being left for a reader to find, and it is. Leave it as it stands.

The remarks also record what the earlier version did, why the negative assertion passed anyway, and that a
reviewer strengthening the assertion is what exposed it. That is the right entry to leave behind.

**B-4(a) (`0x0013.md` said three paths) — resolved.** It now says `MOVE_ITEMSPACE` dispatches to four
paths, names the fourth as the same-list move handed to `StoreBackedItemContainer.MoveToAsync`, notes that
this is where the "whichever end holds it" rule came from in the first place, and states eight judgements.
The paragraph above it that still reads 「三条路现在都按「哪一端有东西就动哪一端」办」 is **not** stale: it
describes the three cross-list paths gaining a reading the same-list one always had, which is what the test
class's own remarks say, and the new paragraph directly below resolves any ambiguity by naming that origin.

That this was a record corrected twice in one attempt is itself recorded, in the Records-corrected table's
`0x0013.md` row. Noting that a correction needed correcting is the harder entry to write and the more
useful one.

**B-4(b) (the falsified universal survived in the live records) — resolved in both places.**

- `AlreadyCarriedOut`'s remarks: "under any reading" is gone. The claim is now scoped to evidence —
  "Every fresh drag that has been observed names the item on an end that holds it" — and a following
  paragraph says in as many words that this is evidence and not a proof, names the exact shape that would
  break it (an identity written on an empty destination end), says the eleven observed put-ons left that
  field zero, says that "not observed" is not "cannot happen", says an earlier revision of the remark
  stated the stronger claim, and points at the change list's section.
- `0x0013.md`: the same, in its own terms. It keeps the 「矛盾而非相似」 argument, then says 「但那是证据,
  不是证明」, names the shape, states that the earlier version of that very document overstated it, and
  records the two readings and that settling them needs a capture at a running client.

`spec.md` is untouched, which is correct: it is the frozen artefact of its stage, and what was learned
after it belongs in the change list.

**O-6 — resolved.** "one of six" is now "one of eight".

**O-7 — resolved.** Markdown bold in this file is now zero (`grep -c '\*\*'` = 0); the 130-character line
is gone, and the only lines over 110 characters are the two that were there at `19fa03d6`; `<para>` is 21
open and 21 closed. See O-10 for what the balancing did structurally.

**O-9 — resolved.** `Dropping_a_piece_back_where_it_already_is_changes_nothing` covers the same-slot no-op
with both instance values non-zero, and the mutation above shows it reddens. This is the frame
`MoveToAsync`'s own remarks say the client sends and expects to succeed, arriving in front of the busiest
of the four guards, and it is now held by a check rather than by this review's reading.

## Non-Blocking Observations

**O-10. Balancing `<para>` produced a nested one.** The file is 21 / 21, but line 356 opens a `<para>`
inside a `<para>` that is still open — nesting depth 2, where `19fa03d6` never exceeds 1 anywhere in the
file. It is well-formed XML and no renderer will fail on it, but a paragraph inside a paragraph is not what
was meant and not what the rest of the file does. One `</para>` moved up closes it. Cosmetic; not worth a
round of its own, but worth doing whenever this file is next opened.

**O-11. O-8 should stay unchanged — this review withdraws it.** The same-list guard reads its two slots
with two `ReadSlotAsync` calls where `ReadSlotsAsync(first, second)` exists. Asked whether to change it:
no. Both are pure, both are inside the same transactional command, so the values agree and there is no
correctness or consistency consequence; the only gain is tidiness, and it is not worth editing a
production file that three passes of checks and four mutations have now settled. Recording the decision is
better than leaving the question open.

**O-12. The "Issue #13 is not reached" section now understates its own evidence.** It still offers only
the reading. Since the remediation, `A_swap_onto_an_occupied_worn_slot_is_carried_out_whatever_either_end_names`
is a check exercising precisely #13's shape — both ends occupied, both instance values naming a piece — and
it comes back `Moved`, with this review's mutation showing it reddens when the occupancy clause is dropped.
C-6 is met either way and understating is the safe direction, so this is a note rather than a finding: one
sentence citing the case would make the section stronger than it currently claims to be.

## Success Conditions

| ID | State | Evidence |
|---|---|---|
| C-1 | met | `A_restated_take_off_does_not_put_the_piece_back_on`: store unchanged, `MoveAlreadyMade` carrying the item's id. |
| C-2 | met | `The_same_two_cells_named_the_other_way_round_are_a_fresh_put_on` differs from C-1's fixture in one field; N2 reddens it. |
| C-3 | met | Occupied-destination by `Naming_the_worn_piece_on_the_end_that_holds_it_still_takes_it_off`; the swap by `A_swap_onto_an_occupied_worn_slot_is_carried_out_whatever_either_end_names`, asserted positively and shown mutation-sensitive by this review's occupancy mutation. |
| C-4 | met | The premise is stated as a premise in the contract, the spec, the guard's remarks, the case remarks, `0x0013.md` and the change list, with what the guard does if it is false. |
| C-5 | met | Four paths, eight conditions, seven new, counting method given and independently confirmed; the missed path, its root cause, and the fact that review rather than the walk found it are all recorded. |
| C-6 | met | Confirmed unreachable independently in all three passes; recorded as a reading, and now also covered by a check the section does not yet cite (O-12). |
| C-7 | met | Four records corrected, each naming what falsified it, including the one this attempt corrected twice. No other live record describes the reverse direction as unguarded. |
| C-8 | met | 2409 / 2 / 0 re-run by this review; body searched, seven banners, zero `[FAIL]` / `error CS` / `MSB<digit>`. |

Contract constraints hold: layering unchanged, one slice, no cross-protocol reference, no new wiring point,
no nested dispatch, the command still transactional; `CommandHandlerArchitectureTests` and
`NotificationHandlerArchitectureTests` unmodified and passing; `TreatWarningsAsErrors` with CS9113 clean;
`SelectCharacterAcceptedTranscriptTests` untouched and passing; no new protocol frame and no field layout
change, `MoveItemSpaceRequest.DestinationInstanceValue` having existed at `19fa03d6`; the two forbidden
worktrees and `ServerS4A21` untouched.

## Reviewer's own boundary

Three diagnostic mutations across the three passes, each to a file-level backup with `sha256sum` before and
after: N3 on `MoveItemSpaceSlice.cs` (pass one), the assertion strengthening on
`EitherEndMayBeHoldingItTests.cs` (pass two), and the occupancy-clause mutation on `MoveItemSpaceSlice.cs`
(this pass). All restored and re-hashed —
`3f50a544c9b8e512dfd8a509096cd3fcec50a827c70a441d93f65820ef6c125d` for the slice — with
`git diff --stat` back to 407 insertions / 73 deletions across the same four tracked files plus the one
untracked attempt directory. No git restoring command was used, nothing was committed, and no other
checkout or worktree was touched.

## What this work does not establish, restated for the record

No restatement of a move has ever been observed. The repair rests on a restatement carrying the original
frame's bytes, and if the client instead rebuilds the frame from its new state, a restatement is
byte-identical to a fresh drag, the guard never fires, and the defect stands. This is stated as a premise
in six places and never as an observation. The honest reading of this outcome is that it may have fixed
nothing a player can see, and that no check in this suite can tell — which the change list says in those
terms. Approving it approves the reasoning, the records and the checks, not a repair anyone has watched
work.
