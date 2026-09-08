---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: A move reads both of its ends

## Delivered

- A move between two different lists is now carried out whenever either of the two ends it names
  holds something, whichever end that is. The rule the within-one-list move already applied is
  applied by the three cross-list paths — `PutOnAsync`, `TakeOffAsync`, `CarryAcrossAsync` — by each
  handing the reversed request to the path that already owns its rules, so no admission rule was
  restated to make it work. Taking a costume off, which the client sends with the item on the second
  end and which was refused every time, now happens.
- Two empty ends are still refused as `BothSlotsEmpty`, and every request whose first-named end
  holds something behaves exactly as before, including the order its refusals are decided in.
- One new refusal, `ItemMoveStatus.MoveAlreadyMade`: a client restating a move it has already been
  acknowledged for, which the reversal rule would otherwise read as "take the worn piece back off".
  It shows the player what `BothSlotsEmpty` showed them — nothing.
- A move's log line names the list as well as the slot at both ends. It named neither list before,
  which is what made this defect take three live rounds to place.
- `behavior-change-list.md`, forward and reverse, arrived at from the code.
- Two repository records corrected: `AGENTS.md`'s measuring-instrument passage and
  `protocol/game/client-to-server/0x0013.md`'s claim that taking off was implemented and unsampled.

## Verification

- `verification/completion-evidence.md` — decision `pass`, 2375 passed / 2 skipped / 0 failed,
  exit code 0, against a baseline of 2361 / 2 / 0 in the same worktree.

## Repository Result

- Changed paths: `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`
  (the only production file), `Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs`,
  `Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/EitherEndMayBeHoldingItTests.cs` (new),
  `AGENTS.md`, `protocol/game/client-to-server/0x0013.md`, and this attempt's artifacts.
- Baseline or accepted result: `8247182c6e555fd22204490272b6d2ada8008508`
- Parent rollback required: no

## Successor Guidance

- **The acknowledgement echoes the request's own two ends, in both directions.** A reversed take-off
  is acknowledged as the request wrote it, which is what upstream does and what
  `0x0013.md` already recorded. A sibling Spec that adds frames after a move must not "correct" the
  acknowledgement to describe the real direction.
- **The log line records the two cells the request named, not the direction the item travelled.**
  `{ItemId}` is what tells a reader which way it went. Recorded in `AGENTS.md` for whoever next uses
  that line as a measuring instrument.
- **The mirror of the new echo guard is unguarded**, deliberately and on upstream's precedent, not on
  a demonstration that it is safe: a client restating a completed *take-off* would be reversed and
  put the piece back on. Written up in `behavior-change-list.md`. If a costume is seen going back on
  by itself, that is the first place to look.
- **More moves now succeed, so a sibling Spec that sends something after a successful move will see
  cases this one made reachable** — in particular the take-off direction, which before this could
  not be exercised live at all.
- **Refusals on a reversed move are now sentences where they used to be silence.** A reversed move
  that a rule turns away gives the player 「无法移动物品」 rather than a drag that springs back.
- The orphan row noticed while diagnosing is still there and still unexplained: `item_slots` main
  slot 3 names `core_uid 8474`, for which `items` holds no row. `0x0013.md` records `8474` as the
  tutorial potion's item id, so the two may be the same number meaning two things; nobody has
  checked.

## Artifacts

- `spec.md`, `behavior-change-list.md`, `reviews/change-review.md` (independent, verdict `approved`,
  seven non-blocking observations, all seven addressed).
