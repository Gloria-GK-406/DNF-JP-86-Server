---
kind: rks-verification
decision: pass
---

# Verification: spec-003 world entry sequence

**Claim under test.** The seven success conditions of this attempt's TaskContract (revision 1) hold.

**Two things about when this was taken.** The review closed with two findings that named the
verification stage as their remedy -- the archived byte comparisons were sampled from a build older
than the one being reviewed -- and verification then did not run. It runs now, so the evidence
below is taken on **the current branch head**, not on the build that was under review. Sixteen
further commits landed on this branch in between, several of them changing the very frames this
attempt is judged on; a re-sample was the only honest way to close it, and re-sampling on the head
is what happened.

## Which reference session

`packet_log.txt` in the upstream server's output directory **was overwritten on 2026-08-27** by a
newer tutorial run. Every protocol page and trace on this branch cites the 2026-08-23 session,
which survives as `packet_log.reference-20260823.txt`. The comparison below is against that file;
`cmpentry2.py`, archived beside it, names it explicitly for exactly this reason. A comparison run
against the current `packet_log.txt` reports six extra differing frames that are nothing but the
other session's character.

## Condition by condition

| SC | Evidence |
|---|---|
| SC-1 — the client enters the world and the connection survives | The player drove a real client through the whole tutorial dungeon and out again on 2026-08-27; the exit path was then proven end to end offline (`0x02EF → 0x00C2 → 0x045C → 0x00FA → 0x0117 → EXIT`, the last answered byte-identically to upstream) in `protocol/patterns/data/traces/a21-exit-and-return-20260827.md`. `clienthack-faults.log` from that session holds five **first-chance** records and no fatal one; the session ended because the player asked it to |
| SC-2 — the three USERINFO entry subtypes match the known-good session | `entry-batch-compare-20260827.txt`: 66 frames against 66, same protocol and same order throughout, **60 byte for byte identical**. Subtype 1 (`#12`, 519 bytes) is identical in length and differs only in character data; subtypes 0 (`#1`) and the tail one (`#63`) differ by exactly two bytes each, which is this character's name being one UTF-16 unit longer than the reference character's |
| SC-3 — `ENTER_GAMEWORLD_COMPLETE` matches | `#54 NOTI 0x007C`, 8 bytes, byte for byte identical -- it is among the 60 |
| SC-4 — every missing and extra send is corrected or has a recorded reason | The batch now has no missing and no extra sends at all: the protocol multiset and order are equal across all 66 frames. What remains recorded rather than corrected is not in this batch -- it is the twenty-two entries of `Docs/ProtocolScaffoldDissonances.md`, each with its own criterion for closing |
| SC-5 — data differences were told apart from layout differences | The six differing frames are each accounted for by character data: `#0` the select answer (identity and timestamp), `#1` and `#63` the name length, `#10 SKILLINFO` identical in length with the reference character's job skill ids, `#12` identical in length, `#27 NOTI 0x006C` a pre-existing difference recorded in the dissonance list. None of the six was "fixed" by changing a layout |
| SC-6 — the conclusions are in `protocol/` and the catalog | The per-protocol pages, `catalog.md` and the traces under `protocol/patterns/data/traces/` carry them; `a21-entry-batch-diff-20260825.md` has been refreshed to this build and now records which reference file it means |
| SC-7 — build and tests | On the current head: build 0 warnings 0 errors; `dotnet test 86JP.Server.slnx -m:1` → **1304 passed, 2 skipped, 0 failed** |

## What this does not claim

The reference character is a 魔枪士 (job 13) and ours a swordman (job 0), so "identical" above means
identical wherever the two characters are the same, and the six exceptions are exactly where they
are not. This is not a proof that a job-13 character would produce the reference's bytes; nobody
has run one.

## Verified conditions: 7 of 7
