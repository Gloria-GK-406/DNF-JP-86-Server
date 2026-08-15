# SELECT_CHARACTER legacy migration matrix

> Status: reverse-engineered from the legacy server code on 2026-08-11. No
> packet capture was performed for this document. A code-explicit packet shape
> is not automatically a confirmed client-protocol meaning.

## Successor status (2026-08-15)

G6 concrete state-message coverage is implemented for static rows 1–58 and
supplemental identities X-01–X-18. G7 now uses Protocol-handler coordination with
sequential top-level commands for X-07 and X-09–X-18 plus L-01–L-10. The former
SelectCharacter projection stream/composer and transport exception have been removed.
Exact current structure, sources/codecs and lifecycle ownership are recorded in
[`SelectCharacterStateSynchronizationRefactor.md`](SelectCharacterStateSynchronizationRefactor.md),
[`SelectCharacterG6Coverage.md`](SelectCharacterG6Coverage.md) and
[`SelectCharacterG7Coverage.md`](SelectCharacterG7Coverage.md).

This status does not change the confidence labels below: all protocol statements
remain code-reversed and unconfirmed by packet capture. Implementation completeness
does not constitute real-client compatibility evidence.

## Reading the matrix

- `C` confidence means that the source, condition, or ownership is explicit in
  executable code.
- `I` means the business meaning is inferred from names, use sites, or adjacent
  persistence and must remain revisable.
- `U` means that the legacy server itself uses an unknown or placeholder
  meaning. A successor must not promote it to a domain fact without evidence.
- `read` is a projection with no intended mutation. `prepare` means the legacy
  load path may reconcile, initialize, expire, or persist data before sending.
  `lifecycle` changes online/session state.
- An empty collection in a successor result is allowed only after its applicable
  source was loaded. An inapplicable feature is modeled separately. An unknown
  or unavailable source is an error or an explicit uncertainty, not an empty
  collection.

The authoritative static order below is `NewCharacterInitSequence.Build()`.
It contains **58 entries**. Duplicate command/type pairs remain separate rows.

## Ordered static sequence

| # | Wire identity | Legacy builder and source | Meaning and send condition | Domain owner and proposed target | Effect | Confidence | Group |
|---:|---|---|---|---|---|---|---|
| 1 | CMD `01/0004` | `SelectCharacterAckBodyBuilder`; character record, active quests, quest-notify selection, premium and wallet fields in the large init snapshot | Successful selection acknowledgement. It mixes character identity/creation/fatigue/quest data with account premium and Cera. Legacy code incorrectly assigns `AckCharSlotIndex` from `TownId`; the successor uses the selected slot. | Mixed composition: `ICharacterStateService`, `IFatigueService`, `IQuestService`, `IAccountEntitlementService`, `IAccountWalletService`; Networking ACK projection | read after prepare | C shape; I semantics for several tail fields | G1, G3, G4, G6 |
| 2 | NOTI `00/000D`, main | `ItemListPacketBuilder`; character inventory | Main character inventory list; always attempted. | Character; `ICharacterInventoryService` | read | C | G2 |
| 3 | NOTI `00/000D`, avatar | `ItemListPacketBuilder`; character inventory | Avatar inventory list; always attempted. | Character; `ICharacterInventoryService` | read | C | G2 |
| 4 | NOTI `00/000D`, personal cargo | `ItemListPacketBuilder`; character inventory | Character-owned personal cargo; always attempted. | Character; `ICharacterInventoryService` | read | C | G2 |
| 5 | NOTI `00/000D`, pet | `ItemListPacketBuilder`; character inventory | Pet inventory list; always attempted. | Character; `IPetService` backed by character inventory | read | C | G2 |
| 6 | NOTI `00/000D`, account cargo | `ItemListPacketBuilder`; account inventory | Account-wide cargo; always attempted. | Account; `IAccountInventoryService` | read | C | G2, G4 |
| 7 | NOTI `00/0069` | `CreatureListBodyBuilder`; `CreatureItemList` snapshot | Pet/creature runtime list; always attempted, possibly loaded-empty. | Character; `IPetService` | read | C source; I exact client name | G3 |
| 8 | NOTI `00/0002`, occurrence 0 | `UserInfoBodyBuilder` → `UserInfoSubtype0Builder`; character record, appearance, subtype-0 tail | Primary current-character public state; always attempted when the character exists. | Character; `ICharacterPublicStateService` plus `IAppearanceService` | read | C | G3 |
| 9 | NOTI `00/0002`, occurrence 1 | `UserInfoBodyBuilder` → `UserInfoSubtype1Builder`; subtype-1 repository, skill state, adventure-group and honor overlays | Extended user info. Legacy structure mixes character detail with account honor/adventure progression and skips the packet when subtype-1 data is unavailable. | Mixed result: character detail service plus `IAccountProgressionService`; Networking combines them | read after prepare | C source; I several fields | G3, G4, G6 |
| 10 | NOTI `00/0245` | special path in `SelectCharacterPacketBuilder`; `KnightShieldDeckBodyBuilder`, eligibility provider and deck repository | Five-entry Knight Shield deck, sent only for an eligible character with a loaded deck. | Character optional feature; `IKnightShieldService` | read, conditional | C | G3 |
| 11 | NOTI `00/0013` | `SkillInfoBodyBuilder`; skill repository/profile, reconciliation, optional PvP override | Complete selected skill pages. The legacy load can seed/reconcile skills. | Character; `ISkillService` | prepare | C | G3 |
| 12 | NOTI `00/0015` | `QuestListBodyBuilder`; character level/job/grow type, quest cleared flags, quest catalog, pet-evolution eligibility | Available quest list, with character progress and static quest definitions. | Character progress from `IQuestService`; definitions from `IGameContentCatalog` | read | C source; I protocol label | G3, G5 |
| 13 | NOTI `00/0164` | `ClearQuestListBodyBuilder`; `CharacInvisibleFalgs` 30,000-byte indexed flags loaded by init flags | Large indexed completion/visibility flag space. Class and field names conflict, so its business meaning is not considered confirmed. | Character; provisional `IQuestService` projection, retained as unresolved protocol data until verified | read | U meaning | G3, G6 |
| 14 | NOTI `00/0286` | `DailyChallengeBodyBuilder`; character level, racing/daily groups, claim flags and tail IDs | Daily challenge/activity state and activity definitions. | Character progress from `ICharacterDailyStateService`; definitions from `IServerActivityService` | read | C layout; I domain names | G3, G5 |
| 15 | NOTI `00/00AD` | `GameOptionBodyBuilder`; `AccountSettingsRepository` main options and quick-chat banks | Account game options. | Account; `IAccountSettingsService` | read | C | G4 |
| 16 | NOTI `00/01C7` | `HotkeyConfigBodyBuilder`; account key type plus character hotkey slots/default reconciliation | Keyboard/hotkey configuration, mixing account input mode and character-specific slots. | Mixed: `IAccountSettingsService` and `ICharacterSettingsService`; composed result view | prepare when defaults corrected | C | G3, G4, G6 |
| 17 | NOTI `00/006C` | `EventInfoBodyBuilder`; fixed three-byte empty list | Server event list. Legacy intentionally emits empty state for the standalone server. | World/content; `IServerEventService` or a confirmed protocol constant | read | C empty legacy behavior; I live semantics | G5 |
| 18 | NOTI `00/0005` | `DungeonPermissionBodyBuilder`; persisted character permissions transformed by account/channel login policy | Dungeon permission/clear state with admission overlay. | Mixed: `IDungeonProgressService` and `IGameChannelAdmissionService` | read | C | G3, G5, G6 |
| 19 | NOTI `00/0187` | `CharacterOptionBodyBuilder`; character option blob | Character-specific option bitmap; empty bitmap only when the applicable saved value is absent by defined default. | Character; `ICharacterSettingsService` | read | C | G3 |
| 20 | NOTI `00/01B9` | `GrowthWeaponBodyBuilder`; growth weapon stage IDs | Character growth-weapon progression. | Character; `ICharacterProgressionService` | read | C | G3 |
| 21 | NOTI `00/0166`, occurrence 0 | `TitleBookListBodyBuilder`; title-book category 0 from live inventory lease or repository | Title-book category 0. | Character; `ICharacterCollectionService` | read | C | G3 |
| 22 | NOTI `00/0166`, occurrence 1 | same as row 21, category 1 | Title-book category 1. | Character; `ICharacterCollectionService` | read | C | G3 |
| 23 | NOTI `00/0166`, occurrence 2 | same as row 21, category 2 | Title-book category 2. | Character; `ICharacterCollectionService` | read | C | G3 |
| 24 | NOTI `00/0166`, occurrence 3 | same as row 21, category 3 | Title-book category 3. | Character; `ICharacterCollectionService` | read | C | G3 |
| 25 | NOTI `00/0166`, occurrence 4 | same as row 21, category 4 | Title-book category 4. | Character; `ICharacterCollectionService` | read | C | G3 |
| 26 | NOTI `00/0167` | `AchievementListBodyBuilder`; live inventory achievements or repository snapshot | Character achievement completion list. | Character; `ICharacterCollectionService` or cohesive achievement slice | read | C | G3 |
| 27 | NOTI `00/015F` | `SkillPointSlotBodyBuilder`; skill-point slot entries | Character skill-point categories/slots. | Character; `ISkillService` | read | C | G3 |
| 28 | NOTI `00/00AC` | `ItemValueListBodyBuilder`; cooldown item values | Character item cooldown state. | Character inventory/runtime; `ICharacterInventoryService` | read | C | G2 |
| 29 | NOTI `00/00AE` | `ItemValueListBodyBuilder`; effect item values | Character item effect state. | Character inventory/runtime; `ICharacterInventoryService` | read | C | G2 |
| 30 | NOTI `00/02DA` | `BuyRestrictItemListBodyBuilder`; legacy `Unknown730` entries | Purchase-restriction-like list. The builder name is inferred and the snapshot name remains unknown. | Provisional account commerce policy; `IAccountPurchaseRestrictionService` only after semantic validation | read | U meaning | G4, G6 |
| 31 | NOTI `00/017B` | `ShowEffectBodyBuilder`; fixed two-entry body from legacy sample comments | Fixed client effect baseline. | Networking protocol constant unless server configuration evidence appears | read | C bytes; I meaning | G5, G6 |
| 32 | NOTI `00/0381`, occurrence 0 | `CollectionBoxBodyBuilder`; static collection-box model plus character saved slots, box index 3 | Collection-box progress for the first configured box. | Character progress from `ICharacterCollectionService`; definitions from `IGameContentCatalog` | read | C | G3, G5 |
| 33 | NOTI `00/0381`, occurrence 1 | same as row 32, box index 4 | Collection-box progress for the second configured box. | Character progress from `ICharacterCollectionService`; definitions from `IGameContentCatalog` | read | C | G3, G5 |
| 34 | NOTI `00/021F` | `BossTowerBodyBuilder`; fixed integer zero placeholder | Boss-tower-like placeholder. No loaded domain state exists in the legacy path. | Unresolved world/content capability; do not create character state for it | read | U | G5, G6 |
| 35 | NOTI `00/0357` | `RentalInfoBodyBuilder`; current character rental equipment, account Lucky Star and current time | Rental state with a wallet-derived field. Expired rental equipment is deleted and the snapshot rebuilt before projection. | Mixed: `ICharacterInventoryService` and `IAccountWalletService`; Networking combines | prepare | C | G2, G4, G6 |
| 36 | NOTI `00/00FB` | `ItemLockListBodyBuilder`; live inventory lease lock state | Locked character equipment list. | Character; `ICharacterInventoryService` | read | C | G2 |
| 37 | NOTI `00/00CD` | `ExpertJobInfoBodyBuilder`; subtype-0 expert type/experience and expert-job state repository | Expert profession state, including profession-specific machine data. | Character; `IExpertJobService` | read | C | G3 |
| 38 | NOTI `00/019F` | special/inject path; `StrikerSupportTagCharacterBodyBuilder` and support-character packet builder | Account cross-character striker/support relation. Exactly one packet is forced, using an explicit empty wire body if no persisted support exists. | Account relation; `IAccountCharacterRelationService` | read, conditional content | C | G4, G6 |
| 39 | NOTI `00/00B1` | `SimpleByteBodyBuilder`; character daily-reset revive-coin claim bit | Character daily claim state. | Character; `ICharacterDailyStateService` | read | C | G3 |
| 40 | NOTI `00/0300` | `CubeInfoBodyBuilder`; cube type and grade init fields | Character cube/progression state. | Character; provisional `ICharacterProgressionService` | read | C bytes; I business boundary | G3 |
| 41 | CMD `01/0312` | inline builder in `InitPacketBuilderRegistry`; premium service data from account contracts and character lottery usage | Premium service acknowledgement/state. It combines account entitlement with character usage. | Mixed: `IAccountEntitlementService` and relevant character usage service | read after prepare | C | G3, G4, G6 |
| 42 | NOTI `00/03D8` | `LotteryBufferBodyBuilder`; account lottery progress repository | Account lottery/increased-chance progress. | Account; `IAccountEventProgressService` | read | C | G4 |
| 43 | NOTI `00/019D` | `LuckyStarInfoBodyBuilder`; wallet Lucky Star | Account wallet currency. | Account; `IAccountWalletService` | read | C | G4 |
| 44 | NOTI `00/025B` | `ChampionBreakSystemBodyBuilder`; key/mode/value snapshot | Champion-break-like progress. Legacy naming is the only business evidence. | Provisional character progression; `ICharacterProgressionService` after validation | read | I | G3 |
| 45 | NOTI `00/0331` | `SimpleByteBodyBuilder`; character gold-limit repository `LoadOrCreate` | Character gold carry/auction upgrade level. | Character; `ICharacterProgressionService` or inventory limit service | prepare on first load | C | G2, G3 |
| 46 | NOTI `00/01EB` | `FatigueAccelBodyBuilder`; fatigue acceleration value and state | Character fatigue acceleration. | Character; `IFatigueService` | read | C | G3 |
| 47 | NOTI `00/0061` | `MailboxBodyBuilder`; character mailbox repository | First character mailbox page and not-loaded count. | Character; `IMailboxService` | read | C | G3 |
| 48 | NOTI `00/0158` | `PvpMissionBodyBuilder`; character PvP mission entries | Character PvP mission progress. | Character; `IPvpProgressService` | read | C | G3 |
| 49 | NOTI `00/02D5`, occurrence 0 | `DailyScheduleBodyBuilder`; legacy `Unknown725Packets[0]` or zero tuple | Schedule/activity tuple 0. Persisted per character but semantic ownership is unclear. | Provisional character daily state plus server schedule definitions | read | U meaning | G3, G5, G6 |
| 50 | NOTI `00/02D5`, occurrence 1 | same as row 49, tuple 1 | Schedule/activity tuple 1. | Provisional character daily state plus server schedule definitions | read | U meaning | G3, G5, G6 |
| 51 | NOTI `00/02D5`, occurrence 2 | same as row 49, tuple 2 | Schedule/activity tuple 2. | Provisional character daily state plus server schedule definitions | read | U meaning | G3, G5, G6 |
| 52 | NOTI `00/01A8` | `SimpleByteBodyBuilder`; `PcRoomPlayTimeState` init field | PC-room/play-time-like state. Legacy ownership and applicability are unclear. | Provisional session/account entitlement service, not character state until validated | read | U meaning | G4, G5, G6 |
| 53 | NOTI `00/007C` | `EmptyBodyBuilder`; no source data | Fixed empty entry marker. | Session/Networking protocol constant | read | C empty body; U meaning | G5, G6 |
| 54 | NOTI `00/0002`, occurrence 2 | `UserInfoBodyBuilder` → subtype-0 builder again | Repeated current-character public state near entry completion. | Character; `ICharacterPublicStateService`; Networking owns repeated placement | read | C | G3, G6 |
| 55 | NOTI `00/0035` | `CeraBodyBuilder`; account Cera, token Cera and happy-token Cera wallet | Account wallet balances. | Account; `IAccountWalletService` | read | C | G4 |
| 56 | NOTI `00/0111` | `UnitedServerFriendInfoBodyBuilder`; fixed empty friend list | Cross-server friend/social initialization. | World/social; `ISocialEntryService` or a confirmed protocol constant | read | C empty legacy behavior; I live semantics | G5 |
| 57 | NOTI `00/0016` | `UserPositionBodyBuilder`; selected character position/direction | Character town position. | Character; `ICharacterPositionService` | read | C | G3 |
| 58 | NOTI `00/0077` | `PetCreatureWelcomeMessageBodyBuilder`; equipped pet from subtype-0 tail, pet item/runtime lookup and script | Pet welcome message; skipped when there is no applicable equipped pet or no valid script body. | Character optional feature; `IPetService` plus static pet content | read, conditional | C | G3, G5 |

Mechanical count: rows 1–58 above correspond one-for-one and in order with
the 58 `Raw(...)`/`Item(...)` additions in `NewCharacterInitSequence.Build()`.

## Conditional and trailing packets outside the static list

| ID | Wire identity or action | Legacy source and condition | Domain owner and proposed target | Effect | Confidence | Group |
|---|---|---|---|---|---|---|
| X-01 | NOTI `00/01C0` | `SelectCharacterPacketBuilder` injects `DarkKnightComboSkillInfoBodyBuilder` immediately after row 11, or as a trailing fallback, only for job 9 with a valid combo body. | Character optional feature; `ISkillService`. `null` is valid only for a non-Dark-Knight character; an applicable loaded empty combo remains an empty combo state. | read, conditional | C | G3, G6 |
| X-02 | NOTI `00/0165` | `CharacterSelectHandler` sends character visibility bits after static stream. | Character; `ICharacterSettingsService` or public-state service | read | C | G3, G6 |
| X-03 | CMD `01/0239` | `CharacterSelectHandler` restores clone-title state from equipped appearance after static stream. | Character; `IAppearanceService` | read | C | G3, G6 |
| X-04 | NOTI `00/0002`, routed subtype 2 | Account character roster rebuilt and resent after the static stream because selection may bypass `GET_USERINFO`. | Account; existing roster/appearance/account-progression composition | read | C | G4, G6 |
| X-05 | NOTI `00/0289` | `HonorLevelSyncService` sends account honor summary after roster. | Account; `IAccountProgressionService` | read | C | G4, G6 |
| X-06 | NOTI `00/0025` | `GrowthCapsuleSyncService` sends only for a max-level selected character with account, character, honor, capsule and skill-point state. | Mixed account progression and selected-character skill/progression result | read, conditional | C | G3, G4, G6 |
| X-07 | NOTI `00/000E`, zero-or-more grouped updates | `DailyRefillItemService` may grant/reset inventory items after the static stream and `InventoryRefreshSender.SendOnlineUpdateItemList` emits one update per changed inventory-list group; transaction failure emits none and reloads the inventory lease. | Character inventory; `ICharacterDailyStateService` coordinating `ICharacterInventoryService` | prepare/mutation, conditional | C | G2, G6, G7 |
| X-08 | CMD `01/0004` rejection then NOTI `00/01F7` server notice | `CharacterSessionLifecycleCoordinator.HandleSelectCharacterAsync` sends both packets and stops before session preparation when `GameChannelAdmissionPolicy` rejects the selected character for the listener. | World/channel admission; `IGameChannelAdmissionService` with Networking error projection | read, conditional | C | G1, G5, G6 |
| X-09 | NOTI `00/0002`, PvP identity roster | `PvpRoomHandler.HandleLobbyReadyAsync` sends a roster of existing room/lobby identities plus the newcomer only when the selected generation is an authenticated, idle, current session on an enabled free-duel listener with a constructed game session. | World/session PvP lobby view from `IPvpEntryService`; character public identities remain character-owned | lifecycle projection, conditional | C | G5, G6, G7 |
| X-10 | NOTI `00/0029`, then NOTI `00/0002` publication | The same PvP handshake sends one room snapshot to the newcomer after X-09, then publishes the newcomer's basic identity to existing listener targets after the direct handshake succeeds. | World/session; `IPvpEntryService`; Networking owns the required direct order and later target publication | lifecycle projection, conditional, one direct plus zero-or-more target sends | C | G5, G6, G7 |
| X-11 | NOTI `00/00FB`, trailing occurrence | `SendAllEquipmentItemLockListRefresh` always emits another complete equipment-lock list after game-session creation and PvP lobby preparation. This is distinct from static row 36. | Character inventory; `ICharacterInventoryService`; Networking preserves the trailing duplicate placement | read | C | G2, G6, G7 |
| X-12 | NOTI `00/023F` | `QuestManager.SyncItemSeekingQuestProgressAsync` sends the active/accepted quest projection only when item-seeking synchronization matches and changes relevant progress. | Character; `IQuestService` preparation result and quest projection | prepare/mutation, conditional | C | G3, G6, G7 |
| X-13 | NOTI `00/006B` | `PetCreatureRuntimeService.BeginTownAsync` sends a revival notification only when the selected session is still current, is in town, owns an inventory lease, has an equipped dead creature, and the revival transition succeeds. | Character pet runtime; `IPetService` | prepare/mutation, conditional | C | G3, G6, G7 |
| X-14 | NOTI `00/0067` | Immediately after a successful X-13 revival and a repeated generation check, pet runtime sends one creature key/state update. It is not sent merely because a pet exists. | Character pet runtime; `IPetService`; Networking preserves adjacency after X-13 | lifecycle projection, conditional | C | G3, G6, G7 |
| X-15 | NOTI `00/02A7` | `DungeonRejoinCoordinator.ProjectCandidateAsync` sends one disconnected-dungeon offer only when the authenticated selected account/character/user has a valid registry candidate; invalid or absent candidates clear the session offer without sending. | Character/session dungeon rejoin; `IDungeonProgressService` and `ICharacterSessionService` | lifecycle projection, conditional | C | G5, G6, G7 |
| X-16 | NOTI `00/02A8`, one per participant | After X-15, the coordinator sends one participant record for every user ID in the accepted rejoin candidate, preserving registry order. | Character/session dungeon rejoin; `IDungeonProgressService` and `ICharacterSessionService`; Networking owns multiplicity/order | lifecycle projection, conditional, zero-or-more after X-15 | C | G5, G6, G7 |
| X-17 | NOTI `00/021C`, expert-store close | `ExpertJobStoreHandler.CloseSessionAsync` emits only when the leaving/displaced/rollback session owns an open expert store. A normal character switch uses `includeOwner=true`, sending once to the owner and broadcasting to other sessions in the store area; displaced-session and rollback cleanup use `includeOwner=false`, broadcasting only to the other area targets. | Character expert-job runtime plus area session projection; `IExpertJobService` and `ICharacterSessionService` | lifecycle mutation/projection, conditional, target-multiplicity | C | G3, G5, G6, G7 |
| X-18 | NOTI `00/0006`, one per eligible area target | `TownHandler.NotifyLeaveAsync` broadcasts a user-leave body when the cleaned session has a positive selected character and a session directory is available. It is invoked when leaving a previous character, cleaning a displaced generation, and after a registered selected generation is removed during rollback. | World/session town presence; `IGameWorldPresenceService` and `ICharacterSessionService` | lifecycle projection, conditional, zero-or-more targets | C | G5, G6, G7 |

`0x019F` and `0x01C0` also have injection logic inside the static-stream builder.
Row 38 accounts for the guaranteed single `0x019F`; X-01 accounts for the
Dark Knight packet absent from the 58-entry template.

## Entry lifecycle projections

These actions are not packet-template rows, but they are part of the selected
character becoming a valid online generation and therefore constrain service
design.

| ID | Wire identity | Observable condition and concrete legacy source | Proposed owner | Effect | Confidence | Group |
|---|---|---|---|---|---|---|
| L-01 | conditional cleanup sends are X-17 and X-18 | `CharacterSessionLifecycleCoordinator.HandleSelectCharacterAsync` clears the rejoin offer for every accepted request and calls `LeaveCurrentCharacterForSelectionAsync` only when the connection already owns a character; that path closes any expert store, announces town leave, persists position, ends its dungeon run, and unregisters it. | `ICharacterSessionService` coordinating expert-job, town-presence, and dungeon lifecycle | lifecycle/mutation/projection | C | G1, G7 |
| L-02 | none directly; later state appears in mapped packets | `CharacterSelectHandler.HandleResolvedSelectCharacterAsync` calls `DungeonPersistentEffectApplicationService.RecoverCharacter` only when that service is configured, then reloads the record when recovery committed effects. | `IDungeonProgressService` preparation | prepare/mutation | C | G3, G7 |
| L-03 | none directly; inventory packets are rows 2–6 and X-07/X-11 | `CharacterSelectHandler.SaveExistingInventoryLeaseBeforeReload`, `TryLoadInventoryForLease`, and `TryRegisterInventoryLease` run for the exact resolved character before packet construction; absence or failure of a valid load prevents a trustworthy prepared inventory. | `ICharacterInventoryService` with session-owned lease registry | lifecycle/prepare | C | G2, G7 |
| L-04 | none directly; position and appearance are rows 8, 57 and X-02/X-03 | `CharacterSelectHandler.HandleResolvedSelectCharacterAsync` resolves spawn for every selected listener, hydrates the player, rebuilds appearance from current equipment, and persists the resolved position only when `GameChannelSpawnPolicy.ShouldPersistPosition` is true. | `IGameChannelAdmissionService`, `ICharacterPositionService`, `IAppearanceService` | prepare/mutation | C | G3, G5, G7 |
| L-05 | displaced-generation cleanup may send X-17 and X-18 | `CharacterSessionLifecycleCoordinator.HandleSelectCharacterAsync` calls `SessionDirectory.RegisterReplacingAsync` after any previous-character cleanup; when a displaced session is returned, `CleanupDisplacedSessionAsync` closes its expert store, announces town leave, persists/detaches its runtime state and closes it. Preparation then proceeds only if the new session remains the registered selected generation. | `ICharacterSessionService`, `IUserService`, and affected expert/town/dungeon services | lifecycle/mutation/projection | C | G1, G7 |
| L-06 | none directly; resulting skills are row 11 and X-01 | `CharacterSelectHandler` calls `PrepareForSkillSynchronization` for the SQLite data source on every selected character; it loads a separate PvP skill profile only on a free-duel listener with a resolvable character record. | `ISkillService` plus channel policy | prepare/mutation | C | G3, G5, G7 |
| L-07 | no additional unidentified wire; concrete sends are X-09–X-11 | After selected-generation publication succeeds, `CharacterSessionLifecycleCoordinator` constructs `GameSession`, invokes `PvpRoomHandler.HandleLobbyReadyAsync`, and invokes `InventoryRefreshSender.SendAllEquipmentItemLockListRefresh`; PvP packets remain conditional while X-11 is sent for every successful continuation. | `ICharacterSessionService`, `IPvpEntryService`, `ICharacterInventoryService` | lifecycle/projection | C | G2, G3, G7 |
| L-08 | no additional wire; conditional send is X-12 | After game-session construction and lock refresh, `CharacterSessionLifecycleCoordinator` invokes `QuestManager.SyncItemSeekingQuestProgressAsync`; only a matching synchronization emits X-12. | `IQuestService` | prepare/mutation | C | G3, G7 |
| L-09 | no additional unidentified wire; conditional sends are X-13–X-16 | For every successful continuation, `CharacterSessionLifecycleCoordinator` invokes `PetCreatureRuntimeService.BeginTownAsync` and then `DungeonRejoinCoordinator.ProjectCandidateAsync`; each service emits only under the conditions recorded by X-13–X-16. | `IPetService`, `IDungeonProgressService`, `ICharacterSessionService` | lifecycle/projection | C | G3, G7 |
| L-10 | rollback may send X-17 and, after successful unregister, X-18 | The `HandleSelectCharacterAsync` catch path calls `RollbackSelectedSessionAsync` and closes the connection on any exception after selected-generation registration. Expert-store close is attempted for the session; town leave is broadcast only when unregister removed this exact selected generation and the player still identifies that character. Earlier admission rejection follows X-08 instead. | Select Character command orchestration, `ICharacterSessionService`, and affected expert/town/dungeon services | lifecycle rollback/projection | C | G1, G7 |

## Explicit legacy corrections

The following behaviors are evidence of defects, not compatibility targets:

1. `ResolveSelectedCharacter` treats the request `u16` as a roster list index,
   resets an out-of-range value to zero, and falls back to a seed character.
   The refactored flow resolves the exact persisted `AccountId + SlotIndex` and
   fails an invalid selection.
2. `SelectCharacterPacketBuilder` overwrites `AckCharSlotIndex` with
   `CharacterRecord.TownId`. The refactored ACK uses the requested/persisted
   character slot.
3. `SqliteSelectCharacterDataSource.Load` performs expiration, skill seeding,
   settings correction, rental persistence, gold-limit creation and adventure
   level persistence while presenting itself as one snapshot load. Successor
   services must expose preparation behavior rather than hide it in a query.
4. Builders directly construct repositories (`MailboxBodyBuilder`,
   `LotteryBufferBodyBuilder`, collection-box setup) or read global inventory
   and session state. Networking must receive completed business views instead.
5. Several fields are named `Unknown725`, `Unknown730`, or reuse
   `CharacInvisibleFalgs` for a 30,000-byte structure. These names do not justify
   confident domain models.

## Dependency-ordered successor groups

| Group | Accepted outcome | Matrix coverage | Prerequisites |
|---|---|---|---|
| G1 | Selection/session consistency: exact account slot, admission result, generation replacement, user binding, active-character publication and rollback semantics | row 1, X-08, L-01, L-05, L-10 | Existing CommandBus account-exclusive gate |
| G2 | Character and account inventory entry services, including pets-as-inventory, rental expiry, locks, item values, cargo, gold limits and daily refill preparation | rows 2–6, 28–29, 35–36, 45; X-07, X-11; L-03, L-07 | G1 identity and session ownership |
| G3 | Character-owned current state services: public/detail state, fatigue, skills, quests, settings, appearance, pets, collections, expert job, progression, dungeon/PvP/mail/daily state and optional class features | rows 7–14, 16, 18–27, 32–40, 41 character portion, 44–51, 54, 57–58; X-01–X-03, X-06 character portion, X-12–X-14, X-17; L-02, L-04, L-06–L-09 | G1; inventory outputs from G2 where referenced |
| G4 | Account-owned entry services: settings, wallet, premium, honor/adventure/growth, lottery, purchase restrictions, cross-character support and account cargo | rows 1 account portion, 6, 9 account portion, 15–16, 30, 35, 38, 41–43, 52, 55; X-04–X-06 | G1; roster baseline; G2 account cargo |
| G5 | World/content/session-adjacent query services and honest constants: quest/activity/collection definitions, events, channel policy, schedules, social/town state and unresolved fixed protocol baselines | rows 12, 14, 17–18, 31–34, 49–53, 56, 58 content portion; X-08–X-10, X-15–X-18; L-04, L-06 | G1; unresolved meanings remain explicit |
| G6 | Protocol-owned concrete state messages/encoders for static order, duplicates, conditional packets, mixed-domain ACKs and trailing synchronization; generic explicit-recipient transport | all rows 1–58 and X-01–X-18 | Accepted G1–G5 business results |
| G7 | Integrated entry workflow for preparation mutations, session publication, post-entry synchronization and rollback | X-07, X-09–X-18 and L-01–L-10 | Accepted G1–G6 |

The groups are coverage boundaries, not pre-created implementation Specs. The
root controller should activate only the next smallest stable group outcome
supported by accepted evidence.

## Primary legacy evidence

- `Game/SelectCharacter/NewCharacterInitSequence.cs`
- `Network/Builders/Init/SelectCharacterPacketBuilder.cs`
- `Network/Builders/Init/InitPacketBuilderRegistry.cs`
- `Game/SelectCharacter/SqliteSelectCharacterDataSource.cs`
- `Network/Handlers/CharacterSelectHandler.cs`
- `Network/Handlers/CharacterSessionLifecycleCoordinator.cs`
- Individual builders under `Network/Builders/Init/`

All paths above refer to the legacy mainline tree under `Server/DfoServer`.
