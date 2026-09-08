# What was uncovered, and since when

`SelectCharacterPreparationService.DecodeAppearances` had no check naming it at the baseline `addbeb52`.
Counting method: `grep -rn "DecodeAppearances" Server/src Server/tests` -- under `Server/tests` the only
matches were compiled assemblies under `bin`, none in source. Read from this repository.

That absence had two origins. They are not the same fact and the difference decides what this attempt is
responsible for.

## The two refusals: a gap that was always there

`InvalidDataException("Character appearance blob is truncated.")` and
`InvalidDataException("Character appearance blob contains trailing bytes.")` have never had a check in this
repository. Nothing removed that coverage, because there was none to remove. It is an ordinary gap, and it
is the older of the two.

## The non-empty decoding: coverage this project removed on 2026-09-08

`SqliteCharacterLifecycleTests.TheRosterCarriesStoredAppearancePartsAndCopesWithNone` wrote a part into
`characters.appearance_blob` and asserted the roster handed it back. That assertion covered part of the decoding
incidentally -- it was not what the case was for.

**How much it covered is worth stating exactly, because it is less than "the decoding".** Read from
`git show 35aaedf3^:Server/tests/Dfo.Server.Persistence.Tests/Characters/SqliteCharacterLifecycleTests.cs`:
its blob was a count byte `0x01` followed by one 23-byte entry beginning
`0x0C, 0x80, 0x80, 0xCB, 0x06, 0x04` and then seventeen zero bytes, and it asserted three members --
`Slot == 0x0C`, `DisplayItemId == 0x06CB8080`, `ExpansionLength == 4`.

`ExpansionData`, `State`, `LinkItemId`, `EnchantValue` and `Flag20` were all zero and none of them was
asserted. So a decoder that misread any of the first three members would have failed that case, and one
that filled any of the last five from the wrong place, or left it unread, would not have.

One qualification, so this is not overstated in the other direction: a misreading that changed the
entry's **total width** would still have been caught there, because the blob's 24 bytes would then stop
adding up and the trailing-bytes refusal would fire. It would have been caught by that refusal rather
than by any assertion about a member. `ReadBytes(4)` narrowed to `(3)` is exactly such a case.

The coverage this attempt restores is therefore wider than the coverage that was lost, and saying only
"the decoding was covered" would overstate what existed.

`35aaedf3` replaced that case with
`SqliteCharacterLifecycleTests.TheRosterDrawsWhatEachCharacterWearsAndNothingWhenBare`.

**The replacement was right and is not being second-guessed here.** The old assertion was a statement of
the defect: `characters.appearance_blob` is written once when a character is created and by nothing
afterwards, so asserting it comes back out is asserting that the roster draws the character the player made
rather than the one they have. The replacement's own remarks say so.

What went unremarked at the time is that the incidental coverage went with it and was not replaced. From
`35aaedf3` until this attempt, the decoding had no check of any kind.

## What this attempt did about each

Both are now covered by
`Server/tests/Dfo.Server.Application.Tests/Characters/TheStoredAppearanceBlobIsReadExactlyAsWrittenTests.cs`.
The coverage is direct rather than incidental: it calls the decoder, so it cannot be lost again by
replacing a case that happened to exercise it.

No production file was changed. `git diff --stat` over the worktree is empty; the only additions are this
attempt's artifacts and the new check file, both untracked.

## The checks are load-bearing

Five mutations of `DecodeAppearances`, each applied to a `sha256sum`-verified copy and restored
immediately, with the file re-hashed after every restoration to `e3bb9182e1f2175922625b95ee797358d18254c60e4dbaae04607ae84f1af6a2`:

| Mutation | What was changed | Cases reddened | Reddened by | Which |
|---|---|---:|---|---|
| M1 | The two refusal messages swapped | 3 | message mismatch | `A_blob_that_promises_more_than_it_carries_is_refused_as_truncated`, `The_shortfall_is_found_after_the_entries_that_do_fit_have_been_read`, `A_blob_carrying_more_than_it_declared_is_refused_for_its_trailing_bytes` |
| M2 | `reader.ReadBytes(4)` narrowed to `(3)`, shifting every later member | 2 | raised exception | `Every_part_of_an_entry_comes_back_as_it_was_written`, `Two_entries_come_back_in_the_order_they_were_written` |
| M3 | The trailing-bytes refusal removed | 1 | no exception raised | `A_blob_carrying_more_than_it_declared_is_refused_for_its_trailing_bytes` |
| R2 | `reader.ReadUInt32()` replaced with `(uint)reader.ReadInt32()` | **0** | -- | none; all six stayed green |
| R3 | `State` and `Flag20` swapped where the entry is constructed, byte order untouched | 1 | **value mismatch**, `Expected: 3, Actual: 200` | `Every_part_of_an_entry_comes_back_as_it_was_written` |

M1 is the one that matters for the two refusals: both raise `InvalidDataException` and differ only in their
message, so a check asserting merely that something was thrown would have stayed green through it. All
three refusal cases went red, which is what proves they identify *which* refusal.

**M1, M2 and M3 all redden through control flow, and that is not enough on its own.** M2's two cases fail
because the shifted read makes the blob's length stop adding up, so the trailing-bytes refusal is raised at
`SelectCharacterPreparationService.cs:250` before a single `Assert.Equal` on the eight members runs. Taken
alone, that mutation set leaves the member assertions -- the substance of the first case -- unproven. R3
supplies what was missing: it moves two members past each other without touching a single width or offset,
so the length still adds up, no exception is raised, and the case fails on the value itself.

**R2 is a negative result and is recorded as one.** Nothing goes red, because signedness is not observable
through a `uint` member: the conversion back is bit-preserving. An earlier revision of `spec.md` and of the
check file's remarks claimed the opposite; both were corrected against this measurement rather than
argued away.

Run output kept at `scratchpad/efdcc/M1.txt`, `M2.txt`, `M3.txt`, `R2.txt`, `R3.txt`.

## What this record does not claim

- It does not claim the decoder is correct. It claims its behaviour is now written down. Whether reading
  this blob at all is the right thing for the entry roster to do is the next attempt's question.
- It does not claim these checks will outlive that attempt. If the entry roster stops reading the blob,
  this decoder loses its only production reader, and deleting it together with these checks is a
  legitimate outcome there.
