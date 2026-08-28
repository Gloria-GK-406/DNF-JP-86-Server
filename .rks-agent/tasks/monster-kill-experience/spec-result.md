---
kind: rks-spec-result
outcome: completed
---

# Result: killing a monster is worth experience

A kill in a dungeon now pays. What it pays is derived from the monster, the run and the character;
what the character carries is added on top as named contributions; and the granting -- adding,
crossing level thresholds, persisting -- is one implementation that the quest-completion path was
moved onto rather than a second copy.

## What is delivered

- `MonsterExperienceRules`: the scaling a kill goes through, as pure functions over content --
  difficulty, party size, the dungeon's weight, the monster's rank, the level gap. Two roundings in
  a specific order, because one rounding answers three of the four captured awards wrongly.
- `IExperienceBonusService` and `ExperienceBonusRules`: one place that knows which of a character's
  things affect experience, reporting each as a named contribution rather than folding them into a
  multiplier. Two sources today -- the account's premium entitlements, and the channel's own rate.
- `ICharacterExperienceGrant`: the one place experience is added. The character's standing is read
  and written in the same breath, so no caller can price against a stale copy -- which is the
  specific defect it replaces.
- `GrantKillExperienceCommand`: the three steps in order, inside the transaction that writes the row.
- The kill path in `MonsterDeathSlice`, which reports the death, awards, corrects the session, and
  refreshes what the character may take when a level was crossed.

## What was found on the way

Three things that were not the task and were fixed because the task ran into them:

- The dungeon weight `[experience increasing point]` is written as a whole number by some dungeons
  and was being dropped, so those dungeons paid a fraction of their worth.
- An AI companion's death arrives looking exactly like a kill and would have been billed as one --
  the tutorial's escort is level 80.
- The client's experience notice never appeared, in any chat tab. It turned out not to be this
  server's doing at all: the client's struct-shaped config reads were calling the profile-struct
  **writer**, so its chat filter was being overwritten with 154 zero bytes on every start and every
  message type was switched off. Fixed in the client tooling, recorded there, and confirmed by the
  notice appearing.

## What is left, and recorded

- `ExperienceBonusKind.Channel` still carries the reference server's name for the slot at `+0x4B`.
  The client draws it as `赛丽亚的欢迎`. `Docs/ExperienceNoticeSlots.md` has all ten names the client
  gave, and which slots are safe to declare into.
- Clear-settlement experience, party play, the named-monster tier, fatigue, and timed effects from
  elixirs are all out of scope here and named in the TaskContract.
- Whether a kill pays half of what its level is worth is the one number no evidence in hand can
  check. It is marked where it is written, and the reference server has since changed its own mind
  about it -- which is confirmation that the marking was right, not that the number is settled.

## Verification

`verification/completion-evidence.md`: every success condition has current evidence, decision
`pass`. Build 0 warnings, 0 errors; suite 1492 passed, 2 skipped, 0 failed.
