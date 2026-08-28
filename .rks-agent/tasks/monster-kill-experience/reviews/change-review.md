---
kind: rks-review
decision: remediated
---

# Review: killing a monster is worth experience

One independent reviewer, reading the complete change against the TaskContract and spec. Fifteen
findings, two blocking. All fifteen dispositioned; the reviewer withdrew one of its own after
checking it.

## What the reviewer verified independently

The four captured awards reproduce, checked three ways rather than by running the tests alone: by
running them, by recomputing from the documented operator order in a separate implementation with
IEEE-754 single widening, and by reading every live input straight out of `Script.pvf` with its own
probe. It also measured what would break the agreement, which is the more useful half:

| Change | Result |
|---|---|
| One rounding at the end instead of two | 63 / 73 / 126 / 147 -- wrong on three of four |
| Level gap folded inside the first rounding | the same three wrong |
| Round-half-up instead of floor | 64 / 74 / 127 / 148 -- wrong on four of four |
| Decimal literals instead of the archive's singles | unchanged |

The last row corrected an overstatement of ours: a remark claimed the single-precision reading was
load-bearing for these awards, and it is not -- 56.55 and 56.5499998 floor alike. The rounding count
and its order are what carry them. The remark now says so.

## Findings and what was done about them

| # | Finding | Verdict | Disposition |
|---|---|---|---|
| 1 | The whole-number form of `[experience increasing point]` was dropped, and a comment asserted the tag is never written that way | **BLOCKING**, confirmed | **Fixed.** Both forms are now read. The counts first written here were wrong in two ways and N2 removed them; what stands is the dungeons checked token by token -- `port.dgn` and `mainbridge.dgn` at 30, `circus_new.dgn` at 15, `circus.dgn` at 35 -- each of which was paying that fraction of its worth |
| 2 | `worth / 2.0` is a reference-server number with no marking, unlike its neighbours | **BLOCKING**, confirmed | **Fixed.** Named `MonsterWorthDivisor` and marked as the one number no evidence can check: halving the worth and doubling the worth table give the same four awards, so the captures constrain the product and say nothing about either factor. The consequence for a reader -- that `MonsterExperienceWorth` is not "what a kill pays" -- is written where it will be met |
| 3 | A doc comment in the changed file still said "the kill awards nothing" | CONFIRMED | **Fixed.** It now says what the frame carries, and keeps the honest part: the bonus lines are still zero, and one of them is now known to be real |
| 4 | The `ExperienceWeight` doc block sat inside the record body and the compiler merged it into `LevelOf` | CONFIRMED | **Fixed.** Moved to a `param` on the record. The repository has documentation generation off, so no warning would ever have shown it |
| 5 | The token-form reasoning was false: difficulty rates are all singles, party rates all integers -- not a mix within one line | CONFIRMED | **Fixed** in both the source and its test. The code was right; the reason given for it was not, and the failure it described cannot happen |
| 6 | An AI companion's death would be billed as a monster: same actor list, same sequence space, rank falls back to 1 | CONFIRMED, reachable | **Fixed.** Ranks above boss are refused with a line in the log. The tutorial's escort is level 80 and would have paid for itself |
| 7 | The quest-completion path had no test at all, before or after the refactor | CONFIRMED | **Fixed.** Five tests over the real handler, real log, real store: what it pays, that a second completion adds to what the first left, that an award that cannot happen is a refusal, that an unmet objective pays nothing, and that the price follows the stored level |
| 8 | Two refusal paths were lost in the refactor; the worse one reports level 1 with no experience | CONFIRMED, unreachable live | **Fixed.** `AwardAsync` now distinguishes "no award happened" from "an award of nothing", and both refusals are back |
| 9 | `ExperienceAward.Nothing(1, 0)` invented a standing the caller would send to the client | CONFIRMED | **Fixed** by the same change as 8 |
| 10 | An entitlement's rounding was not saturating, unlike the rule's beside it | CONFIRMED | **Fixed.** The rate is an operator's number and can be anything; an unchecked cast of an oversized double lands on zero on x64, turning a misconfigured huge bonus into none |
| 11 | "Eight bands" and "eight intervals" -- the switch has twelve arms carrying eight distinct rates | CONFIRMED | **Corrected** |
| 12 | The rounding claim was attached to the wrong condition: all four intermediates are fractional, but only three answers move | CONFIRMED | **Corrected**, and now names the champion level-4 sample as the one that agrees either way -- reading the rule off that sample alone would make the order look like a free choice |
| 13 | The award ran before the missing-mirror check, so that path spent the death, wrote the row, and left the session holding the old total with no frame to correct it | CONFIRMED | **Fixed.** The mirror is asked before anything is consumed |
| 14 | `[monster exp bonusrate]` was the one live input nothing pinned | CONFIRMED | **Fixed.** Pinned; a change to it would have moved all four awards silently |
| 15 | Entry-batch comparison thought to be outside the suite | **Withdrawn by the reviewer.** `SelectCharacterAcceptedTranscriptTests` pins every entry frame by SHA-256 against the reference session and runs in the suite | No action; SC-8's evidence stands |

## What the reviewer checked and found clean

Repeat death reports cannot pay twice -- `TryReportActorDead` is the single guard, keyed by cell and
sequence, and a repeat now short-circuits everything downstream of it. The boss-room clear still
fires exactly once. No other caller grants experience. The DI lifetimes are consistent and the
ambient transaction reaches the singleton handlers. The read-write window is narrower than the
quest path's used to be. What a quest pays, when it persists and what it reports are unchanged on
the normal path, argument for argument. The wire is untouched: the encoders are byte-identical and
the only frame added on a kill is the offer list, and only when a level was crossed. The evidence
marking on the rank rates, the level-gap bands and the entitlement policy is honest.

## Second pass

The same reviewer read the remediation. **No blocking findings.** Seven non-blocking, every one
constructible, and three of them were false statements written during the remediation itself.

| # | Finding | Disposition |
|---|---|---|
| N1 | One of the three regression pins for the whole-number weight landed on the wrong branch: `bakalcastle.dgn` declares 4 as a *single*, so that case passes with the fix reverted | **Fixed.** Replaced with `port.dgn` (30) and `circus_new.dgn` (15), both verified token by token to be genuine integers. A pin on the wrong branch says nothing about the fix it was written for |
| N2 | Three survey numbers that disagreed (52 / 46 / 43), and one value in the wrong column: nothing writes 2 as an integer -- those 19 dungeons write `2f`, which already worked | **Fixed.** The counts are gone. What is left is the dungeons actually checked by hand and the fraction each was paying, which is better evidence than a number nobody can re-derive |
| N3 | A test stub named `PvfBackedQuestParameters` whose doc claimed archive backing; it is invented and agrees with the archive only at level 1 | **Fixed.** Renamed `InventedQuestParameters` and the remark says where it agrees and where it does not. The 101 it produces is right and is checked against the archive elsewhere |
| N4 | The handler promises a refusal unwinds the completion. It does not: a refusal is a normal return, so the unit of work commits with the quest cleared and the items given | **Corrected.** The remark now says which three paths reach that state, that none is reachable from a real client, and that making them unwind is a change to what every refused command does. The promise was already false at baseline for the bag-full path; the remediation had added a fourth path behind it |
| N5 | `ExperienceAward.Unknown` was dead API surface | **Removed.** The distinction it documented is stated on `Nothing`, which is the member that needs it |
| N6 | The companion guard was right but unpinned, and actor type 4 is classified three ways across the repository | **Fixed.** The decision is now a pure predicate with tests, including one that pins the disputed type 4 to the reading that pays nothing -- so flipping it is a deliberate act |
| N7 | A weight declared zero was rejected as invalid and silently promoted to 1, so two dungeons that say they pay nothing paid in full | **Fixed.** Zero is a value; only an absent tag takes the default |

The reviewer also withdrew its own dungeon 247 example after checking: that dungeon declares 1, not
4. The finding stood; the illustration did not.

### What the second pass re-checked clean

Every caller of `AwardAsync` enumerated rather than sampled: two production callers, neither
dereferencing, neither reading nothing-awarded as a zero award. The reordering of the mirror check
was judged a strict improvement -- that path now consumes nothing and writes nothing, which is what
the baseline did. The repeat-report guarantee and the once-only boss clear survive it. The new
quest tests were confirmed to drive the real handler over a real store, and the 101 they assert
confirmed against the archive independently. The shared test double by linked file was confirmed
sound and an existing convention in the same project.

## After remediation

- Build: 0 warnings, 0 errors.
- Suite: **1481 passed, 2 skipped, 0 failed** (1463 at the first review; eighteen tests added).
- No unresolved blocking finding.
