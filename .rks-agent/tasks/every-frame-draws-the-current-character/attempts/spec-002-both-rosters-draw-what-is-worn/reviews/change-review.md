---
kind: rks-review
verdict: approved
---

# Change Review: Both rosters draw what is worn

Scope `change`, against baseline `77ff3236` (nothing committed on top of it), `TaskContract.md`
revision 1 (C-1..C-8), `spec.md` (RQ-001..RQ-008 / AC-001..AC-012), `plan.md` (DP-001, T-001..T-012)
and `behavior-change-list.md`. No adversarial challenge set was supplied, so there is no
`Challenge Dispositions` section.

Four rounds. Six blocking findings were raised and all six are resolved and independently verified.
**Approved.**

## The bytes this approval covers

`git diff --stat`: **8 files changed, 283 insertions(+), 65 deletions(-)**, plus the untracked
attempt folder. Recorded so a later reader can tell whether what was accepted is what was reviewed:

| File | sha256 |
|---|---|
| `Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs` | `496400bc8e39597c1cb4dca4763292a256fde6adab1be48f99f52b76228afedf` |
| `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs` | `cc234a788dabb39d126d59e0e0897baddb57d6fab499d4aedb36607904167fe8` |
| `Server/src/Dfo.Server.Application/Characters/DrawnAppearance.cs` | `f9ddc7f76663ce5b2abb230dd5b92d9565c75c1678e54941ae7a5374de014ce9` |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterProductionStateTests.cs` | `ec77b2faa9221ac971e871257d61ba1ebce8911ace814923eedb06ccc1cca9c0` |
| `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs` | `3e3b111863224a473aa857595cd83ce8255d2761e15a563e2d6024dbff4c2989` |
| `Server/tests/Dfo.Server.Persistence.Tests/Characters/SqliteCharacterLifecycleTests.cs` | `5700451fc06a86331b0af784246117c377836ec99103eea7bedadf92d9867c0e` |
| `Docs/CharacterEntryFacesInventory.md` | `38d628bd78b0bb5895a59bdca1c41dc2b63bd9757ae381d4e0865dd010ef0157` |
| `.rks-agent/.../multi-spec-map.md` | `26be00e4ab82d1af0a550c44fc0e7581aa1e7c7de844c05711165ed3c8a0b618` |

No production code and no check changed after round two; rounds three and four edited only the
untracked `behavior-change-list.md`.

## Check run

No `Dfo.Server` process was running. From the worktree root with an absolute solution path,
`PVF_ARCHIVE_PATH=…/Script.pvf dotnet test …/Server/86JP.Server.slnx --nologo`, output retained:

**2401 passed / 2 skipped / 0 failed**, exit 0. Pvf 60, Networking 52, Protocol 615, Host 6,
Persistence 333, Infrastructure 259 (+2 skipped), Application 1076 — sum 2401.

Output body searched rather than its summary: **zero** `[FAIL]`, **zero** `error CS`, **zero**
`MSB<digit>`, **seven** `的测试运行` banners, so every assembly executed. Run four times across this
review, the last on exactly the bytes hashed above; identical every time. Against the 2398 / 2 / 0
baseline this is **+3, all in Protocol.Tests (612 to 615)**, equal to the three cases added.
**C-8 and AC-011 met.**

## Success conditions

| ID | Verdict | How it was verified here |
|---|---|---|
| C-1 | met | M-A (the appearance taken from the stored blob again) reddens three cases, the first by **value mismatch** `Expected: 12, Actual: 0`. The fixture's two sources genuinely differ: the hand-written blob decodes to slot 0, the worn piece is slot 12. |
| C-2 | met | M-B (`DrawnAppearance.LastDrawnSlot` 12 to 11) reddens **six cases across three sides**, including `SelectCharacterProductionStateTests.The_roster_frame_is_drawn_from_what_is_worn_and_not_from_the_stored_blob`. Not circular: expected values are literals, `DrawnAppearance` appears only on the actual side. |
| C-3 | met | 62 anchor hashes unchanged, stated as measured; the recorded reason re-derived clause by clause — the frame is not in the anchor at all, and the anchor starts downstream of the decision. |
| C-4 | met | Reader enumeration reproduced (`DecodeAppearances`: one declaration in `Server/src`, seven test matches in one class; the column: one `INSERT`, zero `UPDATE`, still SELECTed twice). D-1 and D-2 record the decisions with reasons that survive inspection. |
| C-5 | met | DP-001's `1 + N` premise verified against the loop; the presence check still calls `GetAccountRosterAsync`; the widened failure surface stated and judged. |
| C-6 | met | The forward-and-reverse walk was re-derived from the code rather than checked off; R-1 (both blob refusals now unreachable from production) confirmed as the entry invisible in the diff. |
| C-7 | met | Each corrected record names what falsified it; every count is now checkable and reproduces. |
| C-8 | met | Above. |

`AC-001`..`AC-012` all met; `AC-012`'s value-mismatch requirement is satisfied by M-A's
`Expected: 12, Actual: 0` rather than by an exception count. `AC-010` / `RQ-008` hold: no
`Server/src/Dfo.Server.Protocol` file is touched, so no frame was added and no field layout moved.

## The six findings and their resolutions

**BF-1 — C-2 / AC-003 unmet.** Resolved. The entry-side case stopped handing the stand-in a
finished `CharacterRosterAppearanceState` and now builds a worn piece, asks `DrawnAppearance.Of`
for the part, and pins the five expected values as literals. Re-ran M-B: the entry side reddens.
The mutation record gained a home (`## Mutations`, with per-mutation "Reddened by" and both restore
hashes), so EV-002 exists in the artifacts and not only in a review.

**BF-2 — a false statement written into a corrected record.** Resolved. The note in
`Docs/CharacterEntryFacesInventory.md` now says the frame is not in the anchor at all, with the
mechanism: the anchor encodes `SelectedCharacterStateMessageFactory.Create`'s
`IReadOnlyList<GameStateSynchronizationMessage>`, while `AccountRosterStateMessage` is a
`SupplementalSynchronizationMessage` (`SupplementalSynchronizationMessages.cs:46`) built at
`SelectCharacterHandler.cs:354`, outside that factory. Every clause re-derived.

**BF-3 — a falsified name and summary left standing.** Resolved.
`TheEntryBurstStillReadsTheBlobCreationWrote` is now `TheStoredBlobIsLeftAloneAndNoLongerRead`, and
the method body was extracted from `git show HEAD:…` and from the working tree and diffed:
**identical, 37 lines, zero differences.**

**BF-4 — C-4's decision unrecorded for the decoder.** Resolved by D-1 and D-2.

**BF-5 — a wrong checkable count.** Resolved. The corrected paragraph publishes its extraction
command; running it gives 32 methods — 31 in `Dfo.Server.Application.Tests` across four classes
(18 / 6 / 6 / 1) plus one in `Dfo.Server.Protocol.Tests` — and `diff` against the set this review
derived independently by a different extraction is empty. `grep -c "This stand-in draws no roster."`
gives 34, matching that assembly's own failure count, so all 34 arrived through the one stand-in.

**BF-6 — D-2's "only executable description" clause.** Resolved, and worth recording that the false
clause came from this review's own round-two suggestion and was adopted without being checked.
The corrected paragraph now names the real writer, `CharacterRosterService.Appearance`
(`CharacterRosterService.cs:72`), whose result is the `appearanceBlob` argument of the one `INSERT`,
and states exactly what it does and does not describe. Verified against the code: it writes a count
byte then 23-byte parts, filling `Slot`, `DisplayItemId` and the constant `ExpansionLength` and
leaving `ExpansionData`, `State`, `LinkItemId`, `EnchantValue` and `Flag20` zero — three of the eight
fields the record declares (`StateSynchronizationModels.cs:56-64`) — and stopping at
`LastAppearanceSlot = 13`. The narrowed claim that replaces it, that the decoder plus `spec-001`'s
six checks is the only executable account of how a complete stored part comes apart and of which
blobs are malformed, holds.

## Non-blocking observations

Recorded, not gating. None changes a decision or a value on the wire.

- D-2 says the writer does not say "the part is 23 bytes divided eight ways". The eight-way division
  is genuinely absent from it; the 23-byte stride is not — `AppearancePartLength = 23` sits beside
  `Appearance()`. The operative half of the sentence is right.
- The same clause is scoped to the *stored* part and to the parsing direction, and both scoping
  words carry weight: `AccountCharacterRosterPayloadCodec.WriteAppearance` and
  `SupplementalSynchronizationCodec.WriteAppearance` do lay out the same eight fields in the same
  order and widths, in the assembling direction, for the *wire* frame. Only `DecodeAppearances`
  parses, and only it defines what a malformed blob is.
- T-2's post-mortem now composes exactly: an anchored extraction pattern drops three methods here
  (32 to 29), and within the two assumed classes that is precisely 6 to 5 and 18 to 17, summing to
  the 22 that was reported; the remaining seven methods are the two classes that were assumed rather
  than derived. Both halves check out arithmetically.
- The new case's remark says a hand-made part "would have left the in-world frame as the only side
  that could feel it"; measured, three sides felt it.
- `TheStoredBlobIsLeftAloneAndNoLongerRead`'s `<summary>` says "nothing reads it any more" while the
  case loads the column two lines later; the `<remarks>` below draws the loaded-versus-decoded
  distinction that makes it true.
- F-2's "would undress every character on the screen" overstates the counterfactual;
  `SupplementalSynchronizationCodec.EncodeRoster` already refuses a null `Appearances`, pinned by
  `SupplementalSynchronizationTests.SelectCharacterSupplemental_RosterRequiresAndEncodesAppearanceForEveryCharacter`.
  The new refusal is still a real path at a better place.
- F-1's acceptability argument is narrower than the fact it defends: the new read reaches the worn
  rows of every character on the account, which the `1 + N` framing does not itself cover. It holds
  for a reason F-1 does not give — the same read already gates `GetAccountCharacterRosterCommand`,
  so a player whose account's worn rows cannot be read never reaches the screen entry starts from.
- `CharacterRosterService`'s new paragraph is nested inside the preceding `<para>`; valid XML,
  cosmetic.
- `TheListingsThatDrawNothingDoNotReadWhatAnyoneWears`'s remark reads slightly stale now that entry
  reaches both listings; not false, since entry does still take the cheap one for the presence check.
- The hand-written blob's item bytes decode to `413384256`, not the `413560000` used in neighbouring
  fixtures. Nothing asserts it and only `Slot` separates the two sources.
- `A_character_wearing_nothing_is_drawn_wearing_nothing` names a fixture it does not build; the
  stand-in simply answers with an empty list. It still proves the right thing.
- The old test name survives as a citation in a sealed earlier attempt's record
  (`…/dressing-a-character-and-seeing-it/…/behavior-change-list.md:211`). The sentence around it is
  still true; sealed records are historical artifacts.

## Boundary

Layering intact (Application reaching Abstractions, the existing direction). Slice architecture
untouched; `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` green with
neither predicate nor exemption list edited. Clean build under `TreatWarningsAsErrors` with CS9113
as an error. LF endings preserved; `git diff --stat` stable at 283/65 after every mutation. No new
protocol frame and no field-layout change. `.worktrees/client-debug-lab`,
`.worktrees/entries-are-what-the-client-reads` and the main checkout's `ClientHack/` untouched;
`E:/Project/DNF/ServerS4A21` not written to. Nothing committed. **No git restoring command was used
in any round**; every mutation used a file-level copy plus `sha256sum`, restored immediately and
re-hashed.

## Verdict

`approved`. No unresolved blocking findings.

The production change is one call site and one guard: the entry burst's roster frame stopped
decoding `characters.appearance_blob` and now takes the appearance off the listing that works it
out, which is the same listing the character-selection frame and the post-creation refresh ask. The
cheap listing keeps the presence check it was split off to protect. The column and its decoder are
kept, by a decision that is written down with the search behind it. The byte anchor does not move,
and the record now says why for the right reason.

Approval does not replace fresh completion verification; the run above is evidence for this review,
not a substitute for the verification stage.
