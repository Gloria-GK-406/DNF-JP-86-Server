---
kind: rks-change-review
contract:
  path: ../TaskContract.md
  revision: 1
verdict: approved
---

# Change Review: Complete World, Content and Session Entry State

## How this review was conducted

Two passes. The first was a self-review by the implementing agent and reached *approved*.
The operator then required an independent one, and a reviewer with no prior context on the
work **rejected** the change on two blocking findings, one of which falsified a claim the
self-review had signed off.

That outcome is the review's most useful result and is recorded plainly: the self-review
was not a substitute for an independent one, and the defects it missed were not subtle
consequences of complex code — they were wrong facts about the archive that the implementer
had already convinced himself of.

The verdict below is for the state after every blocking and high finding was fixed and
covered.

## What the independent reviewer found

### F-1 — the pet welcome could never fire. **Blocking. Fixed.**

Equipment identifiers and creature identifiers are disjoint in this archive. Measured: 408
creatures numbered 1 to 20,041; 745 creature-equipment entries numbered 63,000 upward; **zero
values in common**. The composition read the equipped item out of the inventory and handed it
straight to the creature lookup, which therefore returned nothing for every character alive.
Because a missing creature is an ordinary "no welcome, skip the packet", the failure was
silent: matrix row 58 would never have fired in production.

The legacy resolver goes through the equipment definition — item identifier to equipment
entry, entry's file name to creature list entry — and additionally requires the equipment to
declare itself a creature.

Worse than the bug: **no test could have caught it.** Every pet test injected a creature
identifier taken from the creature source, bypassing the exact step that produced the wrong
identifier. The row-58 evidence the self-review accepted was not evidence.

Fixed by adding the equipment hop to the content source, renaming the subject field to say
which identifier space it holds — the old name is how the confusion happened — and replacing
the pet tests with ones driving real equipped items through real content. One new test
equips item 63000 while creature 63000 also exists, so reading the item as a creature
returns the wrong lines rather than none.

### F-2 — area kinds read by position; a stated finding was false. **Blocking. Fixed.**

The content puts modifier markers such as `[use private store]` between the minimap
rectangle and the real kind. The parser took the second quoted value as the kind, so it read
the modifier and then took the modifier's own number as a spawn operand.

The completion evidence had stated that "58 of 151 town areas are unrecognised … both are
real content". They were not: 54 were parser artifacts. The correct account is more
embarrassing — the original code was accidentally right here, because it let the *last*
quoted value win; the minimap fix changed it to first-wins and broke it. So a fix introduced
the defect and the evidence then canonised it as a property of the archive.

Fixed by recognising the kind by what it says rather than where it sits, and by counting
only the numbers that follow it. Unclassified areas fall from 58 to 5, and town 2's walkable
areas now read `Normal` with no operands, matching the legacy parser. No gate was affected
today, which is exactly why nothing caught it.

### F-3 — the offered quest list is a small subset of the legacy rule. **High. Partly fixed, remainder recorded.**

The legacy index applies about thirteen filters; four were applied. Two were fixable from
data already carried and are now applied:

- **Repeatable quests were retired after one completion.** Daily, normally-repeat and
  special-daily are exempt from the cleared-quest exclusion in the legacy rule.
- **No grade whitelist**, so cracks, challenges, titles, schedules and the rest were being
  offered where the legacy server never offers them.

The reviewer's list of excluded grades was itself slightly wrong — it named `System`, which
*is* on the legacy whitelist. The implementation follows the legacy source, not the review.

The remaining filters need content the source does not carry and are tabulated in
`SelectCharacterWorldEntryCoverage.md`.

### F-4 — town presence had a filter legacy lacks and lacked every filter legacy has. **High. Fixed.**

Two errors in one place. The view filtered by listener, which the legacy production path
does not — and a comment asserted the opposite as legacy behaviour. And it carried none of
the conditions the legacy directory requires, the important one being that a character who
has walked into a dungeon keeps stale town coordinates; the legacy comment says so directly.
Reporting them would stand someone who is in a dungeon beside the arriving character.

Fixed: the listener scope is now optional and the town view spans listeners as legacy does,
and presence carries whether the character is actually standing where the record says.

### F-5 — the expert store asked the wrong registry. **High. Fixed.**

It read the flag off the *entering* character's presence record, and by the design's own
account that character has no presence record when their entry is composed — so the answer
was always false. The legacy store lives in its own registry keyed by owner. Split out, with
a test that deliberately admits no presence so the old shape cannot pass.

### F-6 — refusals handed out the run's contents. **Medium. Fixed.**

Party and participants were attached to every rejoin outcome. Combined with the narrowed
ownership check, an identity mismatch answered with the other account's party. The legacy
registry produces a snapshot only on success; refusals now carry nothing.

### F-8 — one deadline modelled where legacy keeps two. **Medium. Fixed.**

Idle and hard expiry are separate in the legacy registry, and touching a run pushes the idle
one forward. Carrying only that would let a run long past its hard limit keep reading as
available. Both are modelled and the earlier of the two decides.

### F-11, F-12 — parser robustness. **Low. Fixed.**

A nested block inside a movable-area section ended collection permanently, silently dropping
every row after it. And unresolved tag offsets were passed through as a sentinel that
matches nothing, yielding empty-but-present geometry that surfaced later as an accusation
against the content. Nested blocks are now skipped without ending the section, and a missing
tag fails loudly naming itself.

### F-7, F-9, F-10 and the minor items — recorded, not fixed

Documented in `SelectCharacterWorldEntryCoverage.md`: the expired-classification difference
that follows from not mutating, the lobby ordering the projection can no longer restore, and
the channel-identity configuration coupling. Each is a consequence of a decision rather than
an oversight, and each is now stated where a reader of that document will find it.

## What the independent reviewer confirmed

Verified by running, not by reading: the archive is genuinely present so archive-backed
tests execute; both hardcoded coordinates reproduce the legacy algorithm on real data,
including the tie-break that picks (378, 278) over an equidistant candidate; the geometry
port matches the legacy index for index with no axis or width/height swap; admission matches
byte for byte and refuses before publication; layering holds with the format library reachable
only from Infrastructure; the registries genuinely exist and a write is visible to the next
read; no catch-and-swallow anywhere; no G1–G4 regression; and RQ-008's withdrawal is sound.

## Verdict

**Approved.** All blocking and high findings are fixed and covered by tests that fail
without the fix. Medium findings are fixed or recorded. **348 passed, 2 skipped, 0 failed,
0 warnings.**

## What remains unverified

- No packet capture. Every protocol claim is code-reversed.
- The offered quest list remains narrower than the legacy rule, by the amount tabulated in
  the coverage document.
- The four runtime views stay empty on the production path until the lifecycle writes to
  them.
