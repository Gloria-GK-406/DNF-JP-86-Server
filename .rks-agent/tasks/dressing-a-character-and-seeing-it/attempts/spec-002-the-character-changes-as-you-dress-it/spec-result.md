---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: The character changes as you dress it

## Delivered

- A move that succeeded and named the equipment list at a slot the character's drawn appearance
  depends on now carries a second frame behind its acknowledgement: NOTI `0x0002` subtype 0, which
  this repository already had and already sent once at entry. The player sees the character change
  as the costume goes on and as it comes off, without leaving the game.
- The appearance that frame reports is read back from the store after the move, not taken from the
  session's copy of the inventory — which is the copy made when the character was selected and which
  no move refreshes.
- A move that changed nothing the appearance is drawn from, a move that ended unchanged, and every
  refusal all answer exactly as they did before.
- A failure while working out the appearance costs the player nothing but the appearance: the
  acknowledgement goes out regardless, and the failure is logged at error.
- `behavior-change-list.md`, forward and reverse, with the path the independent review found named
  as such.
- Two protocol records annotated where a reader would otherwise generalise a true observation into a
  false rule.

## Verification

- `verification/completion-evidence.md` — decision `pass`, 2384 passed / 2 skipped / 0 failed,
  exit code 0, against a baseline of 2375 / 2 / 0.

## Repository Result

- Changed paths: `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`,
  `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs`,
  three check fixtures updated for the handler's new dependencies, one new check file, two protocol
  records, and this attempt's artifacts.
- Baseline or accepted result: `e766d97152a6147323d3279248858ffb1ef4918c`
- Parent rollback required: no

## Successor Guidance

- **The appearance projection is still only bytes.** `EncodeCurrentUserState` derives the appearance
  inside itself, which is why this work needed nothing extracted. Defect C needs the same projection
  as a value (`CharacterRosterAppearanceState[]`), and extracting it is that Spec's to do — with the
  subtype-zero encoder made to use it, so the world's picture and the selection screen's picture
  cannot drift apart.
- **Three slot filters in this repository still disagree**, and upstream's is a fourth that contains
  none of them: subtype zero draws from `0..12` and `23`; a character is created with a blob
  filtered to `<= 13`; the costume bar is `0..11`; upstream takes `0..13` plus `24`, `25` and `29`,
  suppressing `12` and `13` inside a dungeon. Defect C is where they have to be reconciled, and
  doing so changes bytes.
- **The session's inventory copy is stale after every move and stays that way.** It was stale before
  this work. What is new is that the appearance frame and that copy now visibly disagree, so a later
  reader must not assume the copy is current. `DungeonSelectionSlice.cs:419-421` is its other reader.
- **A real storage failure while building the appearance now looks like a character that did not
  change.** Logged at error with the connection id. If the owner reports the old symptom again,
  that log line is the first place to look.
- **Slot 24 is over-served**: the trigger answers true for the creature slot, but subtype zero only
  draws a creature there when the item's kind is 5, so a non-creature at 24 sends a frame saying
  nothing new. Harmless, and narrowing it would mean a second copy of the encoder's condition.

## Artifacts

- `spec.md`, `behavior-change-list.md`, `reviews/change-review.md` (independent, two rounds:
  `changes_required` on one blocking defect, then `approved`).
