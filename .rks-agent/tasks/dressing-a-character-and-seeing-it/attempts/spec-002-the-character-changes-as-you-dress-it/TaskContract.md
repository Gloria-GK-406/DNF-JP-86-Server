---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-4, SC-5, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: The character changes as you dress it

## Context

### Source Request

Derived from the root TaskContract `dressing-a-character-and-seeing-it` (revision 1), defect B. The
second of the three bounded outcomes recorded in the root map; spec-001 is accepted at `e766d971`.

### Current Situation

`MoveItemSpaceHandler.HandleMessageAsync` sends `new GameExplicitMessageDelivery([response], [])`
and nothing else (read from this repository). The client is told where the item went and nothing
about what the character now looks like, so the eleven costumes the owner put on stayed invisible
until they left to character selection and came back (observed live, 2026-09-08).

Upstream sends the acknowledgement and then, for a move that touched an appearance slot, a
NOTI `0x0002` subtype-0 frame (`InventoryHandler.Move.cs:112-128` and
`InventoryRefreshSender.SendNoti2AppearanceUpdate:40-48`, read from source).

Three facts make this a small piece of work rather than a large one, all read from this repository:

- **The frame exists and is already sent.** `CurrentUserStateMessage` (NOTI `0x0002`, subtype 0) is
  built once today, in `SelectedCharacterStateMessageFactory.cs:43`. Nothing new goes on the wire.
- **The appearance projection is already inside it.** `EncodeCurrentUserState`
  (`StateSynchronizationPayloadCodec.Entry.cs:815-849`) derives the appearance entries from the
  equipment items it is handed. This work does not have to build or extract that; defect C, which
  needs the same projection as a value rather than as bytes, is where extraction belongs.
- **Three of the frame's four arguments are already to hand.** `ISelectedSessionRuntimeDirectory`
  carries the character, the public entry state and the pets, and a move changes none of them.

The one that is not to hand is the equipment list. The runtime directory's copy is the snapshot
taken when the character was selected, and no move refreshes it: `ReplaceExact` has eight call
sites in `Server/src` and none of them is after an item move (counted by
`grep -rn "ReplaceExact" Server/src`; the eight are the interface and implementation plus
`SelectCharacterLifecycleServices`, `DungeonClearSlice`, `MonsterDeathSlice`, `AcceptQuestHandler`,
`FinishQuestHandler` and `TutorialLevelUpSlice`). So the appearance frame has to be sent from what
the store now holds, not from that snapshot — and **the stale snapshot is itself a fact this work
has to deal with rather than step around**.

## Goal

When a move changes what the character is wearing, the player sees the character change, then and
there, without leaving the game.

## Scope

### In

- A successful move that changed a slot the character's appearance is drawn from tells the client
  what the character now wears.
- A successful move that changed nothing the character's appearance is drawn from does not.
- The equipment the frame reports being what the store holds after the move, not what it held when
  the character was selected.
- Every production path this work adds or changes, enumerated forward and reverse.
- Correcting repository records this work falsifies.

### Out

- The character-selection roster. Defect C, its own Spec. Its need for the same projection as a
  value is why the extraction belongs there and not here.
- The three subsystem-specific sends upstream makes alongside the appearance refresh — pet runtime,
  knight-shield deck, sort/lock signals — excluded by the root contract with its reasons.
- An inventory delta frame after a move. The acknowledgement already tells the client where the item
  went, and upstream sends no such frame either; nothing observed needs one.
- Any new protocol frame, and any change to the bytes of an existing one. If the appearance the
  frame carries is found to be wrong as well as stale, that is a separate finding to report, not to
  fix here.
- Reconciling the three slot filters this repository draws appearances through
  (`SlotIndex <= 12 || == 23` in the subtype-0 encoder, `SlotIndex <= 13` when a character is
  created, `0..11` for the costume bar). They disagree; deciding which is right changes bytes and
  belongs with defect C, which is where the two pictures have to be made to agree.

## Constraints

Every constraint in the root TaskContract applies unchanged. The ones that bear hardest here:

- No new protocol frame, and `SelectCharacterAcceptedTranscriptTests` stays byte-identical.
- `NotificationHandlerArchitectureTests` and `CommandHandlerArchitectureTests` keep passing with
  neither predicate nor exemption list relaxed. A command handler may not dispatch another command.
- Layering: a change in `Dfo.Server.Protocol` may not reach past `Application`.
- No existing check's expected value is rewritten to accommodate an implementation.
- No git restoring command; mutation checks use file backups plus `sha256sum`.

## Success Conditions

| ID | Observable condition | Required evidence | Root |
|---|---|---|---|
| C-1 | After a successful move that changed a slot the character's appearance is drawn from, the client is sent what the character now wears, in the same exchange as the acknowledgement. | Automated checks covering putting a costume on and taking one off, asserting the frame is sent and that the appearance it carries is the post-move equipment. | SC-4 |
| C-2 | A successful move that changed nothing the character's appearance is drawn from does not send that frame. | Automated checks naming at least two such moves, one of them inside a single list. | SC-5 |
| C-3 | A refused move sends nothing beyond its refusal. | An automated check. | SC-4 |
| C-4 | The appearance reported is what the store holds after the move. | An automated check that would fail if the frame were built from the selection-time snapshot — that is, one where the two differ. | SC-4 |
| C-5 | Every production path this work adds or changes is listed, forward and reverse, arrived at by reading the code. | A change list, plus an independent review that scanned the code itself. | SC-3 |
| C-6 | No protocol frame is added and no frame's bytes change. | The byte anchor unchanged; no message type registered that was not registered before. | SC-8 |
| C-7 | The full check suite is no weaker than its baseline. | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` at 2375 passed / 2 skipped / 0 failed or better, exit code 0, with no `Dfo.Server.Host` process running. | SC-9 |
| C-8 | Repository records this work falsifies are corrected. | The search set stated, actually executed, its output read, and the corrections listed. | SC-10 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root TaskContract `dressing-a-character-and-seeing-it` revision 1, Authorization row `task`. | This bounded outcome inside the root's authorized scope: execution and verification in the shared root worktree, and one local accepted-result commit after review and fresh verification. |

## Assumptions

- Whether a move touched the character's appearance can be decided from the request's two ends: the
  appearance is drawn from the equipment list, so a move that changed it named that list at one of
  its ends. This survives spec-001's reversal rule, because reversal exchanges the two ends and
  changes neither list. `ItemMoveResult` carries no set of changed slots and does not need one.
- Two ways to obtain the post-move equipment are visible, and either satisfies every condition
  above, so the choice belongs to the Spec rather than here. Reloading the character's inventory
  and refreshing the runtime snapshot follows an existing pattern (`TutorialLevelUpSlice.cs:214-241`)
  and repairs the stale snapshot as a side effect, at the cost of a full inventory read per drag.
  Teaching the equipment container to answer a snapshot lifts a restriction whose reason is written
  down at `ItemContainers.cs:767-784` — that an equipment snapshot needs avatar detail the store's
  vocabulary does not carry — and that reason has to be answered rather than ignored.
- The stale runtime snapshot is a defect of its own that this work touches. Whichever route is
  taken, the Spec says plainly whether the snapshot is left stale or refreshed, because a later
  reader will otherwise assume the frame and the snapshot agree.
