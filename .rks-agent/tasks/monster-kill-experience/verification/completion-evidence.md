---
kind: rks-verification
decision: pass
---

# Verification: killing a monster is worth experience

## The claim

Killing a monster in a dungeon increases the character's experience by an amount the reference
capture can be checked against, levels the character when the total crosses a threshold, tells the
client the new standing, and does its granting through one implementation shared with quest
completion.

Evaluated against `TaskContract.md` revision 1 at `dc4a9116` plus the uncommitted change, build 0
warnings / 0 errors, suite **1481 passed, 2 skipped, 0 failed**.

## Condition by condition

| ID | Result | Evidence |
|---|---|---|
| SC-1 | **pass** | Live, 2026-08-28. The client drew `获得75(基础:72+赛丽亚的欢迎:3)点经验值` and the server logged `award=75 (base 72 + channel 3) -> level 3 at 3041`, with the total rising kill by kill. Automated beside it: `AnAwardRaisesTheStoredTotal` and `ASecondAwardAddsToWhatTheFirstLeftBehind` over a real store. |
| SC-2 | **pass** | `TheCapturedKillsAreWorthWhatWasCaptured` -- four rows, 62 / 72 / 126 / 146 exactly, with every live input pinned beside them. The review measured what breaks them: one rounding instead of two is wrong on three of four, round-half-up on four of four. |
| SC-3 | **pass** | The level half live: `玩家[32333]已达到Lv4` in the client's system window while the frame carried the new level, and automated over the store by `AnAwardPastAThresholdLevelsTheCharacter`, `AnAwardPastSeveralThresholdsCrossesThemAll`, `ATotalCannotRunPastTheLastThreshold`. The refresh half was the gap this verification found: nothing drove `QuestOfferRefresher` at all, so it is now covered by `QuestOfferRefresherTests` -- that the list is recomputed at the level handed in rather than the session's entry-time one, and that a session with no subject is answered with itself. The contract admits either a live session or automated checks; this is the automated route. **What is still not observed is the client redrawing the list**: the frame is sent and byte-pinned, but no session confirmed a newly-banded quest appearing without re-entry. |
| SC-4 | **pass** | `AwardAsync` has one implementation and exactly two production callers. `FinishQuestCommand` holds no `Advance`, no threshold loop, no `SetProgressAsync`. That the quest still pays what it did is `FinishQuestCommandHandlerTests`, five checks over the real handler, log and store. |
| SC-5 | **pass** | Live: the server log carries `Kill experience ... award=75 (base 72 + channel 3)`, the award broken into named parts. Pinned by `AnAwardCanSayWhereItCameFrom` and `ContributionsAreAddedAndCarriedBackNamed`. |
| SC-6 | **pass** | `ExperienceBonusRulesTests`, now 13 checks: a configured entitlement adds under its own name, an expired one adds nothing, an unconfigured server or type adds nothing, a row with no declared end is still held, two entitlements report two contributions, and the channel rate reproduces each captured award. |
| SC-7 | **pass** | Three markings where the values are written -- `MonsterWorthDivisor` (not independently checkable), `RankRates` (half evidenced), `LevelGapRate` (eight rates, one evidenced, naming the covered band). The premium rate is marked policy in both the rule and its tests. |
| SC-8 | **pass** | `SelectCharacterAcceptedTranscriptTests` pins every entry frame by SHA-256 against the reference session and passes. Full suite **1492 passed, 2 skipped, 0 failed**. |

## What landed after the review

The independent review covered the change as it stood on 2026-08-28 morning. Three things landed
after it and are covered by tests but not by that review:

- **the channel bonus** -- `ExperienceChannelPolicy`, paid at `floor(worth x 0.05)` and declared in
  the frame at `+0x4B`. Four captured awards, four agreements; 9 tests.
- **the label probe** -- `Experience:LabelProbe`, off by default, which pays a distinct amount into
  every bonus slot so the client names them itself. It is a measurement, and what it declares is
  invented; it raises only what is reported, never what is stored.
- **`Docs/ExperienceNoticeSlots.md`** -- what that probe found, over two runs.

The probe also corrected a name this task had taken from the reference server: `+0x4B` is drawn by
the client as `赛丽亚的欢迎`, not "channel bonus". The rate and the slot are right and live-confirmed;
the identifier `ExperienceBonusKind.Channel` still carries the reference server's word for it and is
recorded as the next change.

## Suite

Build 0 warnings, 0 errors. **1492 passed, 2 skipped, 0 failed.**

`Dfo.Server.Host.Tests` fails when this repository's own server is already listening on 7001 -- it
starts a real host on that port. That is a fixture collision, not a regression: with the port free
the four pass. Worth knowing before reading a red run.
