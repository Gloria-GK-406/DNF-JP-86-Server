---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-6, SC-3, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: The selection screen shows what is worn

## Context

### Source Request

Derived from the root TaskContract `dressing-a-character-and-seeing-it` (revision 1), defect C. The
last of the three bounded outcomes; spec-001 is accepted at `e766d971` and spec-002 at `ce6839ed`.

### Current Situation

Observed live, 2026-09-08: after eleven costumes were put on and were visible in the game, the
character-selection screen still drew the character undressed.

The selection screen's appearance comes from a blob stored on the character row.
`CharacterRosterService.cs:130` decodes `row.AppearanceBlob` and
`AccountCharacterRosterPayloadCodec.WriteAppearance` writes it back out, twenty-three bytes a part
(read from this repository). **Nothing updates that blob when equipment changes.** Counted by
`grep -rn "appearance_blob"` over the whole repository: under `Server/src` there is one `INSERT`,
in `CharacterEntryTables.InsertAsync`, reached only when a character is created, and no `UPDATE` at
all. Two of this repository's own documents already say so — `Docs/CharacterDomainCut.md:73` and
`Docs/CharacterEntryFacesInventory.md:137`.

What is written at creation is thinner still: only slots up to 13, and only the slot, the item id
and a length — the other fourteen bytes of each part are zero
(`CharacterRosterService.cs:71-85`, read from this repository).

Upstream does not read a stored blob for this screen. It derives the same list from the characters'
currently equipped items at the moment the roster is built
(`AppearanceService.LoadRosterAppearancesFromDb`, read from source), and keeps a blob only as a
fallback for when that derivation comes back empty.

Two facts bound this work:

- **This server already derives an appearance from worn equipment, once**, inline inside
  `StateSynchronizationPayloadCodec.EncodeCurrentUserState` — the frame spec-002 now sends after a
  move. It produces bytes, not values, so the selection screen cannot call it as it stands.
- **There is no query for "what every character on this account is wearing."** Every equipped-item
  read in `InventoryItemTables` is by one character, or by one account's cargo (read from this
  repository).

## Goal

The character a player is about to choose is drawn wearing what that character is wearing.

## Scope

### In

- The character-selection roster's appearance derived from what each character currently wears,
  rather than from a blob written once when the character was made.
- The rule that decides it being the same rule the in-world frame uses, in one place rather than
  two, so the two pictures cannot drift apart.
- Reading what every character on one account wears.
- Every production path this work adds or changes, enumerated forward and reverse.
- Correcting repository records this work falsifies — including the two that record the blob as
  having no writer, which stay true but stop being the whole story.

### Out

- **The roster frame inside the entry burst.** `SelectCharacterPreparationService.cs:226` decodes
  the same blob for a different screen, and that frame is inside
  `SelectCharacterAcceptedTranscriptTests`. Changing it moves the byte anchor, it is not the screen
  the owner reported, and it is a decision of its own. It keeps reading the blob, and that the two
  now disagree is recorded rather than left for a reader to discover.
- Writing the blob, keeping it current, or removing it. It stays exactly as it is: written once at
  creation, read by the entry roster frame.
- Dye colour and clone-avatar links on the drawn appearance. The in-world frame writes neither
  today; this work makes the selection screen agree with the in-world frame, not with upstream.
- Reconciling every slot filter in this repository. The one this work needs is the one the in-world
  frame uses; the creation-time filter (`<= 13`) governs a blob this work stops reading, and the
  costume bar's `0..11` answers a different question.
- The three subsystem sends upstream makes after a move, and everything else the root contract
  already excludes.

## Constraints

Every constraint in the root TaskContract applies unchanged. The ones that bear hardest:

- `SelectCharacterAcceptedTranscriptTests` stays byte-identical. No new protocol frame.
- Layering: Data → Abstractions → Application → {Infrastructure, Persistence, Networking} →
  Protocol → Host. A new read belongs in Persistence behind an Abstractions seam.
- No existing check's expected value is rewritten to accommodate an implementation.
- `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` keep passing
  unrelaxed.
- No git restoring command; mutation checks use file backups plus `sha256sum`.

## Success Conditions

| ID | Observable condition | Required evidence | Root |
|---|---|---|---|
| C-1 | Changing what a character wears changes what the selection screen draws for that character, with no other event in between. | An automated check that changes worn equipment and then reads the roster, asserting the drawn appearance followed. | SC-6 |
| C-2 | The selection screen and the in-world frame draw the same character the same way. | An automated check comparing the two for one character, part by part, rather than asserting each against a literal. | SC-6 |
| C-3 | A character wearing nothing is drawn wearing nothing, and an account's characters do not borrow each other's appearance. | Automated checks with at least two characters on one account, at least one of them bare. | SC-6 |
| C-4 | The rule deciding which worn slots are drawn, and what each drawn part carries, exists in one place. | The in-world frame and the selection screen reach the same code; a mutation of that code fails checks on both sides. | SC-6 |
| C-5 | Every production path this work adds or changes is listed, forward and reverse, arrived at by reading the code. | A change list, plus an independent review that scanned the code itself. | SC-3 |
| C-6 | No protocol frame is added and no frame's bytes change. | The byte anchor unchanged; no message type registered that was not registered before. | SC-8 |
| C-7 | The full check suite is no weaker than its baseline. | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` at 2384 passed / 2 skipped / 0 failed or better, exit code 0, with no `Dfo.Server.Host` process running. | SC-9 |
| C-8 | Repository records this work falsifies are corrected, and the two screens now disagreeing is written down. | The search set stated, executed, its output read, and the corrections listed. | SC-10 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root TaskContract `dressing-a-character-and-seeing-it` revision 1, Authorization row `task`. | This bounded outcome inside the root's authorized scope: execution and verification in the shared root worktree, and one local accepted-result commit after review and fresh verification. |

## Assumptions

- The selection screen's frame is not inside the byte anchor and the entry burst's roster frame is.
  That is why the boundary falls where it does, and it is checkable: the anchor's frame list is in
  `SelectCharacterAcceptedTranscriptTests`.
- Deriving costs one account-level read per visit to the selection screen. That screen is reached
  once per login and once per return from the game, not once per action.
- The blob becomes unread by this screen and stays read by the entry roster frame. It is not
  removed, not written and not defended; a later result may retire it, and this one records where it
  is still consumed so that result can find it.
