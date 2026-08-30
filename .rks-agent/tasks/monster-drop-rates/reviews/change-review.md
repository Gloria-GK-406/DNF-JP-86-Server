---
kind: rks-change-review
decision: approved
---

# Change review: 怪物死了会按档案掉东西

Second pass, after remediation. Scope: the complete working-tree change against `TaskContract.md`
(rev 1), `spec.md`, `plan.md`, `archive-findings.md` and `Docs/DropRates.md`. Read-only throughout:
`git status --short`, `git diff`, and every new file read in full. No build, no test run (forbidden
by the review brief). Test assemblies under `Server/tests/**/bin/Debug/net10.0` are dated
2026-08-30 16:39, later than the first pass's 16:17, so the remediated tree was compiled.

**Verdict: approved.** All three blocking findings from the first pass are fixed, and each is fixed
at the right level rather than patched at the symptom. Ten non-blocking findings were also taken.
What remains below is three observations about *test strength*, none of which describes a defect in
the shipped behaviour.

## The three blockers, re-checked against the tree

### B1 — the gold reading now touches the archive. Resolved.

`PvfDropRateTableSourceTests.The_gold_table_is_what_the_two_files_declare` calls
`((IGoldDropTableSource)new PvfDropRateTableSource(provider)).Load(DropRateTables.Monster)` and
asserts `BaseFor(1) == 17`, `BaseFor(86) == 1357`, `BaseFor(200) == 4877`,
`BaseAmounts.Count == 200`, `RankQuantities == [1,2,4,6]`, `VolumeBand == (100,110)`, and
`BaseFor(0) == BaseFor(201) == 0`.

I worked the mutations rather than taking the claim:

- **Row width.** `[gold drop ref table]` holds 600 numbers, so widths 2, 4 and 6 all divide it and
  none of them trips the `% GoldBaseRowWidth` guard at `PvfDropRateTableSource.cs:78`. Each does
  move `BaseAmounts.Count` off 200 (to 300, 150, 100) *and* misaligns `BaseFor(86)`. Caught twice
  over. Width 2 is the nastiest case — it happens to read `(1, 17)` correctly as its first pair —
  and the count assertion is what kills it. That assertion is doing real work.
- **Column swap.** Taking the amount from column 2 gives `BaseFor(86) == 9`; taking the level from
  column 1 turns the amounts into keys. Both caught by the transcribed levels.
- **Three levels rather than one.** Correct, and necessary: a single row can line up by accident
  under a shifted reading, three spread across the range cannot.
- **The unidentified third column** is named in a comment (9 at level 86) and deliberately not
  pinned as meaning anything. That is the honest treatment — `archive-findings.md` §4 and
  `Docs/DropRates.md` §11 both still carry it as 未查明, and the test does not quietly promote it.

`GoldAmount`'s `/100` reading of `[gold volume]` is now anchored by the `(100, 110)` assertion.

### B2 — the instance divergence is fixed structurally. Resolved, and I could not find a remaining path.

The fix moved the fact rather than the call: `AllocatedMonsterDrop` gained `ActorSequence`
(`IDungeonRunDomainService.cs`), `AllocateMonsterDrops` takes it and stores it once per kill
(`DungeonRunDomainService.cs`), `FindMonsterDrop` returns the whole record, and
`GroundPickupSlice.Piece` derives `DropInstance(run, dropped.ActorSequence)`.

You asked whether any path can still diverge. I traced every input on both sides:

- `DropInstance` (`DungeonEntrySlice.cs:371`) reads exactly three things: `run.CurrentRoomMapId`,
  `run.Layout.Seed`, and the index it is handed.
- The index is now the same stored `ActorSequence` on both sides — announce at
  `MonsterDeathSlice.cs:427`, bank via the stored record.
- `Layout.Seed` is immutable for the run.
- `CurrentRoomMapId` is the only input that can move, and it cannot move relative to the piece:
  `FindMonsterDrop` returns a record only when `allocated.Cell == CurrentCell`, and `Cell` was
  `current.CurrentCell` at allocation. Cell and `CurrentRoomMapId` are kept in lockstep by `Begin`
  and `TryMove` (`Layout.Rooms[cell]`), so standing in the piece's cell guarantees the kill's map
  id. `A_dropped_piece_stays_in_the_room_it_fell_in` pins the gate that makes this true.
- The per-kill half is fixed too: one `ActorSequence` is stored per `AllocateMonsterDrops` call,
  which is one call per kill, so a two-piece kill banks one value —
  `Every_piece_of_one_kill_carries_one_value` asserts exactly that, and asserts two kills differ.
- Declared drops are untouched (`ObjectIndex` on both sides, `FloorPieces.cs:72` against
  `GroundPickupSlice.Piece`), non-equipment never reaches `CarryOverAsync`, and the
  `MonsterDeathNotification.Empty` path carries no pieces.

**No remaining divergence path.** See O1 below for a residual *guarding* asymmetry — not a
divergence, and not blocking.

### B3 — the avatar slot draws nothing, as a decision. Resolved.

`MonsterDropService.Draw` returns `null` for `DropItemKind.Avatar` before any pool is consulted,
and the remark now states this is a decision rather than a consequence, with the reason (avatars
share `equipment.lst`, so routing the slot there would pay fashion's rate in gear) and the
magnitude. `MonsterDropServiceTests.The_avatar_slot_draws_nothing` makes only slot 4 certain and
asserts an empty result — reinstating the old `Equipment or Avatar` mapping turns it red.
`Each_item_slot_draws_from_the_family_it_is_mapped_to` additionally pins slots 1 and 3 to the
stackable pool and slot 2 to the equipment pool (3 pieces: two stackables, one weapon), which is
new coverage the first pass did not have and which guards the slot-to-family mapping as a whole.

## Non-blocking findings from the first pass

All ten taken, and I verified each in the tree rather than on report:

- **N1** — the count change is now deliberate and pinned:
  `A_declared_stack_is_banked_at_the_count_the_room_declares` banks a `Count: 3` declared stack and
  asserts 3, with a remark naming dungeon 144's boss room as the real case.
- **N2** — `NextMonsterDropNumber`'s remark now reads *"This deviates from live capture, not from
  the reference server"* and states the six-frame observation, then gives the reason for deviating.
  Correct authority ordering.
- **N3** — `DropRateContent.cs:52` now says "chronicle band".
- **N4** — `Docs/DropRates.md:176-177` replaces the contradictory 787/41485 paragraph and records
  that the full scan superseded the first version's sampling. Better than deleting it.
- **N5** — the cross-reference now names `The_ladders_are_the_groups_the_archive_declares`.
- **N6** — `ConcurrentDictionary<DropItemFamily, Lazy<DropItemPool>>` with the default thread-safety
  mode, and a remark explaining why the factory needed the lock.
- **N7** — recorded in `Docs/DropRates.md` §14's 本期没做的 table, with the honest scope note that
  the piece stays claimable and only stops being drawn on re-entry.
- **N8** — rewritten and now internally consistent: the cap never bites on the monster table as it
  stands, and the reason it exists is the factor-of-two margin.
- **N9** — `NoDropRateAdjustment` is its own file at the BD-010 path.
- **N10** — correctly left to the verification stage.

## Residual observations (none blocking, none a behaviour defect)

### O1 — the announce side of the instance invariant is unguarded.

`GroundPickupTests.The_banked_piece_carries_the_value_its_kill_announced` asserts the banked
`Value` against `DropInstance(run, Actor)` **recomputed in the test**, not against the frame the
handler produces. `MonsterDeathHandler` has no handler-level test at all (pre-existing —
`MonsterDeathCodecTests` builds `MonsterDeathNotification` by hand with `Instance` as a parameter).
So the mutation you verified — changing the *pickup* back — goes red, but the symmetric mutation,
changing `MonsterDeathSlice.cs:427` to derive from the drop number instead, stays green. The
invariant is guarded from one end only.

Not blocking: the code is correct, the primary mutation is caught, and the two sides now derive
from one stored fact rather than two independent guesses. Cheapest close: give the derivation one
name — e.g. an `InstanceIn(DungeonRunState run)` member on `AllocatedMonsterDrop`, or
`DungeonEntryPayloadCodec.KillInstance(run, actorSequence)` — and have both the encoder path and
the pickup path call it, so there is a single place that can be wrong.

### O2 — `The_gold_multiples_come_from_the_file_being_read` does not assert its own claim.

Its remark says the rank quantities belong to the file being read and that "reading the rank
quantities from a fixed file would be invisible until the breakable-object path is built." Its two
assertions are `monster.BaseFor(86) == breakable.BaseFor(86)` — true by construction, both go
through `ReadSections(CommonPath)` — and `breakable.VolumeBand.Low <= breakable.VolumeBand.High`,
which is tautological given the constructor is called as `(low, Math.Max(low, high))`
(`PvfDropRateTableSource.cs:98-101`). Nothing about `breakable.RankQuantities` is asserted, so the
named mutation stays green.

Not blocking, because nothing in phase one depends on it: `MonsterDropService` only ever asks for
`DropRateTables.Monster`, and that file's values are pinned by the test above. But the test's name
and remark promise coverage the body does not deliver, which is the shape this task is most
careful about elsewhere. Fix: assert `breakable.RankQuantities` against what
`itemdropinfo_object.etc` actually declares — if it declares its own group, transcribe it; if it
declares none and falls through to the `[1]` default, assert that and `NotEqual` against the
monster's `[1,2,4,6]`. Either way one assertion turns a tautology into a guard. If the two files
genuinely declare identical groups, say so in the remark and drop the claim.

### O3 — `No_avatar_declares_itself_creatable` asserts one item, names a universal.

It checks a single id (101550507) across every grade and rarity. The name and the remark claim
that *no* avatar in the archive is creatable. Harmless now — `Draw` declines the slot
unconditionally, so no behaviour rests on the claim, and the remark in `MonsterDropService`
correctly points here for it. Worth either narrowing the name to the item it checks, or widening
the assertion (the pool is already fully materialised; a check that no entry's id falls in the
avatar id range would cost nothing). Also misplaced: it constructs a `PvfDropItemPoolSource` from
inside `PvfDropRateTableSourceTests`, and reaches for a fully-qualified
`Dfo.Server.Abstractions.World.DropRarity` because that file has no using for it.

## The two deliberate omissions

I agree with both.

- **RQ-003's wording** (史诗与传说, where Epic and *Chronicle* are what the six-band ladder
  reaches). Editing an approved spec after implementation would be worse than the mismatch:
  it would erase the record that the reading changed during execution, which is the most
  instructive thing this task produced. Recording the correction in `spec-result.md` is the right
  disposition, and the substance — the top two bands reachable, nothing above epic creatable — is
  pinned by `The_two_rarest_bands_are_reachable` and `Nothing_above_epic_declares_itself_creatable`.
- **N10** is verification-stage work. This gate does not cover `dotnet build` / `dotnet test`
  output or SC-6's 实机截图, and should not be read as having covered them.

## Standing conclusions from the first pass

Re-verified unchanged by the remediation: the rarity basis is read from the table and never
assumed; the ladder group width is recovered by the archive's own closing invariant; ladders are
taken per item slot rather than from group zero; rank multipliers come from the equipment group;
`DropRules` holds no basis literal and `DropRateOptions.RateBasis` is honestly labelled server
policy with both candidates named; pool eligibility is the item's own `[creation rate]`; the
48-byte record and gold-as-item-zero are pinned byte-for-byte against two captured frames; and the
three upstream-only readings are labelled where they are used (RQ-013 / CK-021 met).

`AllocateMonsterDrops` remains concurrency-correct after gaining the `actorSequence` parameter:
the CAS still compares a freshly built `List<AllocatedMonsterDrop>` by reference, so a losing
writer always fails and re-reads, and nothing in a run's history removes elements, so the compared
state cannot revert. Two kills cannot be handed one number.

No verified wire bytes moved. A no-drop kill still encodes the same seven bytes.

## Verdict

`approved`. O1, O2 and O3 are recorded for the author's judgement; none of them blocks progression,
and I would not hold the gate for any of them.
