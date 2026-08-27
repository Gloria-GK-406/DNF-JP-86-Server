---
kind: rks-spec-result
outcome: completed
---

# Result: spec-003 world entry sequence

The A21 client selects a character and stays in the game world. The entry batch this server sends
is the reference session's batch: 66 frames against 66, same protocols in the same order, 60 of
them byte for byte identical, and the six that are not are the six places where a swordman is not
a 魔枪士.

## What was built

The three USERINFO entry subtypes (`0x00`, `0x01`, `0x06`) and `ENTER_GAMEWORLD_COMPLETE` were laid
out from measured bytes, the nine frames this server was not sending were sent and the six it was
sending in excess were withdrawn, and the conclusions went into `protocol/` as they were reached.

## How it closed, and why that is worth saying

This attempt was reviewed and approved and then **sat unsealed while the branch went on working**.
Sixteen commits landed after it -- the tutorial dungeon, the exit path, the spec profile, quest
acceptance, the quest log -- and several of them changed the frames this attempt is judged on. The
review had left two findings whose remedy was "re-sample on the final build"; the final build was
no longer the one that had been reviewed.

Sealing it therefore meant re-taking the evidence on the current head rather than reconstructing
what was true weeks ago. That is what `verification/completion-evidence.md` records, and it says so
in its own words. The alternative -- sealing on the reviewed build's numbers -- would have recorded
a state no commit on this branch has ever been in.

Two things surfaced while doing it:

- **The reference capture had been overwritten.** `packet_log.txt` was replaced on 2026-08-27 by a
  newer upstream run. Every page on this branch cites the 08-23 session, which survives under its
  own name; the comparator archived here names that file rather than the directory's default, and
  the trace page now says why.
- **The archived comparison was stale.** `a21-entry-batch-diff-20260825.md` recorded our side as
  167 / 25 / 330 bytes for the three subtypes, from before the starting-gear and skill-page fix. It
  now records the current 190 / 70 / 519 and keeps the old numbers with the reason they moved.

## Evidence

- `reviews/change-review.md` — two rounds, both by independent readers; approved.
- `verification/completion-evidence.md` — seven of seven, taken on the head.
- `verification/entry-batch-compare-20260827.txt` — the 66-frame comparison.
- `verification/cmpentry2.py` and `verification/entry-batch-20260827.txt` — how to take it again.

Build 0/0. Suite 1304 passed, 2 skipped, 0 failed.
