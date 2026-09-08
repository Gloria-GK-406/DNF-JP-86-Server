---
kind: rks-review
verdict: approved
---

# Change review: spec-003 "The selection screen shows what is worn"

Scope `change`, gate `auto`, fresh reviewer, same reviewer across both rounds. Reviewed against the
root TaskContract's constraint section, the child TaskContract revision 1 (C-1..C-8), `spec.md`
(RQ-001..RQ-008 / AC-001..AC-011), `plan.md` (T-001..T-009, DP-001/DP-003),
`behavior-change-list.md`, `routing-decision.md`, and the complete change between
`ce6839ed1730747d81a71efcb7342d0b8668965b` and the uncommitted worktree state on branch
`dressing-a-character-and-seeing-it`. No adversarial challenge set was supplied, so there is no
Challenge Dispositions section.

Round one raised two blocking findings, B-1 and B-2. **Verdict: approved.** Both are resolved, I
re-derived the fixes from the code rather than from the executor's account, and neither brought a
regression with it. What I could not close is recorded below as non-blocking.

## Round two: what I verified before accepting anything

**The claim that only comments changed in production, checked rather than taken.** I extracted every
added non-comment line in `git diff ce6839ed -- Server/src` (91 lines, `grep "^+"` with `///` and
`//` lines and blanks removed) and compared it against the set I verified in round one: identical,
line for line. Separately, `DrawnAppearance.cs` still matches the `sha256sum` I recorded in round
one (`de2bf90a...`), and `CharacterRosterService.cs` differs from my round-one byte-level backup
only inside one `<remarks>` block (`diff -u` against the backup shows one hunk, all `///`). So the
executable change is exactly what round one reviewed, and round one's code findings carry.

**Checks, run fresh.** `tasklist | grep -i Dfo.Server` returned nothing beforehand.
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
Server/86JP.Server.slnx --nologo`: **2392 passed / 2 skipped / 0 failed, exit code 0** (Networking
52, Protocol 612, Pvf 60, Persistence 333, Host 6, Infrastructure 259 + 2 skipped, Application
1070 -- seven lines summing to 2392/2). Baseline 2384/2/0, so C-7 / AC-009 holds. This is my third
full run of this change set across the two rounds; the `PersistentContainerBoundaryTests` failure
has not appeared in any of them.

**The five "not in the diff" claims, checked against `git diff ce6839ed --name-only`.** Absent, all
five: `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`,
`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`,
`Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs`,
`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` and its
notification counterpart. The name-only list is 26 paths; the two that are new since round one are
`AGENTS.md` and `Docs/DungeonEntryNotes.md`, which is the line-number correction discussed below.
C-6 / RQ-005 / AC-006 stand.

## B-1 -- resolved

The change list no longer claims one path. Its P-table has four rows and marks **P-1
(`CMD 0x0208`)** and **P-2 (`CMD 0x0005` CREATE_CHARACTER)** as both reaching the drawing listing;
F-2..F-6 are re-marked "P-1, P-2"; F-11 is added for the byte change on the post-creation frame; and
both production remarks I named are rewritten.

I redid the closure myself rather than checking theirs. Every hop holds:

| Hop | Call sites found in `Server/src` |
| --- | --- |
| `ReadWornByAccountAsync` | one, `CharacterRosterService.cs:160` (the other three hits are the declaration, the implementation and a `<see cref>`) |
| `ListByAccountWithAppearanceAsync` | one, `CharacterDomainService.cs:49` |
| `GetAccountRosterWithAppearanceAsync` | one, `GetAccountCharacterRosterCommand.cs:23` |
| `GetAccountCharacterRosterCommand` | eight mentions, of which exactly **two** construct it: `GetAccountCharacterRosterHandler.cs:29` and `CreateCharacterSlice.cs:138` |

Both senders are `ClientMessageHandler`s registered at `ProtocolModule.cs:233`
(`GameGetAccountCharacterRoster`) and `:246` (`GameCreateCharacter`) -- I printed lines 230-250 and
both line numbers are exact. **There is no third reacher.** The opcodes in the P-table are right
against `NetworkMessages.cs`: `GameGetAccountCharacterRoster = 0x0208`, `GameCreateCharacter =
0x0005`, `GameSelectCharacter = 0x0004`, `GameDeleteCharacter = 0x0006`.

**The correction to my own round-one statement is right, and I was the one overstating.** I wrote
that the state byte and flag "become derived" on the post-creation frame in a way that changes
bytes. It does not change bytes today. `CharacterRosterService.EquipmentCore` (`:347-352`) builds
`InventoryItemCoreState` positionally, and against that record's parameter list
(`InventoryModels.cs:81-92`) position 4 is `Attribute = 0`, position 8 is
`EnchantUpgradeCount = 0` and position 9 is `AmplifyType = 0`. So `DrawnAppearance.Part` computes
`state = (0 & 0x1F) * 2 + 0 = 0` and `Flag20 = 0`, which is what the blob already wrote. The
round-trip through `InventoryItemCore.Decode` is not in doubt -- `ChangingWhatACharacterWearsChangesWhatTheRosterDraws`
asserts `State == 11` and `Flag20 == 9` for a piece stored with `Attribute = 5`, `AmplifyType = 2`,
`EnchantUpgradeCount = 9`, so non-zero values survive it and zero values a fortiori. F-11(3) is
correctly stated as "no byte difference today, listed because the fields are now derived", and had
they left my wording in, F-11 would have been **overstated**, not understated.

The other two components of F-11 I checked and both are real:

- **The filter.** Slot 13 is `[title]` and slot 23 is `[magic stone]`
  (`PvfEquipmentSlots.cs:73` and `:84`). Creation's blob takes `SlotIndex <= 13`
  (`CharacterRosterService.cs:74`, `LastAppearanceSlot = 13`); the drawn set takes `0..12` and `23`.
  Creation's own `CreateAsync` loop holds *all* initial equipment into the container with no slot
  filter (`:282-304`), so a slot-23 grant is stored and is now drawn where it was not before.
  Declining to measure whether any job's create list reaches either slot is the honest call: that
  is an archive question this work did not open.
- **The order**, which they found on their own and I had missed. `Appearance()` is
  `equipment.Where(...)` with no sort, so the blob preserved the archive's grant order;
  `DrawnAppearance.Of` sorts by slot. For a create list not already in slot order the parts come out
  in a different order and every byte after the first differing part moves. Verified by reading both.

**The two production remarks are now accurate.** `ICharacterServices.cs:14-19` says the four are
call sites rather than four requests and names CREATE_CHARACTER as reaching both; `:32-39` says
plainly that creating a character does reach the drawing listing, through the refresh and not
through the slot count. `CharacterRosterService.cs:107-122` says the split "does not spare a
creation altogether" and that it "decides which half of a request pays, not which request". Against
the code -- `CharacterDomainService.cs:134` for the gating slot count, `CreateCharacterSlice.cs:138`
for the refresh after `CreateCharacterResponse` has gone out at `:127-134` -- that is exactly what
happens. The coordinator's summary of it ("what the narrowing protects is entry, deletion,
roster-position resolution, and the slot count a creation takes before it writes") is what the code
does.

## B-2 -- resolved

All three records now name `ListByAccountWithAppearanceAsync`, and each adds a sentence saying that
`ListByAccountAsync` is the deliberately-kept cheap listing that reads no worn row -- which is the
half a future reader most needed. `CharacterDomainCut.md` and `ProtocolScaffoldDissonances.md` also
now say that both drawn frames go through the same roster codec, which is true
(`AccountCharacterRosterResponse` -> `AccountCharacterRosterResponseEncoder.cs:25` ->
`AccountCharacterRosterPayloadCodec.Encode`, and both senders build that one response type).

Nothing is overstated. I checked each factual claim in the three additions against the code: the
blob still has one `INSERT` and no `UPDATE` under `Server/src`; the entry burst still decodes the
column through `CharacterEntryStateTables` -> `BuildRosterEntryAsync`
(`SelectCharacterPreparationService.cs:226`, file untouched); the two slot filters are `0..12` plus
`23` against `<= 13`; the anchor argument for leaving the entry frame alone is the one the contract
made. `NOTI-0x0002.md` is unchanged, which matches my round-one finding that it was already right.

## The extra record correction -- it belongs in this round, and it is right

`new CharacterFatigueState(188, 0)` moved because this change inserted `ListByAccountWithAppearanceAsync`
and its two helpers above it. I checked both ends: the literal is now at
`CharacterRosterService.cs:214`, and line 147 now holds an unrelated `///` line. So
`AGENTS.md:205` and `Docs/DungeonEntryNotes.md:137` were pointing at nothing, this change is what
falsified them, and `:147` -> `:214` is the exact correction in both. Nothing else in either
sentence was touched. This is in scope for C-8 -- a record this work falsified -- and it was right
to fix rather than leave for someone to trip over.

## The enumeration method itself -- sound in principle, and its table is not the whole list

The method's core claim is right and is the valuable part: **what crosses a dispatch boundary is
never the callee's name, it is a name the caller writes**, so a closure has to ask at each frontier
which symbol the next hop up actually spells. That is exactly why two rounds of grepping listing
method names could not see `CreateCharacterSlice`, and stating the invariant rather than only the
corrected output is the right response to three consecutive results failing the same way.

The stated invariant generalises. The four-row table under it does not, and a reader who takes the
table as the checklist will still have blind spots. This repository has at least three more frontier
kinds where a callee is reached without any call site naming it, all of which I confirmed exist:

- **Outbound message encoders resolved from a message-type registry.** `ProtocolModule` has 52
  `AddGameNotiMessageEncoder<,>` / `AddGameCmdMessageEncoder<,>` registrations. This is the mirror
  of the command-bus hop and it is directly on this change's own path: `AccountCharacterRosterPayloadCodec.Encode`
  is reached by *constructing* `AccountCharacterRosterResponse`, never by naming the encoder. The
  executor traced it correctly this time, but the table would have stopped the walk at the encoder.
- **Registry-invoked participants.** `ITransactionParticipant` (`Abstractions/Transactions/DomainTransactions.cs:13`)
  is enlisted into a dictionary in `DomainTransaction` and invoked from there; a participant's commit
  path has no naming caller.
- **Hosted services.** `NotificationRetryDrainService` is started by the host, not called.

Recommend adding those rows, or -- better -- promoting the one sentence that already covers them:
at every frontier, if the answer to "what symbol does the next hop write" is "nothing, a registry
resolves it", the next step is to find the registry. That sentence is what makes the method
transferable; the table is an instance of it. **This is a suggestion, not a finding** -- the method
as applied produced a closure I independently reproduced and agree with.

## Non-blocking observations

- **One remark of the same class as B-1 survives, twenty lines from its own correction.**
  `ICharacterServices.cs:71-76`, on `ICharacterDomainService.GetAccountRosterWithAppearanceAsync`,
  still reads "One caller, and it is the character-selection frame." The first clause is literally
  true at the method level -- `GetAccountCharacterRosterCommandHandler:23` is its only direct caller
  -- but the second is not: that handler serves two senders. Left non-blocking because the same
  file states the truth plainly at `:32-39`, and because this remark's own `<see cref>` points the
  reader straight at it. Worth a one-line fix whenever the file is next open.
- **The unreachability argument for the exception-order change reaches the right conclusion by an
  incomplete route.** It says the count cannot exceed fourteen because the drawn set is `0..12` plus
  `23` and `item_slots` is unique per `(owner_scope, owner_id, list_type, slot_index)`. Uniqueness
  alone does not bound it: `DrawsFrom` is `slot <= 12`, which admits negative slot indices, and
  `item_slots` places no lower bound on `slot_index` (`CharacterDatabaseSchema.cs:200`), so a store
  could hold hundreds of distinct negative-slot rows that all qualify. The conclusion still holds,
  by the other half of the same code: `DrawnAppearance.Part`'s `checked((byte)item.SlotIndex)`
  raises on the first negative slot before `appearance.Count` is ever examined, so with negatives
  present the `u8` refusal is unreachable, and without them at most fourteen slots qualify. Either
  way the refusal is dead. Only the stated reason is short, not the answer.
- **`Docs/CharacterEntryFacesInventory.md:137`** still says "选人界面那一帧" in the table row where
  the note below it now correctly says "那两帧". Cosmetic; the load-bearing claim in that row -- that
  the entry `Appearance` is the blob's only remaining consumer -- is true.
- **The mutation runs' raw output is still not on disk**, now disclosed in "Known and left" with a
  commitment about where later rounds will write it. This did not affect either verdict: I ran two
  mutations myself in round one with file-level backups and `sha256sum` restore checks -- the shared
  rule (`LastDrawnSlot` 12 -> 13, exactly three red, one pin on each side, the two-screen comparison
  and the byte anchor both staying green as AC-003 predicted) and M4, the rejected shape
  (`ListByAccountAsync` delegating to the drawing listing, exactly one red,
  `TheListingsThatDrawNothingDoNotReadWhatAnyoneWears`). Because the executable code is unchanged
  since -- proven above by hash and by the non-comment line comparison -- those results still stand,
  and C-4 is structurally satisfied rather than merely asserted.
- **F-10's count is now right.** Nine test types, listed by name; that is the same nine I counted
  independently in round one.
- **"Known and left" now separates the two coverage gaps correctly**: the non-empty blob decode lost
  its only exercise in this round (this work's loss), and `DecodeAppearances`'s two refusals never
  had a check (inherited). Both are true as written; `grep -rn "DecodeAppearances" Server/tests
  Server/src` still has exactly one hit, the production call.

## Round one's findings, for the record

Both are resolved above. Everything round one verified and accepted is unaffected by this round and
is not restated here: the byte-for-byte identity of the in-world frame's appearance run, the
`CurrentUserStateDrawsFrom` truth table, C-2 holding in production because both in-world call sites
feed the shared rule from the `InventoryContainerType.Equipment` container only, the CHECK
constraints at `CharacterDatabaseSchema.cs:199-200` making F-2/F-3/F-5 unproducible, the sufficiency
of F-6 plus the structural argument for the narrowing, the two `.Appearances` readers and why
refusing null is right, the correctness of the rewritten `SqliteCharacterLifecycleTests` case and
the absence of any weakened expectation anywhere in the diff, the layering being permitted by
`AGENTS.md:47-49` and the project reference graph, and the `git reset -- <path>` judgment being
correct with nothing lost.
