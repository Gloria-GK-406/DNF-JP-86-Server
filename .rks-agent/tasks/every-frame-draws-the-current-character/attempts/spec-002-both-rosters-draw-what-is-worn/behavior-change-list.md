# What this change makes possible, and what it stops making possible

Derived from the code at the changed revision, not from the list of edits that were intended. The method
is stated first so a reader can repeat it.

## Method

Walk out from every changed symbol, and at each frontier ask **what the next caller up actually writes
down**:

- an ordinary method gives its own name, so `grep` finds it;
- a command handler gives the **command record's** type name, not the handler's;
- a message handler gives a type resolved in `ProtocolModule`;
- when the answer is "nothing -- a registry resolves it", go and read that registry.

Then walk **down** as well: a value constructed here reaches an encoder that runs later, and that encoder
is a consequence of the change even though nothing here calls it.

Changed symbols this walk started from: `SelectCharacterPreparationService.PrepareAsync`,
`SelectCharacterPreparationService.BuildRosterEntryAsync`,
`SelectCharacterPreparationService.DecodeAppearances`, and the two test stand-ins that refused
`GetAccountRosterWithAppearanceAsync`.

## Forward: what can now happen that could not before

**F-1. Entry preparation can fail because equipment could not be read.**
`PrepareAsync` now calls `ICharacterDomainService.GetAccountRosterWithAppearanceAsync`, which reads
`ICharacterItemTable.ReadWornByAccountAsync`. Before, nothing on the entry path read worn rows for the
whole account. Any failure that read can produce now propagates out of entry preparation.

Reached from: `ISelectCharacterPreparationDomainService.PrepareAsync`, which the select-character path
calls. The frontier here is an interface, so the symbol the caller writes down is
`ISelectCharacterPreparationDomainService`, not the class name.

**Judged acceptable, with the reason stated rather than assumed.** `BuildRosterEntryAsync` already calls
`ICharacterEntryStateDomainService.LoadAsync` once per character on the account, in a loop. The path
already performs `1 + N` loads against the same store before any appearance exists, and a failure in any
of them already stops entry. This adds one account-scoped read to the path that was already the expensive
one. It does **not** touch the roster presence check at the top of `PrepareAsync`, which still uses
`GetAccountRosterAsync` and still works nothing out -- that check is one of the four the cheap listing was
split off for.

**F-2. A new refusal: `InvalidDataException("The account roster this frame is drawn from did not work out
appearances.")`.**
`BuildRosterEntryAsync` refuses a summary whose `Appearances` is null rather than drawing the character
wearing nothing. Null means "this listing did not work it out", which is what the cheap listing returns.

Reachable today only if someone feeds this frame the cheap listing. Nothing does: the only caller passes
the listing that always sets a non-null list. It is a guard against a future wiring mistake, and it is
guarded rather than allowed because drawing null as empty would undress every character on the screen and
read as missing content rather than as the wiring error it would be.

Covered by `SelectCharacterProductionStateTests.A_listing_that_worked_nothing_out_is_refused`.

**F-3. One more read of the account's character summaries per entry.**
`GetAccountRosterAsync` and `GetAccountRosterWithAppearanceAsync` each read summaries, so the entry path
reads them twice. This is the price of not putting an equipment read behind a membership check.

It opens a window that did not exist: the two reads are not one transaction, so a character deleted between
them appears in the presence check and not in the drawn frame. The frame would then be correct about the
account as of the second read. No check covers this window and none is added; it is recorded rather than
claimed to be impossible.

## Reverse: what can no longer happen that could before

**R-1. A corrupt `characters.appearance_blob` can no longer fail entry.**
The entry path no longer calls `DecodeAppearances`, so neither of its refusals --
`"Character appearance blob is truncated."` and `"Character appearance blob contains trailing bytes."` --
is reachable from production any more. Before this change, a character whose stored blob was truncated or
carried trailing bytes made entry preparation throw. Now that blob is loaded and ignored.

**This is a removed production refusal and is the entry on this list most easily missed**, because nothing
about it appears in the diff: the call site was replaced, and the refusals live in a method the diff does
not touch. It was found by walking down from the removed call rather than by reading the change.

Whether that is an improvement is not this attempt's judgement to make and is not claimed. What is true is
that a stored blob can no longer stop a player entering the world.

**R-2. `DecodeAppearances` has no production reader.**
Counting method: `grep -rn "DecodeAppearances" Server/src Server/tests`. Under `Server/src` the only match
is the declaration. Under `Server/tests`, seven matches, all in
`TheStoredAppearanceBlobIsReadExactlyAsWrittenTests`.

**R-3. Nothing decodes `characters.appearance_blob` any more.**
The column is still written once at creation (`CharacterEntryTables.InsertAsync`, one `INSERT`, zero
`UPDATE`), still SELECTed by `CharacterEntryStateTables` and `CharacterEntryTables`, and still carried on
`CharacterEntryState.Public.Appearance` through `CharacterEntryAssembly.cs:211`. No consumer reads it.

## Values that change on the wire

**W-1. The roster frame inside the entry burst now carries the character's current appearance.**
Its field layout, count and order are unchanged; only the values differ, and only for a character whose
worn equipment differs from what creation wrote.

Reached downward through `SelectCharacterResult.RosterEntry` ->
`SupplementalSynchronizationCodec` / `SelectCharacterHandler.cs:354` -> the registered encoder. That
encoder is not called from anything changed here, which is why the downward half of the walk exists.

**W-2. The byte anchor does not move, and both halves of the reason were got wrong once.**
All 62 hashes in `SelectCharacterAcceptedTranscriptTests` are unchanged.

**The roster frame is not in the anchor at all.** The anchor encodes what
`SelectedCharacterStateMessageFactory.Create` returns, which is
`IReadOnlyList<GameStateSynchronizationMessage>`. `AccountRosterStateMessage`, which carries this frame,
is a `SupplementalSynchronizationMessage` (`SupplementalSynchronizationMessages.cs:46`) and is constructed
at `SelectCharacterHandler.cs:354`, outside that factory. Read from this repository.

**And even if it were, the anchor could not feel this change.** It takes a hand-built
`SelectCharacterResult` from `SelectCharacterPayloadTestData.CreateComplete()` and encodes it, so it pins
the encoder rather than the source of the appearance, and the decision that changed sits upstream of where
it starts.

This falsifies the claim this project made when the work was scoped -- that the entry frame being inside
the anchor meant changing it would move anchored bytes -- and it falsifies the first correction of that
claim as well, which conceded the second half while repeating the first. Both are recorded in
`Docs/CharacterEntryFacesInventory.md`, together with the reason the second mistake happened: the premise
of the sentence being corrected was never itself checked.

The check that does catch the change is `SelectCharacterProductionStateTests`, which runs the production
path.

## Existing checks whose expectations moved, and which change each corresponds to

**T-1. `SelectCharacterProductionStateTests.Sources.GetAccountRosterWithAppearanceAsync` stopped refusing.**
It threw `NotSupportedException("The entry burst does not draw the roster.")`. That sentence was a
statement about the production path, and F-1 falsified it. It now answers.

The answer it gives is empty, and that is not a convenience: this seed's stored blob is `[0]`, a declared
count of no parts, so the decoder it replaces produced an empty list from it too. The complete-stream case
is therefore about the same stream it was about before. No assertion in that case changed; its `Sources`
is constructed with an explicit `[]` where it previously took a default.

**T-2. `CharacterEntryInventoryTests.StubCharacterDomainService.GetAccountRosterWithAppearanceAsync`
stopped refusing.** Same cause, same correspondence: it threw `NotSupportedException("This stand-in draws
no roster.")`. It now answers with an empty appearance, which is what the real listing would answer for a
character wearing nothing.

**No assertion in anything that reached it changed.** What reached it was 34 failing cases in
`Dfo.Server.Application.Tests`, spread over 31 distinct methods in **four** classes that all share this
one stand-in:

| Class | Methods |
|---|---:|
| `SelectCharacterLifecycleTests` | 18 |
| `CharacterEntryInventoryTests` | 6 |
| `SelectCharacterEntryLifecycleServiceTests` | 6 |
| `SessionGenerationOwnershipTests` | 1 |

Counting method, against `scratchpad/efdcc/spec002-first.txt`:

```sh
grep -oE "Dfo\.Server\.[A-Za-z0-9_.]+\.[A-Za-z_0-9]+(\([^)]*\))? \[FAIL\]" <file>   | sed 's/ \[FAIL\]//' | sed 's/(.*//' | sort -u
```

That yields 32 methods, 31 of them in `Dfo.Server.Application.Tests` and one in
`Dfo.Server.Protocol.Tests`. The 34 is that assembly's own failure count in the same file, and
`grep -c "This stand-in draws no roster"` on it gives 34 -- so every one of those failures arrived through
this stand-in and none through anything else.

**This count was wrong twice, and both errors are worth keeping because neither was arithmetic.**

The first version said "34 cases" of one class, mixing a case count with a method count.

The correction said 22 methods in two classes, and each half was wrong for its own reason. The methods
number was short by three because the pattern used to extract names required the name to end at `[FAIL]`,
so it silently dropped every data-driven case written as `Method(arg: 1) [FAIL]` -- and rows a pattern
fails to match look exactly like rows that do not exist. The classes number was wrong for a different and
worse reason: **the two class names were not derived at all.** They were the two that appeared in the stack
traces read while diagnosing the failures, and the counting was then done inside that assumed set instead
of over the whole `[FAIL]` list. Counting carefully within a set nobody established is the same mistake as
W-2's, in a different costume.

The command above is written out so the next reader can rerun it rather than trust the number.

**T-3. `SqliteCharacterLifecycleTests.TheEntryBurstStillReadsTheBlobCreationWrote` was renamed to
`TheStoredBlobIsLeftAloneAndNoLongerRead`, and its `<summary>` with it.**
Corresponds to R-3. The name and the summary both asserted that the entry burst still reads the blob,
which stopped being true. Its body and every assertion in it are untouched -- the case still writes a blob,
still reads the roster back, and still asserts the same values; what changed is that it no longer claims in
its own name to be checking something it is not.

An earlier round of this work corrected only the `<remarks>` of that case, leaving the name and summary
saying the opposite of the remark directly above the body. That was found in review, not by the author.

**Nothing else was touched.** Every other expected value and assertion in the suite is as it was.

## The decisions this attempt was required to make

**D-1. `characters.appearance_blob` is kept.**
Retiring it means the `CREATE TABLE` in `CharacterDatabaseSchema` and the field list in
`CharacterBodyShapeTests`, which is a migration of existing saves. The TaskContract puts that outside this
attempt and says to stop rather than widen, so it stops.

**D-2. `SelectCharacterPreparationService.DecodeAppearances` is kept, and the reason is not the one first
given.**
It has no production reader (R-2). The rule in the contract is to keep or retire by whether a reader
remains, which read alone would say retire it.

It is kept because **the column, the two SELECTs that load it, the `Public.Appearance` field that carries
it and the decoder that reads it are one unretired unit**, and D-1 puts the head of that unit out of scope.
Removing only its last link would leave the other three in place and split one retirement into two.

The stronger half of the reason: **the decoder together with `spec-001`'s six checks is the only
executable account of how a complete stored part comes apart.** Whoever performs the migration D-1 defers
is reading rows written by a version of this server they no longer have, and they need to know how those
bytes divide.

The blob's writer is `CharacterRosterService.Appearance` (`CharacterRosterService.cs:72`), whose result is
the `appearanceBlob` argument of the one `INSERT`. It fills three of a part's eight fields -- slot,
item id, and the constant expansion length -- and stops at slot 13, leaving `ExpansionData`, `State`,
`LinkItemId`, `EnchantValue` and `Flag20` zero. So it describes what creation writes, not what the format
is: nothing in it says the part is 23 bytes divided eight ways, and nothing in it says which blobs are
malformed. `DecodeAppearances` and its checks say both.

**An earlier draft of this paragraph claimed the decoder was the only executable description of the stored
format at all, and named `AccountCharacterRosterPayloadCodec.WriteAppearance` as the writer.** That is the
wire frame's writer, not the blob's; the blob's writer sits about ninety lines above the listing this
attempt changed, in the same file. The claim came from the independent review and was adopted without being
checked -- **the same failure that produced W-2's first wrong correction, made a third time in the same
attempt.** The review found it, in its own suggestion.

The first reason offered for this was that deleting the decoder would leave data nothing could read. The
review pointed out that this is already true whether or not the decoder exists, because nothing reads it
either way, and that reason is withdrawn rather than quietly reworded.

`spec-001`'s sealed result says deleting the decoder together with its six checks would be a legitimate
outcome here. That remains true; this attempt did not take it, and a successor retiring the unit whole
should take those six checks with it.

## Mutations

Each applied to a `sha256sum`-verified copy and restored immediately, with the file re-hashed afterwards.

| Mutation | File | What was changed | Cases reddened | Reddened by | Which |
|---|---|---|---:|---|---|
| M-A | `SelectCharacterPreparationService.cs` | the appearance taken from the stored blob again, as before this change | 3 | **value mismatch** on the first, `Expected: 12, Actual: 0` | `The_roster_frame_is_drawn_from_what_is_worn_and_not_from_the_stored_blob`, `A_character_wearing_nothing_is_drawn_wearing_nothing`, `A_listing_that_worked_nothing_out_is_refused` |
| M-B | `DrawnAppearance.cs` | `LastDrawnSlot` 12 -> 11 | 6 | value and set mismatches | `BothScreensDrawTheSameCharacterTests.The_in_world_frame_draws_exactly_the_slots_the_rule_names`, `.The_selection_screen_draws_exactly_the_slots_the_rule_names`, `SqliteCharacterLifecycleTests.TheRosterDrawsExactlyTheSlotsTheRuleNames`, `.TheRosterDrawsWhatEachCharacterWearsAndNothingWhenBare`, `.TheStoredBlobIsLeftAloneAndNoLongerRead`, `SelectCharacterProductionStateTests.The_roster_frame_is_drawn_from_what_is_worn_and_not_from_the_stored_blob` |

Hashes: `SelectCharacterPreparationService.cs` back to
`496400bc8e39597c1cb4dca4763292a256fde6adab1be48f99f52b76228afedf`; `DrawnAppearance.cs` back to
`f9ddc7f76663ce5b2abb230dd5b92d9565c75c1678e54941ae7a5374de014ce9`.

**M-B is the one that answers the contract's convergence condition, and it did not at first.** The
entry-side case originally handed the stand-in a hand-written `CharacterRosterAppearanceState`, which
stubbed the shared projection out: an independent review ran this same mutation and found the entry side
stayed green while five cases on the other two sides went red. The case now builds its worn piece and asks
`DrawnAppearance.Of` for the part, pinning the expected values by hand instead -- so the projection is
inside the case rather than replaced by it. Run output for both is kept in `scratchpad/efdcc/`.

## Records corrected

| Record | What it said | What falsified it |
|---|---|---|
| `CharacterRosterService` remarks | "The two screens therefore disagree" | The entry frame now asks this same listing |
| `DrawnAppearance` remarks | "Two screens draw a character" | Three places ask it now |
| `SqliteCharacterLifecycleTests` remarks | "The two screens now disagree, and this is where that is checkable" | Same |
| `Docs/CharacterEntryFacesInventory.md`, appearance row | the blob's one consumer is the entry frame | It has none |
| `Docs/CharacterEntryFacesInventory.md`, 2026-09-08 note | the entry frame is inside the byte anchor, so changing it means changing the anchor | W-2: the frame is not in the anchor at all, **and** the anchor starts downstream of the decision, so neither half of that sentence held. The first correction of it repeated the first half and was itself corrected. |

## Known and left

- **`SelectCharacterResult.Roster` has no production reader.** It carries the cheap listing, whose
  appearances are null. Counting method: `grep -rnE "(result|prepared|placed|entry|Result)\.Roster\b"` over
  `Server/src` and `Server/tests` -- one match, in a check that asserts membership rather than appearance.
  Not removed here; noticed while enumerating F-3 and recorded so it is not rediscovered as a surprise.
- **The two-read window in F-3.** Recorded, not closed.
- **The blob's retirement.** R-3 leaves a column that is written, loaded and read by nobody. Retiring it
  means the schema and `CharacterBodyShapeTests`' field list, which is a migration of existing saves and is
  outside this attempt by its contract.
