# Completion Evidence: A restated move does not undo itself

Verified in the worktree at
`E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character`, branch
`every-frame-draws-the-current-character`, on top of accepted baseline `19fa03d6`.

## The run

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

**2409 passed / 2 skipped / 0 failed**, exit 0.

| Assembly | Passed | Skipped | Baseline at `19fa03d6` |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 60 |
| Dfo.Server.Networking.Tests | 52 | 0 | 52 |
| Dfo.Server.Protocol.Tests | 623 | 0 | 615 |
| Dfo.Server.Host.Tests | 6 | 0 | 6 |
| Dfo.Server.Persistence.Tests | 333 | 0 | 333 |
| Dfo.Server.Infrastructure.Tests | 259 | 2 | 259 (+2) |
| Dfo.Server.Application.Tests | 1076 | 0 | 1076 |

Every assembly equals its baseline except Protocol, which is 615 + 8 -- the eight cases this attempt added
to `EitherEndMayBeHoldingItTests`, whose `[Fact]` count went 14 -> 22. Output retained at
`scratchpad/efdcc/spec003-verification.txt`; searching it for `[FAIL]`, `error CS` and `MSB<digit>` returns
0 matches each, and all seven test-run banners are present.

## Condition by condition

| ID | Outcome | Evidence |
|---|---|---|
| C-1 | met | `A_restated_take_off_does_not_put_the_piece_back_on`, `A_restated_carry_across_does_not_carry_it_back`, `A_restated_drag_inside_one_list_does_not_drag_it_back` -- each asserts the store is unchanged and the answer is `MoveAlreadyMade`. Mutation N1 reddens all three by value mismatch. |
| C-2 | met | `The_same_two_cells_named_the_other_way_round_are_a_fresh_put_on` differs from C-1's fixture in one field, which end names the item. Mutation N2 -- the guard told to look at the wrong end -- reddens it. |
| C-3 | met | `A_swap_onto_an_occupied_worn_slot_is_carried_out_whatever_either_end_names`, `Naming_the_worn_piece_on_the_end_that_holds_it_still_takes_it_off`, `A_move_naming_neither_end_is_carried_out`, `Dropping_a_piece_back_where_it_already_is_changes_nothing`. Mutation N4 -- the occupancy clause removed -- reddens two of them, which is what holds the "cannot misfire" property to a check rather than an argument. |
| C-4 | met | Stated as a premise in six places, none of them as an observation: the child contract's Assumptions, `spec.md`'s Assumptions and its frame table, the enum's remarks, `protocol/game/client-to-server/0x0013.md`, and the change list's "Known and left". The change list also says plainly that this work may have fixed nothing observable and that no check here can tell. |
| C-5 | met | `behavior-change-list.md`: three forward entries, two reverse, the wire section, three moved expectations, four records corrected, and the mutation table. The fourth dispatch path was missed on the first pass and found by review; that is recorded with its cause rather than silently added. |
| C-6 | met | Issue #13's shape is both ends occupied, which the guard's first clause excludes. Now covered by a check of that exact shape rather than by reading alone, with N4 as its mutation. |
| C-7 | met | Four records corrected, one of them twice -- `0x0013.md` was corrected in this attempt and then corrected again, because the first correction carried a stale path count and repeated a claim this attempt had already conceded was evidence rather than proof. |
| C-8 | met | The run above. |

## Boundaries held

- Byte anchor `SelectCharacterAcceptedTranscriptTests` untouched and passing; no new protocol frame; no
  frame's field layout changed -- `DestinationInstanceValue` was already decoded at the baseline and was
  being dropped at the command boundary.
- `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` unmodified and passing.
- One slice; no layering change; `IItemContainer` unchanged, which is why the same-list judgement is made
  in the slice rather than pushed into the container that cannot see what the client named.
- No existing assertion rewritten. The only change to existing check code is a defaulted
  `destinationInstanceValue` parameter on the harness; the 14 pre-existing cases are untouched.
- No git restoring command used. Four mutations, each with a file-level backup, restored and re-hashed to
  `9f602eca49c6ca5eb9bf...` -- the delivered file, so a reader can compute it on the change under review.

## Review

`reviews/change-review.md`, `verdict: approved`, after three rounds of `changes_required` carrying four
blocking findings: a fourth dispatch path left unguarded, a read-count claim that misread the baseline, a
check whose name said "is carried out" while the move it made was refused, and a falsified universal claim
left standing in the records that ship with the code.

**The reviewer wrote a mutation this attempt had not.** All three delivered mutations struck clauses about
naming; none struck the occupancy clause the safety argument rests on. That one reddens exactly the two
cases added in remediation. A mutation set can be load-bearing about what it was aimed at and silent about
what matters most, and this one was until review.

## What this does not establish

No restatement has ever been observed. The repair rests on a restatement carrying the original frame's
bytes; if the client rebuilds the frame from its new state instead, nothing here distinguishes it from a
fresh drag and the guard never fires. What was verified is the reasoning, the records and the checks --
not that anyone has seen this work.
