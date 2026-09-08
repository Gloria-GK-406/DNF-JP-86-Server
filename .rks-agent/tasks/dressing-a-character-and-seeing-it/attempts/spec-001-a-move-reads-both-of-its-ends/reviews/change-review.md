---
kind: rks-review
verdict: approved
---

# Change review: spec-001 "A move reads both of its ends"

Scope `change`, gate `auto`, fresh reviewer. Reviewed against the root TaskContract (SC-1..SC-10),
the child TaskContract revision 1 (C-1..C-9), `spec.md` (RQ-001..RQ-009 / AC-001..AC-011), and the
complete change between `8247182c6e555fd22204490272b6d2ada8008508` and the uncommitted worktree
state on branch `dressing-a-character-and-seeing-it`. No adversarial challenge set was supplied, so
there is no Challenge Dispositions section.

Nothing in this review was taken from the executor's own account. Every number below was re-measured
or re-derived here.

## What I verified independently

### Suite

`tasklist | grep -i Dfo.Server` empty before the run.
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
returned exit code 0 with **2373 passed / 2 skipped / 0 failed**, summed from the seven per-assembly
lines the run printed: Networking 52, Protocol 597, Pvf 60, Host 6, Persistence 329, Application
1070, Infrastructure 259+2 skipped. Baseline was 2361/2/0, so +12, all of them in Protocol.Tests
(585 -> 597), which is the 12 cases in the new file. No flaky failure occurred. **C-8 / AC-011 met.**

### The dispatch's served pairs, derived from the code rather than from the check

Read `MoveItemSpaceCommandHandler.HandleAsync` directly. `ListNotServed` is returned from exactly one
place, the `source is null || destination is null` guard, and `CarriedListFor` admits only Main(0)
and Avatar(1). So the pairs that do **not** land on `ListNotServed` are exactly
{Main,Avatar} x {Main,Avatar} (4) + {Main,Avatar} x Equipment (2) + Equipment x {Main,Avatar} (2)
= **8 ordered pairs**. That is the same 8 the check asserts, arrived at without reading the check.

`The_dispatch_serves_these_pairs_of_lists_and_no_others` is **not circular**. It derives the served
set by probing every ordered pair of the 16 `InventoryContainerType` members with both ends empty and
keeping the non-`ListNotServed` answers, then compares that derived set against a literal 8-element
expectation. The derivation and the expectation are independent, and the sibling helper
`CrossListPairsAsync` feeds the same derivation to four other cases, so a dispatch change breaks the
literal assertion rather than silently shrinking the walk. The probe's discriminator is sound: no
served pair can answer `ListNotServed` (the three paths never return it) and no unserved pair can
avoid it (shown above). `Build()` registers all three containers, so `NoBoundContainer` cannot
produce a false positive. **RQ-008 / AC-002 satisfied.**

I walked all six cross-list pairs by hand through the new code (seeded second end, `SlotIn` giving
Main=12, Avatar=0, Equipment=3, costume `413500000` = `[coat avatar]` worn at 3) and confirmed each
reaches `Moved` with the item at the first-named end. **C-1 / RQ-001 / AC-001 met.**

### Recursion boundary — independently verified, no counterexample found

The claim "at most one reversal per request" holds, and the argument is stronger than the executor
stated. For each of the three delegations the handed-to path re-reads the *same slot* the delegating
path had just found occupied:

- `PutOnAsync` -> `TakeOffAsync(Reversed(c))`: `TakeOffAsync` reads `worn[c.DestinationSlotIndex]`,
  which is literally the `alreadyWorn` the caller just read non-null.
- `TakeOffAsync` -> `PutOnAsync(Reversed(c))`: reads `bag[c.DestinationSlotIndex]` = `carried`.
- `CarryAcrossAsync` -> `CarryAcrossAsync(Reversed(c), into, from, from.Type)`: reads
  `into[c.DestinationSlotIndex]` = `other`.

No write happens between the two reads. I checked the container implementation for the "second read
returns null" counterexample the task asked about: `StoreBackedItemContainer.ReadSlotAsync`
(`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:335`) is a plain
`session.ReadAsync(id, slotIndex)` inside the command's own transactional boundary, with no
invalidation, no cache and no cursor. Depth is one hop for every served pair. The container-argument
threading in the `CarryAcrossAsync` self-call (`into, from, from.Type`) is also correct.

### `Reversed` zeroing `SourceInstanceValue`

Correct, but the stated reason is vacuous rather than wrong: the `MoveAlreadyMade` guard lives only
in `PutOnAsync`'s `piece is null` branch, and on any delegated `PutOnAsync` call that branch is
unreachable, because the delegating `TakeOffAsync` reached it only after reading that same bag slot
non-null. So the guard could not have misfired on a delegated call whether or not the field were
zeroed. Not zeroing it would therefore be equally correct today; zeroing it is the safer statement of
intent and matches the field's documented meaning ("the identity belonged to the end the client named
as the source"). It has no other consumer — `SourceInstanceValue` is read in exactly one place in the
whole repository. No other consequence found.

### `MoveAlreadyMade` — new enum member, exhaustiveness, and the command's new parameter

- `StatusFor` maps `ItemMoveRefusal` -> `ItemMoveStatus`, so a new *status* cannot affect it. Read
  and confirmed.
- `ErrorCodeFor` gained the arm; the discard arm still throws.
- Two checks walk the enum: `ItemRefusalCodeTests.Every_move_refusal_is_answered_with_a_code:87` and
  `.No_move_refusal_borrows_the_sentence_about_space:133`, both `Enum.GetValues<ItemMoveStatus>()`
  with only `Moved`/`Unchanged` skipped. Both cover the new member automatically and both pass.
  Exhaustiveness is preserved.
- `MoveItemSpaceCommand`'s new `int SourceInstanceValue = 0`: I re-ran the grep myself. Construction
  sites are **6, namely** `MoveItemSpaceSlice.cs:701` (production, now passing
  `message.SourceInstanceValue`), `CreatureGoesBackOnTests.cs:215`,
  `MoveItemSpaceHandlerTests.cs:340`, `ItemPathFrameTests.cs:54`, `ItemPathFrameTests.cs:82`,
  `TheAvatarBagIsServedTests.cs:380`. The five check sites all pass six positional arguments and get
  0, so none can reach the guard. The decoder always reads the field (body >= 14 bytes guarantees
  offsets 3..6). Confirmed by reading each site.

### Error-code changes — R-2 confirmed, and it is the complete set

I enumerated this from the code, not from the list. The diff only adds an `else` on `piece is null` /
`moving is null`; every path with an occupied first-named end is byte-for-byte the code it was. So
the only requests whose answer can change are cross-list requests with an empty first-named end,
which previously all answered `BothSlotsEmpty` -> `Silent` (2). After the change they answer:

- no error code at all (a success frame), or
- `Silent` (2) — both ends empty, or `MoveAlreadyMade`, or
- `NotMovable` (23) — via `NotWearable` or `ItemCannotOccupySlot`.

`MergeExceedsStackLimit` (17) and code 4 are unreachable from a cross-list path, `ListNotServed` is
decided before any of this, and `NoBoundContainer` is decided in the unchanged prologues. So **R-2 is
real and it is the only error-code change in this work.** I found no second one of the same kind.

### Existing assertions

`RefusedEquipIsRecordedWithItsTemplateTests` is the only existing check touched: 2 lines removed, 6
added, all six inside the two expected log strings. Read line by line — `BagSlot`,
`NamedAvatarSlot`, `WrongSlotForACoat`, `CoatAvatarTemplate`, `OrdinaryCoatTemplate`, the status word
`ItemCannotOccupySlot` and the `MoveItemSpaceResponse.NotMovable` assertion are **unchanged to the
character**. Nothing was weakened; the rewrite corresponds to RQ-007 and to nothing else.
`SelectCharacterAcceptedTranscriptTests`, `CommandHandlerArchitectureTests` and
`NotificationHandlerArchitectureTests` are untouched (verified by path-scoped `git diff --stat`
returning empty), and `git status` shows exactly the four modified files. **C-7 / AC-009 met.**

### Mutations (my own runs, file-level backup + sha256, restored and re-hashed each time)

Baseline hash of `MoveItemSpaceSlice.cs`: `51260486722f03475119b23a54da66a9171b492ab8047879557b3e2f9f8408fa`
(binary-mode `sha256sum`; the executor recorded a different digest, which I take to be a different
hashing mode rather than a different file — my own before/after digests match exactly). No git
restoring command was used.

| # | Mutation | Result |
|---|---|---|
| A | The executor's M2 — delete the `MoveAlreadyMade` guard block | **Exactly 1 failure in the whole suite**, `A_request_naming_the_worn_piece_on_its_empty_end_does_not_take_it_off`. M2 reproduced. |
| B | mine — widen the guard's `SourceInstanceValue > 0` to `>= 0` | **0 failures.** The mutation is semantically inert: the identity equality still gates the branch and `0 != 413500000`. This shows the `> 0` clause is redundant in this codebase (see observations). |
| C | mine — drop the `alreadyWorn.ItemId == command.SourceInstanceValue` clause, keeping `> 0` | **Exactly 1 failure**, `A_request_naming_some_other_piece_on_its_empty_end_takes_the_piece_off`. The guard's narrowness is genuinely pinned by a check, not merely asserted in prose. |

Hash re-verified as the baseline value after each restore; final `git status` is the original four
modified files plus the two untracked paths.

### RQ-006 / `MoveAlreadyMade` — my judgment: it belongs in this round

I read the upstream original at
`E:/Project/DNF/ServerS4A21/Server/DfoServer/Game/Inventory/Services/InventoryMoveService.cs`
(read-only, not written to). `IsEquipmentClientEchoWhenSourceMissing` is:

```
request.SourceInstanceValue > 0
&& request.DestinationListType == InventoryListType.Equipment
&& equipmentItem.ItemId == request.SourceInstanceValue
```

This repository's guard is a **condition-for-condition port**: it sits in `PutOnAsync`, which
`HandleAsync` enters only when the destination list is Equipment, so the middle clause is structural;
the other two are verbatim. Upstream also places it at the same point in its reverse branch, after
`reverseSource` is found non-null. This is not an invention.

On the three questions asked:

- **Out of scope?** No. It does not repair a fourth defect; it contains a hazard that the authorized
  repair itself creates. Without it, `RQ-001` reads a restated equip as "put the worn thing back in
  this empty cell" and silently undoes the move the player just made — a regression introduced by the
  authorized change. Both contracts explicitly require the reverse consequences of this change to be
  enumerated and answered (root Scope/In, C-3, C-5), which is what this is.
- **Narrow enough?** Yes. The branch is only reached when the named source slot is empty; it then
  requires the request to assert a positive identity on that empty end *equal to* the item at the
  other end. The live take-off that motivated the whole work (`1:0->3:1`, `srcIV=0`,
  `dstIV=413560000`) does not trigger it, and mutation C shows the equality is load-bearing and
  checked.
- **Can it block a legitimate move?** Not one whose source end holds anything — the branch is
  unreachable then. A legitimate request naming an identity on an *empty* end contradicts the
  protocol's own observed convention (the identity rides with the item, which is precisely what
  `srcIV=0 / dstIV=template` records). Its client code is `Silent` (2), the same code the request got
  before, so even a false trigger is invisible to the player and loses only the move.

It rests on upstream source plus an argument, not on a live echo observed here, and both `AS-002` and
`R-1` say so. That is honest labelling of a defensive port, not an over-claim.

### Records

I re-ran the declared search set myself over `Docs/`, `Document/`, `protocol/`, `AGENTS.md` for
`BothSlotsEmpty`, `MOVE_ITEMSPACE|MoveItemSpace`, the literal `Move item space`, the old log shape
`{From}->{To}`, and additionally `TakeOffAsync|PutOnAsync|CarryAcrossAsync|ItemMoveStatus|SourceInstanceValue`,
and read the output. Findings:

- `AGENTS.md:702` — the quoted log shape. Corrected, and correctly. I checked the neighbouring
  claims the edit sits inside and they survive: item 2's "`TakeOffAsync`'s five refusal returns,
  three of which carry a template" is still exactly true after the change (the delegation is not a
  sixth refusal), and item 1's `PutOnAsync` entry condition is unchanged.
- `protocol/game/client-to-server/0x0013.md` — both halves of the falsified sentence corrected. The
  frame quoted (`源列表 1 / 源槽 0 / srcIV 0 / 目标列表 3 / 目标槽 1 / dstIV 413560000`) matches
  `EV-001` and matches what `PutOnAsync` now handles by reversal.
- The two deliberate non-changes hold up. `0x0013.md:172` (「仅 list 0…其余容器一律回 `0x02`」) is at
  line 172, between the `rks:a12:begin` marker at 63 and `rks:a12:end` at 186 — I checked the marker
  positions — so the wholesale-replacement migration rule genuinely applies, and the sentence was
  already false at baseline (`ListNotServed` has mapped to 23, not 2, since before this change), so
  it is not this change's falsification. The 「后面什么都不跟」 sentence describes an upstream
  capture and this work sends nothing new, so it is untouched correctly.
- Nothing else in the search output is falsified by this change. `Docs/CreatureDomainDesign.md:231,236`
  describes `PutOnAsync`/`TakeOffAsync` mechanics that this change does not alter.

**C-9 / SC-10 met**: search set stated, executed by me as well as by the executor, output read,
corrections listed.

### Change list completeness (C-5 / SC-3 / AC-010) — scanned from the code

I built my own account of the change's reverse surface from the diff and the three paths before
checking it against `behavior-change-list.md`. The list's F-1..F-3 and R-1..R-7 cover everything I
found; I found **no missed production refusal or admission path**. The specific under-reporting
pattern this repository has failed on before — a new refusal reachable only on a path no check walks
— did not recur: the three new reverse-reachable refusal classes are `MoveAlreadyMade`,
`NotWearable`, and `ItemCannotOccupySlot`, and the latter two each have a dedicated reversed-refusal
check pinned against its forward twin.

### Architecture and structure

Layering unchanged (the change is confined to `Dfo.Server.Protocol`, adds no dependency). Slice
architecture intact: one directory, no cross-protocol reference, no wiring change, and the three
reversals are private method calls inside one command handler, not nested command dispatch. Build is
clean under `TreatWarningsAsErrors` with CS9113 as an error. No protocol frame added, no message type
registered, no frame's bytes changed.

## Verdict

**approved.** C-1 through C-9 are met, and the contributed root conditions SC-1, SC-2, SC-3, SC-7,
SC-8, SC-9, SC-10 are met for this Spec's share of them. No blocking defect found.

## Non-blocking observations

None of these blocks acceptance. They are recorded so a later reader is not misled.

1. **`behavior-change-list.md` over-reports two statuses.** F-1 lists `DisplacedCannotOccupySlot` and
   `NoBoundContainer` as reachable from the reversed put-on, and F-2 lists `NoBoundContainer`; F-3
   lists `DisplacedCannotOccupySlot`. None of the four is reachable. On any delegated call the
   handed-to path's destination slot **is** the original source slot, which was just found empty, so
   `displaced` is always null; and the equipment container was already found non-null by the
   delegating path. The document's own "Properties that have to hold" section states this correctly,
   so the table contradicts the prose two sections later. This is over-reporting, not the
   under-reporting SC-3 exists to catch, but the table should be trimmed to what the code can
   actually produce.

2. **The mirror of the RQ-006 hazard is unguarded and undiscussed.** A client restating a completed
   *take-off* — `Equipment:<now empty> -> Main:<now occupied>`, identity riding on the occupied end so
   `srcIV=0` — is reversed by F-2 and silently puts the piece back on. Upstream does not guard this
   either (its guard is `DestinationListType == Equipment` only), so matching upstream is defensible,
   and the transition is disclosed as F-2. But the change list argues the echo hazard only in the
   equip direction; a reader should know the take-off direction was left to upstream's precedent
   rather than shown to be safe.

3. **The log line does not distinguish a forward move from a reversed one.** Both lines name
   `message.SourceListType/SourceSlotIndex` and `message.DestinationListType/DestinationSlotIndex`,
   i.e. the request's ends, so a reversed take-off logs "from list 0 slot 12 to list 3 slot 3" for a
   move that went 3:3 -> 0:12. C-6 asks only that both lists and both slots be named, and matching the
   acknowledgement (RQ-005) is a coherent choice — `item {ItemId}` does let a reader tell, since for a
   reversed move it names the piece that was at the destination end. Still, the line's stated purpose
   was to stop a pair of numbers belonging to several paths, and it does not yet say which direction
   was carried out.

4. **The guard's `SourceInstanceValue > 0` clause is redundant here** (mutation B: widening it to
   `>= 0` reddens nothing, because the identity equality already excludes 0 for any real item id). It
   is upstream's clause verbatim and harmless; the test file's remark that "the three assertions
   together are what makes this narrow" credits it with work the equality is doing.

5. **Two small check-coverage thinnesses**, neither demanded by AC-003's stated minimum: the reversed
   *carry-across* has no dedicated refusal case (only its success path is walked by
   `Whichever_end_holds_the_item_is_the_end_that_moves`), and `NotWearable` is not exercised on any
   reversed path. Both rules are asked through the same `MayOccupySlotAsync` / `WornSlotForAsync`
   calls the forward paths use — reuse by delegation rather than restatement is what makes RQ-002
   structurally true — so drift is unlikely rather than impossible.

6. **`An_unserved_list_is_still_refused_without_reading_a_slot`** proves the refusal and that the
   store is unchanged; it cannot observe whether a slot was read. The name claims slightly more than
   the assertions establish.

7. **`0x0013.md`'s 「所以实际上一次也没成功过」** generalizes from one captured frame. It is
   supportable — the owner reported that none of eleven costumes could be taken off, and a take-off
   sent the forward way would have succeeded, so none was sent forward — but the sentence does not
   mark which half is the capture and which is the inference, where the surrounding document is
   careful to.
