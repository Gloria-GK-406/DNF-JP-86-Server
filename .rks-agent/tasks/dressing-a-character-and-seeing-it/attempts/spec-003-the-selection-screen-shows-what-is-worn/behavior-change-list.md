# Behaviour change list: the selection screen shows what is worn

Production files changed:

- `Server/src/Dfo.Server.Application/Characters/DrawnAppearance.cs` (new)
- `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs`
- `Server/src/Dfo.Server.Application/Characters/CharacterDomainService.cs`
- `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs`
- `Server/src/Dfo.Server.Abstractions/Characters/ICharacterServices.cs`
- `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`
- `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`
- `Server/src/Dfo.Server.Protocol/Game/AccountRoster/GetAccountCharacterRosterCommand.cs`
- `Server/src/Dfo.Server.Protocol/Game/AccountRoster/AccountCharacterRosterPayloadCodec.cs`
- `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs`

## How the paths below were enumerated

**Stated first, because the previous two versions of this list were wrong in the same way and the
method is why.** Both earlier versions enumerated by grepping the *names of the listing methods*.
That finds every caller that names one -- and finds nothing that reaches one across a dispatch
boundary, because what crosses such a boundary is never the method's name. It is the name of the
message the boundary carries. Grepping method names is therefore structurally blind to exactly the
paths this repository keeps failing to report.

The method used now is a **two-sided closure**: start at the read, walk up until every frontier item
is an entry point, and at each step grep for *the symbol that the next hop up actually names*.

| Frontier kind | The symbol the caller names | What to grep |
|---|---|---|
| an ordinary method | the method | its name |
| an `ICommandHandler<TCommand, T>` | **the command record**, in `commandBus.SendAsync(new TCommand(...))` | the command type's name -- the handler's name appears only in `ProtocolModule` |
| a `ClientMessageHandler<TMessage>` | nothing in code: the dispatcher looks it up | the handler type in `ProtocolModule`, which yields a `ClientMessageType` -- an entry point |
| a notification handler | the notification record | its type name, plus its `ProtocolModule` registration |

**That table is a list of the frontiers this walk met, and it is not the whole set.** The
independent review named three more this repository has, one of them on this very path: an outbound
encoder is reached by constructing its message and is registered by 52 `AddGame*MessageEncoder<,>`
lines in `ProtocolModule` -- which is how `AccountCharacterRosterPayloadCodec.Encode` is reached
here; an `ITransactionParticipant` is called out of a dictionary by `DomainTransaction`; a
`BackgroundService` such as `NotificationRetryDrainService` is reached by the host starting it.

So the rule to carry forward is the invariant rather than the table: **at every frontier, ask what
symbol the next caller up actually writes down. When the answer is "nothing -- a registry resolves
it", the next step is to go and read that registry.** A table of frontier kinds will always be
behind the code; that question is not.

Commands run, with their output:

```sh
grep -rn "ReadWornByAccountAsync\|ListByAccountWithAppearanceAsync\|GetAccountRosterWithAppearanceAsync" Server/src --include=*.cs
grep -rn "GetAccountCharacterRosterCommand" Server/src --include=*.cs
grep -rn "ListByAccountAsync\|GetAccountRosterAsync\|LoadByRosterPositionAsync" Server/src --include=*.cs
grep -n "GetAccountCharacterRosterHandler\|CreateCharacterHandler\|GameCreateCharacter\|GameGetAccountCharacterRoster" Server/src/Dfo.Server.Protocol/ProtocolModule.cs
```

The closure, upward from the new read:

1. `SqliteCharacterItemTable.ReadWornByAccountAsync` -- one caller,
   `CharacterRosterService.ListByAccountWithAppearanceAsync:160`.
2. That -- one caller, `CharacterDomainService.GetAccountRosterWithAppearanceAsync:49`.
3. That -- one caller, `GetAccountCharacterRosterCommandHandler:23`. **This is the hop the old
   method could not cross.** Its callers name `GetAccountCharacterRosterCommand`, not the handler.
4. `GetAccountCharacterRosterCommand` -- **two** senders:
   `GetAccountCharacterRosterHandler.cs:29` and `CreateCharacterSlice.cs:138`.
5. Both are `ClientMessageHandler`s, registered at `ProtocolModule.cs:233` for
   `ClientMessageType.GameGetAccountCharacterRoster` and `ProtocolModule.cs:246` for
   `ClientMessageType.GameCreateCharacter`. Two entry points; the walk stops.

No third reacher exists: step 1, step 2 and step 3 each have exactly one caller, and step 4's grep
covers every mention of the command in `Server/src`.

## Which request paths perform the account-scoped worn read

**Two.** The character-selection request, and creating a character.

| # | Request | Reaches the drawing listing | Also reaches the cheap listing |
|---|---|---|---|
| P-1 | `CMD 0x0208` GET_ACCOUNT_CHARACTER_ROSTER -- the selection screen (`GetAccountCharacterRosterHandler.cs:29`) | **yes** | no |
| P-2 | `CMD 0x0005` CREATE_CHARACTER -- the roster refresh pushed after a successful creation (`CreateCharacterSlice.cs:138`, sent as `AnswersRosterQuery: false` at `:142`) | **yes** | yes, separately, for the slot count at `CharacterDomainService.cs:134` |
| P-3 | `CMD 0x0004` SELECT_CHARACTER -- the entry burst, twice: roster-position resolution (`SelectCharacterPreparationService.cs:73` -> `CharacterDomainService.cs:87`) and the roster check (`SelectCharacterPreparationService.cs:100` -> `:33`) | no | yes |
| P-4 | `CMD 0x0006` DELETE_CHARACTER -- the name lookup (`CharacterDomainService.cs:190`) | no | yes |

P-2's two halves are the point of the split and worth separating: the **slot count**, which runs
before the write and gates it, takes the cheap listing; the **roster refresh**, which runs after the
write has been committed and acknowledged, takes the drawing one. A refusal in the second cannot
prevent the character being created -- `CreateCharacterResponse` has already gone out
(`CreateCharacterSlice.cs:127-134`) -- but it does raise after an acknowledged write, and that is a
post-commit failure surface this work widens. It is the same shape the previous result in this task
was corrected for, and it is not a new failure mode: the frame's old blob decode could raise there
too.

### Two earlier versions of this section were wrong. Both are kept here on purpose

**Version 1** put the worn read inside the single listing every caller shared, so it ran on all of
them, and reported that as a cost rather than a defect. The controller rejected it: entering the
world, deleting a character and gating a creation would have failed whenever a picture could not be
worked out.

**Version 2** narrowed the read correctly but claimed "**One.** `CMD 0x0208`", marked
CREATE_CHARACTER "no", and stamped six forward entries "P-1 only". All of that was false, and the
independent review found it. The cause was not carelessness about the create path specifically; it
was the enumeration method described above, which cannot see across a command bus. The narrowing
itself was and is right -- the post-creation refresh draws a roster, so it must have appearances;
handing it the cheap listing would trip F-8.

Three results in this task have now each under-reported a reachable path, and each time the check
followed the change's own origin downstream. That is why the method, not just its output, is written
down above.

## Forward: paths that can now be taken and could not before

Reachability is stated per path. "P-1, P-2" means both frames that draw a roster.

| ID | New path | What reaches it |
|---|---|---|
| F-1 | A drawn roster carries the parts the character currently wears, derived by `DrawnAppearance.Of`, instead of the parts decoded out of `characters.appearance_blob`. | **P-1 and P-2.** The intended outcome. |
| F-2 | `InvalidDataException "Inventory row {uid} item core length is N; expected 82."` fails the listing. | An equipment row of any live character on the account whose `items.item_core` is not 82 bytes -- **not producible through the table**, see "Reachability, corrected". **P-1, P-2.** |
| F-3 | `InvalidDataException "Inventory row {uid} has no item core."` | The same rows with a NULL `item_core`. Same constraint, same limit. **P-1, P-2.** |
| F-4 | `OverflowException` from `checked((byte)item.SlotIndex)` in `DrawnAppearance.Part`. | A worn row whose `item_slots.slot_index` is **negative**: `DrawsFrom` admits it (`slot <= 12`) and the narrowing refuses it. `item_slots` declares no constraint on `slot_index` (`CharacterDatabaseSchema.cs:200`), so unlike F-2/F-3 the store can hold one; `SqliteItemDomainStore.ValidateSlot` refuses negatives on the write path it owns. **P-1, P-2.** The in-world frame has always had this refusal. |
| F-5 | `OverflowException` from `checked((int)piece.ItemUid)` in `CharacterRosterService.Worn`. | An `items.item_uid` above `int.MaxValue` -- **refused by the table**. **P-1, P-2.** |
| F-6 | Any failure of the new statement -- a busy or locked database, a cancelled read, a missing table. | The read itself. **P-1, P-2.** The reachable member of the family, and the reason the narrowing matters even though F-2/F-3/F-5 are blocked. On P-2 it raises after the creation is committed and acknowledged. |
| F-7 | The selection-screen frame's body length now varies with what each character wears: up to fourteen twenty-three-byte parts per character (slots `0..12` and `23`). | **P-1.** Wire-visible and intended; not in the byte anchor. Per-character maximum part count is unchanged (creation's filter also allowed fourteen), so no new `checked((byte)Appearances.Count)` overflow. |
| F-8 | `InvalidDataException "Character {id} reached the roster frame from a listing that did not work out what it is wearing."` | Any caller handing `AccountCharacterRosterPayloadCodec` a roster built from the cheap listing. No production path does; the refusal exists so a future one is told instead of silently drawing undressed characters. |
| F-9 | `DrawnAppearance.Of` throws `ArgumentNullException(nameof(worn))` for a null worn list. | Only a caller passing null. The in-world frame's previous behaviour was the same exception type from `Enumerable.Where`; the parameter name changes from `source` to `worn`. |
| F-10 | New names outside their assembly: `public static class DrawnAppearance` (Application), `public sealed record WornItemRow` (Abstractions), one new member each on `ICharacterItemTable`, `ICharacterRepository`, `ICharacterDomainService`. | Compilation. Counted by `grep -rn "ReadWornByAccountAsync\|ListByAccountWithAppearanceAsync\|GetAccountRosterWithAppearanceAsync" Server/src Server/tests --include=*.cs` filtered to implementations: **3 production** (`SqliteCharacterItemTable`, `CharacterRosterService`, `CharacterDomainService`) and **9 test types** -- `CharacterEntryInventoryTests`, `CharacterLifecycleServiceTests`, `ItemUseFixtures.OneCharacter`, `OnlineUserServiceTests`, `AvatarCarrierRulesTests`, `CharacterSide.PerCallUseCase`, `SqliteCharacterLifecycleTests.RefusesTheWornRead`, `CarriedGoldStandIns.OnePlayingCharacter`, `SelectCharacterProductionStateTests`. One delegates wholly, one delegates its siblings and refuses the new member, seven refuse. |
| F-11 | **The roster frame pushed after a successful creation changes its bytes.** | **P-2**, for every created character. Detailed below. |

### F-11, in detail

Before this change that frame's parts came from `characters.appearance_blob`, written moments
earlier by `CharacterRosterService.Appearance` (`:72-95`). Now they come from `DrawnAppearance.Of`
over the same starting gear. The two disagree in three ways, all wire-visible, none anchored:

1. **The slot filter.** Creation's blob takes `SlotIndex <= 13`; the drawn set takes `0..12` and
   `23`. Slot 13 is `[title]` and slot 23 is `[magic stone]`
   (`PvfEquipmentSlots.ByName`). So a job whose `[create equipment list]` names a title loses that
   part from this frame, and a job that names a magic stone gains one. **Whether any job's create
   list names either is not measured here** -- it is an archive question, and this work did not
   open it. The filters differ regardless.
2. **The order.** The blob preserved the order the archive's grants arrived in
   (`equipment.Where(...)` with no sort); `DrawnAppearance.Of` orders by slot. For a job whose
   create list is not already in slot order, the parts come out in a different order and every byte
   after the first differing part moves.
3. **Two per-part bytes stop being constants.** The blob left `state` and `flag` zero; the drawn
   part computes them from the item core. **For a freshly created character they still come out
   zero**, because `CharacterRosterService.EquipmentCore` gives every starting piece
   `Attribute = 0`, `AmplifyType = 0` and `EnchantUpgradeCount = 0` -- so this one produces no byte
   difference today. It is listed because the fields are now derived: the day creation grants a
   piece with an attribute, this frame changes and nothing will announce it.

The count byte follows (1). Nothing pins this frame's bytes, so the suite is green either way --
which is what an unreached new path looks like.

### One more change of behaviour inside the in-world encoder

The per-part `checked` casts in `DrawnAppearance.Part` now run *before*
`EncodeCurrentUserState`'s `appearance.Count > byte.MaxValue` refusal, where they used to run after
it. So for input that would trip both, the exception raised changes from `InvalidDataException` to
`OverflowException`. **Unreachable**, but not for the reason first given here. Uniqueness per
`(owner_scope, owner_id, list_type, slot_index)` (`CharacterDatabaseSchema.cs:200`) does not cap the
count, because `DrawsFrom` is `slot <= 12` and `slot_index` carries no lower bound, so a store
holding negative slots could hold arbitrarily many drawn parts. What makes it unreachable is the
other half of the same loop: `checked((byte)item.SlotIndex)` raises on the first negative slot,
before any count is examined. The independent review found the first argument wanting and this is
the corrected one. Listed because it is a behaviour of the changed code, not because it can happen.

## Reverse: paths that existed and no longer do

| ID | Path removed | Consequence |
|---|---|---|
| R-1 | `SelectCharacterPreparationService.DecodeAppearances` is no longer called from `CharacterRosterService`. Its two refusals -- `"Character appearance blob is truncated."` and `"Character appearance blob contains trailing bytes."` -- are no longer reachable from any of P-1..P-4. | They remain reachable from the entry burst's roster frame, which still decodes the same column through `CharacterEntryStateTables` -> `BuildRosterEntryAsync`. The refusals are gone from these paths, not from the server. |
| R-2 | A blob written by anything other than this server's creation path -- an older row, an import, a hand-edit -- no longer shows up on either drawn roster. | This is the defect being fixed, stated as a loss so that it is visible: someone relying on writing that column to change what those screens draw no longer can. |
| R-3 | `CharacterSummaryRow.AppearanceBlob` has **no production reader left**. | Still selected by `SqliteCharacterTable.ReadSummariesByAccountAsync`, and `EntryBlob.Bounded(..., allowNull: true)` still refuses an appearance blob over 16 KiB on that read -- so that refusal survives although nothing consumes the value. Deliberately not removed: the blob is out of scope and the row type is shared with `FindBySlotAsync`. |
| R-4 | The in-world frame no longer computes the drawn parts inline. | No behavioural consequence: the eight fields written are the same eight, now read off the projected part. `SelectCharacterAcceptedTranscriptTests` hashes unchanged. |
| R-5 | `CharacterSummary.Appearances` is no longer guaranteed non-null. | Every reader must decide what null means. Both production readers do: the roster codec refuses it (F-8); `SupplementalSynchronizationCodec` never sees a `CharacterSummary` -- it reads `CharacterRosterMemberState`, still non-null. |

## Reachability, corrected

The first version of this list asserted F-2, F-3 and F-5 as new reachable refusals. **Two of those
three claims were wrong, and the correction was found by writing the check for them.** Read from
`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:199`:

```sql
items ( item_uid INTEGER PRIMARY KEY AUTOINCREMENT
          CONSTRAINT world_identity_fits_the_wire CHECK (item_uid BETWEEN 1 AND 2147483647),
        item_core BLOB NOT NULL CHECK (length(item_core) = 82), ... )
```

The table refuses a NULL core (F-3), a core of the wrong length (F-2) and an identity past the
wire's range (F-5). Writing a ten-byte core for the check failed with
`SQLite Error 19: 'CHECK constraint failed: length(item_core) = 82'`. Those three stay listed
because the narrowing is new on these paths, not because a write can produce them.

Reachable are **F-6** -- the read failing for its own reasons -- and **F-4**, whose column carries
no constraint.

## Not changed, checked rather than assumed

- **`CurrentUserStateDrawsFrom` answers exactly as before.** Old: `slot <= 12 || slot == 23 || slot == 24`. New: `DrawnAppearance.DrawsFrom(slot) || slot == 24`, and `DrawsFrom` is `slot <= 12 || slot == 23`. Same truth table for every input, negatives included. Its one caller, `MoveItemSpaceSlice.ChangesWhatTheCharacterLooksLike`, emits the post-move appearance frame in exactly the same cases.
- **The entry burst's roster frame is untouched and does not pay the worn read.** `BuildRosterEntryAsync` builds its parts from `entry.Public.Appearance.EncodedEntries`, never from `CharacterSummary.Appearances`. `SelectCharacterPreparationService.cs` is not in the diff.
- **No protocol frame added, no message type added.** `Dfo.Server.Data/Networking/NetworkMessages.cs` is not in the diff.
- **The blob is not written, not cleared and not removed.** `grep -rn "appearance_blob" Server/src` still returns one `INSERT` (`CharacterEntryTables.InsertAsync`) and no `UPDATE`.
- **Two screens now disagree.** Both drawn rosters (P-1, P-2) draw what is worn under `0..12` plus `23`; the entry burst's roster frame draws the creation-time blob under `<= 13`. Recorded in `Docs/CharacterDomainCut.md`, `Docs/CharacterEntryFacesInventory.md`, `Docs/ProtocolScaffoldDissonances.md` §7 and `protocol/game/server-to-client/NOTI-0x0002.md`.

## Known and left

- **A blob-decode check was lost in this round, and it is this work's loss, not an inherited gap.**
  The half of `TheRosterCarriesStoredAppearancePartsAndCopesWithNone` that this work removed wrote a
  non-empty blob by hand and asserted the roster returned its part. It was the only thing anywhere
  decoding a **non-empty** appearance blob. Its replacement,
  `TheEntryBurstStillReadsTheBlobCreationWrote`, asserts the *stored bytes* rather than a decode.
  `grep -rn "DecodeAppearances" Server/tests Server/src` now has one hit, the production call at
  `SelectCharacterPreparationService.cs:226`. The decoder is still consumed by the entry burst, so
  the loss is live. Not restored here because that frame is Scope Out.
- **Separately, and pre-existing:** `DecodeAppearances`'s two refusals -- truncated, trailing bytes
  -- have never had a check, before or after this work. That is an inherited gap; the item above is
  not.
- **F-4's negative worn slot is storable but is written by nothing in this repository.** Listed
  because the narrowing is new on P-1 and P-2.
- **Whether any job's starting gear reaches slot 13 or slot 23 is not measured.** F-11(1) states the
  filters differ; it does not claim a job that trips the difference.
- **The mutation runs' raw output was not kept.** M1..M5 were observed and their reds recorded in
  the execution report, but the console output was not written to a file, so the evidence is the
  report's word rather than a log. Later mutation rounds write to
  `<scratchpad>/mutation*/` alongside the `sha256sum` records.
- **The cost of the worn read is stated as a fact, not measured.** P-1 is reached once per login and
  once per return from the game; P-2 once per character created.
