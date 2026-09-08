---
kind: rks-review
verdict: approved
---

# Change Review: The stored appearance decoder is pinned

Scope: `change`. Reviewed against `TaskContract.md` (revision 1, C-1..C-6), `spec.md` (state ready,
RQ-001..RQ-006 / AC-001..AC-011), `routing-decision.md`, `coverage-record.md`, `sdd-execution.yaml`, the
parent `../../TaskContract.md` (SC-1, SC-2, SC-11), and the complete change between `addbeb52` and the
current uncommitted worktree state.

This is the second pass. The first pass returned `changes_required` on two findings; both are now resolved
and both resolutions were re-measured here rather than read. Nothing in the executor's report was taken as
given.

## A correction to this review's own record

The first pass wrote that the removed lifecycle case's blob was "one non-zero prefix followed by eighteen
zero bytes". **That was wrong; it is seventeen.** Counted here from
`git show 35aaedf3^:Server/tests/Dfo.Server.Persistence.Tests/Characters/SqliteCharacterLifecycleTests.cs`
by extracting every `0x??` literal in the `byte[] blob` initialiser: 24 literals in total, of which 7 are
non-zero (`0x01, 0x0C, 0x80, 0x80, 0xCB, 0x06, 0x04`) and 17 are `0x00`. That is the count byte plus a
23-byte entry whose first six bytes are non-zero and whose remaining seventeen are zero. The error came
from counting wrapped source lines instead of literals. `coverage-record.md` says seventeen and is right;
this review was the one that needed fixing.

The same paragraph's other figures were checked and stand: the case asserted exactly three members, and
`Assert.Equal(0x06CB8080, part.DisplayItemId)` is the literal in the source. The number 27600 belongs to
the *creation* half of that same case (`new NewCharacterEquipment(12, 27600, 45, ...)` and the
`Assert.Equal([27600], worn.Select(...))` that follows it), not to the hand-written blob half; the first
pass did not attribute it to the blob and the record does not either.

## What changed since the first pass

`git status --short` still returns exactly two untracked paths and nothing else. Comparing against the
first pass, three files were edited and nothing else moved:

| File | Changed | Untouched |
|---|---|---|
| `spec.md` | RQ-001 reworded, AC-004 weakened to match what its case proves, AC-011 added, Distinguishability's `EnchantValue` bullet rewritten, new **What cannot be observed** section | frontmatter (`state: ready`, `contract.revision: 1`) |
| `coverage-record.md` | new paragraph quantifying the removed coverage, mutation table gains a "Reddened by" column and rows R2 and R3, new paragraphs on control-flow-only mutations and on R2 as a negative result | the two-origins structure, both case names |
| the check file | class `<remarks>` rewritten, `The_shortfall_...` summary and remarks rewritten (152 -> 160 lines) | every assertion |
| `TaskContract.md`, `routing-decision.md`, `sdd-execution.yaml`, `multi-spec-map.md` | nothing | all |

**No check assertion changed.** All 22 `Assert.` lines in the check file are identical in content to the
first pass; only their line numbers moved, by the eight comment lines added above them. The six `[Fact]`
methods, both `CharacterRosterAppearanceState` constants, and the `Blob` helper are byte-for-byte as they
were. `TaskContract.md` was not touched at all, which matters for Blocking Finding 1's resolution below.

## Blocking Finding 1 -- resolved

The claim that `EnchantValue > int.MaxValue` makes a signed read observable is gone from all three places
it appeared, and each replacement says something that is true:

- `spec.md` RQ-001 now asks for values that survive neither "a member being filled from the wrong place in
  the entry" nor "a member being read at the wrong width", and points at the new section for signedness.
- The new **What cannot be observed** section states the structural reason -- the member is `uint`, the
  conversion back is bit-preserving, `3_000_000_000` unsigned is `-1_294_967_296` signed and converts back
  unchanged -- says it was measured rather than reasoned about, and says explicitly that choosing different
  values cannot recover it. It also records the amendment: "An earlier revision of this Spec asserted the
  opposite. It was wrong."
- The Distinguishability bullet now claims only what the value does buy: it makes those four bytes carry a
  value no other member carries, so a member filled from that offset by mistake shows up. That is true and
  is a different property from signedness.
- The check file's `<remarks>` no longer contains "the only way its being unsigned shows up at all", and
  says instead that signedness is not among the things these cases can catch, with the same reason and the
  same note that it was measured.

**This is a correction, not a goalpost move, and the contract is what settles it.** `TaskContract.md` C-1
reads, unchanged since 17:07 and never edited: "values that would expose a field read in the wrong order or
at the wrong width." Signedness was never in the contract; RQ-001 added it, and removing it brings the Spec
back into line with the contract rather than lowering the contract to meet the implementation. Both
dimensions C-1 does require are still required and both now have mutation evidence (placement by R3, width
by M2/R1). The clause removed was one no implementation could have satisfied.

## Blocking Finding 2 -- resolved

`coverage-record.md`'s mutation table now carries a "Reddened by" column with M1 as `message mismatch`,
M2 as `raised exception`, M3 as `no exception raised`, R2 as `--`, and R3 as `value mismatch,
Expected: 3, Actual: 200`. The paragraph beneath states plainly that M1, M2 and M3 all redden through
control flow and that this "is not enough on its own", names the exact place M2's two cases die
(`SelectCharacterPreparationService.cs:250`, verified here: line 249 is the `if`, line 250 is the throw,
and the production file is unchanged so the citation is still exact), and says the member assertions go
unreached. R2's zero result is recorded as a negative result under its own heading rather than buried.

`spec.md` AC-011 now makes this a standing requirement rather than a one-off repair: at least one recorded
mutation must redden through a value mismatch on the eight member assertions, "however many cases"
a control-flow-only set reddens.

### AC-011 is met by evidence already in hand, not left open

The coordinator asked whether AC-011 is satisfied or whether it has become a new unmet requirement. It is
satisfied. R3 is recorded in the mutation table as reddening by value mismatch; its retained output
(`scratchpad/efdcc/R3.txt`) shows one failure, `Every_part_of_an_entry_comes_back_as_it_was_written`, with
`Assert.Equal() Failure: Values differ / Expected: 3 / Actual: 200` -- an assertion failure, no exception
raised. That assertion is `Assert.Equal(First.State, decoded.State)`, one of the eight AC-011 names. This
review reproduced the same mutation independently in the first pass and obtained the identical failure and
the identical message, so the evidence is confirmed, not merely present.

## R2 and R3: reproduction and equivalence

The coordinator's `scratchpad/efdcc/R2.txt` shows 6 passed / 0 failed, and `R3.txt` shows 1 failed /
5 passed with the message above. Both match this review's own first-pass runs of the same two mutations
exactly, in count, in case name, and in failure text.

`mutate_r3.py` was read and is equivalent to the mutation this review applied. It replaces the
constructor call with eight locals read in the **original order at the original widths** --
`slot, display, expansionLength, expansion, state, link, enchant, flag` -- and then passes
`slot, display, expansionLength, expansion, flag, link, enchant, state`, swapping only positions 5 and 8.
This review's version used the same shape with names `mA`..`mH` and the same two positions swapped. Nothing
else is changed by it: the reads are the same calls in the same sequence, so the stream still advances
exactly 23 bytes per entry, the truncation guard and the trailing-bytes guard see identical positions, and
that is precisely why one case reddens and it reddens on a value rather than a throw. The script writes in
binary mode and asserts `data.count(old) == 1` before writing, which matches the contract's binary-I/O
constraint and guards against a silent no-op mutation.

Restoration is proven independently of the script: the live file hashes to
`e3bb9182e1f2175922625b95ee797358d18254c60e4dbaae04607ae84f1af6a2`, and so does
`scratchpad/efdcc/SelectCharacterPreparationService.cs.pristine`, and so does
`git show addbeb52:Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs`
piped through `sha256sum`. Three independent paths to the same digest.

## Check run, re-run here

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`,
run in this worktree after confirming `tasklist | grep -i Dfo.Server` was empty. Exit code 0.

| Assembly | Passed | Skipped | Failed |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 612 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 333 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 259 | 2 | 0 |
| Dfo.Server.Application.Tests | 1076 | 0 | 0 |
| **Total** | **2398** | **2** | **0** |

Identical to this review's first-pass run, assembly by assembly, which is what a comments-and-artifacts-only
remediation should produce. No `[FAIL]`, no `error CS`, no `MSB` line appears anywhere in the output, so
nothing needed classifying by exception type and stack root. Output retained at
`scratchpad/review/recheck-run.txt`. Every per-assembly figure except Application still matches the
recorded `addbeb52` baseline exactly, and Application is 1070 + 6 against a file containing exactly 6
`[Fact]` methods, so the 2392 / 2 / 0 baseline stays corroborated arithmetically as well as by the run.

## Non-blocking Observations

1. **`coverage-record.md` still says "Three mutations" above a five-row table.** The sentence introducing
   the mutation table reads "Three mutations of `DecodeAppearances`, each applied to a `sha256sum`-verified
   copy and restored immediately"; the table below it now lists M1, M2, M3, R2 and R3. The table is
   correct and complete, so no reader is misled about a substantive fact, but the prose count is stale.
   The root contract's SC-10 is specifically about "counts stated in prose rather than in code", which is
   why it is named precisely rather than waved at. One word.

2. **The removed case's insensitivity is now stated slightly too strongly in the other direction.** The
   new paragraph says "a decoder that misread any of the first three members would have failed that case;
   one that misread any of the last five would not have." The first half holds. The second does not hold
   for width errors: narrowing `ReadBytes(4)` to `(3)` misreads `ExpansionData`, one of the last five, and
   would have left that 24-byte blob one byte over at the end, raising the trailing-bytes refusal and
   failing the old case even though nothing asserted `ExpansionData`. The claim is exact for members
   filled from the wrong place or left unread -- which is the case for all five, since they were zero and
   unasserted -- but not for a change in total width. The conclusion the paragraph draws from it ("the
   coverage this attempt restores is wider than the coverage that was lost") is unaffected and remains
   true. Suggested repair, if it is worth one: qualify the second half with "filled from the wrong place
   or left unread", and note that a width error anywhere in the entry would still have shown up because
   the blob's length would stop adding up.

3. **`scratchpad/efdcc/R3.txt` cites a check-file line number that has since moved.** Its stack trace
   points at `TheStoredAppearanceBlobIsReadExactlyAsWrittenTests.cs:line 66`, which was
   `Assert.Equal(First.State, decoded.State)` when the mutation was run; after the eight comment lines were
   added above it, that assertion is at line 73 and line 66 is now `var decoded = Assert.Single(`. The run
   is genuine evidence and re-running it to refresh a line number would be waste. Noted only so that a
   later reader following the retained output does not conclude the record is wrong. `coverage-record.md`
   itself cites no line number for R3, so the record is unaffected; the production-code citation it does
   make, `SelectCharacterPreparationService.cs:250`, was re-verified and is still exact.

4. **`AC-011` sits between AC-007 and AC-008 in the criteria table.** It is grouped with the other RQ-004
   criterion rather than appended, which reads sensibly, but a reader scanning to the end of the table for
   the highest-numbered ID will not find it there.

5. **The Spec was amended after reaching `state: ready`, and says so.** RQ-001 was narrowed and AC-004
   weakened, both of which are requirement-level edits made during the review stage. Two things make this
   acceptable rather than a rewrite to fit the implementation: the amendments are recorded in the Spec
   itself ("An earlier revision of this Spec asserted the opposite. It was wrong"), and neither leaves the
   Spec weaker than the TaskContract, which was not edited -- C-1 never asked for signedness and C-2 never
   asked AC-004 to prove where the guard sits. AC-011 was added in the same pass and is a strengthening.
   Flagged for controller disposition rather than as a defect, since a Spec amendment after `ready` is the
   kind of thing that should be visible in the attempt's trail and not only in a review.

## Boundary and Success Conditions

| Item | Result | Evidence |
|---|---|---|
| Production code unchanged | pass | `git diff --stat` empty, `git ls-files -m` empty, `git status --short` two untracked lines; decoder hashes `e3bb9182...`, equal to the `addbeb52` blob and to the pristine backup |
| All five mutations reverted cleanly | pass | the single digest above; this review's own R1/R2/R3 in the first pass were likewise reverted and re-hashed |
| No existing check's expected value or assertion altered | pass | no tracked file is modified; within the one new untracked check file, all 22 `Assert.` lines are unchanged from the first pass and only comments moved |
| Byte anchor `SelectCharacterAcceptedTranscriptTests` byte-identical | pass | tracked and unmodified; Protocol.Tests 612 / 0 / 0 |
| `CommandHandlerArchitectureTests`, `NotificationHandlerArchitectureTests` | pass | both tracked and unmodified; Application.Tests 1076 passed, 0 failed |
| Layering and slice architecture | pass | the only added file is a check in `Dfo.Server.Application.Tests` over `Dfo.Server.Application` and `Dfo.Server.Data`; no production wiring, no slice touched |
| No new protocol frame | pass | no production change of any kind |
| No git restoring command used | pass | mutations used file-level backup plus `sha256sum`; `mutate_r3.py` writes in binary mode |
| Untouched: main checkout, `.worktrees/client-debug-lab`, `.worktrees/entries-are-what-the-client-reads`, `E:/Project/DNF/ServerS4A21` | pass | this review read and wrote only inside this worktree, plus its own scratchpad |

| ID | Verdict | Note |
|---|---|---|
| C-1 | met | Eight distinct non-zero members, all asserted; R3 proves the member assertions bite and that a member filled from the wrong place fails; M2/R1 proves a wrong width fails. Both dimensions C-1 names now have mutation evidence, and the signedness claim that was never in C-1 has been withdrawn from the Spec and the check file. |
| C-2 | met | Truncation refused, asserted by message rather than by "something threw"; exercised both by a blob too short to hold anything and by one whose second entry falls short. |
| C-3 | met | Trailing bytes refused, asserted by message; M1 shows it reddens if replaced by the truncated refusal. |
| C-4 | met | Five mutations, applied and reverted with a `sha256sum` match against a pre-mutation backup, each with its reddened count and case names; the set now includes one that reddens by value and one recorded negative result. |
| C-5 | met | Both halves separated and both verified here against history; the strength of the removed coverage is now quantified rather than asserted. Observation 2 is a refinement, not a correction of the substance. |
| C-6 | met | 2398 / 2 / 0, exit 0, re-run independently against the stated 2392 / 2 / 0 baseline; no failure to classify; output retained. |

Parent contributions SC-1, SC-2 and SC-11 are met by the above. No blocking finding remains. The four
observations are non-blocking and none of them needs to be resolved before this outcome is sealed; the
first is a one-word fix that a later pass over the record can absorb.
