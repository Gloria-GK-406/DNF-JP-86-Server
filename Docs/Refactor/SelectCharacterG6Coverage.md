# SELECT_CHARACTER G6 projection coverage

> Status: implementation coverage audited on 2026-08-12. The protocol identities,
> layouts and conditions in this document were reverse-engineered from server code;
> they have not been confirmed by packet capture or a real 86JP client.

This document records the G6 boundary: typed result values are converted into
deterministic frames, static occurrence order is preserved, and already-addressed
typed projection batches can be delivered without discovering recipients. It does **not** claim
that G7 entry lifecycle orchestration is complete.

## Implementation seams

- `SelectCharacterResult` combines owner-correct character, character-inventory,
  account-inventory, account, world/content/channel and session-adjacent results.
- `SelectCharacterStaticPayloadCodec` and its body-family partial encode static
  bodies; required data is validated rather than silently replaced.
- `SelectCharacterStaticSequence` is the authoritative successor list of 58 static
  descriptors. `SelectCharacterResponseComposer` preserves its order, duplicates,
  conditions and the X-01 injection immediately after row 11.
- `ISelectCharacterProjectionComposer` is the public trigger-neutral X-01–X-18
  framing boundary; the internal supplemental codec remains Networking-owned.
  Optional presence and all lifecycle decisions remain caller-owned.
- `SelectCharacterProjectionDelivery` and `INetworkMessageSender.SendProjectionAsync`
  carry ordered typed required-direct projections and explicit typed best-effort
  recipient batches; the runtime sender composes their frames. The
  sender neither enumerates sessions nor calls back into business services.

`Static.Ack`, `Static.Inventory`, `Static.Premium`, `Static.Notification`, and
`Supplemental.*` below abbreviate the corresponding methods on those codecs.

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

## Supplemental projections X-01–X-18

| ID | Wire identity | Explicit G6 input | G6 codec / batch guarantee | Presence condition at codec boundary | Remaining G7 / caller responsibility |
|---|---|---|---|---|---|
| X-01 | NOTI `00/01C0` | optional `SelectCharacterDarkKnightComboProjection.Body` | `Supplemental.EncodeDarkKnightCombo`; one frame | Null = absent; supplied body = present | Resolve class/applicability and supply body. Accepted static path already preserves row-11 adjacency |
| X-02 | NOTI `00/0165` | wire user ID and visibility bits | `EncodeVisibility` | Supplied value | Invoke after the completed static stream at the lifecycle-defined point |
| X-03 | CMD `01/0239` | clone-title item ID | `EncodeCloneTitle` | Supplied value | Rebuild/resolve current appearance and choose invocation point |
| X-04 | NOTI `00/0002`, routed subtype 2 | complete `SelectCharacterRosterProjection`, including every character appearance | `EncodeRoster`; route byte 2 | Supplied value; missing appearance collection/entry data fails | Compose the current account roster/appearance/progression value and invoke it |
| X-05 | NOTI `00/0289` | honor level and current-level experience | `EncodeHonor` | Supplied value | Obtain current account progression and invoke after roster |
| X-06 | NOTI `00/0025` | complete growth/skill/honor protocol state | `EncodeGrowthCapsule`; zero or one frame | Null = absent; supplied value = present | Decide max-level/account applicability and supply current values |
| X-07 | NOTI `00/000E`, zero-or-more grouped updates | ordered owner-produced update bodies | `EncodeInventoryUpdates`; preserves group order | Null/empty = none | Perform daily refill transaction, rollback/reload on failure, then supply only committed groups |
| X-08 | CMD `01/0004` rejection then NOTI `00/01F7` server notice | rejection code, notice mode/text | `EncodeRejection`; exactly ordered pair | Supplied rejection | Admission owner decides rejection before preparation; command workflow stops after required pair |
| X-09 | NOTI `00/0002`, PvP identity roster | ordered validated subtype-0 identity bodies | `EncodePvpLobby` direct frame 1 | Supplied lobby projection | Validate selected generation/free-duel applicability and build ordered wire identities |
| X-10 | NOTI `00/0029`, then NOTI `00/0002` publication | ordered room values plus newcomer identity | `EncodePvpLobby` direct frame 2 and separate publication frame | Supplied lobby projection | Discover explicit publication targets only after direct handshake; deliver through explicit-target capability |
| X-11 | NOTI `00/00FB`, trailing occurrence | completed lock result | `EncodeTrailingEquipmentLocks`; distinct frame seam | Supplied completed result | Invoke after session/PvP preparation at the lifecycle-defined trailing point |
| X-12 | NOTI `00/023F` | optional loaded quest list | `EncodeAcceptedQuests`; null none, empty emits zero-count body | Caller supplies null/empty/populated distinction | Run item-seeking synchronization/mutation and emit only its successful projection |
| X-13 | NOTI `00/006B` | optional revival user/key/state result | `EncodePetRevival` frame 1 | Null = no transition | Validate current generation/town/lease and perform revival mutation |
| X-14 | NOTI `00/0067` | same successful revival result | `EncodePetRevival` frame 2 immediately after X-13 | Cannot be independently emitted by this batch | Recheck generation between transition and delivery as required by lifecycle |
| X-15 | NOTI `00/02A7` | optional accepted party ID and ordered participants | `EncodeDungeonRejoin` first frame | Null = no offer | Query/validate registry candidate and maintain session offer state |
| X-16 | NOTI `00/02A8`, one per participant | participant user IDs in registry order | `EncodeDungeonRejoin` remaining frames | Zero-or-more only after X-15 | Supply exact accepted participant order; later rejoin mutation is outside G6 |
| X-17 | NOTI `00/021C`, expert-store close | store-owner wire user ID | `EncodeExpertStoreClose` body | Supplied value | Close store state, decide include-owner mode and discover explicit area targets for normal/displaced/rollback paths |
| X-18 | NOTI `00/0006`, one per eligible area target | leaving wire user ID | `EncodeTownLeave` body | Supplied value | Unregister the exact generation, discover eligible area targets and invoke on leave/displacement/rollback |

Supplemental identity result: **18 entries**, X-01 through X-18 exactly once in
this audit. X-08 ordering, X-09/X-10 direct/publication separation, X-13/X-14
adjacency, X-15/X-16 participant multiplicity and X-17/X-18 target-independent
bodies are behavior-tested.

## G6/G7 handoff

G6 is complete only as a **result-to-wire capability**:

- Rows 1–58 have implemented sources, deterministic bodies and one explicit static
  occurrence sequence.
- X-01–X-18 have packet-ready inputs and deterministic body/batch encoders.
- Required-direct and already-addressed best-effort batches have transport semantics.

G7 remains responsible for the integrated entry workflow, especially:

- choosing when optional supplemental projections exist;
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
