# WireSmoke

Drives this server's entry and quest-acceptance path over a real socket and records what came
back — the frames and the rows, not a pass/fail.

The unit suite never touches the wire. A change can keep 1300 tests green and still send the
client different bytes, because what the tests assert is what the codecs produce in isolation,
not what a running host puts on a socket after the pipeline, the stores and the transaction
have all had their say. This is the cheap check for that: about ninety seconds, no GUI, no
real client.

## Running it

```sh
Tool/WireSmoke/capture.sh <label> [outdir]
```

Needs a built host (`dotnet build Server/86JP.Server.slnx`) and the A21 PVF archive; set
`PVF_ARCHIVE_PATH` if it is not at `D:/project/DNF/DNFClient-A21-protocol/Script.pvf`. The
account, character name and job can be overridden with `SMOKE_ACCOUNT`, `SMOKE_PWHASH`,
`SMOKE_CHARACTER`, `SMOKE_JOB`.

It boots the host twice from a wiped database — once to record an entry batch on a character
that has accepted nothing, once to accept two quests from a cold start — and writes five files
into `outdir` (default `./capture-<label>`). Working files go to a temp directory and are
removed; nothing is written next to the script.

**It wipes `Server/src/Dfo.Server.Host/bin/Debug/net10.0/Data/dfo-server.db`.** Do not run it
against a database anyone cares about, and not while a real client is connected — it binds
7001 and 10011.

## Reading the result

Two ways, and they answer different questions.

**Against the reference session.** `entry-compare-<label>.txt` lines our entry batch up beside
the upstream A21 capture. The standing result is *66 frames against 66, same protocols in the
same order, 60 byte for byte identical*; the six that differ are the six places a swordman is
not the reference's 魔枪士, and they are itemised in
[`a21-entry-batch-diff-20260825`](../../protocol/patterns/data/traces/a21-entry-batch-diff-20260825.md).
A seventh differing frame is a finding.

`quest-accept-<label>.txt` ends in `matches the reference` or `FAILED`, and the script's exit
code follows it.

**Against your own before-run.** Capture once before a change and once after, then diff the two
archives. This is the stronger check — it holds every character-dependent value fixed, so
anything that moves is the change.

### What moves anyway

Five places vary between two runs of the *same* build, so a diff is not clean even when nothing
changed:

| Frame | Where | What it is |
|---|---|---|
| `#0 NOTI 0x0001` | body 21..22 | a per-session value, repeated in the select answer |
| `#10 CMD 0x0004` | body 5..6 | the same value |
| `#22 NOTI 0x0002` | body 123, 183, 243 | three item instance values, four bytes each |

Everything else is stable run to run — the two archives that closed the quest-log refactor were
identical outside these. A difference anywhere else is a real change, not noise.

The rows are stable in full: `character_active_quests` comes back as `(1, 0, 1016, 1)` and
`(1, 1, 101, 1)`, and entry alone leaves none.

## The pieces

| File | What it does |
|---|---|
| `capture.sh` | the harness: boots, drives, archives, cleans up |
| `wireclient.py` | creates the character the entry sequence needs, by asking the server to |
| `entryprobe.py` | logs in, selects, and dumps every frame; also the `Conn` the others use |
| `questprobe.py` | accepts two quests and checks the answers against the reference server's |
| `cmpentry2.py` | lines an entry batch up against the reference capture |
| `questrows.py` | prints a character's quest rows in a database-independent order |
| `framediff.py` | byte-level diff of two frame dumps |

`cmpentry2.py` names `packet_log.reference-20260823.txt` rather than the upstream output
directory's `packet_log.txt`, deliberately: that default was overwritten on 2026-08-27 by a
newer run, and every protocol page here cites the 08-23 session. Comparing against the wrong
one reports six extra differing frames that are nothing but the other session's character.
