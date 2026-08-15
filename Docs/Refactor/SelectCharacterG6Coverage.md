# SELECT_CHARACTER G6 state-synchronization coverage

> Status: implementation coverage refactor-audited on 2026-08-15. The protocol identities,
> layouts and conditions in this document were reverse-engineered from server code;
> they have not been confirmed by packet capture or a real 86JP client.

This document preserves the row-by-row reverse-engineering evidence behind G6. The
current implementation no longer models the result as a SelectCharacter projection
stream: concrete semantic messages convert narrow state inputs into deterministic
frames, while the handler preserves the reviewed compatibility order. See
[`SelectCharacterStateSynchronizationRefactor.md`](SelectCharacterStateSynchronizationRefactor.md)
for the current structure.

## Implementation seams

- `SelectedCharacterStateMessageFactory` constructs the reviewed static compatibility
  sequence as concrete `GameStateSynchronizationMessage` instances and validates each
  body before delivery.
- `StateSynchronizationPayloadCodec` is split by domain family. It accepts only the
  state required by each concrete message; no encoder consumes `SelectCharacterResult`.
- `SupplementalSynchronizationMessage` covers X-02–X-18 concrete events and fixed
  adjacent batches. X-01 is `DarkKnightComboStateMessage` beside the normal skill
  synchronization message.
- `ExplicitMessageDelivery` carries already-constructed messages and explicit
  recipients. `TouchSocketNetworkMessageSender` knows only required-direct order,
  endpoint validation and per-target best-effort isolation.
- `SelectCharacterHandler` owns compatibility placement and sequentially dispatches
  cohesive commands. Its command handlers contain neither `ICommandBus` nor a sender.

The `Static.*` labels in the historical table below identify audited compatibility
positions; they are not current code symbols or a runtime occurrence-routing API.

## Static rows 1–58

| Row | Wire identity | Implemented typed source | G6 codec / occurrence | G6 condition | G7 responsibility |
|---:|---|---|---|---|---|
| 1 | CMD `01/0004` | `Character`, `CharacterEntry.Quests`, `Account.Wallet/Entitlements`, `World.PreparedAt` | `Static.Ack` | Always after a completed accepted result; missing limbs fail | No per-row work; lifecycle chooses when a completed success result may be delivered |
| 2 | NOTI `00/000D`, main | `CharacterInventory.Containers[Main]`, virtual resources, `World.PreparedAt` | `Static.Inventory(Main)` | Required, including loaded-empty | Same overall success boundary |
| 3 | NOTI `00/000D`, avatar | `CharacterInventory.Containers[Avatar]` with required avatar detail | `Static.Inventory(Avatar)` | Required, including loaded-empty | Same overall success boundary |
| 4 | NOTI `00/000D`, personal cargo | `CharacterInventory.Containers[PersonalCargo]` | `Static.Inventory(PersonalCargo)` | Required, including loaded-empty | Same overall success boundary |
| 5 | NOTI `00/000D`, pet | `CharacterInventory.Containers[Pet]` | `Static.Inventory(Pet)` | Required, including loaded-empty | Same overall success boundary |
| 6 | NOTI `00/000D`, account cargo | `AccountInventory.Cargo.Container` and account virtual resources | `Static.Inventory(AccountCargo)` | Required, including loaded-empty | Same overall success boundary |
| 7 | NOTI `00/0069` | `CharacterEntry.Features.Pets` | `Static.Notification(0069)` | Always; loaded-empty emits count zero | Same overall success boundary |
| 8 | NOTI `00/0002`, occurrence 0 | `Character`, `CharacterEntry.Public`, equipped inventory and pet runtime | `Static.Notification(0002,0)` | Always; required public identity fails if unavailable | Same overall success boundary |
| 9 | NOTI `00/0002`, occurrence 1 | `CharacterEntry.Public.Subtype1`, skills, equipped inventory, avatar detail, PvP/special-reward projections | `Static.Notification(0002,1)` | Always in successor; required normalized limbs fail | Same overall success boundary |
| 10 | NOTI `00/0245` | `Character.Identity.Job`, `CharacterEntry.Skills.KnightShieldDeck` | `Static.Notification(0245)` | Job 12 and loaded deck; exactly five slots required | Same overall success boundary; G6 owns omission from completed input |
| 11 | NOTI `00/0013` | `CharacterEntry.Skills.Pages/ProtocolMirror` | `Static.Notification(0013)` | Always; mirror required | Same overall success boundary |
| 12 | NOTI `00/0015` | `Character.Progression.Level`, `World.Content.AvailableQuests` | `Static.Notification(0015)` | Always; loaded-empty emits count zero | Same overall success boundary |
| 13 | NOTI `00/0164` | `CharacterEntry.Quests.VisibilityFlags` | `Static.Notification(0164)` | Always; exact 30,000-byte bitmap required | Same overall success boundary |
| 14 | NOTI `00/0286` | `CharacterEntry.Progression.DailyChallenges`, claim flags and tail IDs | `Static.Notification(0286)` | Always; exact six claim flags required | Same overall success boundary |
| 15 | NOTI `00/00AD` | `Account.Settings` option and quick-chat blobs | `Static.Notification(00AD)` | Always | Same overall success boundary |
| 16 | NOTI `00/01C7` | `Account.Settings.HotkeyKeyType`, `CharacterEntry.Settings.HotkeySlots` | `Static.Notification(01C7)` | Always | Same overall success boundary |
| 17 | NOTI `00/006C` | Observed constant | `Static.Notification(006C)` | Always; fixed three-byte body | Same overall success boundary |
| 18 | NOTI `00/0005` | `CharacterEntry.Progression.Dungeons` | `Static.Notification(0005)` | Always; loaded-empty emits count zero | Same overall success boundary |
| 19 | NOTI `00/0187` | `CharacterEntry.Settings.OptionBitmap` | `Static.Notification(0187)` | Always; defined absent value maps to four-byte baseline | Same overall success boundary |
| 20 | NOTI `00/01B9` | `CharacterEntry.Progression.GrowthWeaponStages` | `Static.Notification(01B9)` | Always; loaded-empty emits count zero | Same overall success boundary |
| 21 | NOTI `00/0166`, occurrence 0 | `CharacterEntry.Features.Collections.TitleBooks[category order 0]` | `Static.Notification(0166,0)` | Required configured category | Same overall success boundary |
| 22 | NOTI `00/0166`, occurrence 1 | Title-book category order 1 | `Static.Notification(0166,1)` | Required configured category | Same overall success boundary |
| 23 | NOTI `00/0166`, occurrence 2 | Title-book category order 2 | `Static.Notification(0166,2)` | Required configured category | Same overall success boundary |
| 24 | NOTI `00/0166`, occurrence 3 | Title-book category order 3 | `Static.Notification(0166,3)` | Required configured category | Same overall success boundary |
| 25 | NOTI `00/0166`, occurrence 4 | Title-book category order 4 | `Static.Notification(0166,4)` | Required configured category | Same overall success boundary |
| 26 | NOTI `00/0167` | `CharacterEntry.Features.Collections.Achievements` | `Static.Notification(0167)` | Always; loaded-empty is valid | Same overall success boundary |
| 27 | NOTI `00/015F` | `CharacterEntry.Skills.SkillPointSlots` | `Static.Notification(015F)` | Always; loaded-empty is valid | Same overall success boundary |
| 28 | NOTI `00/00AC` | `CharacterInventory.ItemValues.Cooldowns` | `Static.Notification(00AC)` | Always; sorted by stored order | Same overall success boundary |
| 29 | NOTI `00/00AE` | `CharacterInventory.ItemValues.Effects` | `Static.Notification(00AE)` | Always; sorted by stored order | Same overall success boundary |
| 30 | NOTI `00/02DA` | `Account.Relations.Unknown730Entries` | `Static.Notification(02DA)` | Always; meaning remains unresolved | Same overall success boundary |
| 31 | NOTI `00/017B` | Observed constant | `Static.Notification(017B)` | Always; fixed bytes | Same overall success boundary |
| 32 | NOTI `00/0381`, occurrence 0 | `CharacterEntry.Features.Collections.CollectionBoxes[order 0].Protocol` | `Static.Notification(0381,0)` | Required configured box and computed protocol state | Same overall success boundary |
| 33 | NOTI `00/0381`, occurrence 1 | Collection-box order 1 and computed protocol state | `Static.Notification(0381,1)` | Required configured box | Same overall success boundary |
| 34 | NOTI `00/021F` | Observed constant | `Static.Notification(021F)` | Always; fixed four-byte body | Same overall success boundary |
| 35 | NOTI `00/0357` | `CharacterInventory.Rentals`, `Account.Wallet.LuckyStar`, `World.PreparedAt` | `Static.Notification(0357)` | Always; only unexpired rentals included | G7/preparation must persist any expiry mutation before supplying the completed result |
| 36 | NOTI `00/00FB` | Character and account-cargo equipment locks | `Static.Notification(00FB)` | Always; distinct from X-11 | Same overall success boundary |
| 37 | NOTI `00/00CD` | `CharacterEntry.Features.ExpertJob` and protocol mirror | `Static.Notification(00CD)` | Always; profession-specific required data validated | Same overall success boundary |
| 38 | NOTI `00/019F` | Owner-matched `Account.Relations.CharacterRelations[].SupportProjection` (character, entry and inventory); loaded-empty is explicit | `Static.Notification(019F)` | Always exactly once; full support record only for the selected owner, otherwise the evidenced two-byte empty body | Same overall success boundary |
| 39 | NOTI `00/00B1` | `CharacterEntry.Progression.DailyReviveCoinClaimed` | `Static.Notification(00B1)` | Always | Same overall success boundary |
| 40 | NOTI `00/0300` | `CharacterEntry.Progression.CubeType/CubeGrade` | `Static.Notification(0300)` | Always | Same overall success boundary |
| 41 | CMD `01/0312` | `Account.Entitlements.Premiums`, character premium usage | `Static.Premium` | Always; fixed eight-slot layout validated | Same overall success boundary |
| 42 | NOTI `00/03D8` | `Account.Progression.LotteryProgress` | `Static.Notification(03D8)` | Always; up to eight ordered 24-byte records in the fixed 204-byte body; loaded-empty remains all zero | Same overall success boundary |
| 43 | NOTI `00/019D` | `Account.Wallet.LuckyStar` | `Static.Notification(019D)` | Always | Same overall success boundary |
| 44 | NOTI `00/025B` | `CharacterEntry.Progression.ChampionBreak*` | `Static.Notification(025B)` | Always; business name remains inferred | Same overall success boundary |
| 45 | NOTI `00/0331` | `CharacterEntry.Progression.GoldLimitUpgradeLevel` | `Static.Notification(0331)` | Always | G7/preparation owns any first-load mutation before result composition |
| 46 | NOTI `00/01EB` | fatigue-acceleration value/state | `Static.Notification(01EB)` | Always | Same overall success boundary |
| 47 | NOTI `00/0061` | `CharacterEntry.Features.Mailbox`, messages and attachments | `Static.Notification(0061)` | Always; loaded-empty is valid and widths are validated | Same overall success boundary |
| 48 | NOTI `00/0158` | `CharacterEntry.Features.PvpMissions` | `Static.Notification(0158)` | Always; loaded-empty is valid | Same overall success boundary |
| 49 | NOTI `00/02D5`, occurrence 0 | `CharacterEntry.Progression.ScheduleCompatibility[order 0]` | `Static.Notification(02D5,0)` | Required tuple; meaning remains unresolved | Same overall success boundary |
| 50 | NOTI `00/02D5`, occurrence 1 | schedule tuple order 1 | `Static.Notification(02D5,1)` | Required tuple | Same overall success boundary |
| 51 | NOTI `00/02D5`, occurrence 2 | schedule tuple order 2 | `Static.Notification(02D5,2)` | Required tuple | Same overall success boundary |
| 52 | NOTI `00/01A8` | `Account.Relations.PcRoomPlayTimeState` | `Static.Notification(01A8)` | Always; meaning remains unresolved | Same overall success boundary |
| 53 | NOTI `00/007C` | Observed constant | `Static.Notification(007C)` | Always; empty body | Same overall success boundary |
| 54 | NOTI `00/0002`, occurrence 2 | Same typed subtype-0 inputs as row 8 | `Static.Notification(0002,2)` | Always; deliberately repeated | Same overall success boundary |
| 55 | NOTI `00/0035` | `Account.Wallet` Cera balances | `Static.Notification(0035)` | Always | Same overall success boundary |
| 56 | NOTI `00/0111` | Observed constant | `Static.Notification(0111)` | Always; fixed empty-social baseline | G7 may later supply a live social result only after separate protocol evidence |
| 57 | NOTI `00/0016` | selected character ID and `World.Channel.Spawn` | `Static.Notification(0016)` | Always | G7 owns any permitted position persistence before/around entry publication |
| 58 | NOTI `00/0077` | resolved `World.Content.PetWelcome`, selected character ID | `Static.Notification(0077)` | Non-null welcome with at least one line; otherwise only row 58 is omitted | G7 does not rediscover pet/content applicability in Networking |

Static identity result: **58 descriptors**, rows **1 through 58** exactly once.
Rows 10 and 58 are the only conditional static descriptors. Row 38 remains one
unconditional occurrence. X-01 is outside this count and, on the accepted static
path, is injected at most once immediately after row 11.

## Supplemental state synchronization X-01–X-18

| ID | Wire identity | Explicit G6 input | G6 codec / batch guarantee | Presence condition at codec boundary | Remaining G7 / caller responsibility |
|---|---|---|---|---|---|
| X-01 | NOTI `00/01C0` | optional loaded Dark Knight combo state | `DarkKnightComboStateMessage`; one frame | Job 9 plus loaded combo state | Factory preserves row-11 adjacency |
| X-02 | NOTI `00/0165` | wire user ID and visibility bits | `UserVisibilityMessage` | Concrete message present | Handler places it after the static compatibility stream |
| X-03 | CMD `01/0239` | clone-title item ID | `CloneTitleStateMessage` | Concrete message present | Handler rebuilds current appearance before construction |
| X-04 | NOTI `00/0002`, routed subtype 2 | `CharacterRosterEntryState` with ordered appearance state | `AccountRosterStateMessage`; route byte 2 | Missing roster/appearance fails | Application prepares roster facts; Protocol encodes them |
| X-05 | NOTI `00/0289` | honor level and current-level experience | `HonorProgressStateMessage` | Concrete message present | Handler obtains current progression values |
| X-06 | NOTI `00/0025` | growth, skill-point and honor values | `GrowthCapsuleStateMessage` | Message presence is the applicability decision | Protocol message owns the wire-shaped combination; Data has no packet-ready record |
| X-07 | NOTI `00/000E`, zero-or-more grouped updates | `CommittedInventoryUpdateState` groups | one `InventoryDeltaMessage` per committed group | Empty result = none | Command owns transaction/rollback; handler preserves group order |
| X-08 | CMD `01/0004` rejection then NOTI `00/01F7` server notice | rejection code, notice mode/text | `EntryAdmissionRejectedMessage`; fixed ordered pair | Admission refusal only | Refusal stops before accepted preparation |
| X-09 | NOTI `00/0002`, PvP identity roster | ordered `PvpParticipantEntryState` facts | `PvpLobbySnapshotMessage` direct frame 1 | Applicable lobby only | Command validates current sessions and builds identities without `SelectCharacterResult` in the message input |
| X-10 | NOTI `00/0029`, then NOTI `00/0002` publication | ordered rooms, newcomer and explicit peer targets | lobby direct frame 2; `PvpParticipantPublicationMessage` per peer | Applicable lobby; peer set may be empty | Mark-ready happens after direct success; peer delivery is best effort |
| X-11 | NOTI `00/00FB`, trailing occurrence | character/account equipment-lock state | `TrailingEquipmentLockSnapshotMessage` | Required accepted continuation | Handler preserves the distinct trailing placement |
| X-12 | NOTI `00/023F` | committed accepted-quest progress | `AcceptedQuestProgressMessage` | No committed change = no message; loaded empty can encode count zero | Quest command owns mutation and returns state facts |
| X-13 | NOTI `00/006B` | committed revival user/key/state | `PetRevivalStateMessage` frame 1 | No transition = no message | Pet command validates and commits the transition |
| X-14 | NOTI `00/0067` | same committed revival state | same message frame 2 immediately after X-13 | Cannot be emitted independently | Fixed batch preserves adjacency |
| X-15 | NOTI `00/02A7` | accepted party ID and ordered participants | `DungeonRejoinStateMessage` frame 1 | No valid candidate = no message | Rejoin command validates and reattaches the candidate |
| X-16 | NOTI `00/02A8`, one per participant | participant user IDs in registry order | same message remaining frames | Zero-or-more only after X-15 | Fixed batch preserves multiplicity and order |
| X-17 | NOTI `00/021C`, expert-store close | cleanup notice fact | `ExpertStoreClosedMessage` | Open store in captured exact cleanup state | Application returns notice plan; Protocol creates the message |
| X-18 | NOTI `00/0006`, one per eligible area target | cleanup notice fact and explicit targets | `TownDepartureMessage` | Captured eligible peer set may be empty | Generic delivery performs target-independent best effort |

Supplemental identity result: **18 entries**, X-01 through X-18 exactly once in
this audit. X-08 ordering, X-09/X-10 direct/publication separation, X-13/X-14
adjacency, X-15/X-16 participant multiplicity and X-17/X-18 target-independent
bodies are behavior-tested.

## G6/G7 handoff

G6 is implemented as a **domain-state-to-concrete-message capability**:

- Rows 1–58 have implemented sources, deterministic concrete messages and a tested
  compatibility sequence.
- X-01–X-18 have narrow domain/workflow facts and deterministic concrete messages or
  fixed adjacent batches.
- Required-direct and already-addressed best-effort behavior is protocol-neutral.

G7 remains responsible for the integrated entry workflow, especially:

- choosing when optional supplemental state exists;
- X-07 inventory mutation/rollback;
- X-09/X-10 selected-generation and PvP target discovery;
- X-11/X-12 post-publication preparation ordering;
- X-13/X-14 pet transition and generation checks;
- X-15/X-16 rejoin registry/offer lifecycle;
- X-17/X-18 cleanup mutation, explicit recipient discovery and rollback;
- the legacy lifecycle constraints L-01 through L-10 recorded in the migration
  matrix.

No G6 document or test result is real-client compatibility evidence. Packet capture
and client execution remain required before inferred field meanings can be promoted
to confirmed protocol semantics.
