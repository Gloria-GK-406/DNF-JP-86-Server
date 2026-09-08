---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: The selection screen shows what is worn

## Why and Outcome

A player who dressed a character and then went back to character selection sees that character
undressed. The screen draws a blob stored on the character row, and nothing has written that blob
since the character was made (read from this repository: one `INSERT` under `Server/src`, in
`CharacterEntryTables.InsertAsync`; no `UPDATE`).

After this work the character a player is about to choose is drawn wearing what that character is
wearing, and it is drawn by the same rule that draws the character inside the game -- so the two
pictures cannot answer differently.

## Scope

In:

- The roster the character-selection screen is built from carries an appearance derived from each
  character's currently worn equipment.
- One rule, in one place, deciding which worn slots are drawn and what each drawn part carries;
  both the in-world frame and the selection screen reach it.
- A read that answers "what is every character on this account wearing".
- Every production path this work adds or changes, forward and reverse.
- Correcting repository records this work falsifies, and writing down that the two screens now
  disagree.

Out (from the TaskContract, restated because they bound the acceptance below):

- **The roster frame inside the entry burst.** `SelectCharacterPreparationService.BuildRosterEntryAsync`
  decodes the same blob for a different screen through `CharacterEntryStateTables`, and that frame
  is inside `SelectCharacterAcceptedTranscriptTests`. It keeps reading the blob.
- Writing the blob, keeping it current, or removing it.
- Dye colour and clone-avatar links on the drawn appearance. The in-world frame writes neither
  today, and this work makes the selection screen agree with the in-world frame, not with upstream.
- Reconciling the other slot filters in this repository (creation's `<= 13`, the costume bar's
  `0..11`, upstream's `0..13 || == 24`).

## Definitions

**Drawn part.** Twenty-three bytes: `slot(1)`, `displayItemId(i32)`, `expansionLength(i32)`,
`expansion(4)`, `state(1)`, `linkItemId(i32)`, `enchantValue(u32)`, `flag(1)`. This is the shape the
selection-screen codec writes (`AccountCharacterRosterPayloadCodec.WriteAppearance`) and the shape
the in-world frame's appearance run writes (`StateSynchronizationPayloadCodec.EncodeCurrentUserState`),
field for field (read from this repository).

**The drawn set.** The worn slots the in-world frame draws its appearance from: `0..12` and `23`
(read from this repository, `Noti2AppearanceLastSlot` / `Noti2AppearanceExtraSlot`). Upstream's
equivalent is `0..13 || == 24` (`EquipmentTypeInfo.IsA21RosterAppearanceSlot`, read from upstream
source); the two are not the same, and this work deliberately keeps this repository's, because the
in-world frame's bytes must not move.

**The rule.** For each worn item in the drawn set, ordered by slot: the part carries the item's
`ItemId` as `displayItemId`; `expansionLength = 4` with four zero expansion bytes;
`state = (Core.Attribute & 0x1F) * 2 + (Core.AmplifyType != 0 ? 1 : 0)`; `linkItemId = 0`;
`enchantValue = 0`; `flag = Core.EnchantUpgradeCount`. This is what the in-world frame writes today
(read from this repository), and it is what both screens must write after this work.

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The character-selection roster's appearance for each character is derived from what that character currently wears. The stored blob is not consulted on that path. |
| RQ-002 | The rule and the drawn set exist in exactly one place in `Server/src`, and both the in-world frame and the selection-screen roster reach that place rather than restating it. |
| RQ-003 | A read answering "what every character on one account wears" exists behind an `Abstractions` seam with its implementation in `Persistence`, and it does not restate the drawn set. |
| RQ-004 | A character wearing nothing carries no parts, and an account's characters do not borrow one another's parts. |
| RQ-005 | No protocol frame is added, no `ClientMessageType` or `ServerMessageType` member is added, and the in-world frame's bytes are unchanged for unchanged input. |
| RQ-006 | Every production path this work adds or changes is enumerated forward and reverse, arrived at by reading the code. |
| RQ-007 | Repository records this work falsifies are corrected, and the fact that the selection screen and the entry-burst roster frame now disagree is written down where a reader of either will meet it. |
| RQ-008 | Only the path that draws a character reads what an account's characters wear. A listing asked for by a caller that draws nothing neither performs that read nor can fail because of it. |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | Given an account whose character wears nothing, when a piece is put into a drawn worn slot with no other event in between, then re-reading the roster shows a part for that slot carrying that piece's item id; and when what is worn changes again, the roster follows again. |
| AC-002 | RQ-001, RQ-002 | Given one worn equipment set, the twenty-three-byte parts the selection-screen codec writes and the parts the in-world frame's appearance run writes are equal byte for byte, part by part, compared to each other rather than each to a literal. |
| AC-003 | RQ-002 | Given a worn set spanning `0`, `12`, `13`, `23` and `24`, both sides draw exactly slots `0`, `12`, `23`; each side is asserted against that expectation on its own, so mutating the single rule turns a check red on each side rather than leaving the two agreeing with each other about the wrong answer. |
| AC-004 | RQ-004 | Given one account holding a bare character and a dressed one, the bare character's roster entry carries no parts and the dressed character's parts name only its own worn items. |
| AC-005 | RQ-003 | The read is declared in `Dfo.Server.Abstractions` and implemented in `Dfo.Server.Persistence`; it answers for every live character of the account what that character wears, and the filtering to the drawn set happens in the one place named by RQ-002, not in the statement. |
| AC-006 | RQ-005 | `SelectCharacterAcceptedTranscriptTests` passes with its recorded hashes unchanged, and the diff shows no addition to either message-type enum. |
| AC-007 | RQ-006 | `behavior-change-list.md` lists forward and reverse entries, produced by scanning the changed code and its callers rather than by following the change's own origin. |
| AC-008 | RQ-007 | The search commands are stated, run, and their output read; the corrections are listed; and the disagreement between the two screens is recorded. |
| AC-010 | RQ-008 | Given a store whose account-scoped worn read refuses, the listing the non-drawing callers use still answers, and says it worked no appearance out rather than saying every character wears nothing; the listing the drawing caller uses fails. Folding the worn read back into the cheap listing turns this red. |
| AC-011 | RQ-008 | A roster built from the cheap listing is refused by the character-selection encoder rather than drawn as a screenful of undressed characters. |
| AC-009 | -- | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` reports at least 2384 passed, 2 skipped and 0 failed, exit code 0, with no `Dfo.Server.Host` process running. |

### AC-003, in detail

C-4 asks that the shared rule be structurally shared rather than asserted to be. A check that only
compares the two sides against each other (AC-002) stays green under any mutation of the shared
code, because both sides move together. So AC-003 pins each side against the drawn set
independently:

- in-world side: the appearance run `EncodeCurrentUserState` emits for that worn set;
- selection side: the parts the roster this repository produces carries for a character wearing it.

A mutation of the one place -- narrowing the run's end from `12`, dropping `23`, admitting `13`, or
altering the state byte -- must turn at least one check red on each side.

## Success Evidence

| ID | Covers | Evidence |
|---|---|---|
| EV-001 | AC-001, AC-004 | Automated checks against a real SQLite database through the production repository, changing the store and re-reading the roster. |
| EV-002 | AC-002, AC-003 | Automated checks in `Dfo.Server.Protocol.Tests` encoding both frames from one worn set. |
| EV-003 | AC-003 | A recorded mutation run: the single rule altered, the named checks observed red on both sides, the file restored from a byte-level backup and its `sha256sum` re-checked. |
| EV-004 | AC-006, AC-009 | The full suite's own output. |
| EV-005 | AC-007, AC-008 | `behavior-change-list.md`, and the stated search commands with their output. |

### RQ-008, and why it is here

It was not in revision 1 of this spec. The first implementation put the account-scoped worn read
inside the one listing that four non-drawing callers share -- resolving a roster position, counting
slots before a creation, finding a character by name before a deletion, and the entry burst's roster
check -- so all four would have failed whenever that read failed. The controller rejected that as a
defect rather than a cost: a login, a creation and a deletion are not worth spending on a picture.
RQ-008 is the narrowing, added as a bounded amendment during rework; the acceptance boundary of this
spec is otherwise unchanged.

## Assumptions

- The selection screen's frame is outside the byte anchor and the entry burst's roster frame is
  inside it. Checkable in `SelectCharacterAcceptedTranscriptTests`'s frame list.
- Deriving costs one account-scoped read per visit to the selection screen, which is reached once
  per login and once per return from the game.
- The blob is left exactly as it is: written once at creation, still read by the entry burst's
  roster frame. This work records where it is still consumed rather than retiring it.
