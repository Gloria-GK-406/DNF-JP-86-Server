---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: The selection screen shows what is worn

## Approach

Three moves, in this order, because each later one consumes the earlier one's shape:

1. **Lift the rule out of the byte loop.** `EncodeCurrentUserState` filters the worn list and then
   writes eight fields per surviving item. Those eight fields are, field for field, the eight
   members of `CharacterRosterAppearanceState` (`Dfo.Server.Data`). So the rule can become a pure
   function from what is worn to a list of that record, and the encoder becomes a loop that writes
   the record's members. The encoder's bytes are then unchanged by construction rather than by
   assertion.
2. **Add the account-scoped read.** One statement joining `characters` to `item_slots` to `items`,
   answering what each live character of the account has in its equipment container. It does not
   filter to the drawn set -- that is the rule's job, and a second copy of the filter in SQL is
   exactly what RQ-002 forbids.
3. **Rewire the selection screen.** `CharacterRosterService.ListByAccountAsync` reads the account's
   worn items, groups them by character, and asks the lifted rule instead of decoding
   `row.AppearanceBlob`.

Tests are written against each step's own observable (DP-002 below explains why the mutation
evidence needs a check on each side, not one comparing them).

## Decisions Before Work

### DP-001: where the lifted rule lands

**`Dfo.Server.Application/Characters/`, as a `public static` class.**

The constraint that decides it: `CharacterRosterAppearanceState` is in `Data`, the caller on one
side is `CharacterRosterService` in `Application`, and the caller on the other side is a codec in
`Protocol`. `Application` never references `Protocol`, so the rule cannot stay where it is.

Two homes were possible and one was chosen:

- `Data` -- reachable by both, zero dependencies. Rejected: `AGENTS.md` pins `Dfo.Server.Data` as
  pure models, and behaviour there would make that description false.
- `Application` -- `Protocol -> Application` is an allowed direction and is already exercised by
  every slice. `AGENTS.md` names the bottom tier of its three as "纯规则: 静态类, 纯函数, 不碰 I/O",
  and `Application/Characters/` already holds three of them (`CharacterNameRules`,
  `CharacterSkillPages`, `CharacterJobFamilies`). This is one more.

It is a pure function over `Dfo.Server.Data` records: no content source, no rule service, no I/O, so
it is injected nowhere and reaches neither architecture check's predicate.

### DP-002: the shape the rule returns, and what still lives in the codec

Returns `IReadOnlyList<CharacterRosterAppearanceState>`, ordered by slot, taking
`IReadOnlyList<InventoryItemState>` -- the shape the encoder already holds and the shape the roster
read is mapped into. Byte-for-byte agreement between the two screens then follows from both writing
the same record through their own already-existing 23-byte writers.

What stays in `Protocol`, deliberately:

- `EncodeCurrentUserState`'s `appearance.Count > byte.MaxValue` refusal. It is about a `u8` count on
  a wire, which the roster codec answers its own way (`checked((byte)...)`); moving it would change
  one of the two paths' failure behaviour.
- `Noti2CreatureSlot` (24) and the creature triple. Slot 24 is not part of the appearance run.
- `CurrentUserStateDrawsFrom`, which is the union of the appearance run and slot 24. Its appearance
  half is delegated to the lifted rule so that the union keeps having exactly one copy of the
  appearance filter; its creature half stays where the creature is.

### DP-003: where the new read is declared

**As one more method on `ICharacterItemTable`** (`Abstractions/Persistence/InventoryTables.cs`),
implemented on `SqliteCharacterItemTable`.

Reason, and it is a practical one worth stating: `CharacterRosterService` already takes
`ICharacterItemTable` (it writes a created character's starting gear through it), and so does the
Persistence test wiring in `Support/CharacterSide.cs`. Putting the read there means no new
registration, no new constructor parameter, and no new fixture wiring -- so nothing in this change
can fail for a reason that has nothing to do with what it is about. The cost is that an interface
documented as "a character's things" gains one account-scoped question; its remark is corrected to
say so rather than left to drift.

The row it returns is a new record in the same file, carrying the character it belongs to, the
item's identity, the worn slot and the core -- not `CharacterItemRow`, whose avatar columns this
read has no use for and would have to select.

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-002, RQ-005 | Add the pure rule in `Server/src/Dfo.Server.Application/Characters/`. Move `Noti2AppearanceLastSlot`/`Noti2AppearanceExtraSlot` and the per-part field values into it. Rewrite `EncodeCurrentUserState`'s appearance loop to project then write; point `CurrentUserStateDrawsFrom`'s appearance half at it. | `dotnet test Server/tests/Dfo.Server.Protocol.Tests` green, `SelectCharacterAcceptedTranscriptTests` hashes unchanged. No production behaviour changes in this step. |
| T-002 | RQ-002 | In `Dfo.Server.Protocol.Tests`, add checks: the two screens' parts byte-equal (AC-002), and each side's drawn slots asserted against `0,12,23` for a worn set spanning `0,12,13,23,24` (AC-003). | Both green after T-001. Both must exist before T-005's mutation. |
| T-003 | RQ-003 | Add the row record and the read method to `ICharacterItemTable`; implement it on `SqliteCharacterItemTable` with one statement joining `characters` (account, `delete_flag = 0`) to `item_slots` (`owner_scope='character'`, `list_type` = `InventoryContainerType.Equipment`) to `items`, ordered by character then slot. | Builds. A `Dfo.Server.Persistence.Tests` check that the read answers per character and returns nothing for an account with no characters. |
| T-004 | RQ-001, RQ-004 | Rewire `CharacterRosterService.ListByAccountAsync` onto T-003 and T-001; stop calling `SelectCharacterPreparationService.DecodeAppearances` there. Correct the remark that explains the decode. | `Dfo.Server.Persistence.Tests` checks: worn changes then roster follows (AC-001); bare and dressed on one account (AC-004); drawn slots for a `0,12,13,23,24` wearer. |
| T-005 | RQ-002 | Mutation evidence. Back up the rule file, take its `sha256sum`, alter the rule (drawn-set end, then the state byte), run the named checks on both sides, restore from the backup and re-check the hash. No git restoring command. | The in-world-side check and the selection-side check are both red under each mutation; the hash matches after restore. |
| T-006 | RQ-006 | Write `behavior-change-list.md` by scanning the changed files and every caller of what they touch -- forward and reverse -- rather than by following this change's origin downstream. | The list names each added, removed and altered production path with its file and the condition that reaches it. |
| T-007 | RQ-007 | Run the stated search set, read its output, correct what this work falsifies, and record that the two screens now disagree. | The corrections and the search commands are listed in the report. |
| T-009 | RQ-008 | Rework, after the controller rejected the first shape. Split the listing in two at the `Abstractions` seam -- `ICharacterRepository.ListByAccountWithAppearanceAsync` and `ICharacterDomainService.GetAccountRosterWithAppearanceAsync` -- leave the existing pair doing one read, and point only `GetAccountCharacterRosterCommandHandler` at the new ones. Make `CharacterSummary.Appearances` nullable so "not asked" and "wears nothing" stop being the same answer, and have the roster codec refuse null. | A Persistence check over a store whose worn read refuses: the cheap listing answers with null appearances, the drawing one throws. A codec check that null is refused. Both red when the read is folded back. |
| T-008 | RQ-005 | Full suite. | `PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo` at 2384 passed / 2 skipped / 0 failed or better, exit code 0, `tasklist` showing no `Dfo.Server.Host` beforehand. |

### T-004, and the one existing check it changes

`SqliteCharacterLifecycleTests.TheRosterCarriesStoredAppearancePartsAndCopesWithNone` writes a blob
by hand into `characters.appearance_blob` for a character created wearing nothing, and asserts the
roster comes back with that blob's one part. That assertion is a statement of the defect being
fixed: after T-004 the roster answers from what is worn, and that character wears nothing.

This is the one existing expectation this work rewrites, and it corresponds to the authorized
behaviour change "the selection screen no longer reads the blob". The hand-written-blob half is
replaced by the AC-001 half -- change what is worn, read the roster again -- so the file keeps a
check of the same weight rather than losing one. The remaining assertions in that test (creation's
own equipment coming back as parts, a bare character carrying none) hold unchanged and are left
alone.

### T-007, the search set

To be run from the worktree root and its output read, not assumed:

- `grep -rn "appearance_blob\|AppearanceBlob" Docs protocol AGENTS.md`
- `grep -rn "DecodeAppearances\|CharacterRosterAppearanceState\|roster appearance" Docs protocol AGENTS.md Server/src`
- `grep -rn "CharacterRosterService\|ListByAccountAsync" Docs protocol AGENTS.md`
- `grep -rn "Noti2AppearanceLastSlot\|Noti2AppearanceExtraSlot\|CurrentUserStateDrawsFrom" Server/src Docs protocol`

`Docs/CharacterDomainCut.md:73` and `Docs/CharacterEntryFacesInventory.md:137` are known in advance
to be affected: both record that the blob has no writer and is frozen at creation. Both stay true
and stop being the whole story, because the screen that motivated them no longer reads it. Whatever
else the search returns is judged on its own.

## Choices During Implementation

- The name of the lifted rule and of the new row record.
- Whether the roster read's rows are grouped in the service with a dictionary or with a lookup;
  either is local.
- Which worn slots the new checks use beyond the boundary set `0, 12, 13, 23, 24` that AC-003 fixes.
- Whether the Persistence-side worn change in AC-001 is made through `ICharacterItemTable`'s own
  insert-and-hold pair or through `IItemSlotTable`; both are production seams and the choice is
  about what reads most plainly.

## Risks

- **The mutation may not turn the byte anchor red on its own.** The anchor's fixture wears one item,
  at slot 1, so a drawn-set mutation at the `12`/`23` boundary leaves its bytes alone. That is why
  T-002 adds an in-world-side check with a worn set that spans the boundary; the anchor is evidence
  for RQ-005, not for RQ-002.
- **`CharacterSummaryRow.AppearanceBlob` becomes unread by production after T-004.** It is not
  removed: the blob is out of scope and the row type is shared with `FindBySlotAsync`. T-006 lists
  it as a reverse change and T-007 records it, so a later result that retires the blob can find it.
