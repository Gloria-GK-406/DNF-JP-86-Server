# Behaviour change list

Arrived at by reading the complete production diff (`git diff -- Server/src` against `e766d971`)
and then walking the handler, the predicate it now asks, and every caller of the two constructors
that changed. Not derived from the Spec's headings.

## What was read

- `MoveItemSpaceHandler` in full, before and after: `HandleMessageAsync`, `ResolveAsync`, the two new
  members, the constructor.
- `StateSynchronizationPayloadCodec.EncodeCurrentUserState` and the two filters inside it.
- `CurrentUserStateMessage` and its other construction site.
- Every construction site of `MoveItemSpaceHandler`
  (`grep -rn "new MoveItemSpaceHandler" Server/src Server/tests --include=*.cs`: none in
  `Server/src` — it is resolved by the container — and three in checks, all read and all updated).

## Forward: what leaves the server that did not before

| # | Condition | Now |
|---|---|---|
| F-1 | A move that ended `Moved`, where either end named the equipment list at a slot subtype zero draws from (`0..12`, `23`, or the creature slot `24`) | A second frame follows the acknowledgement in the same delivery: `CurrentUserStateMessage`, NOTI `0x0002` subtype 0, carrying the character's post-move equipment. The acknowledgement is still first. |

That is the whole of it. Nothing else is sent, nothing is sent in any other case, and nothing that
was sent before has stopped.

## Reverse: what is deliberately not sent

| # | Case | Answer |
|---|---|---|
| R-1 | A move that ended `Unchanged` — the request named one slot twice | Acknowledgement alone. `Unchanged` is not `Moved`, and nothing about the character altered. |
| R-2 | Any refusal | The refusal alone. Asserted. |
| R-3 | A successful move naming the equipment list at a slot subtype zero does not draw from (`13..22`, and `25` upward) | Acknowledgement alone. Asserted with slot 22. |
| R-4 | A successful move inside the bag, inside the avatar page, or between them | Acknowledgement alone. Asserted for both cross-list directions. |
| R-5 | A successful appearance move made when the session cannot be read — no published session, or no runtime state for it | Acknowledgement alone. A frame drawn from a session that is not there would describe nobody, and the move stands either way — it was committed before this was asked, and the acknowledgement carrying it goes out whether or not this answers. (An earlier draft of this row said the move "has already been acknowledged", which is the same wrong claim about ordering that R-7 was about; it was corrected in the code and missed here.) This is a **new silent path**, reachable only in a race with disconnection. |
| R-6 | The session's own copy of the inventory | **Not replaced.** Every other reader of it — `DungeonSelectionSlice.cs:419-421` takes its equipment from there — draws exactly what it drew before. The copy stays stale, as it already was; this frame is built beside it. Asserted by counting replacements. |
| R-7 | A successful appearance move where working out the appearance raises — the store is read after the transaction has closed, and both the read and `InventoryEntryProjection.ProjectCharacter` can raise | Acknowledgement alone, and the failure logged at error. **This path was found by the independent review and was missing from the first draft of this list**, in the exact shape this repository keeps missing: a newly reachable path that a green suite cannot see. Before the guard, a raise here took the acknowledgement with it — and the client holds the inventory shut until it is answered, which is the symptom this whole work exists to remove. Asserted, and mutation M3 (guard removed) reddens exactly that one case. |

## Things that changed shape without changing behaviour

| # | What | Consequence |
|---|---|---|
| N-1 | `MoveItemSpaceHandler`'s constructor gained `ISelectedSessionRuntimeDirectory` and `ICharacterInventoryEntryDomainService`. | Both are registered already. No production construction site exists to update — the container resolves it. Three check fixtures construct it directly and were given doubles; none of their assertions moved. |
| N-2 | `ResolveAsync` returns the move's result beside the response. | Private; the response it returns is byte-for-byte what it returned before. |
| N-3 | `StateSynchronizationPayloadCodec` gained `CurrentUserStateDrawsFrom` and two named constants for the numbers its appearance filter already used. | The filter's expression is unchanged in meaning: `<= 12 || == 23` before and after. The byte anchor is untouched, which is the check that would notice if it were not. |
| N-4 | The `MoveItemSpace` slice now references the `StateSynchronization` slice. | Following the precedent already in the tree: `DungeonSelectionSlice` does the same for `ExtendedUserStateMessage`. The state frames are shared material, not another endpoint's protocol. |

## Cost

One inventory read per move that changed the character's look, and none for any other move. An
ordinary drag inside the bag pays nothing. The read is
`ICharacterInventoryEntryDomainService.LoadAsync`, which loads the character's whole inventory; only
its equipment container is used.

## Mutation evidence

File-level backup, restored and re-hashed after each, with no git restoring command used. M1 and M2
were run against the pre-review file and its hash was
`853f05f5234a4a608c0e293c0d51d31050a7525fd1a476c44878ad0a4c7b31db` before and after each; M3 was run
after the review's blocking finding was fixed, against
`1ffca317688157e7f6b4903e46a58789f035421a7f1799b191894b80298db45f`, the same before and after.

| Mutation | Reddened |
|---|---|
| M1 — send the acknowledgement alone again | Exactly 3 cases, all in `TheCharacterChangesAsYouDressItTests`, and nothing else in the suite. |
| M2 — keep the load but build the frame from the session's copy | The same 3, including `The_frame_follows_the_store_and_not_the_session_copy`, which is the case that exists to catch exactly this and would be the only one to fail if the other two agreed by accident. |
| M3 — remove the guard that keeps a failed appearance from costing the acknowledgement | Exactly 1 case, `A_move_is_still_acknowledged_when_the_appearance_cannot_be_built`, and nothing else. |
| (reviewer's own) — make the trigger predicate always true | The three "acknowledgement alone" cases, with the acknowledgement showing no error code and a move value of one — so those moves really do succeed rather than passing because they were refused. |

The cases that stayed green under M1 and M2 are the ones asserting what is *not* sent, which is
correct: neither mutation makes the server send more.

## Existing checks changed, and why

Three fixtures construct `MoveItemSpaceHandler` directly and had to be handed the two new
dependencies (N-1). Each was given a session directory that has nothing published and an inventory
loader that raises if reached — so those cases take the path where no frame is built. **No expected
value, assertion or case in any of the three changed**, but the reason they still hold has changed
underneath them: before this work there was nothing to build, and now there is something that is
deliberately not built because nothing is published. `EitherEndMayBeHoldingItTests`'s
`Assert.Single(sender.Sent)` is the assertion that would have caught a frame appearing there, and it
holds because of the fixture's choice rather than because the handler cannot send one. The files are
`RefusedEquipIsRecordedWithItsTemplateTests`, `EitherEndMayBeHoldingItTests` (both
`Dfo.Server.Protocol.Tests`) and `MoveItemSpaceHandlerTests` (`Dfo.Server.Application.Tests`).

## Records corrected

Search set: `protocol/`, `Docs/`, `AGENTS.md` for claims about what follows a move and about when
subtype zero is sent — three searches, run and read.

Two records say a move's acknowledgement has 「后面什么都不跟」. **Read against their own subject,
neither is falsified**, and neither was rewritten as though it were: both describe an ordinary
shoulder going to equipment slot 15, which subtype zero does not draw from, so nothing followed it
then and nothing follows it now. What was added to each is the qualifier a reader needs — which
slots the frame is drawn from, and that a costume slot is answered differently now.

- `protocol/game/client-to-server/0x0013.md` — the A21 section's sentence, annotated.
- `protocol/patterns/data/traces/a21-tutorial-ending-20260826.md` — the same sentence about this
  server's own answer, annotated with a pointer.

Nothing else in the search output claimed anything this work falsifies.

## Known and left

- **Slot 24 is over-served.** The predicate answers true for the creature slot, but subtype zero
  only draws a creature there when the item's kind is 5, so a non-creature at 24 sends a frame that
  says nothing new. Harmless — a redundant frame, never a missing one — and narrowing it would mean
  a second, finer copy of the encoder's own condition.
- **A sealed artifact was touched and put back.** `spec-001`'s `routing-decision.md` was committed
  with CRLF line endings and then normalised to LF along with the rest of this task's artifacts,
  which showed as a 124-line rewrite of an accepted result. It has been restored to the bytes
  `e766d971` holds. That one file is CRLF where the others are LF; a sealed result staying as it was
  accepted matters more than the inconsistency.

- **The three slot filters in this repository still disagree**: subtype zero draws appearance from
  `0..12` and `23`, a character is created with an appearance blob filtered to `<= 13`, and the
  costume bar is `0..11`. Upstream's is different again rather than wider — neither contains the
other: it takes `0..13` plus `24`, `25` and `29`, suppressing `12` and `13` while the character is
inside a dungeon, and it does not take `23`, which this server's subtype zero does. Deciding between them changes
  bytes and belongs with defect C, which is where the world's picture and the selection screen's
  picture have to be made to agree. This work asks the subtype-zero filter as it stands, and adds no
  fourth one.
- **The session's inventory copy is stale after every move** and stays that way (R-6). It was stale
  before this work; what is new is that the appearance frame and that copy now visibly disagree.
