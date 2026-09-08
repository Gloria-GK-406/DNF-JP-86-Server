---
kind: rks-verification
decision: pass
---

# Verification: spec-001 "A move reads both of its ends"

## The claim being evaluated

That the change in the task worktree, uncommitted on `dressing-a-character-and-seeing-it` over
baseline `8247182c6e555fd22204490272b6d2ada8008508`, satisfies the child TaskContract's C-1 through
C-9 — that a move is carried out whenever either of the two ends it names holds something, that
nothing which decided a move before still fails to decide it, and that its record says which lists
it was between.

It does not claim anything about what the server sends after a move (root defect B) or about the
character-selection roster (root defect C). Those are sibling Specs.

## What ran

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
in the task worktree, with `tasklist` first confirming no `Dfo.Server.Host` process was running.

Exit code 0. **2375 passed / 2 skipped / 0 failed**: Pvf 60, Protocol 599, Networking 52,
Persistence 329, Host 6, Infrastructure 259 (+2 skipped), Application 1070. Baseline in this same
worktree was 2361 / 2 / 0; the difference is the 14 checks added by this work, all in
`Dfo.Server.Protocol.Tests` (585 → 599). The two skips are `A_missing_archive_*`, whose being
skipped is the sign that the archive loaded.

One run during implementation reported a single Application failure that did not reproduce; its
exception type and stack root were not captured, so it is **not** claimed as the known SQLite flake.
Two full runs since — including the final one above — have been green, and the project on its own
was green immediately afterwards.

## Condition by condition

| ID | Condition | Evidence | Met |
|---|---|---|---|
| C-1 | A move naming one empty end and one occupied end moves the item to the empty end, for every pair of lists served, in both orders | `Whichever_end_holds_the_item_is_the_end_that_moves` walks every cross-list served pair, seeding the second-named end; `The_end_named_first_still_moves_when_it_is_the_one_holding_the_item` walks the same pairs the other way. The pairs are derived by probing the dispatch, not written out. Mutation M1 (single-ended read restored in all three paths) reddened 8 cases and nothing else in the suite. | yes |
| C-2 | Two empty ends still refused, still as "both slots empty" | `Neither_end_holding_anything_is_still_refused_as_both_slots_empty`, over the same derived pairs, asserting the status and an item identifier of zero. | yes |
| C-3 | Every rule that decided whether a move was allowed still decides it, for the end that actually holds the item | `A_reversed_take_off_is_refused_by_the_bag_rule_the_forward_one_obeys`, `A_reversed_put_on_is_refused_by_the_slot_rule_the_forward_one_obeys` and `A_reversed_carry_across_is_refused_by_the_list_rule_the_forward_one_obeys` each assert the reversed request and the forward one are refused alike — one per cross-list path. `A_template_the_archive_does_not_know_is_refused_on_the_reversed_path` walks `NotWearable` from the reversed side. Structurally, the reversal delegates to the path that already owns the rules rather than restating them. | yes |
| C-4 | The acknowledgement describes the move that happened | `The_acknowledgement_for_a_reversed_move_names_the_two_cells_the_request_did` asserts the response's four fields and its move value against the store after a reversed take-off. Matches upstream's reverse branch (`InventoryMoveService.cs:139-140`, read from source) and the record already in `0x0013.md`. | yes |
| C-5 | Every production refusal and admission path added, removed or altered is listed forward and reverse, from the code | `behavior-change-list.md`: 3 forward transitions, 7 reverse entries, an unguarded mirror hazard, 6 deliberate non-changes, 3 properties, 5 mutations, 1 rewritten existing check and 2 record corrections plus 2 findings deliberately left. An independent reviewer scanned the code itself and found the list **over**-reported four unreachable statuses — corrected — and found no omission. | yes |
| C-6 | A move's log line names list and slot at both ends, worded as ordinary operational logging | `The_line_a_move_leaves_names_the_list_at_both_ends` pins the rendered success line; the two cases in `RefusedEquipIsRecordedWithItsTemplateTests` pin the refusal line. Mutation M3 (list numbers removed) reddened exactly those three. The wording carries no diagnostic markers: `from list 0 slot 12 to list 3 slot 3`. The owner's own criterion is applied in place of their acceptance — see below. | yes |
| C-7 | No protocol frame added, no frame's bytes changed | `SelectCharacterAcceptedTranscriptTests` passes and its file is not in the diff. The production diff contains no occurrence of `ClientMessageType` or `ServerMessageType` (grep count 0), so neither enum gained a member. The codec, decoder and encoder are untouched. | yes |
| C-8 | The suite is no weaker than its baseline | 2375 / 2 / 0, exit code 0, against a baseline of 2361 / 2 / 0. | yes |
| C-9 | Records this change falsifies are corrected | Search set `Docs/`, `Document/`, `protocol/`, `AGENTS.md` for `BothSlotsEmpty`, for `MOVE_ITEMSPACE`/`MoveItemSpace`, and for the log line's own text — all three run and their output read. Two corrections: `AGENTS.md`'s measuring-instrument passage, and `0x0013.md`'s claim that taking off was implemented and unsampled. Two further findings left alone with stated reasons. | yes |

Also held: `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` pass and
neither file is in the diff, so no predicate or exemption list moved. The build carries
`TreatWarningsAsErrors` and produced 0 warnings and 0 errors. Layering is untouched — the only
production file changed is in `Dfo.Server.Protocol`.

## Not verified here

**C-6's second half rests on the owner's criterion, not on the owner's acceptance.** The
condition asks that the line read as ordinary operational logging rather than as instrumentation
left behind by a diagnosis. The owner stated the criterion in their own terms —
「别有特殊的监控语义…不是一眼就是排障的log就行」— and it is that criterion, not a stand-in
judgement, that is applied here: the line reads

```
Move item space <connection> character=1: item 413560000 from list 3 slot 1 to list 1 slot 0 count=1
```

with no `asked=`, no `iv=`, and no field whose name only means something to whoever was debugging.
The diagnostic wording used during the investigation was deliberately not carried over.

**This is the one condition the owner can overturn in a word**, and it is put in front of them with
the line quoted rather than described. If they want it worded differently, that is a text change to
two format strings and three pinned expectations.

**Nothing here is live evidence.** The three symptoms that started this work are the owner's to
re-test. This Spec's part of that is taking a costume off; the other two symptoms belong to the
sibling Specs and are expected to remain until those land.
