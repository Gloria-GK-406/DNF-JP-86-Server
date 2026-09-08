# Behaviour change list

Arrived at by reading the complete production diff line by line
(`git diff -- Server/src`, 98 changed lines in one file) and then walking the three cross-list
paths, the dispatch, and the two mapping tables that stand behind them. It is not derived from the
Spec's headings, and it is not a check of the Spec's own claims.

## What was read

- `MoveItemSpaceCommandHandler.HandleAsync` -- the dispatch, for which pairs reach which path.
- `PutOnAsync`, `TakeOffAsync`, `CarryAcrossAsync` -- each in full, before and after.
- `Reversed` -- what it carries and what it drops.
- `ItemMoveStatus`, `ItemMoveResult.Refused` (both overloads), `StatusFor`, `ErrorCodeFor`.
- `MoveItemSpaceResponse.Success` / `.Failure`, and `MoveItemSpacePayloadCodec.Encode`.
- `MoveItemSpaceHandler.ResolveAsync`, both log lines.
- Every construction site of `MoveItemSpaceCommand` (`grep -rn "new MoveItemSpaceCommand"
  Server/src Server/tests --include=*.cs`: one in production, five in checks -- all read).

## Forward: requests that used to be refused and now proceed

Each of these was `BothSlotsEmpty` with an item identifier of zero, and the client was shown
nothing (code `0x02`).

| # | Condition | Now |
|---|---|---|
| F-1 | Source list is Main or Avatar, destination is Equipment, the source slot is empty and the worn slot holds a piece | Handed to `TakeOffAsync` with the two ends exchanged: the worn piece goes to the named carried slot. Reaches `Moved`, `NotWearable` or `ItemCannotOccupySlot`. |
| F-2 | Source list is Equipment, destination is Main or Avatar, the worn slot is empty and the carried slot holds a piece | Handed to `PutOnAsync` with the ends exchanged: the carried piece is worn. Reaches `Moved`, `NotWearable` or `ItemCannotOccupySlot`. |
| F-3 | Source and destination are the two different carried lists, the source slot is empty and the destination slot holds a piece | Handed back to `CarryAcrossAsync` with the ends and the containers exchanged. Reaches `Moved`, `NotWearable` or `ItemCannotOccupySlot`. |

**Three refusals belong to those paths and are still unreachable through them.**
`DisplacedCannotOccupySlot` needs the handed-to path's destination slot to hold something, and on a
delegated call that slot **is** the original source slot, which was just found empty.
`NoBoundContainer` is decided in the delegating path's own prologue, before it ever reads a slot, so
a delegated call cannot be the first to notice an unbound container. `BothSlotsEmpty` needs both
ends empty, and one of them was just found occupied. This is the same fact the properties section
below states; an earlier draft of this table listed all three as reachable and was wrong.

The six ordered pairs those cover are Main-to-Equipment, Avatar-to-Equipment,
Equipment-to-Main, Equipment-to-Avatar, Main-to-Avatar and Avatar-to-Main. The two same-list pairs
already behaved this way inside `StoreBackedItemContainer.MoveToAsync` and are untouched.

## Reverse: refusals that are new, changed, or newly reachable

| # | What changed | Consequence |
|---|---|---|
| R-1 | **`ItemMoveStatus.MoveAlreadyMade` is a new refusal.** Reachable from one place only: `PutOnAsync`, source slot empty, destination is Equipment and occupied, and the request carries a source instance value greater than zero equal to the worn piece's identifier. | The request used to be `BothSlotsEmpty`; it is now `MoveAlreadyMade`. **Both map to `Silent` (`0x02`), so the client is shown exactly what it was shown before.** What changes is the word in the log and the fact that F-1 does not apply to it -- without this the worn piece would have been taken off. |
| R-2 | **Refusals that used to be silent are now sentences.** In every F-1/F-2/F-3 case where the reversed move is then refused, the status is `NotWearable` or `ItemCannotOccupySlot`, both of which `ErrorCodeFor` maps to `NotMovable` (`23`). The same request used to be `BothSlotsEmpty` and therefore `Silent` (`2`). | **The player now sees 「无法移动物品」 where the drag used to spring back with no message.** This is a real, user-visible change and the largest one in this work. |
| R-3 | **The bag's own band rules are newly reachable from the other side.** A reversed carry-across or take-off lands a piece in a slot named as the request's *source*. Main slots 3..8 are the quick-slot band, and `ItemSlotRuleService` turns five kinds away from it; slots 65..120 are the consumable band and take no equipment. Those rules are asked, unchanged -- `MayOccupySlotAsync` is called with the direction the item is actually travelling. Nothing is widened, but a slot that could previously only be reached by naming it as the destination can now be reached by naming it as the source. | Guarded, and asserted: `A_reversed_take_off_is_refused_by_the_bag_rule_the_forward_one_obeys`. |
| R-4 | **The worn-slot rule is newly reachable from the other side**, by the same argument: a reversed put-on asks `WornSlotForAsync` for the piece at the destination end and refuses if it does not name the request's source slot. | Guarded, and asserted: `A_reversed_put_on_is_refused_by_the_slot_rule_the_forward_one_obeys`. |
| R-5 | **`ErrorCodeFor` gained one case.** No existing mapping changed. The enum is still listed exhaustively rather than defaulted, so the check that walks it still holds. | None beyond R-1. |
| R-6 | **`MoveItemSpaceCommand` gained `SourceInstanceValue`, defaulted to zero.** Every existing construction site compiles and behaves unchanged: one in production (`ResolveAsync`, which now passes the request's own value) and five in checks, all of which omit it and therefore cannot reach R-1. | None. |
| R-7 | **Both log lines changed shape**, from `{From}->{To}` to `from list {FromList} slot {From} to list {ToList} slot {To}`. | Server-side record only; nothing on the wire. `AGENTS.md` recorded the old shape as a measuring instrument and is corrected. |

## The mirror of R-1, which is not guarded

A client restating a completed **take-off** -- `Equipment:<now empty> -> Main:<now occupied>`, with
the identity riding on the occupied end so the source instance value is zero -- is reversed by F-2
and quietly puts the piece back on. R-1 does not catch it, because R-1 is keyed on the destination
being the equipment list.

Upstream does not guard it either: `IsEquipmentClientEchoWhenSourceMissing` tests
`request.DestinationListType == InventoryListType.Equipment` and nothing else (read from source).
So this work matches the reference implementation rather than going past it, and the transition is
disclosed above as F-2 rather than hidden.

**It is left this way on precedent, not on a demonstration that it is safe.** Nothing observed here
shows the client restating a take-off, and nothing here shows it does not. If a costume is seen
going back on by itself after being taken off, this is the first place to look.

## Deliberately not changed

| # | What | Why |
|---|---|---|
| N-1 | The acknowledgement. `MoveItemSpaceResponse.Success` still echoes the request's source list and slot, its destination list, and takes the destination slot from the request. | This is already what a reversed move needs. Upstream sets its result's destination back to the request's destination on the reverse branch (`InventoryMoveService.cs:139-140`, read from source), and `protocol/game/client-to-server/0x0013.md` already recorded 「ACK 无论走哪个分支都原样回请求自己的 src/dst」. Asserted against the store in `The_acknowledgement_for_a_reversed_move_names_the_two_cells_the_request_did`. |
| N-2 | The wire encoder, the decoder, the minimum body length, and every message type. | No frame is added and no frame's bytes change. `SelectCharacterAcceptedTranscriptTests` passes with no expected value edited, and no member was added to `ClientMessageType` or `ServerMessageType` -- the production diff touches neither. |
| N-3 | `ListNotServed`, decided from the two list numbers before either slot is read. Equipment-to-Equipment and every unserved list still land there. | Reversal only applies inside a pair the dispatch already serves. Asserted in `An_unserved_list_is_still_refused_without_reading_a_slot`. |
| N-4 | `NoBoundContainer`, still decided before either end is read, in each of the three paths' unchanged prologues. | The reversal was placed after those prologues on purpose, so no refusal changed the order it is decided in. |
| N-5 | `MoveCount`. `Reversed` carries it through untouched, and the three cross-list paths move one piece regardless, as they did before. | Nothing about counts is in this work. |
| N-6 | Upstream's `IsEmptyTitleNameClientEcho` (`InventoryMoveService.cs:908-917`, read from source), which turns a Main-to-title-slot request with **both** ends empty into a silent success rather than a refusal. | It only fires where this work says the answer is `BothSlotsEmpty`, it is a title-slot client quirk, and nothing observed here depends on it. Adopting it would be a separate decision about titles. Recorded in the Spec as `NA-003`. |

## Properties that have to hold, and why they do

- **Reversal happens at most once per request.** Each path reverses only after finding its own end
  empty *and* the other end occupied, and the path it hands to reads that same occupied slot as its
  source. There is no write between the two reads, so the second read cannot be null and the
  handed-to path goes forward. Depth is one hop.
- **The identity does not travel.** `Reversed` sets `SourceInstanceValue` to zero, so R-1 cannot
  fire on a delegated call. It belongs to the end the client named as the source, and a reversed
  request names none of its own.
- **The displaced branch is unreachable on a reversed call.** Both `TakeOffAsync` and
  `CarryAcrossAsync` read their destination slot to see what is displaced; on a reversed call that
  is the request's original source slot, which was just found empty. So a reversed move is always a
  move into an empty cell, never a swap.

## Mutation evidence

Backed up with `sha256sum` and restored and re-hashed after each. The file's hash before and after
every mutation was `c9b1338e25d4c09bf9c61edcff647c182a63727f4fddb1cb88f96ca727a8f005`.

| Mutation | Reddened |
|---|---|
| M1 -- put the single-ended read back in all three paths | Exactly 8 cases, every one of them in `EitherEndMayBeHoldingItTests`, and **nothing else in the whole suite**. That is the measurement of how much coverage these paths had before: none. The other new cases stayed green, correctly -- they assert what this work preserves. Run before the two cases added after review, against the 12 that existed then. |
| M2 -- drop only the R-1 guard | Exactly 1 case, `A_request_naming_the_worn_piece_on_its_empty_end_does_not_take_it_off`. |
| M3 -- take the list numbers back out of both log lines | Exactly 3 cases: the two in `RefusedEquipIsRecordedWithItsTemplateTests` and `The_line_a_move_leaves_names_the_list_at_both_ends`. |

Two further mutations were run by the independent reviewer, on the R-1 guard: widening
`SourceInstanceValue > 0` to `>= 0` reddened nothing, because the identity equality already excludes
zero for any real template; removing the identity equality reddened exactly one case. The first says
the "greater than zero" clause is upstream's wording carried over rather than load-bearing here; the
second says the guard's narrowness is pinned by a check rather than only asserted in prose.

## Existing checks rewritten, and which change each corresponds to

One file, two assertions, both corresponding to R-7 and to nothing else:
`RefusedEquipIsRecordedWithItsTemplateTests.An_avatar_aimed_at_the_wrong_costume_slot_is_recorded_with_its_template_and_that_slot`
and `.A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot`. Each pins the
rendered log line word for word, so naming the lists changes the expected string. The slot numbers,
template numbers, status word, error code and every other assertion in that file are unchanged --
the diff for it is 8 lines, all inside those two strings.

## Records corrected

The search set was `Docs/`, `Document/`, `protocol/`, `AGENTS.md` for `BothSlotsEmpty`, for
`MOVE_ITEMSPACE`/`MoveItemSpace`, and for the log line's own text `Move item space`. All three were
run and their output read.

- `AGENTS.md` -- the passage that keeps the refusal log as a measuring instrument quoted the old
  line shape. Corrected, with a note that the two list numbers were added on 2026-09-08 and why.
- `protocol/game/client-to-server/0x0013.md` -- 「**脱下来是同一 type 两个列表对调**,本仓已实现,
  但**没有抓包样本**」 was false in both halves: there is a sample now, and "已实现" held only for
  the direction the client does not send when taking something off. Corrected in the A21 section,
  with the live frame and with the upstream echo guard named.

Two things were found and deliberately left alone:

- The same file's a12 region says 「仅 list 0（主背包）。其余容器一律回 `0x02`」, which the avatar
  list being bound already made stale before this work. It sits inside `<!-- rks:a12:begin -->`,
  which the migration rule says is replaced wholesale when A21 is reworked, and it is not this
  change's falsification to repair.
- The same file's A21 section says an equip acknowledgement has 「**后面什么都不跟** —— 没有外观
  更新,没有属性刷新」, read off an upstream capture. Whether this server should send more after a
  move is root defect B and belongs to its own Spec; this work sends nothing new, so the sentence
  is not touched here.
