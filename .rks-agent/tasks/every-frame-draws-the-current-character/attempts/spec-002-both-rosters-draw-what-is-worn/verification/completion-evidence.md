# Completion Evidence: Both rosters draw what is worn

Verified in the worktree at
`E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character`, branch
`every-frame-draws-the-current-character`, on top of accepted baseline `77ff3236`.

## The run

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

**2401 passed / 2 skipped / 0 failed**, exit 0.

| Assembly | Passed | Skipped | Baseline at `77ff3236` |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 60 |
| Dfo.Server.Networking.Tests | 52 | 0 | 52 |
| Dfo.Server.Protocol.Tests | 615 | 0 | 612 |
| Dfo.Server.Host.Tests | 6 | 0 | 6 |
| Dfo.Server.Persistence.Tests | 333 | 0 | 333 |
| Dfo.Server.Infrastructure.Tests | 259 | 2 | 259 (+2) |
| Dfo.Server.Application.Tests | 1076 | 0 | 1076 |

Every assembly equals its baseline except Protocol, which is 612 + 3 -- the three cases this attempt added.
Output retained at `scratchpad/efdcc/spec002-final-verification.txt`; searching it for `[FAIL]`,
`error CS` and `MSB<digit>` returns 0 matches each, and it carries all 7 test-run banners, so every
assembly actually ran. No failure needed classifying.

## Condition by condition

| ID | Outcome | Evidence |
|---|---|---|
| C-1 | met | `SelectCharacterProductionStateTests.The_roster_frame_is_drawn_from_what_is_worn_and_not_from_the_stored_blob` -- the fixture's two sources disagree by construction (worn coat in slot 12, stored blob says slot 0), so it cannot pass against the previous code. Mutation M-A confirms: reverting the source reddens it with `Expected: 12, Actual: 0`. |
| C-2 | met | Mutation M-B (`DrawnAppearance.LastDrawnSlot` 12 -> 11) reddens 6 cases across all three sides that draw a character. Independently reproduced by the reviewer, who had shown the earlier version of the entry-side case stayed green under the same mutation. |
| C-3 | met | All 62 anchor hashes unchanged, and the reason established rather than assumed: the frame is not in the anchor (`AccountRosterStateMessage` is a `SupplementalSynchronizationMessage` built outside the encoded factory), and the anchor encodes a hand-built result anyway. See `behavior-change-list.md` W-2. |
| C-4 | met | `behavior-change-list.md` D-1 and D-2 -- the column kept because retiring it needs a save migration the contract excludes; the decoder kept as part of one unretired unit, with the reader enumeration and its counting method recorded. |
| C-5 | met | F-1: entry preparation gains one account-scoped read on a path that already performs `1 + N` loads against the same store, so it gains no failure mode it lacked; the presence check is untouched and still uses the cheap listing. |
| C-6 | met | `behavior-change-list.md`, three forward and three reverse, with the walk's method and frontiers stated. R-1 -- the removed refusals for a corrupt blob -- is invisible in the diff and was found by walking down from the removed call. |
| C-7 | met | Four records corrected, each naming what falsified it, including one this attempt falsified twice. |
| C-8 | met | The run above. |

## Boundaries held

- No new protocol frame, no new encoder or `ProtocolModule` registration, no change to any frame's field
  widths or order.
- No existing assertion rewritten. Three expectation moves are recorded as T-1, T-2 and T-3, each naming
  the authorized change it corresponds to; T-3's method body was diffed against `git show HEAD:` by the
  reviewer and is identical.
- `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` pass; layering and slice
  architecture unchanged.
- No git restoring command used. Both mutations used file-level backups with `sha256sum` restoration,
  re-hashed after each.

## Review

`reviews/change-review.md`, `verdict: approved`. Two rounds of `changes_required` preceded it, carrying six
blocking findings: a convergence check that did not converge, a correction that repeated the false premise
it was correcting, a falsified claim left in a method name, a decision recorded for only half its subject,
and a count that was wrong twice. The last was found in the reviewer's own suggestion, which it retracted.
