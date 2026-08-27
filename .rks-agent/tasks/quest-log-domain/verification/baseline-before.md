# The baseline this refactor must not move

Taken at commit `589adf34`, before any code changed. Reproduced by
`scratchpad/capture.sh <label>`, which boots the host twice from a wiped database:

- **run A** creates the character and selects it, recording the whole entry batch;
- **run B** starts cold again, creates the character, and accepts two quests.

Server: `Dfo.Server.Host.exe` on 7001/10011, `PVF_ARCHIVE_PATH` pointing at
`D:\project\DNF\DNFClient-A21-protocol\Script.pvf` (72894573 bytes, 2017-01-05).
Account `10038`, character `Nagisa`, job 0, roster index 0.

## What was recorded

| File | What it holds |
|---|---|
| `entry-batch-before.txt` | every frame of one entry, 76 logged, 66 of them S->C after `CMD 0x0004` |
| `entry-compare-before.txt` | that batch beside the reference session's |
| `quest-accept-before.txt` | the two accepts, the repeat, and the frames each drew |
| `quest-rows-after-entry-before.txt` | the quest rows entry alone leaves |
| `quest-rows-after-accept-before.txt` | the quest rows the two accepts leave |

## The numbers that decide RQ-017, RQ-018, RQ-019

- `questprobe.py` answered `matches the reference`: the ack for quest 1016 is
  `01 f8 03 01 00 00 00 00`, `NOTI 0x0015` follows both accepts, and a second attempt at
  1016 is refused with `00 12`.
- Rows after the accepts: `(1, 0, 1016, 1)` and `(1, 1, 101, 1)`. Entry alone leaves none.
- The entry batch's quest-related frames are `#18 NOTI 0x0015 (25B)` and `#69 NOTI 0x0016 (9B)`.

## How the after-run is judged

By diffing the archives against each other, not by re-reading the reference. The claim
under test is that this refactor changes nothing, so before-versus-after is the judge;
the reference comparison is what the earlier task already settled and is carried here
only so a drift against it would also show.

One note on `entry-compare-before.txt`: its reference column counts 55 frames against our
66 because `cmpentry.py` windows the reference log by line index rather than by S->C
position, so eleven C->S lines fall inside its window. That is a property of the
comparator, not a divergence, and it is identical in both runs.
