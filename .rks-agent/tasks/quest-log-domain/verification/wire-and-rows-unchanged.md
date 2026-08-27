# What the player saw before and after

Same procedure throughout (the harness now kept as [`Tool/WireSmoke`](../../../../Tool/WireSmoke/README.md)), same archive, same account and
character, database wiped before each run. Four captures exist:

| Label | Build | Why it was taken |
|---|---|---|
| `before` | `589adf34`, nothing changed yet | the baseline |
| `after` | the refactor, before review | the first comparison |
| `after-control` | the same build as `after`, run again | to say what a rerun changes on its own |
| `reviewed` | the refactor with the review fixes applied | **the build being accepted** |

The control is what makes the comparison mean anything. Two runs of one build are never
byte-identical: the server stamps the time into the greeting and the roster answer, and mints a
fresh instance id for every item it creates. Without a control those differences would have to be
argued about; with one, they can be measured.

## Accepting quests: identical

`quest-accept-before.txt` and `quest-accept-reviewed.txt` are the same file, byte for byte, and so
are the rows they left:

```text
character_active_quests: (1, 0, 1016, 1)  (1, 1, 101, 1)
```

Both answered `matches the reference`, which is `questprobe.py` asserting that the ack for quest
1016 is `01 f8 03 01 00 00 00 00`, that `NOTI 0x0015` follows both accepts, and that a second
attempt at 1016 is refused with `00 12`.

`quest-rows-after-entry-*.txt` are identical too: entry alone still leaves no quest rows.

## Entering the world: identical except where a rerun differs anyway

`framediff.py` compares the two 76-frame logs frame by frame; the full output is in
`entry-framediff.txt`.

```text
before vs reviewed                   after vs after-control (one build, two runs)
every frame same shape               every frame same shape
#0  NOTI 0x0001  21+2                #0  NOTI 0x0001  21+1
#10 CMD  0x0004  5+2                 #10 CMD  0x0004  5+1
#22 NOTI 0x0002  123+4,183+4,243+4   #22 NOTI 0x0002  123+4,183+4,243+4
3 of 76 frames differ                3 of 76 frames differ
```

The same three frames at the same offsets, in both comparisons. Frame 0 offset 21 and frame 10
offset 5 are the server timestamp; frame 22 offsets 123, 183 and 243 are the three item instance
ids the character's creation minted. The run lengths read `+2` in one column and `+1` in the other
only because the timestamp's second byte happened to roll over in one pair and not the other --
the same field either way.

**73 of 76 frames are byte for byte identical, and the 3 that are not carry nothing but per-run
values.** The two quest-related frames of the entry batch -- `#18 NOTI 0x0015` (25B, the acceptable
quest list) and `#69 NOTI 0x0016` (9B) -- are among the 73.

`entry-compare-*.txt` sets the batch beside a reference session and is identical across all four
runs, which is all it is relied on for here. It is worth saying which session, because the answer
turned out to be the wrong one: `cmpentry.py` names the upstream output directory's `packet_log.txt`
by path, and that file was overwritten on 2026-08-27 by a newer upstream run. So these four files
compare against a session no page on this branch cites. Being identical across all four, they still
show what they are used for -- that nothing drifted between runs -- and the comparison against the
session the branch does cite lives in
`../../a21-world-entry-protocol/attempts/spec-003-world-entry-sequence/verification/`, taken with a
comparator that names the file rather than the directory.

## What this does not cover

The entry-time progress correction (RQ-016) never runs in these captures: it fires only for a quest
whose definition declares seeking items and whose stored progress disagrees with the bag, and the
character here accepts its quests after entering rather than before. That path is proven by test,
not on the wire -- `SelectCharacterEntryLifecycleServiceTests` drives it and asserts the slot, the
guard, and the corrected value. Saying otherwise would be reading a wire capture for something it
never exercised.
