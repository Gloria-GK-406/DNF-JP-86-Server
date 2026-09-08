# Who reads the session's copy, and what each of them takes

Enumerated at accepted baseline `1d887c59`, before the treatment was chosen. The table describes that baseline; the delivered change makes the Move row write back through ReplaceExact. Line numbers are baseline locations.

## What the copy is

**It is not an inventory snapshot.** `ISelectedSessionRuntimeDirectory` holds a whole
`SelectCharacterResult` -- the entire prepared entry result -- of which the inventory is one member among
nine: `Character`, `CharacterEntry`, `CharacterInventory`, `AccountInventory`, `Account`, `World`,
`Session`, `Roster`, `RosterEntry`.

Issue #15 calls it 「会话的背包快照」, which is how the reader set came to look like one screen. It is not
one screen.

## Method

Readers are the callers of `GetRequired` and `TryGet`; the interface and its implementation are excluded.

```sh
grep -rn "\.GetRequired(\|\.TryGet(" Server/src --include=*.cs \
  | grep -v ISessionPresence.cs | grep -v SelectedSessionRuntimeDirectory.cs
```

**16 call sites in 12 files.** For each, the returned variable was followed to where it is used, not read
off the line that fetches it; where the whole result is handed to another method the callee was read too.
Triggers are named by the message record's type resolved through `ProtocolModule`, not by the handler's
method name.

## The sixteen

| Site | Triggered by | Members read | Changed by a move? | Writes back |
|---|---|---|---|---|
| `SelectCharacterLifecycleServices.cs:84` | internal cleanup; reached from `PrepareSelectedCharacterSessionCommand`, `CompensateSelectedCharacterSessionCommand`, and connection loss | none -- existence check only | no | no |
| `SelectCharacterLifecycleServices.cs:155` | `PublishSelectedCharacterSessionCommand` | via `CreatePvpParticipant`: `CharacterInventory` Equipment items, `Character`, `CharacterEntry.Public`, `CharacterEntry.Features.Pets` | **yes** | no |
| `ExperienceBonusDomainService.cs:32` | `GrantKillExperienceCommand` | `Account.Entitlements.Premiums`, `World.Channel.ChannelId` | no | no |
| `CardRewardHandlers.cs:83` | `SelectCardRequest` | `Character.Progression.Level`, `World.PreparedAt` | no | no |
| `DungeonClearSlice.cs:591` | `SetPlayResultRequest` | `Character.Progression.Level`, `CharacterEntry.Public.Extras.CharacterExperience`, `World.Content.AvailableQuests`, `World.Subject` | no | yes |
| `LeaveDungeonSlice.cs:109` | `EplpCommandRequest` | via `TownReturnProjection.Frames`: `World.Channel.Spawn` | no | no |
| `DungeonEntrySlice.cs:461` | `SelectDungeonRequest` | `Character.Progression.Level` | no | no |
| `DungeonSelectionSlice.cs:188` | `BackToVillageRequest` | via `TownReturnProjection.Frames`: `World.Channel.Spawn` | no | no |
| `DungeonSelectionSlice.cs:380` | `EnterSelectDungeonRequest` | `World.Channel.Spawn`, **`CharacterInventory` Equipment items**, `Character`, `CharacterEntry.Public`, `CharacterEntry.Skills`, `World.PreparedAt` | **yes**, in one member | no |
| `MonsterDeathSlice.cs:376` | `MonsterDeathRequest` | `Character.Progression.Level`, `CharacterEntry.Public.Extras.CharacterExperience`, `World.Content.AvailableQuests`, `World.Subject` | no | yes |
| `MoveItemSpaceSlice.cs:834` | `MoveItemSpaceRequest` | `Character`, `CharacterEntry.Public`, `CharacterEntry.Features.Pets` -- **the equipment it needs is deliberately reloaded from the store instead** | no | no |
| `AcceptQuestHandler.cs:84` | `AcceptQuestRequest` | `Character.Progression.Level`, `World.Content.AvailableQuests`, `World.Subject` | no | yes |
| `FinishQuestHandler.cs:47` | `FinishQuestRequest` | `Character`, `CharacterEntry.Public.Extras`, `World.Content.AvailableQuests`, `World.Subject` | no | yes |
| `TownProjectionSlice.cs:233` | `SetUserAreaRequest` | `World.Channel.Spawn` | no | no |
| `TownProjectionSlice.cs:346` | `SetUserPositionRequest` | none -- existence check only | no | no |
| `TutorialLevelUpSlice.cs:175` | `TutorialLevelUpRequest` | `Character`, `CharacterEntry.Public.Extras`, `World.Content.AvailableQuests`, `World.Subject` | no | yes |

## Two of the sixteen go stale, and both take the same member

Every reader that a move can affect reads the **Equipment container of `CharacterInventory`**. Verified
independently of the walk: `grep -rn "\.CharacterInventory\b" Server/src` returns twelve lines, and the
only two inside a method that fetched the copy from the directory are
`SelectCharacterLifecycleServices.cs:221` and `DungeonSelectionSlice.cs:419`. The other ten consist of nine in
`SelectCharacterHandler` and `SelectedCharacterStateMessageFactory`, plus one in
`SelectCharacterPostEntryServices.SelectCharacterQuestEntryService.SynchronizeAsync`. All work on the
prepared result passed through the entry burst rather than on the resident copy.

**R-1. The dungeon selection screen shows the equipment the character had at selection.**
`EnterSelectDungeonRequest` -> `DungeonSelectionSlice.cs:380`. The Equipment container goes straight into
an `ExtendedUserStateMessage`. A player who changes gear in town and then walks to the dungeon gate is
rebuilt from the old list. This frame is not optional -- the slice's own remarks record that without it the
character's stats all display as zero, so the client rebuilds appearance and stat slots from it.

This is the reader issue #15 names, and it is consistent with what `MoveItemSpaceSlice`'s remarks already
say: the move refreshes its own frame from the store and deliberately leaves the copy alone.

**R-2. The PvP lobby shows other players wearing what they wore at their own character selection.**
`PublishSelectedCharacterSessionCommand` -> `SelectCharacterLifecycleServices.cs:155`. `PlanPvp` asks
`pvp.ReadyOn(port, session.CharacterId)` for the **other** ready participants and reads **their** session
copies through `CreatePvpParticipant`.

**Nothing had recorded this one.** It differs from R-1 in a way that decides the treatment: the stale
picture is not on the screen of the player who moved the item. It is on the screen of whoever enters the
lobby afterwards, and the player wearing the wrong thing cannot see that they are.

## What this means for the three candidate treatments

Issue #15 offered three. The enumeration disqualifies one of them and refines another.

| Treatment | Verdict on the evidence |
|---|---|
| Repair the dungeon selection screen only | **Disqualified.** It leaves R-2, which is on another player's screen and invisible to the player who caused it. The reason this treatment looked cheapest is that the report named one reader. |
| Keep in the copy only what a move cannot change | **Not needed, and it would be a bigger change than the defect.** Fourteen of sixteen readers already take only members a move cannot change; the copy is not the wrong shape, it is merely not maintained. |
| Reload and replace after a move | **Chosen**, in a narrower form than the issue described -- see below. |

The issue costed the third treatment as "a full inventory read per drag, and it changes what **all**
readers draw". The second half of that cost is avoidable and the enumeration is what shows it: **replace
only `CharacterInventory`** on the resident copy, leaving the other eight members as the same object
references they already are. In an uncontended refresh the fourteen unaffected readers receive the captured instances. This is
not a concurrency guarantee: a concurrent aggregate writer can be overwritten after the load.

That leaves one real cost: one inventory read per move that changed the character's inventory.

## What is not established

- **`CharacterEntry` was not exhaustively proven to be independent of equipment.** `CharacterPublicEntryState`
  has eleven members. `DerivedStats` was traced to `CharacterEntryAssembly.cs:124`, which builds from a
  stored stats row rather than from any equipment list, so that member is not a counter-example. The rest
  were accepted on the rule that a move writes `item_slots` and `items` and nothing else, not verified one
  by one.
- **`AccountInventory` cannot be changed by this message at all.** `MoveItemSpaceCommandHandler` serves
  only `Main`, `Avatar` and `Equipment`, all character-owned. No reader among the sixteen reads
  `AccountInventory` anyway. Whether some other message can change it was not investigated, and does not
  bear on this outcome.
- **Whether R-2 has ever been seen.** It is derived from the code. No live observation of a PvP lobby
  drawing stale equipment exists, and none is claimed.
